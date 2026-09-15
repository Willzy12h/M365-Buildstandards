using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Recovery;
using BDIT.TenantToolkit.Engine.Assessment;

namespace BDIT.TenantToolkit.Engine.Execution;

/// <summary>Preview, approve, apply once and re-verify tenant settings and activation. No unreviewed assignments.</summary>
public sealed class ReviewedChangeService(EvidenceStore evidence, IClock clock)
{
    public async Task<ReviewedChangePlan> PreviewAsync(IGraphClient graph, TenantSession session, TenantProfile profile,
        StandardCatalogue standard, TenantSnapshot snapshot, ReviewedChangeKind kind, string controlId, string objectId,
        IEnumerable<string> included, IEnumerable<string> excluded, CancellationToken ct, IEnumerable<string>? deviceIds = null)
    {
        AssertSession(graph, session, false);
        using var lease = evidence.AcquireTenantWriteLease(session.TenantId);
        evidence.AssertNoUnresolvedReviewedChanges(session.TenantId);
        evidence.AssertNoUnresolvedRecovery(session.TenantId, new[] { controlId });
        var stored = evidence.RequireDeploymentSnapshot(snapshot, standard);
        if (stored.TenantId != session.TenantId || profile.TenantId != session.TenantId) throw new TenantMismatchException("Change evidence belongs to another tenant.");
        Fresh(stored.CapturedAt);
        var licences = LicenceEvaluator.FromSnapshot(stored);
        if (kind is ReviewedChangeKind.SecureCompliance or ReviewedChangeKind.MdmAll || ReviewedChangeSafety.IsUpdates(kind) || ReviewedChangeSafety.IsAssignment(kind))
        {
            if (!licences.Available || !licences.Has("INTUNE_A")) throw new SafetyViolationException("A provisioned Intune licence is required for this operation.");
        }
        var mappings = evidence.LoadMappings(session.TenantId);
        var p = new ReviewedChangePlan { TenantId = session.TenantId, OperatorId = session.OperatorObjectId!, ClientId = session.ClientId,
            StandardDigest = CanonicalJson.Sha256Value(standard), ProfileDigest = CanonicalJson.Sha256Value(profile),
            SnapshotId = stored.Id, SnapshotDigest = stored.IntegrityDigest, MappingsDigest = CanonicalJson.Sha256Value(mappings),
            CreatedAt = clock.UtcNow, Kind = kind, ControlId = controlId, ObjectId = objectId,
            IncludeGroups = included.Select(g => g.Trim().ToLowerInvariant()).ToList(), ExcludeGroups = excluded.Select(g => g.Trim().ToLowerInvariant()).ToList(),
            DeviceIds = (deviceIds ?? Enumerable.Empty<string>()).Select(g => g.Trim().ToLowerInvariant()).ToList() };
        using var reads = CancellationTokenSource.CreateLinkedTokenSource(ct); reads.CancelAfter(TimeSpan.FromSeconds(60));
        foreach (var id in p.IncludeGroups.Concat(p.ExcludeGroups))
        {
            if (!ProfileValidator.IsGuid(id)) throw new ConfigurationException("Use group object IDs from this tenant.");
            var group = await graph.GetAsync(GraphApi.V1, "/groups/" + id + "?$select=id,displayName,securityEnabled,groupTypes", reads.Token);
            if (group["id"]?.ToString() != id || group["securityEnabled"]?.ToString() != "true") throw new SafetyViolationException("Target is not a verified security group.");
            p.ResolvedTargets.Add((p.ExcludeGroups.Contains(id) ? "EXCLUDE " : "INCLUDE ") + group["displayName"] + " [" + id + "]");
        }
        if (ReviewedChangeSafety.IsUpdates(kind))
        {
            if (session.Mode != SessionMode.Deployment) throw new WriteDeniedException("Autopatch API reads also require WindowsUpdates.ReadWrite.All. Use deployment access for this preview.");
            if (!snapshot.Collections.TryGetValue("managedDevices", out var devices) || !devices.Usable) throw new SafetyViolationException("Capture Intune managed devices first.");
            foreach (var id in p.DeviceIds)
            {
                if (!ProfileValidator.IsGuid(id)) throw new SafetyViolationException("Invalid Entra device ID.");
                var device = devices.Items.SingleOrDefault(d => d["azureADDeviceId"]?.ToString() == id && d["operatingSystem"]?.ToString() == "Windows")
                    ?? throw new SafetyViolationException("A selected device is not a captured, Intune-managed Windows device.");
                p.ResolvedTargets.Add(device["deviceName"] + " [" + id + "] · Windows " + device["osVersion"]);
                if (ReviewedChangeSafety.IsUnenrol(kind) && !evidence.LoadReviewedChangeRuns(session.TenantId).Any(run =>
                    run.WriteAcceptance == WriteAcceptance.Accepted && EnrolledBy(run, id, kind)))
                    throw new SafetyViolationException("Unenrolment requires a toolkit record of enrolment in this update category.");
            }
            p.ControlId = "UPD-001"; p.Api = GraphApi.Beta; p.Method = "POST"; p.RequiredScope = "WindowsUpdates.ReadWrite.All";
            p.Path = ReviewedChangeSafety.UpdatesPath + (ReviewedChangeSafety.IsUnenrol(kind) ? "/unenrollAssets" : "/enrollAssets");
            p.Payload = ReviewedChangeSafety.UpdatePayload(p);
            p.Before = await ReadAsync(graph, standard, p, reads.Token);
            p.Consequence = "Change Windows Autopatch update-management authority for exactly these devices and this category. This does not create Autopatch groups, select an OS release, or confirm update delivery. Confirm Business Premium/A3/E3/F3 or another eligible licence per device, supported OS, diagnostic data and no conflicting update authority. Unenrolment does not uninstall updates.";
        }
        else if (ReviewedChangeSafety.IsObjectAction(kind))
        {
            var m = mappings.Find(controlId) ?? throw new SafetyViolationException("Select a toolkit-created control.");
            var def = standard.FindCollection(m.Collection) ?? throw new SafetyViolationException("Collection is unavailable.");
            RequireOwned(session.TenantId, m);
            evidence.AssertNoUnknownPackageWrite(session.TenantId, m.ObjectId);
            p.ObjectId = m.ObjectId; p.Collection = m.Collection; p.Api = def.ApiVersion;
            p.Path = def.BasePath + "/" + p.ObjectId;
            p.RequiredScope = def.Write ?? throw new SafetyViolationException("This standard does not allow changes to the collection.");
            p.Before = await RecoveryObjectReader.ReadAsync(graph, def, p.ObjectId, reads.Token);
            if (kind is not (ReviewedChangeKind.RemoveAssignments or ReviewedChangeKind.DisableConditionalAccess))
            {
                if (!RecoveryObjectReader.IsInactive(def, p.Before)) throw new SafetyViolationException("Activation requires a disabled or unassigned candidate. Remove existing assignments separately.");
                var check = (JsonObject)p.Before.DeepClone();
                if (def.Children is not null) check["settings"] = check[def.Children.Split('?')[0]]?.DeepClone();
                if (m.LastApplied is null || !CanonicalJson.IsSubset(check, m.LastApplied)) throw new SafetyViolationException("Candidate drifted from recorded settings. Reconcile before activation.");
                if (ConditionalAccessSafety.IsConditionalAccess(def))
                {
                    var required = profile.Parameters.EmergencyAccountIds.Concat(profile.Parameters.AdditionalExclusionAccountIds).Append(session.OperatorObjectId!);
                    if (profile.Parameters.EmergencyAccountIds.Count < 2 || required.Any(id => !ConditionalAccessSafety.ExcludedUsers(p.Before).Contains(id, StringComparer.OrdinalIgnoreCase)))
                        throw new SafetyViolationException("Activation requires two emergency accounts and the operator retained in user exclusions.");
                }
                if (def.BasePath == "/deviceAppManagement/mobileApps" && p.Before["publishingState"]?.ToString() != "published")
                    throw new SafetyViolationException("Application content has not finished publishing.");
            }
            if (ReviewedChangeSafety.IsAssignment(kind))
            {
                if (!def.Assignments) throw new SafetyViolationException("Assignments were not collected for this object.");
                p.Path += "/assign"; p.Method = "POST";
                p.Payload = ReviewedChangeSafety.AssignmentPayload(def.BasePath, p.IncludeGroups, p.ExcludeGroups);
                p.Consequence = kind == ReviewedChangeKind.RemoveAssignments
                    ? "Remove ALL current assignments from this toolkit-created object. Device settings may persist; this is containment, not device rollback."
                    : "Replace the empty assignment list with exactly these groups. Group membership is dynamic and can change later. Exclusions apply only according to Intune targeting rules; review user versus device group use. Required app installs may begin.";
            }
            else
            {
                p.Payload = new JsonObject { ["state"] = kind == ReviewedChangeKind.EnableConditionalAccess ? "enabled" : kind == ReviewedChangeKind.ReportOnlyConditionalAccess ? "enabledForReportingButNotEnforced" : "disabled" };
                p.Consequence = "Change only the Conditional Access state. Stored users, applications, locations, exclusions and session controls remain effective. Enabled policies can block sign-ins immediately. Report-only evaluates without enforcing.";
            }
            var control = standard.FindControl(controlId);
            if (control?.Dependencies.Count > 0) p.Consequence += " Confirm prerequisites: " + string.Join(", ", control.Dependencies) + ".";
        }
        else
        {
            ConfigureTenantRoute(p);
            p.Before = await ReadAsync(graph, standard, p, reads.Token);
            switch (kind)
            {
                case ReviewedChangeKind.SecureCompliance:
                    var settings = p.Before["settings"] as JsonObject ?? throw new SafetyViolationException("Compliance settings are missing.");
                    var updated = (JsonObject)settings.DeepClone(); updated["secureByDefault"] = true;
                    p.Payload = new JsonObject { ["settings"] = updated };
                    p.Consequence = "Mark devices without a compliance policy noncompliant. Existing Conditional Access can immediately deny access. Other tenant compliance settings are preserved."; break;
                case ReviewedChangeKind.MdmAll:
                    p.Payload = new JsonObject { ["appliesTo"] = "all" };
                    p.Consequence = "Automatic Intune enrolment scope becomes All. Existing selected groups are removed by Microsoft Graph. Devices still require applicable licensing and enrolment eligibility. WIP/MAM scope is unchanged."; break;
                case ReviewedChangeKind.ConfigureTap:
                    if (p.IncludeGroups.Count != 1) throw new SafetyViolationException("Select exactly one TAP onboarding group.");
                    p.Payload = new JsonObject { ["@odata.type"] = "#microsoft.graph.temporaryAccessPassAuthenticationMethodConfiguration", ["state"] = "enabled",
                        ["defaultLength"] = 16, ["defaultLifetimeInMinutes"] = 60, ["minimumLifetimeInMinutes"] = 10, ["maximumLifetimeInMinutes"] = 60, ["isUsableOnce"] = true,
                        ["includeTargets"] = new JsonArray(new JsonObject { ["id"] = p.IncludeGroups[0], ["targetType"] = "group" }) };
                    p.Consequence = "Enable one-use Temporary Access Pass for the selected group, replacing existing included targets and preserving excluded targets. Lifetime is at most one hour. No pass or secret is issued."; break;
                default:
                    p.Payload = new JsonObject { ["@odata.type"] = p.Before["@odata.type"]?.DeepClone(), ["state"] = kind == ReviewedChangeKind.EnableAuthenticator ? "enabled" : "disabled" };
                    p.Consequence = kind == ReviewedChangeKind.EnableAuthenticator ? "Enable Microsoft Authenticator for the existing method targets. Registration and target coverage must be checked; number matching is Microsoft-enforced."
                        : "Disable this authentication method tenant-wide. Users relying on it can lose access. Confirm alternative registered methods and emergency access before approving."; break;
            }
        }
        ReviewedChangeSafety.Assert(p);
        evidence.SaveReviewedChangePlan(p);
        return p;
    }

