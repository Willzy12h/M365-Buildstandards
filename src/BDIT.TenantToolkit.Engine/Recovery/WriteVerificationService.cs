using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Evidence;

namespace BDIT.TenantToolkit.Engine.Recovery;

/// <summary>Re-reads confirmed writes. Never sends a mutation or resolves modern unknown acceptance by searching.</summary>
public sealed class WriteVerificationService(EvidenceStore evidence, IClock clock)
{
    public async Task<WriteVerification> VerifyDeploymentAsync(IGraphClient graph, TenantSession session, StandardCatalogue standard,
        string runId, string controlId, bool acknowledgeHistorical, CancellationToken ct)
    {
        AssertSession(graph, session);
        using var lease = evidence.AcquireTenantWriteLease(session.TenantId);
        var run = evidence.RequireIntactRuns(session.TenantId).SingleOrDefault(r => r.Id == runId)
            ?? throw new SafetyViolationException("Deployment evidence not found.");
        var item = run.Results.Single(r => r.ControlId == controlId);
        var historical = EvidenceStore.IsHistoricalCandidate(run, item);
        if (run.Status == RunStatus.Running || (item.WriteAcceptance != WriteAcceptance.Accepted && !(historical && acknowledgeHistorical))
            || !ProfileValidator.IsGuid(item.ObjectId ?? ""))
            throw new SafetyViolationException("Re-verification requires an accepted write with an exact ID, or explicit acknowledgement of a completed 1.0.0 record. Unknown modern writes are not resolved by reads.");
        var definition = Definition(standard, item.Collection);
        var mappings = evidence.LoadMappings(session.TenantId);
        var mapping = mappings.Find(controlId);
        if (mapping is null || mapping.RunId != run.Id || mapping.ObjectId != item.ObjectId || mapping.Collection != item.Collection)
            throw new SafetyViolationException("The latest ownership mapping does not match this recorded write. No object is adopted or older update re-verified.");
        var payload = item.WrittenPayload ?? (historical ? mapping.LastApplied : null)
            ?? throw new SafetyViolationException("The exact written settings are unavailable.");
        if (CanonicalJson.Sha256(payload) != item.PayloadDigest || CanonicalJson.Sha256(mapping.LastApplied) != item.PayloadDigest
            || mapping.LastAppliedDigest != item.PayloadDigest)
            throw new SafetyViolationException("Recorded payload and ownership digests do not agree.");
        var record = Record(session, "Deployment", run.Id, run.IntegrityDigest, controlId, item.ObjectId!, mappings);
        record.HistoricalAcknowledged = historical && acknowledgeHistorical;
        return await ObserveAsync(record, ct, async token =>
        {
            record.ObservedObject = await RecoveryObjectReader.ReadAsync(graph, definition, item.ObjectId!, token);
            if (!CanonicalJson.IsSubset(record.ObservedObject, payload) || !RecoveryObjectReader.IsInactive(definition, record.ObservedObject))
                throw new SafetyViolationException("Current settings or inactive/unassigned state do not match the recorded write.");
            AssertMappingsUnchanged(record);
            record.MappingsAfterDigest = record.MappingsBeforeDigest;
        });
    }

