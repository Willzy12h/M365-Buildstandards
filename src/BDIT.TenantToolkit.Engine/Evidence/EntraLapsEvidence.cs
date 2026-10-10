using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Evidence;

public sealed partial class EvidenceStore
{
    private string LapsDirectory(string tenantId) => Path.Combine(TenantDirectory(tenantId), "entra-laps");
    private string LapsFile(string tenantId, string kind, string id) => Path.Combine(LapsDirectory(tenantId), kind + "-" + SafeId(id) + ".json");

    public void SaveEntraLapsPlan(EntraLapsPlan plan)
    {
        var file = LapsFile(plan.TenantId, "plan", plan.Id);
        if (File.Exists(file)) throw new SafetyViolationException("LAPS previews cannot be replaced.");
        plan.IntegrityDigest = RecoveryDigest(plan);
        WriteJsonAtomic(file, plan);
    }

    public EntraLapsPlan RequireEntraLapsPlan(string tenantId, string id)
    {
        var plan = ReadJson<EntraLapsPlan>(LapsFile(tenantId, "plan", id)) ?? throw new SafetyViolationException("LAPS preview is not durably saved.");
        AssertTenant(tenantId, plan.TenantId, "LAPS preview");
        if (plan.Id != id || plan.IntegrityDigest != RecoveryDigest(plan)) throw new SafetyViolationException("LAPS preview integrity failed.");
        return plan;
    }

    public bool EntraLapsAttemptExists(string tenantId, string id) => File.Exists(LapsFile(tenantId, "run", id));

    public void SaveEntraLapsRun(EntraLapsRun run)
    {
        run.IntegrityDigest = RecoveryDigest(run);
        WriteJsonAtomic(LapsFile(run.TenantId, "run", run.Id), run);
    }

    public IReadOnlyList<EntraLapsRun> LoadEntraLapsRuns(string tenantId)
    {
        if (!Directory.Exists(LapsDirectory(tenantId))) return Array.Empty<EntraLapsRun>();
        return Directory.EnumerateFiles(LapsDirectory(tenantId), "run-*.json").Select(file =>
        {
            var run = ReadJson<EntraLapsRun>(file) ?? throw new SafetyViolationException("Empty LAPS run evidence.");
            AssertTenant(tenantId, run.TenantId, "LAPS run");
            if (run.IntegrityDigest != RecoveryDigest(run) || file != LapsFile(tenantId, "run", run.Id)) throw new SafetyViolationException("LAPS run integrity failed.");
            return run;
        }).OrderByDescending(r => r.StartedAt).ToList();
    }

    public void SaveEntraLapsVerification(EntraLapsVerification verification)
    {
        var file = LapsFile(verification.TenantId, "verification", verification.Id);
        if (File.Exists(file)) throw new SafetyViolationException("LAPS verification records cannot be replaced.");
        verification.IntegrityDigest = RecoveryDigest(verification);
        WriteJsonAtomic(file, verification);
    }

    public void AssertNoUnresolvedEntraLaps(string tenantId)
    {
        foreach (var run in LoadEntraLapsRuns(tenantId))
        {
            if (run.WriteAcceptance == WriteAcceptance.Unknown) throw new SafetyViolationException("A LAPS request has unknown acceptance. Reconcile it before any further LAPS write; never retry it.");
            if (run.WriteAcceptance != WriteAcceptance.Accepted || run.Verification == ConfigurationVerification.Pass) continue;
            var verified = Directory.EnumerateFiles(LapsDirectory(tenantId), "verification-*.json").Select(file =>
            {
                var v = ReadJson<EntraLapsVerification>(file) ?? throw new SafetyViolationException("Empty LAPS verification.");
                AssertTenant(tenantId, v.TenantId, "LAPS verification");
                if (v.IntegrityDigest != RecoveryDigest(v) || file != LapsFile(tenantId, "verification", v.Id)) throw new SafetyViolationException("LAPS verification integrity failed.");
                return v;
            }).Any(v => v.RunId == run.Id && v.RunDigest == run.IntegrityDigest && v.Verification == ConfigurationVerification.Pass);
            if (!verified) throw new SafetyViolationException("An accepted LAPS write needs read-only re-verification before another change.");
        }
    }
}
