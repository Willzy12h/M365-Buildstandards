using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Evidence;

public sealed partial class EvidenceStore
{
    /// <summary>Serialises policy deployment and recovery across processes sharing this evidence root.</summary>
    public IDisposable AcquireTenantWriteLease(string tenantId)
    {
        var directory = TenantDirectory(tenantId);
        Directory.CreateDirectory(directory);
        try { return new FileStream(Path.Combine(directory, "policy-write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new SafetyViolationException("Another deployment or recovery is using this tenant's evidence store. Wait for it to finish. " + ex.Message); }
    }

    public IReadOnlyList<DeploymentRun> RequireIntactRuns(string tenantId)
    {
        var dir = RunsDirectory(tenantId);
        if (!Directory.Exists(dir)) return Array.Empty<DeploymentRun>();
        return Directory.EnumerateFiles(dir, "*.json").Select(file =>
        {
            var run = ReadJson<DeploymentRun>(file) ?? throw new ConfigurationException("Empty deployment evidence.");
            AssertTenant(tenantId, run.TenantId, "Deployment evidence");
            if (!EvidenceIntegrity.Verify(run, run.IntegrityDigest)) throw new SafetyViolationException("Deployment evidence was modified; recovery is blocked.");
            return run;
        }).OrderByDescending(r => r.StartedAt, StringComparer.Ordinal).ToList();
    }

    private string RecoveryDirectory(string tenantId) => Path.Combine(TenantDirectory(tenantId), "recovery");
    private string RecoveryPlanFile(string tenantId, string id) => Path.Combine(RecoveryDirectory(tenantId), "plan-" + SafeId(id) + ".json");
    private string RecoveryRunFile(string tenantId, string id) => Path.Combine(RecoveryDirectory(tenantId), "run-" + SafeId(id) + ".json");

    public void SaveRecoveryPlan(RecoveryPlan plan)
    {
        plan.IntegrityDigest = RecoveryDigest(plan);
        var file = RecoveryPlanFile(plan.TenantId, plan.Id);
        if (File.Exists(file)) throw new SafetyViolationException("A recovery preview cannot be replaced.");
        WriteJsonAtomic(file, plan);
    }

    public RecoveryPlan RequireRecoveryPlan(string tenantId, string id)
    {
        var plan = ReadJson<RecoveryPlan>(RecoveryPlanFile(tenantId, id)) ?? throw new SafetyViolationException("Recovery preview is not durably saved.");
        AssertTenant(tenantId, plan.TenantId, "Recovery plan");
        if (plan.Id != id || plan.IntegrityDigest != RecoveryDigest(plan)) throw new SafetyViolationException("Recovery preview integrity failed.");
        return plan;
    }

    public void SaveRecoveryRun(RecoveryRun run)
    {
        run.IntegrityDigest = RecoveryDigest(run);
        WriteJsonAtomic(RecoveryRunFile(run.TenantId, run.Id), run);
    }

    public bool RecoveryAttemptExists(string tenantId, string planId) => File.Exists(RecoveryRunFile(tenantId, planId));

    public IReadOnlyList<RecoveryRun> LoadRecoveryRuns(string tenantId)
    {
        var dir = RecoveryDirectory(tenantId);
        if (!Directory.Exists(dir)) return Array.Empty<RecoveryRun>();
        return Directory.EnumerateFiles(dir, "run-*.json").Select(file =>
        {
            var run = ReadJson<RecoveryRun>(file) ?? throw new ConfigurationException("Empty recovery evidence.");
            AssertTenant(tenantId, run.TenantId, "Recovery run");
            if (run.IntegrityDigest != RecoveryDigest(run)) throw new SafetyViolationException("Recovery evidence integrity failed.");
            return run;
        }).OrderByDescending(r => r.StartedAt, StringComparer.Ordinal).ToList();
    }

    public void AssertNoUnresolvedRecovery(string tenantId, IEnumerable<string> controls)
    {
        var selected = controls.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (LoadRecoveryRuns(tenantId).Any(r => selected.Contains(r.ControlId)
            && (r.WriteAcceptance == WriteAcceptance.Unknown || (r.WriteAcceptance == WriteAcceptance.Accepted
                && (r.Verification != ConfigurationVerification.Pass || r.Status != RunStatus.Completed)
                && !HasVerification(tenantId, "Recovery", r.Id, r.IntegrityDigest, r.ControlId)))))
            throw new SafetyViolationException("An earlier recovery write needs reconciliation. No retry or new deployment is permitted for these controls.");
    }

    public static string RecoveryDigest<T>(T value)
    {
        var node = ToolkitJson.ParseObject(ToolkitJson.Serialize(value));
        node.Remove("integrityDigest");
        return CanonicalJson.Sha256(node);
    }
}