    public async Task<ReviewedChangeRun> ExecuteAsync(IGraphClient graph, TenantSession session, TenantProfile profile, StandardCatalogue standard,
        string planId, string approvedDigest, string typedTenant, CancellationToken ct)
    {
        AssertSession(graph, session, true);
        using var lease = evidence.AcquireTenantWriteLease(session.TenantId);
        var p = evidence.RequireReviewedChangePlan(session.TenantId, planId);
        if (typedTenant.Trim() != session.TenantId || approvedDigest != p.IntegrityDigest || p.OperatorId != session.OperatorObjectId || p.ClientId != session.ClientId
            || p.ProfileDigest != CanonicalJson.Sha256Value(profile) || p.StandardDigest != CanonicalJson.Sha256Value(standard)
            || p.MappingsDigest != CanonicalJson.Sha256Value(evidence.LoadMappings(session.TenantId)))
            throw new SafetyViolationException("Approval, tenant, operator or relevant inputs changed. Review again.");
        if (clock.UtcNow < p.CreatedAt || clock.UtcNow - p.CreatedAt > TimeSpan.FromMinutes(10)) throw new SafetyViolationException("Preview expired.");
        var snapshot = evidence.LoadSnapshot(session.TenantId, p.SnapshotId) ?? throw new SafetyViolationException("Before-snapshot is missing.");
        evidence.RequireDeploymentSnapshot(snapshot, standard);
        if (snapshot.IntegrityDigest != p.SnapshotDigest) throw new SafetyViolationException("Before-snapshot changed.");
        Fresh(snapshot.CapturedAt);
        evidence.AssertNoUnresolvedReviewedChanges(session.TenantId);
        evidence.AssertNoUnresolvedRecovery(session.TenantId, new[] { p.ControlId });
        if (evidence.ReviewedChangeAttemptExists(session.TenantId, p.Id)) throw new SafetyViolationException("Preview is single-use. Re-verify an accepted write or review a fresh plan.");
        ReviewedChangeSafety.Assert(p);
        var run = new ReviewedChangeRun { Id = p.Id, TenantId = p.TenantId, ControlId = p.ControlId, PlanDigest = p.IntegrityDigest, StartedAt = clock.UtcNow };
        evidence.SaveReviewedChangeRun(run);
        try
        {
            using var reads = CancellationTokenSource.CreateLinkedTokenSource(ct); reads.CancelAfter(TimeSpan.FromSeconds(60));
            var current = await ReadAsync(graph, standard, p, reads.Token);
            if (CanonicalJson.Sha256(current) != CanonicalJson.Sha256(p.Before)) throw new SafetyViolationException("Object changed since preview. No write was sent.");
            ct.ThrowIfCancellationRequested();
            run.WriteAcceptance = WriteAcceptance.Unknown; run.Verification = ConfigurationVerification.Unknown;
            evidence.SaveReviewedChangeRun(run);
            await graph.ApplyReviewedChangeAsync(p, ct);
            run.WriteAcceptance = WriteAcceptance.Accepted; evidence.SaveReviewedChangeRun(run);
            using var after = CancellationTokenSource.CreateLinkedTokenSource(ct); after.CancelAfter(TimeSpan.FromSeconds(60));
            run.After = await ReadAsync(graph, standard, p, after.Token);
            run.Verification = Matches(p, run.After) ? ConfigurationVerification.Pass : ConfigurationVerification.Unknown;
            run.Status = run.Verification == ConfigurationVerification.Pass ? RunStatus.Completed : RunStatus.ReviewRequired;
            if (run.Verification != ConfigurationVerification.Pass) run.Error = "Write accepted; current configuration is not confirmed. Use read-only re-verification.";
        }
        catch (Exception ex)
        {
            if (run.WriteAcceptance != WriteAcceptance.Accepted)
            {
                if (ex is WriteNotSentException or WriteDeniedException or AuthenticationRequiredException) run.WriteAcceptance = WriteAcceptance.NotAttempted;
                else if (run.WriteAcceptance == WriteAcceptance.Unknown && ex is GraphRequestException { StatusCode: >= 400 and < 500 } g && g.StatusCode != 408) run.WriteAcceptance = WriteAcceptance.Rejected;
            }
            if (run.WriteAcceptance is WriteAcceptance.NotAttempted or WriteAcceptance.Rejected) run.Verification = ConfigurationVerification.NotRun;
            run.Status = RunStatus.ReviewRequired; run.Error = SensitiveDataScrubber.Scrub(ex.Message);
        }
        finally { run.EndedAt = clock.UtcNow; evidence.SaveReviewedChangeRun(run); }
        return run;
    }