    public async Task<WriteVerification> VerifyRecoveryAsync(IGraphClient graph, TenantSession session, StandardCatalogue standard,
        string recoveryId, CancellationToken ct)
    {
        AssertSession(graph, session);
        using var lease = evidence.AcquireTenantWriteLease(session.TenantId);
        var run = evidence.LoadRecoveryRuns(session.TenantId).SingleOrDefault(r => r.Id == recoveryId)
            ?? throw new SafetyViolationException("Recovery evidence not found.");
        if (run.Status == RunStatus.Running || run.WriteAcceptance != WriteAcceptance.Accepted)
            throw new SafetyViolationException("Only accepted recovery writes can be re-verified. Unknown requests cannot be retried or resolved by a missing search result.");
        var plan = evidence.RequireRecoveryPlan(session.TenantId, run.Id);
        if (!StandardDigestCompatibility.MatchesForReadOnlyVerification(standard, plan.StandardDigest))
            throw new SafetyViolationException("Load the original reviewed standard before re-verifying this recovery.");
        if (run.PlanDigest != plan.IntegrityDigest || run.ObjectId != plan.ObjectId || run.Action != plan.Action || run.ControlId != plan.ControlId)
            throw new IntegrityException("Recovery run and original preview do not agree.");
        var source = evidence.RequireIntactRuns(session.TenantId).Single(r => r.Id == plan.SourceRunId);
        if (source.IntegrityDigest != plan.SourceRunDigest) throw new IntegrityException("Recovery source evidence changed.");
        var item = source.Results.Single(r => r.ControlId == plan.ControlId);
        if (item.ObjectId != run.ObjectId || !evidence.HasAcceptedWrite(source, item)) throw new IntegrityException("Recovery source ID or acceptance is not established.");
        var definition = Definition(standard, plan.Collection);
        RecoverySafety.AssertPayload(definition.BasePath, plan.Action, plan.Payload, definition.ApiVersion);
        var mappings = evidence.LoadMappings(session.TenantId);
        var mapping = mappings.Find(plan.ControlId);
        if (mapping is null && plan.Action != RecoveryAction.DeleteCreatedObject)
            throw new SafetyViolationException("Recovery ownership is missing.");
        if (mapping is not null && (mapping.ObjectId != plan.ObjectId || mapping.Collection != plan.Collection))
            throw new SafetyViolationException("Another object owns this control. Re-verification will not overwrite its mapping.");
        if (mapping is not null && plan.Action != RecoveryAction.RestoreUpdate && mapping.RunId != source.Id
            && CanonicalJson.Sha256Value(mappings) != plan.MappingsDigest)
            throw new SafetyViolationException("A later write owns this object. Re-verification will not remove or reconcile its mapping.");
        if (plan.Action == RecoveryAction.RestoreUpdate && (item.BeforeMapping is null
            || (mapping!.RunId != source.Id && CanonicalJson.Sha256Value(mapping) != CanonicalJson.Sha256Value(item.BeforeMapping))))
            throw new SafetyViolationException("A later mapping prevents restoration reconciliation.");
        var record = Record(session, "Recovery", run.Id, run.IntegrityDigest, run.ControlId, run.ObjectId, mappings);
        return await ObserveAsync(record, ct, async token =>
        {
            if (plan.Action == RecoveryAction.DeleteCreatedObject)
            {
                // A child/assignments 404 must never stand in for the object's own 404.
                try { record.ObservedObject = await graph.GetAsync(definition.ApiVersion, definition.BasePath + "/" + plan.ObjectId, token); }
                catch (GraphRequestException ex) when (ex.StatusCode == 404) { record.ObjectAbsent = true; }
                if (!record.ObjectAbsent) throw new SafetyViolationException("The object is still returned by Graph. No DELETE was repeated.");
                var objects = await graph.GetAllAsync(definition.ApiVersion, definition.Path, token);
                if (objects.Any(o => string.Equals(o["id"]?.GetValue<string>(), plan.ObjectId, StringComparison.OrdinalIgnoreCase)))
                    throw new SafetyViolationException("The collection still contains the object. Verification remains incomplete.");
            }
            else
            {
                record.ObservedObject = await RecoveryObjectReader.ReadAsync(graph, definition, plan.ObjectId, token);
                if (!CanonicalJson.IsSubset(record.ObservedObject, plan.Payload!) || !RecoveryObjectReader.IsInactive(definition, record.ObservedObject))
                    throw new SafetyViolationException("The requested recovery settings are not yet confirmed.");
            }
            AssertMappingsUnchanged(record);
            // Persist successful read evidence before updating only this control's local ownership entry.
            record.Detail = "Current configuration verified; local mapping finalisation pending.";
            evidence.SaveVerification(record);
            if (plan.Action == RecoveryAction.DeleteCreatedObject && mapping is not null) mappings.ByControl.Remove(plan.ControlId);
            else if (plan.Action == RecoveryAction.RestoreUpdate) mappings.ByControl[plan.ControlId] = Copy(item.BeforeMapping!);
            if (plan.Action != RecoveryAction.DisableConditionalAccess) evidence.SaveMappings(mappings);
            record.MappingsAfterDigest = CanonicalJson.Sha256Value(evidence.LoadMappings(session.TenantId));
        });
    }

    private async Task<WriteVerification> ObserveAsync(WriteVerification record, CancellationToken ct, Func<CancellationToken, Task> observe)
    {
        evidence.SaveVerification(record);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await observe(budget.Token);
            record.Verified = true;
            record.Detail = "Fresh configuration verification passed; no tenant write was sent. Original write evidence is unchanged.";
        }
        catch (Exception ex)
        {
            record.Verified = false;
            record.Detail = ex is OperationCanceledException ? "Re-verification cancelled or its 60-second read budget expired. No tenant write was sent."
                : SensitiveDataScrubber.Scrub(ex.Message);
        }
        evidence.SaveVerification(record);
        return record;
    }

    private WriteVerification Record(TenantSession session, string kind, string runId, string digest, string control, string objectId, ManagedObjectMappings mappings) => new()
    {
        TenantId = session.TenantId, SourceKind = kind, SourceRunId = runId, SourceDigest = digest, ControlId = control, ObjectId = objectId,
        At = Timestamps.Format(clock.UtcNow), MappingsBeforeDigest = CanonicalJson.Sha256Value(mappings), Detail = "Read-only verification started.",
        Actor = new RunActor { Account = session.Account, ObjectId = session.OperatorObjectId!, ClientId = session.ClientId, AuthenticationType = session.AuthenticationType }
    };
    private void AssertMappingsUnchanged(WriteVerification record)
    {
        if (CanonicalJson.Sha256Value(evidence.LoadMappings(record.TenantId)) != record.MappingsBeforeDigest)
            throw new SafetyViolationException("Ownership changed during the read. Re-verification did not finalise.");
    }
    private static CollectionDefinition Definition(StandardCatalogue standard, string collection)
    {
        var definition = standard.FindCollection(collection) ?? throw new SafetyViolationException("Unknown collection.");
        if (!definition.Writable || !RecoverySafety.Supports(definition.ApiVersion, definition.BasePath))
            throw new SafetyViolationException("Re-verification is limited to supported policy collections.");
        return definition;
    }
    private static void AssertSession(IGraphClient graph, TenantSession session)
    {
        if (!session.TenantVerified || !session.OperatorVerified || !ProfileValidator.IsGuid(session.OperatorObjectId ?? "")
            || !ProfileValidator.IsGuid(session.ClientId) || !string.Equals(graph.TenantId, session.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("Re-verification requires the verified tenant and signed-in engineer.");
    }
    private static T Copy<T>(T value) => ToolkitJson.Deserialize<T>(ToolkitJson.Serialize(value));
}
