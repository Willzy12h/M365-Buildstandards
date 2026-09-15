using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Evidence;

namespace BDIT.TenantToolkit.Engine.Prerequisites;

/// <summary>UI-independent preview, approved enablement and read-only verification of the tenant LAPS switch.</summary>
public sealed class EntraLapsService(EvidenceStore evidence, IClock clock)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(60);

    public async Task<EntraLapsPlan> PreviewAsync(IGraphClient graph, TenantSession session, StandardCatalogue standard,
        TenantSnapshot snapshot, CancellationToken ct)
    {
        AssertSession(graph, session, false);
        using var lease = evidence.AcquireTenantWriteLease(session.TenantId);
        evidence.AssertNoUnresolvedEntraLaps(session.TenantId);
        if (snapshot.TenantId != session.TenantId) throw new TenantMismatchException("LAPS snapshot belongs to another tenant.");
        var stored = evidence.RequireDeploymentSnapshot(snapshot, standard);
        AssertSnapshotAge(stored);
        var def = standard.Collections.SingleOrDefault(c => c.Value.ApiVersion == GraphApi.V1 && c.Value.BasePath == EntraLapsSafety.Path);
        if (def.Value is null || !def.Value.Singleton || def.Value.Write != EntraLapsSafety.WriteScope)
            throw new SafetyViolationException("This standard does not declare the LAPS prerequisite.");
        var captured = stored.Collections[def.Key].Items.SingleOrDefault() ?? throw new SafetyViolationException("LAPS evidence must contain exactly one device registration policy.");
        using var reads = CancellationTokenSource.CreateLinkedTokenSource(ct); reads.CancelAfter(ReadBudget);
        var before = await graph.GetAsync(GraphApi.V1, EntraLapsSafety.Path, reads.Token);
        if (!EntraLapsSafety.SameState(captured, before)) throw new SafetyViolationException("Device registration changed after capture. Capture again before reviewing LAPS.");
        var plan = new EntraLapsPlan
        {
            TenantId = session.TenantId, OperatorId = session.OperatorObjectId!, ClientId = session.ClientId,
            StandardDigest = CanonicalJson.Sha256Value(standard), SnapshotId = stored.Id, SnapshotDigest = stored.IntegrityDigest,
            CreatedAt = clock.UtcNow, Before = before, Payload = EntraLapsSafety.EnablePayload(before), AlreadyEnabled = EntraLapsSafety.IsEnabled(before)
        };
        evidence.SaveEntraLapsPlan(plan);
        return plan;
    }

    /// <param name="approvedDigest">The exact preview digest approved by the engineer after reviewing consequences.</param>
    public async Task<EntraLapsRun> ExecuteAsync(IGraphClient graph, TenantSession session, StandardCatalogue standard,
        string planId, string approvedDigest, CancellationToken ct)
    {
        AssertSession(graph, session, true);
        using var lease = evidence.AcquireTenantWriteLease(session.TenantId);
        var plan = evidence.RequireEntraLapsPlan(session.TenantId, planId);
        if (string.IsNullOrEmpty(approvedDigest) || plan.IntegrityDigest != approvedDigest
            || plan.OperatorId != session.OperatorObjectId || plan.ClientId != session.ClientId
            || plan.StandardDigest != CanonicalJson.Sha256Value(standard)) throw new SafetyViolationException("LAPS approval, operator, application or standard changed. Review a new preview.");
        AssertAge(plan.CreatedAt, "LAPS preview");
        var snapshot = evidence.LoadSnapshot(session.TenantId, plan.SnapshotId) ?? throw new SafetyViolationException("LAPS before-evidence is missing.");
        evidence.RequireDeploymentSnapshot(snapshot, standard);
        if (snapshot.IntegrityDigest != plan.SnapshotDigest) throw new SafetyViolationException("LAPS before-evidence changed.");
        AssertSnapshotAge(snapshot);
        if (evidence.EntraLapsAttemptExists(session.TenantId, plan.Id)) throw new SafetyViolationException("LAPS previews are single-use. Review a fresh preview.");
        evidence.AssertNoUnresolvedEntraLaps(session.TenantId);
        var run = new EntraLapsRun { Id = plan.Id, TenantId = plan.TenantId, PlanDigest = plan.IntegrityDigest, StartedAt = clock.UtcNow };
        evidence.SaveEntraLapsRun(run);
        try
        {
            using var preflight = CancellationTokenSource.CreateLinkedTokenSource(ct); preflight.CancelAfter(ReadBudget);
            var current = await graph.GetAsync(GraphApi.V1, EntraLapsSafety.Path, preflight.Token);
            if (!EntraLapsSafety.SameState(plan.Before, current)) throw new SafetyViolationException("Device registration drifted after preview; no write sent.");
            ct.ThrowIfCancellationRequested();
            if (!plan.AlreadyEnabled)
            {
                run.WriteAcceptance = WriteAcceptance.Unknown;
                run.Verification = ConfigurationVerification.Unknown;
                evidence.SaveEntraLapsRun(run); // Durable intent before transport; a crash remains ambiguous.
                await graph.EnableEntraLapsAsync(plan.Before, preflight.Token);
                run.WriteAcceptance = WriteAcceptance.Accepted;
                evidence.SaveEntraLapsRun(run);
            }
            using var readback = CancellationTokenSource.CreateLinkedTokenSource(ct); readback.CancelAfter(ReadBudget);
            run.After = await graph.GetAsync(GraphApi.V1, EntraLapsSafety.Path, readback.Token);
            run.Verification = EntraLapsSafety.SameState(plan.Payload, run.After) ? ConfigurationVerification.Pass : ConfigurationVerification.Unknown;
            run.Status = run.Verification == ConfigurationVerification.Pass ? RunStatus.Completed : RunStatus.ReviewRequired;
            if (run.Verification != ConfigurationVerification.Pass) run.Error = "LAPS or preserved registration settings did not match readback. Re-verify without repeating the write.";
        }
        catch (Exception ex)
        {
            if (run.WriteAcceptance != WriteAcceptance.Accepted)
            {
                if (ex is WriteNotSentException)
                    run.WriteAcceptance = WriteAcceptance.NotAttempted;
                else if (ex is GraphRequestException g && g.StatusCode is >= 400 and < 500 && g.StatusCode != 408)
                    run.WriteAcceptance = run.WriteAcceptance == WriteAcceptance.Unknown ? WriteAcceptance.Rejected : WriteAcceptance.NotAttempted;
            }
            if (run.WriteAcceptance is WriteAcceptance.NotAttempted or WriteAcceptance.Rejected) run.Verification = ConfigurationVerification.NotRun;
            run.Status = RunStatus.ReviewRequired;
            run.Error = SensitiveDataScrubber.Scrub(ex.Message);
        }
        finally { run.EndedAt = clock.UtcNow; evidence.SaveEntraLapsRun(run); }
        return run;
    }

    public async Task<EntraLapsVerification> ReverifyAsync(IGraphClient graph, TenantSession session, string runId, CancellationToken ct)
    {
        AssertSession(graph, session, false);
        using var lease = evidence.AcquireTenantWriteLease(session.TenantId);
        var run = evidence.LoadEntraLapsRuns(session.TenantId).Single(r => r.Id == runId);
        if (run.WriteAcceptance != WriteAcceptance.Accepted) throw new SafetyViolationException("Read-only LAPS re-verification requires confirmed write acceptance.");
        var plan = evidence.RequireEntraLapsPlan(session.TenantId, run.Id);
        if (run.PlanDigest != plan.IntegrityDigest) throw new SafetyViolationException("LAPS plan binding failed.");
        var result = new EntraLapsVerification { TenantId = session.TenantId, RunId = run.Id, RunDigest = run.IntegrityDigest, OperatorId = session.OperatorObjectId!, CheckedAt = clock.UtcNow };
        try
        {
            using var reads = CancellationTokenSource.CreateLinkedTokenSource(ct); reads.CancelAfter(ReadBudget);
            result.After = await graph.GetAsync(GraphApi.V1, EntraLapsSafety.Path, reads.Token);
            result.Verification = EntraLapsSafety.SameState(plan.Payload, result.After) ? ConfigurationVerification.Pass : ConfigurationVerification.Unknown;
        }
        catch (Exception ex) { result.Error = SensitiveDataScrubber.Scrub(ex.Message); }
        evidence.SaveEntraLapsVerification(result);
        return result;
    }

    private static void AssertSession(IGraphClient graph, TenantSession session, bool write)
    {
        if (graph.TenantId != session.TenantId || !session.TenantVerified || !session.OperatorVerified
            || !ProfileValidator.IsGuid(session.TenantId) || !ProfileValidator.IsGuid(session.OperatorObjectId) || !ProfileValidator.IsGuid(session.ClientId))
            throw new TenantMismatchException("LAPS requires a verified tenant, operator and application.");
        if (write && (graph.Mode != SessionMode.Deployment || session.Mode != SessionMode.Deployment)) throw new WriteDeniedException("LAPS enablement requires deployment access.");
    }

    private void AssertAge(DateTimeOffset at, string label)
    {
        if (at > clock.UtcNow || clock.UtcNow - at > MaxAge) throw new SafetyViolationException(label + " is stale. Capture and review again.");
    }

    private void AssertSnapshotAge(TenantSnapshot snapshot)
    {
        if (!Timestamps.TryParse(snapshot.CapturedAt, out var at)) throw new SafetyViolationException("Invalid snapshot timestamp.");
        AssertAge(at, "Snapshot");
    }
}
