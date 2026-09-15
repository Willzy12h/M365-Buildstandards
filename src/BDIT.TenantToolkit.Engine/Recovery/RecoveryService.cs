using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Evidence;

namespace BDIT.TenantToolkit.Engine.Recovery;

/// <summary>Selective, single-use recovery backed by recorded writes and fresh evidence. Never adopts by name or retries a write.</summary>
public sealed class RecoveryService(EvidenceStore evidence, IClock clock)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public IReadOnlyList<ChangeRegisterRow> Register(string tenantId)
    {
        var recoveries = evidence.LoadRecoveryRuns(tenantId);
        var verifications = evidence.LoadVerifications(tenantId);
        return evidence.RequireIntactRuns(tenantId).SelectMany(run => run.Results
            .Where(r => r.PlannedAction is nameof(PlanAction.Create) or nameof(PlanAction.Update))
            .Select(r =>
            {
                var recovery = recoveries.FirstOrDefault(x => x.SourceRunId == run.Id && x.ControlId == r.ControlId);
                var verified = verifications.FirstOrDefault(v => v.Verified && v.SourceKind == "Deployment" && v.SourceRunId == run.Id && v.SourceDigest == run.IntegrityDigest && v.ControlId == r.ControlId);
                var recoveryVerified = recovery is not null && verifications.Any(v => v.Verified && v.SourceKind == "Recovery" && v.SourceRunId == recovery.Id && v.SourceDigest == recovery.IntegrityDigest);
                var detail = recovery is null ? "No recovery attempted" : $"{recovery.Action}: {(recoveryVerified ? "Completed / Pass (re-verified)" : recovery.Status + " / " + recovery.Verification)}";
                if (verified is not null) detail += $" · Deployment re-verified {verified.At}; original evidence retained.";
                return new ChangeRegisterRow(run.Id, r.ControlId, r.Name, r.Collection, r.PlannedAction,
                    r.ObjectId ?? "Not returned", r.WrittenAt ?? run.StartedAt, r.WriteAcceptance,
                    verified is null ? r.Configuration : ConfigurationVerification.Pass, detail, recovery?.Id, EvidenceStore.IsHistoricalCandidate(run, r));
            })).ToList();
    }

    public async Task<RecoveryPlan> PreviewAsync(IGraphClient graph, TenantSession session, StandardCatalogue standard,
        TenantSnapshot snapshot, string runId, string controlId, RecoveryAction action, CancellationToken ct)
    {
        AssertSession(graph, session);
        var captured = evidence.RequireDeploymentSnapshot(snapshot, standard);
        AssertFresh(captured.CapturedAt, TimeSpan.FromMinutes(20));
        var (source, item, definition, mappings) = RequireSource(session.TenantId, standard, runId, controlId, action);
        evidence.AssertNoUnresolvedRecovery(session.TenantId, new[] { controlId });
        evidence.AssertNoUnresolvedReviewedChanges(session.TenantId, new[] { controlId });
        var current = await RecoveryObjectReader.ReadAsync(graph, definition, item.ObjectId!, ct);
        JsonObject? payload = null;
        var drift = item.AfterObject is null || CanonicalJson.Sha256(item.AfterObject) != CanonicalJson.Sha256(current);
        if (action == RecoveryAction.RestoreUpdate)
        {
            if (drift) throw new SafetyViolationException("The object differs from the recorded post-write state. Restoration would overwrite later changes; it is blocked.");
            if (!RecoveryObjectReader.IsInactive(definition, current)) throw new SafetyViolationException("Only inactive, unassigned objects can have an update restored.");
            payload = RestorePayload(item, definition);
        }
        else if (action == RecoveryAction.DisableConditionalAccess)
        {
            if (ConditionalAccessSafety.State(current) == "disabled") throw new SafetyViolationException("This policy is already disabled. No containment write is needed.");
            payload = new JsonObject { ["state"] = "disabled" };
        }
        // Assignment removal is a different operation, with different consequences and dependencies.
        if (definition.Assignments && current[RecoveryObjectReader.AssignmentsKey] is not JsonArray { Count: 0 })
            throw new SafetyViolationException("This object is assigned. Review and remove its assignments separately before recovery; this tool will not silently remove targeting.");
        RecoverySafety.AssertPayload(definition.BasePath, action, payload, definition.ApiVersion);
        var plan = new RecoveryPlan
        {
            Id = Guid.NewGuid().ToString(), TenantId = session.TenantId, OperatorId = session.OperatorObjectId!, ClientId = session.ClientId,
            StandardDigest = CanonicalJson.Sha256Value(standard), BeforeSnapshotId = captured.Id, BeforeSnapshotDigest = captured.IntegrityDigest,
            SourceRunId = source.Id, SourceRunDigest = source.IntegrityDigest, ControlId = item.ControlId, Name = item.Name,
            Collection = item.Collection, ObjectId = item.ObjectId!, Action = action, CreatedAt = Timestamps.Format(clock.UtcNow),
            CurrentObject = current, Payload = payload, MappingsDigest = CanonicalJson.Sha256Value(mappings), DriftDetected = drift,
            Consequence = action switch
            {
                RecoveryAction.DeleteCreatedObject => "Permanently delete this recorded toolkit-created policy. It will stop applying. Re-creating it would use a new object ID; this is not an automatic rollback of device or user effects.",
                RecoveryAction.RestoreUpdate => "Restore only the settings changed by the recorded update, using its captured before-values. Keep the object disabled or unassigned. Device and sign-in effects require separate verification.",
                _ => "Disable this toolkit-created Conditional Access policy. Its current targeting and exclusions remain stored, but enforcement stops. Other policies can still affect sign-in."
            }
        };
        evidence.SaveRecoveryPlan(plan);
        return evidence.RequireRecoveryPlan(plan.TenantId, plan.Id);
    }

    public async Task<RecoveryRun> ExecuteAsync(IGraphClient graph, TenantSession session, StandardCatalogue standard,
        RecoveryPlan reviewed, string typedTenantId, bool approved, bool reviewedDrift, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) throw new SafetyViolationException("A recovery operation is already running.");
        try { return await ExecuteCoreAsync(graph, session, standard, reviewed, typedTenantId, approved, reviewedDrift, ct); }
        finally { _gate.Release(); }
    }

    private async Task<RecoveryRun> ExecuteCoreAsync(IGraphClient graph, TenantSession session, StandardCatalogue standard,
        RecoveryPlan reviewed, string typedTenantId, bool approved, bool reviewedDrift, CancellationToken ct)
    {
        AssertSession(graph, session);
        if (!approved || !string.Equals(typedTenantId.Trim(), session.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new SafetyViolationException("Approve the displayed recovery consequence and type the exact tenant ID.");
        using var lease = evidence.AcquireTenantWriteLease(session.TenantId);
        var plan = evidence.RequireRecoveryPlan(session.TenantId, reviewed.Id);
        if (EvidenceStore.RecoveryDigest(reviewed) != plan.IntegrityDigest || plan.OperatorId != session.OperatorObjectId || plan.ClientId != session.ClientId
            || plan.StandardDigest != CanonicalJson.Sha256Value(standard)) throw new SafetyViolationException("Recovery inputs changed; preview again.");
        AssertFresh(plan.CreatedAt, TimeSpan.FromMinutes(5));
        if (plan.DriftDetected && !reviewedDrift) throw new SafetyViolationException("Explicitly acknowledge the displayed drift and current settings before recovery.");
        if (evidence.RecoveryAttemptExists(plan.TenantId, plan.Id)) throw new SafetyViolationException("This recovery plan has already been attempted. Writes are never replayed.");
        var snapshot = evidence.LoadSnapshot(plan.TenantId, plan.BeforeSnapshotId) ?? throw new SafetyViolationException("Recovery before-snapshot is missing.");
        evidence.RequireDeploymentSnapshot(snapshot, standard);
        if (snapshot.IntegrityDigest != plan.BeforeSnapshotDigest) throw new SafetyViolationException("Recovery snapshot changed.");
        AssertFresh(snapshot.CapturedAt, TimeSpan.FromMinutes(20));
        var (source, item, definition, mappings) = RequireSource(plan.TenantId, standard, plan.SourceRunId, plan.ControlId, plan.Action);
        if (source.IntegrityDigest != plan.SourceRunDigest || CanonicalJson.Sha256Value(mappings) != plan.MappingsDigest)
            throw new SafetyViolationException("Recorded writes or ownership changed; preview recovery again.");
        evidence.AssertNoUnresolvedRecovery(plan.TenantId, new[] { plan.ControlId });
        evidence.AssertNoUnresolvedReviewedChanges(plan.TenantId, new[] { plan.ControlId });
        var current = await RecoveryObjectReader.ReadAsync(graph, definition, plan.ObjectId, ct);
        if (CanonicalJson.Sha256(current) != CanonicalJson.Sha256(plan.CurrentObject))
            throw new SafetyViolationException("The object changed after recovery review. No write was made; preview its current state again.");
        var expectedPayload = plan.Action == RecoveryAction.RestoreUpdate ? RestorePayload(item, definition)
            : plan.Action == RecoveryAction.DisableConditionalAccess ? new JsonObject { ["state"] = "disabled" } : null;
        if (CanonicalJson.Sha256(expectedPayload) != CanonicalJson.Sha256(plan.Payload)) throw new SafetyViolationException("Recovery payload differs from recorded evidence.");
        RecoverySafety.AssertPayload(definition.BasePath, plan.Action, plan.Payload, definition.ApiVersion);
        ct.ThrowIfCancellationRequested();
        var run = new RecoveryRun
        {
            Id = plan.Id, TenantId = plan.TenantId, SourceRunId = source.Id, ControlId = item.ControlId, ObjectId = plan.ObjectId,
            Action = plan.Action, StartedAt = Timestamps.Format(clock.UtcNow), PlanDigest = plan.IntegrityDigest,
            Actor = new RunActor { Account = session.Account, ObjectId = session.OperatorObjectId!, ClientId = session.ClientId, AuthenticationType = session.AuthenticationType },
            BeforeObject = current
        };
        evidence.SaveRecoveryRun(run);
        var saved = evidence.LoadRecoveryRuns(plan.TenantId).Single(r => r.Id == plan.Id);
        if (saved.IntegrityDigest != run.IntegrityDigest) throw new SafetyViolationException("Recovery intent could not be verified on disk.");
        var invoked = false;
        try
        {
            // Once a request is sent, cancellation must not abandon acceptance/readback evidence.
            run.WriteAcceptance = WriteAcceptance.Unknown;
            run.Verification = ConfigurationVerification.Unknown;
            evidence.SaveRecoveryRun(run);
            invoked = true;
            await graph.RecoverAsync(definition.ApiVersion, plan.Action, definition.BasePath + "/" + plan.ObjectId, plan.Payload, CancellationToken.None);
            run.WriteAcceptance = WriteAcceptance.Accepted;
            evidence.SaveRecoveryRun(run);
            using var verification = CancellationTokenSource.CreateLinkedTokenSource(ct);
            verification.CancelAfter(TimeSpan.FromSeconds(60));
            if (plan.Action == RecoveryAction.DeleteCreatedObject)
            {
                try { run.AfterObject = await graph.GetAsync(definition.ApiVersion, definition.BasePath + "/" + plan.ObjectId, verification.Token); }
                catch (GraphRequestException ex) when (ex.StatusCode == 404) { run.ObjectAbsent = true; }
                if (run.ObjectAbsent)
                {
                    var remaining = await graph.GetAllAsync(definition.ApiVersion, definition.Path, verification.Token);
                    run.ObjectAbsent = remaining.All(x => !string.Equals(x["id"]?.GetValue<string>(), plan.ObjectId, StringComparison.OrdinalIgnoreCase));
                }
                run.Verification = run.ObjectAbsent ? ConfigurationVerification.Pass : ConfigurationVerification.Unknown;
            }
            else
            {
                run.AfterObject = await RecoveryObjectReader.ReadAsync(graph, definition, plan.ObjectId, verification.Token);
                run.Verification = CanonicalJson.IsSubset(run.AfterObject, plan.Payload!) && RecoveryObjectReader.IsInactive(definition, run.AfterObject)
                    ? ConfigurationVerification.Pass : ConfigurationVerification.Unknown;
            }
            if (run.Verification == ConfigurationVerification.Pass)
            {
                // Do not overwrite a different process's ownership edits made during the request.
                if (CanonicalJson.Sha256Value(evidence.LoadMappings(plan.TenantId)) != plan.MappingsDigest)
                    throw new SafetyViolationException("Recovery was verified but ownership changed concurrently; reconcile the register.");
                if (plan.Action == RecoveryAction.DeleteCreatedObject) mappings.ByControl.Remove(item.ControlId);
                else if (plan.Action == RecoveryAction.RestoreUpdate) mappings.ByControl[item.ControlId] = Copy(item.BeforeMapping!);
                if (plan.Action != RecoveryAction.DisableConditionalAccess) evidence.SaveMappings(mappings);
                run.Status = RunStatus.Completed;
            }
            else { run.Status = RunStatus.ReviewRequired; run.Reason = "Write accepted; the requested final configuration was not confirmed. Do not retry the write."; }
        }
        catch (Exception ex)
        {
            if (!invoked || (run.WriteAcceptance != WriteAcceptance.Accepted && ex is WriteNotSentException))
            {
                run.WriteAcceptance = WriteAcceptance.NotAttempted;
                run.Verification = ConfigurationVerification.NotRun;
            }
            else if (run.WriteAcceptance != WriteAcceptance.Accepted && ex is GraphRequestException { StatusCode: >= 400 and < 500 } request && request.StatusCode != 408)
                run.WriteAcceptance = WriteAcceptance.Rejected;
            run.Status = RunStatus.ReviewRequired;
            run.Reason = SensitiveDataScrubber.Scrub(ex.Message);
        }
        finally
        {
            run.EndedAt = Timestamps.Format(clock.UtcNow);
            EvidencePersistence.SaveTerminal(() => evidence.SaveRecoveryRun(run), message => { run.Status = RunStatus.ReviewRequired; run.Reason = EvidencePersistence.Append(run.Reason, message); });
        }
        return run;
    }

    private (DeploymentRun, RunResult, CollectionDefinition, ManagedObjectMappings) RequireSource(string tenant, StandardCatalogue standard, string runId, string control, RecoveryAction action)
    {
        var runs = evidence.RequireIntactRuns(tenant);
        var source = runs.SingleOrDefault(r => r.Id == runId) ?? throw new SafetyViolationException("Source deployment was not found.");
        var item = source.Results.SingleOrDefault(r => r.ControlId == control) ?? throw new SafetyViolationException("Recorded change was not found.");
        if (!evidence.HasAcceptedWrite(source, item) || !ProfileValidator.IsGuid(item.ObjectId ?? ""))
            throw new SafetyViolationException("Recovery needs a confirmed write and its exact returned object ID. Unknown writes require manual reconciliation.");
        var definition = standard.FindCollection(item.Collection) ?? throw new SafetyViolationException("Unknown policy collection.");
        if (!definition.Writable || !RecoverySafety.Supports(definition.ApiVersion, definition.BasePath))
            throw new SafetyViolationException("Automated recovery is not supported for this object type.");
        var mappings = evidence.LoadMappings(tenant);
        var mapping = mappings.Find(item.ControlId);
        if (mapping is null || mapping.ObjectId != item.ObjectId || mapping.Collection != item.Collection)
            throw new SafetyViolationException("Current ownership does not match this recorded object. Recovery cannot adopt an object by name.");
        var origin = runs.Any(record => record.Results.Any(r => r.ObjectId == item.ObjectId && r.Collection == item.Collection
            && r.PlannedAction == nameof(PlanAction.Create) && evidence.HasAcceptedWrite(record, r)));
        if (!origin) throw new SafetyViolationException("No confirmed toolkit creation record establishes ownership.");
        if (action == RecoveryAction.DeleteCreatedObject && item.PlannedAction != nameof(PlanAction.Create))
            throw new SafetyViolationException("Select the original creation record to delete this object.");
        if (action == RecoveryAction.RestoreUpdate && (item.PlannedAction != nameof(PlanAction.Update) || mapping.RunId != source.Id))
            throw new SafetyViolationException("Only the latest recorded update can be restored. Undo later changes first.");
        if (action == RecoveryAction.DisableConditionalAccess && !ConditionalAccessSafety.IsConditionalAccess(definition))
            throw new SafetyViolationException("Containment is supported only for Conditional Access policies.");
        return (source, item, definition, mappings);
    }

    private static JsonObject RestorePayload(RunResult item, CollectionDefinition definition)
    {
        if (item.BeforeObject is null || item.WrittenPayload is null || item.BeforeMapping?.LastApplied is null)
            throw new SafetyViolationException("This historical update has no complete recovery values. Automatic restoration is unavailable.");
        var payload = new JsonObject();
        foreach (var key in item.WrittenPayload.Select(p => p.Key))
        {
            if (key == "@odata.type") { payload[key] = item.WrittenPayload[key]?.DeepClone(); continue; }
            if (key.Contains("@") || key == RecoveryObjectReader.AssignmentsKey || key is "id" or "assignments")
                throw new SafetyViolationException("The recorded update contains unsupported restoration fields.");
            if (!item.BeforeObject.ContainsKey(key)) throw new SafetyViolationException($"No before-value exists for '{key}'; missing is not the same as null. Restore manually.");
            payload[key] = item.BeforeObject[key]?.DeepClone();
        }
        if (!RecoveryObjectReader.IsInactive(definition, item.BeforeObject)) throw new SafetyViolationException("Restoration cannot reactivate or reassign a policy.");
        RecoverySafety.AssertPayload(definition.BasePath, RecoveryAction.RestoreUpdate, payload, definition.ApiVersion);
        return payload;
    }

    private static void AssertSession(IGraphClient graph, TenantSession session)
    {
        if (graph.Mode != SessionMode.Deployment || session.Mode != SessionMode.Deployment) throw new WriteDeniedException("Recovery requires deployment access.");
        if (!session.TenantVerified || !session.OperatorVerified || !ProfileValidator.IsGuid(session.OperatorObjectId ?? "") || !ProfileValidator.IsGuid(session.ClientId)
            || !string.Equals(graph.TenantId, session.TenantId, StringComparison.OrdinalIgnoreCase)) throw new TenantMismatchException("Recovery requires the verified tenant, engineer and application.");
    }

    private void AssertFresh(string timestamp, TimeSpan maximum)
    {
        if (!Timestamps.TryParse(timestamp, out var time) || time > clock.UtcNow || clock.UtcNow - time > maximum)
            throw new SafetyViolationException("Recovery evidence expired. Capture and review fresh evidence.");
    }

    private static T Copy<T>(T value) => ToolkitJson.Deserialize<T>(ToolkitJson.Serialize(value));
}
