using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Recovery;
using BDIT.TenantToolkit.Engine.Standards;
namespace BDIT.TenantToolkit.Engine.Execution;

/// <summary>Publishes encrypted Content Prep Tool packages into recorded unassigned Win32 app candidates.</summary>
public sealed class ApplicationPackageService(EvidenceStore evidence, IClock clock)
{
    public async Task<PackagePublishPlan> PreviewAsync(IGraphClient graph, TenantSession session, StandardCatalogue standard, TenantSnapshot snapshot,
        string controlId, string packagePath, CancellationToken ct)
    {
        Session(graph, session);
        using var lease = evidence.AcquireTenantWriteLease(session.TenantId);
        var saved = evidence.RequireDeploymentSnapshot(snapshot, standard);
        if (saved.TenantId != session.TenantId) throw new TenantMismatchException("Snapshot belongs to another tenant.");
        Fresh(saved.CapturedAt);
        var maps = evidence.LoadMappings(session.TenantId);
        var m = maps.Find(controlId) ?? throw new SafetyViolationException("Create and verify the unassigned Win32 app candidate first.");
        evidence.AssertNoUnknownPackageWrite(session.TenantId, m.ObjectId);
        evidence.AssertNoUnresolvedReviewedChanges(session.TenantId);
        evidence.AssertNoUnresolvedRecovery(session.TenantId, new[] { controlId });
        if (!evidence.RequireIntactRuns(session.TenantId).Any(run => run.Results.Any(r => r.ControlId == controlId && r.ObjectId == m.ObjectId && r.PlannedAction == nameof(PlanAction.Create) && evidence.HasAcceptedWrite(run, r))))
            throw new SafetyViolationException("Package publishing requires a confirmed toolkit app creation record.");
        using var package = await Task.Run(() => new IntuneWinPackage(packagePath), ct);
        using var reads = CancellationTokenSource.CreateLinkedTokenSource(ct); reads.CancelAfter(TimeSpan.FromSeconds(60));
        var def = standard.FindCollection(m.Collection) ?? throw new SafetyViolationException("App collection is missing.");
        if (def.BasePath != Win32ContentSafety.Root || def.ApiVersion != GraphApi.Beta) throw new SafetyViolationException("Use the application's beta collection for Win32 publishing.");
        var before = await RecoveryObjectReader.ReadAsync(graph, def, m.ObjectId, reads.Token);
        if (before["@odata.type"]?.ToString() != "#microsoft.graph.win32LobApp" || !RecoveryObjectReader.IsInactive(def, before)
            || m.LastApplied is null || !CanonicalJson.IsSubset(before, m.LastApplied)) throw new SafetyViolationException("App is assigned, drifted, or not a recorded Win32 candidate.");
        if (before["committedContentVersion"]?.ToString() is string version && version.Length > 0 && version != "0")
            throw new SafetyViolationException("This app already has committed content. Content replacement needs a separately reviewed upgrade workflow.");
        if (before["fileName"]?.ToString() != package.InstallerName) throw new SafetyViolationException("Package installer name differs from the reviewed app fileName.");
        var p = new PackagePublishPlan { TenantId = session.TenantId, OperatorId = session.OperatorObjectId!, ClientId = session.ClientId,
            ControlId = controlId, ObjectId = m.ObjectId, StandardDigest = CanonicalJson.Sha256Value(standard), MappingsDigest = CanonicalJson.Sha256Value(maps),
            SnapshotId = saved.Id, SnapshotDigest = saved.IntegrityDigest, PackageSha256 = package.Sha256, InstallerName = package.InstallerName,
            EncryptedBytes = package.EncryptedBytes, UnencryptedBytes = package.UnencryptedBytes, CreatedAt = clock.UtcNow, Before = before };
        evidence.SavePackagePlan(p); return p;
    }
    public async Task<PackagePublishRun> ExecuteAsync(IGraphClient graph, TenantSession session, StandardCatalogue standard, string planId,
        string approvedDigest, string typedTenant, string packagePath, CancellationToken ct)
    {
        Session(graph, session);
        if (session.Mode != SessionMode.Deployment || graph.Mode != SessionMode.Deployment) throw new WriteDeniedException("Package upload needs deployment access.");
        using var lease = evidence.AcquireTenantWriteLease(session.TenantId);
        var p = evidence.RequirePackagePlan(session.TenantId, planId);
        // The mappings are read once: the copy whose digest is checked here is the copy used below, so a change between
        // the check and the lookup cannot slip past either.
        var mappings = evidence.LoadMappings(p.TenantId);
        if (p.IntegrityDigest != approvedDigest || p.OperatorId != session.OperatorObjectId || p.ClientId != session.ClientId
            || !TenantConfirmation.Matches(typedTenant, p.TenantId)
            || p.StandardDigest != CanonicalJson.Sha256Value(standard) || p.MappingsDigest != CanonicalJson.Sha256Value(mappings)
            || p.CreatedAt > clock.UtcNow || clock.UtcNow - p.CreatedAt > TimeSpan.FromMinutes(10)) throw new SafetyViolationException("Package preview, approval or relevant inputs changed.");
        var snapshot = evidence.LoadSnapshot(p.TenantId, p.SnapshotId) ?? throw new SafetyViolationException("Package before-evidence is missing.");
        evidence.RequireDeploymentSnapshot(snapshot, standard); Fresh(snapshot.CapturedAt);
        if (snapshot.IntegrityDigest != p.SnapshotDigest) throw new SafetyViolationException("Package snapshot integrity changed.");
        var runs = evidence.LoadPackageRuns(p.TenantId);
        if (runs.Any(r => r.Id == p.Id)) throw new SafetyViolationException("Package previews are single-use.");
        evidence.AssertNoUnknownPackageWrite(p.TenantId, p.ObjectId);
        evidence.AssertNoUnresolvedReviewedChanges(p.TenantId);
        evidence.AssertNoUnresolvedRecovery(p.TenantId, new[] { p.ControlId });
        using var package = await Task.Run(() => new IntuneWinPackage(packagePath), ct);
        if (package.Sha256 != p.PackageSha256) throw new SafetyViolationException("Package file changed after preview.");
        var mapping = mappings.Find(p.ControlId) ?? throw new SafetyViolationException("The app mapping is missing.");
        if (!string.Equals(mapping.ObjectId, p.ObjectId, StringComparison.OrdinalIgnoreCase)) throw new SafetyViolationException("Ownership changed after preview.");
        var def = standard.FindCollection(mapping.Collection) ?? throw new SafetyViolationException("App collection is missing.");
        using var preflight = CancellationTokenSource.CreateLinkedTokenSource(ct); preflight.CancelAfter(TimeSpan.FromSeconds(60));
        var current = await RecoveryObjectReader.ReadAsync(graph, def, p.ObjectId, preflight.Token);
        if (CanonicalJson.Sha256(current) != CanonicalJson.Sha256(p.Before)) throw new SafetyViolationException("App changed after preview.");
        var run = new PackagePublishRun { Id = p.Id, TenantId = p.TenantId, ControlId = p.ControlId, ObjectId = p.ObjectId, PlanDigest = p.IntegrityDigest, StartedAt = clock.UtcNow };
        evidence.SavePackageRun(run);
        try
        {
            var version = await Write(Win32ContentAction.CreateVersion, new JsonObject());
            run.VersionId = version["id"]?.ToString();
            if (string.IsNullOrEmpty(run.VersionId) || !run.VersionId.All(char.IsAsciiDigit)) throw new ToolkitException("Graph did not return a content version ID.");
            evidence.SavePackageRun(run);
            var file = await Write(Win32ContentAction.CreateFile, new JsonObject { ["name"] = package.ContentName, ["size"] = package.UnencryptedBytes, ["sizeEncrypted"] = package.EncryptedBytes, ["isDependency"] = false });
            run.FileId = file["id"]?.ToString();
            if (!ProfileValidator.IsGuid(run.FileId)) throw new ToolkitException("Graph did not return a content file ID.");
            evidence.SavePackageRun(run);
            var ready = await WaitFile("azureStorageUriRequestSuccess");
            if (!Uri.TryCreate(ready["azureStorageUri"]?.ToString(), UriKind.Absolute, out var uri)) throw new ToolkitException("Graph did not return a package upload URI.");
            using (var content = package.OpenEncryptedContent())
                await graph.UploadEncryptedPackageAsync(uri, content, package.EncryptedBytes, ct);
            var commit = package.CommitPayload();
            try { await Write(Win32ContentAction.CommitFile, commit); }
            finally { commit.Clear(); }
            await WaitFile("commitFileSuccess");
            using (var beforePublish = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                beforePublish.CancelAfter(TimeSpan.FromSeconds(60));
                var app = await RecoveryObjectReader.ReadAsync(graph, def, p.ObjectId, beforePublish.Token);
                if (!RecoveryObjectReader.IsInactive(def, app) || mapping.LastApplied is null || !CanonicalJson.IsSubset(app, mapping.LastApplied))
                    throw new SafetyViolationException("App was assigned or its reviewed metadata changed during upload. Content is not published.");
            }
            await Write(Win32ContentAction.PublishVersion, new JsonObject { ["committedContentVersion"] = run.VersionId });
            using var readback = CancellationTokenSource.CreateLinkedTokenSource(ct); readback.CancelAfter(TimeSpan.FromSeconds(60));
            var after = await RecoveryObjectReader.ReadAsync(graph, def, p.ObjectId, readback.Token);
            run.After = after;
            run.Verification = after["committedContentVersion"]?.ToString() == run.VersionId && after["publishingState"]?.ToString() == "published"
                && RecoveryObjectReader.IsInactive(def, after) ? ConfigurationVerification.Pass : ConfigurationVerification.Unknown;
            run.Status = run.Verification == ConfigurationVerification.Pass ? RunStatus.Completed : RunStatus.ReviewRequired;
        }
        catch (Exception ex)
        {
            run.Status = RunStatus.ReviewRequired;
            // Do not persist Graph error bodies, signed storage URIs or encryption material.
            run.Error = "Package publishing stopped (" + ex.GetType().Name + "). Review recorded stage acceptance and IDs. No failed write is automatically replayed; encryption and storage credentials are never saved.";
        }
        finally { run.EndedAt = clock.UtcNow; EvidencePersistence.SaveTerminal(() => evidence.SavePackageRun(run), message => { run.Status = RunStatus.ReviewRequired; run.Error = EvidencePersistence.Append(run.Error, message); }); }
        return run;
        async Task<JsonObject> Write(Win32ContentAction action, JsonObject body)
        {
            ct.ThrowIfCancellationRequested();
            var step = new PackagePublishStep { Action = action.ToString() }; run.Steps.Add(step); evidence.SavePackageRun(run);
            try
            {
                var result = await graph.WriteWin32ContentAsync(p.ObjectId, action == Win32ContentAction.CreateVersion ? null : run.VersionId,
                    action == Win32ContentAction.CommitFile ? run.FileId : null, action, body, ct);
                step.Acceptance = WriteAcceptance.Accepted; step.ObjectId = result["id"]?.ToString(); evidence.SavePackageRun(run);
                return result;
            }
            catch (Exception ex)
            {
                if (ex is WriteNotSentException or WriteDeniedException or AuthenticationRequiredException) step.Acceptance = WriteAcceptance.NotAttempted;
                else if (ex is GraphRequestException { StatusCode: >= 400 and < 500 } g && g.StatusCode != 408) step.Acceptance = WriteAcceptance.Rejected;
                evidence.SavePackageRun(run); throw;
            }
        }
        async Task<JsonObject> WaitFile(string expected)
        {
            using var reads = CancellationTokenSource.CreateLinkedTokenSource(ct); reads.CancelAfter(TimeSpan.FromMinutes(3));
            while (true)
            {
                var obj = await graph.GetAsync(GraphApi.Beta, FilePath(p.ObjectId, run.VersionId!, run.FileId!), reads.Token);
                var state = obj["uploadState"]?.ToString() ?? "";
                if (state == expected) return obj;
                if (state.Contains("Failed", StringComparison.OrdinalIgnoreCase) || state.Contains("Error", StringComparison.OrdinalIgnoreCase) || state.Contains("TimedOut", StringComparison.OrdinalIgnoreCase))
                    throw new ToolkitException("Intune content processing failed.");
                await Task.Delay(TimeSpan.FromSeconds(2), reads.Token);
            }
        }
    }
    public async Task<bool> ReverifyAsync(IGraphClient graph, TenantSession session, StandardCatalogue standard, string runId, CancellationToken ct)
    {
        Session(graph, session);
        using var lease = evidence.AcquireTenantWriteLease(session.TenantId);
        var run = evidence.LoadPackageRuns(session.TenantId).Single(r => r.Id == runId);
        var plan = evidence.RequirePackagePlan(session.TenantId, runId);
        if (run.PlanDigest != plan.IntegrityDigest || run.VersionId is null || !run.Steps.Any(s => s.Action == nameof(Win32ContentAction.PublishVersion) && s.Acceptance == WriteAcceptance.Accepted))
            throw new SafetyViolationException("Only an accepted final publication can be re-verified.");
        var mapping = evidence.LoadMappings(session.TenantId).Find(run.ControlId) ?? throw new SafetyViolationException("The app mapping is missing.");
        if (mapping.ObjectId != run.ObjectId) throw new SafetyViolationException("Ownership changed.");
        using var reads = CancellationTokenSource.CreateLinkedTokenSource(ct); reads.CancelAfter(TimeSpan.FromSeconds(60));
        var def = standard.FindCollection(mapping.Collection)!;
        var after = await RecoveryObjectReader.ReadAsync(graph, def, run.ObjectId, reads.Token);
        var pass = after["committedContentVersion"]?.ToString() == run.VersionId && after["publishingState"]?.ToString() == "published" && RecoveryObjectReader.IsInactive(def, after);
        evidence.SavePackageVerification(new PackageVerification { TenantId = session.TenantId, RunId = run.Id, RunDigest = run.IntegrityDigest,
            OperatorId = session.OperatorObjectId!, Verified = pass, VersionId = run.VersionId, CheckedAt = clock.UtcNow, After = after });
        return pass;
    }
    private static string FilePath(string app, string version, string file) => Win32ContentSafety.Root + "/" + app + "/microsoft.graph.win32LobApp/contentVersions/" + version + "/files/" + file;
    private static void Session(IGraphClient graph, TenantSession s)
    {
        if (graph.TenantId != s.TenantId || !s.TenantVerified || !s.OperatorVerified || !ProfileValidator.IsGuid(s.OperatorObjectId) || !ProfileValidator.IsGuid(s.ClientId))
            throw new TenantMismatchException("Package publishing needs a verified tenant, operator and application.");
    }
    private void Fresh(string timestamp)
    {
        if (!Timestamps.TryParse(timestamp, out var at) || at > clock.UtcNow || clock.UtcNow - at > TimeSpan.FromMinutes(20)) throw new SafetyViolationException("Capture a fresh complete snapshot.");
    }
}