    public async Task<ReviewedChangeVerification> ReverifyAsync(IGraphClient graph, TenantSession session, StandardCatalogue standard, string runId, CancellationToken ct)
    {
        AssertSession(graph, session, false);
        using var lease = evidence.AcquireTenantWriteLease(session.TenantId);
        var run = evidence.LoadReviewedChangeRuns(session.TenantId).Single(r => r.Id == runId);
        var p = evidence.RequireReviewedChangePlan(session.TenantId, run.Id);
        if (run.PlanDigest != p.IntegrityDigest || run.WriteAcceptance != WriteAcceptance.Accepted || p.StandardDigest != CanonicalJson.Sha256Value(standard))
            throw new SafetyViolationException("Re-verification needs an accepted write and the original standard.");
        var v = new ReviewedChangeVerification { TenantId = session.TenantId, RunId = runId, RunDigest = run.IntegrityDigest, OperatorId = session.OperatorObjectId!, CheckedAt = clock.UtcNow };
        try { using var reads = CancellationTokenSource.CreateLinkedTokenSource(ct); reads.CancelAfter(TimeSpan.FromSeconds(60)); v.After = await ReadAsync(graph, standard, p, reads.Token); v.Verified = Matches(p, v.After); }
        catch (Exception ex) { v.Error = SensitiveDataScrubber.Scrub(ex.Message); }
        evidence.SaveReviewedChangeVerification(v); return v;
    }
    private static bool Matches(ReviewedChangePlan p, JsonObject after)
    {
        if (ReviewedChangeSafety.IsUpdates(p.Kind))
        {
            if (after["assets"] is not JsonArray assets || assets.Count != p.DeviceIds.Count) return false;
            return assets.OfType<JsonObject>().All(a => a["enrollments"] is JsonArray enrolments &&
                enrolments.OfType<JsonObject>().Any(e => e["updateCategory"]?.ToString() == ReviewedChangeSafety.UpdateCategory(p.Kind)) != ReviewedChangeSafety.IsUnenrol(p.Kind));
        }
        if (p.Kind == ReviewedChangeKind.MdmAll && after["_includedGroups"] is not JsonArray { Count: 0 }) return false;
        if (!ReviewedChangeSafety.IsAssignment(p.Kind)) return CanonicalJson.IsSubset(after, p.Payload);
        var actual = after[RecoveryObjectReader.AssignmentsKey] as JsonArray;
        if (actual is null) return false;
        var expected = p.Payload.First().Value as JsonArray;
        if (expected is null || actual.Count != expected.Count) return false;
        return expected.All(e => actual.Any(a => a is JsonObject o && e is JsonObject x && CanonicalJson.IsSubset(o, x)));
    }
    private static async Task<JsonObject> ReadAsync(IGraphClient graph, StandardCatalogue standard, ReviewedChangePlan p, CancellationToken ct)
    {
        if (ReviewedChangeSafety.IsUpdates(p.Kind))
        {
            var assets = new JsonArray();
            foreach (var id in p.DeviceIds)
            {
                try { assets.Add(await graph.GetAsync(GraphApi.Beta, ReviewedChangeSafety.UpdatesPath + "/" + id, ct)); }
                catch (GraphRequestException ex) when (ex.StatusCode == 404) { assets.Add(new JsonObject { ["id"] = id, ["enrollments"] = new JsonArray(), ["absent"] = true }); }
            }
            return new JsonObject { ["assets"] = assets };
        }
        if (ReviewedChangeSafety.IsObjectAction(p.Kind)) return await RecoveryObjectReader.ReadAsync(graph, standard.FindCollection(p.Collection)!, p.ObjectId, ct);
        var obj = await graph.GetAsync(p.Api, p.Path, ct); obj.Remove("@odata.context"); obj.Remove("@odata.etag");
        if (p.Kind == ReviewedChangeKind.MdmAll)
            obj["_includedGroups"] = new JsonArray((await graph.GetAllAsync(p.Api, p.Path + "/includedGroups", ct)).Select(g => (JsonNode?)g.DeepClone()).ToArray());
        return obj;
    }
    private void RequireOwned(string tenant, ManagedObjectMapping mapping)
    {
        var runs = evidence.RequireIntactRuns(tenant);
        if (!runs.Any(run => run.Results.Any(r => r.ObjectId == mapping.ObjectId && r.Collection == mapping.Collection && r.PlannedAction == nameof(PlanAction.Create) && evidence.HasAcceptedWrite(run, r))))
            throw new SafetyViolationException("A confirmed toolkit creation record is required; names do not establish ownership.");
        if (runs.Any(run => run.Results.Any(r => r.ControlId == mapping.ControlId && r.WriteAcceptance == WriteAcceptance.Unknown)))
            throw new SafetyViolationException("Resolve unknown deployment writes before activation.");
    }
    private bool EnrolledBy(ReviewedChangeRun run, string id, ReviewedChangeKind kind)
    {
        var old = evidence.RequireReviewedChangePlan(run.TenantId, run.Id);
        return old.IntegrityDigest == run.PlanDigest && ReviewedChangeSafety.IsUpdates(old.Kind) && !ReviewedChangeSafety.IsUnenrol(old.Kind)
            && ReviewedChangeSafety.UpdateCategory(old.Kind) == ReviewedChangeSafety.UpdateCategory(kind) && old.DeviceIds.Contains(id);
    }
    private static void ConfigureTenantRoute(ReviewedChangePlan p)
    {
        p.ControlId = p.Kind == ReviewedChangeKind.SecureCompliance ? "CMP-001" : p.Kind == ReviewedChangeKind.MdmAll ? "ENR-001" : "ID-002";
        p.RequiredScope = "Policy.ReadWrite.AuthenticationMethod";
        p.Path = ReviewedChangeSafety.AuthenticationPath + (p.Kind switch { ReviewedChangeKind.DisableSms => "Sms", ReviewedChangeKind.DisableVoice => "Voice",
            ReviewedChangeKind.ConfigureTap => "TemporaryAccessPass", ReviewedChangeKind.EnableAuthenticator => "MicrosoftAuthenticator", _ => "" });
        if (p.Kind == ReviewedChangeKind.SecureCompliance) { p.Path = "/deviceManagement"; p.RequiredScope = "DeviceManagementConfiguration.ReadWrite.All"; }
        if (p.Kind == ReviewedChangeKind.MdmAll) { p.Api = GraphApi.Beta; p.Path = "/policies/mobileDeviceManagementPolicies/" + p.ObjectId; p.RequiredScope = "Policy.ReadWrite.MobilityManagement"; }
    }
    private static void AssertSession(IGraphClient graph, TenantSession s, bool write)
    {
        if (graph.TenantId != s.TenantId || !s.TenantVerified || !s.OperatorVerified || !ProfileValidator.IsGuid(s.OperatorObjectId) || !ProfileValidator.IsGuid(s.ClientId))
            throw new TenantMismatchException("Verified tenant, operator and application are required.");
        if (write && (graph.Mode != SessionMode.Deployment || s.Mode != SessionMode.Deployment)) throw new WriteDeniedException("Deployment access is required.");
    }
    private void Fresh(string timestamp)
    {
        if (!Timestamps.TryParse(timestamp, out var at) || at > clock.UtcNow || clock.UtcNow - at > TimeSpan.FromMinutes(20)) throw new SafetyViolationException("Capture a fresh complete snapshot.");
    }
}
