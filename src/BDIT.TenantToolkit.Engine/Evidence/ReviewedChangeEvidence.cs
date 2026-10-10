using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Evidence;

public sealed partial class EvidenceStore
{
    private string ChangeDirectory(string tenant) => Path.Combine(TenantDirectory(tenant), "reviewed-changes");
    private string ChangeFile(string tenant, string kind, string id) => Path.Combine(ChangeDirectory(tenant), kind + "-" + SafeId(id) + ".json");
    public void SaveReviewedChangePlan(ReviewedChangePlan plan)
    {
        var file = ChangeFile(plan.TenantId, "plan", plan.Id);
        if (File.Exists(file)) throw new SafetyViolationException("A reviewed change preview cannot be replaced.");
        plan.IntegrityDigest = RecoveryDigest(plan); WriteJsonAtomic(file, plan);
    }
    public ReviewedChangePlan RequireReviewedChangePlan(string tenant, string id)
    {
        var p = ReadJson<ReviewedChangePlan>(ChangeFile(tenant, "plan", id)) ?? throw new SafetyViolationException("Reviewed change preview is missing.");
        AssertTenant(tenant, p.TenantId, "Reviewed change");
        if (p.Id != id || p.IntegrityDigest != RecoveryDigest(p)) throw new SafetyViolationException("Reviewed change integrity failed.");
        return p;
    }
    public void SaveReviewedChangeRun(ReviewedChangeRun run)
    {
        run.IntegrityDigest = RecoveryDigest(run); WriteJsonAtomic(ChangeFile(run.TenantId, "run", run.Id), run);
    }
    public bool ReviewedChangeAttemptExists(string tenant, string id) => File.Exists(ChangeFile(tenant, "run", id));
    public IReadOnlyList<ReviewedChangeRun> LoadReviewedChangeRuns(string tenant)
    {
        if (!Directory.Exists(ChangeDirectory(tenant))) return Array.Empty<ReviewedChangeRun>();
        return Directory.EnumerateFiles(ChangeDirectory(tenant), "run-*.json").Select(f =>
        {
            var v = ReadJson<ReviewedChangeRun>(f) ?? throw new SafetyViolationException("Empty reviewed change evidence.");
            AssertTenant(tenant, v.TenantId, "Reviewed change run");
            if (v.IntegrityDigest != RecoveryDigest(v) || f != ChangeFile(tenant, "run", v.Id)) throw new SafetyViolationException("Reviewed change run integrity failed.");
            return v;
        }).OrderByDescending(r => r.StartedAt).ToList();
    }
    public void SaveReviewedChangeVerification(ReviewedChangeVerification v)
    {
        var file = ChangeFile(v.TenantId, "verification", v.Id);
        if (File.Exists(file)) throw new SafetyViolationException("Verification records cannot be replaced.");
        v.IntegrityDigest = RecoveryDigest(v); WriteJsonAtomic(file, v);
    }
    public void AssertNoUnresolvedReviewedChanges(string tenant, IEnumerable<string>? controls = null)
    {
        var selected = controls?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var run in LoadReviewedChangeRuns(tenant))
        {
            if (selected is not null && !selected.Contains(run.ControlId)) continue;
            if (run.WriteAcceptance == WriteAcceptance.Unknown) throw new SafetyViolationException("A reviewed tenant change has unknown acceptance. Reconcile before further writes.");
            if (run.WriteAcceptance != WriteAcceptance.Accepted || run.Verification == ConfigurationVerification.Pass) continue;
            var verified = Directory.EnumerateFiles(ChangeDirectory(tenant), "verification-*.json").Select(f =>
            {
                var v = ReadJson<ReviewedChangeVerification>(f) ?? throw new SafetyViolationException("Empty reviewed change verification.");
                AssertTenant(tenant, v.TenantId, "Reviewed change verification");
                if (v.IntegrityDigest != RecoveryDigest(v) || f != ChangeFile(tenant, "verification", v.Id)) throw new SafetyViolationException("Verification integrity failed.");
                return v;
            }).Any(v => v.RunId == run.Id && v.RunDigest == run.IntegrityDigest && v.Verified);
            if (!verified) throw new SafetyViolationException("An accepted tenant change needs read-only re-verification.");
        }
    }
}
