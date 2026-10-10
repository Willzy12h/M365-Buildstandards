using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
namespace BDIT.TenantToolkit.Engine.Evidence;

public sealed partial class EvidenceStore
{
    private string PackageDirectory(string tenant) => Path.Combine(TenantDirectory(tenant), "packages");
    private string PackageFile(string tenant, string kind, string id) => Path.Combine(PackageDirectory(tenant), kind + "-" + SafeId(id) + ".json");
    public void SavePackagePlan(PackagePublishPlan p)
    {
        var file = PackageFile(p.TenantId, "plan", p.Id);
        if (File.Exists(file)) throw new SafetyViolationException("Package previews cannot be replaced.");
        p.IntegrityDigest = RecoveryDigest(p); WriteJsonAtomic(file, p);
    }
    public PackagePublishPlan RequirePackagePlan(string tenant, string id)
    {
        var p = ReadJson<PackagePublishPlan>(PackageFile(tenant, "plan", id)) ?? throw new SafetyViolationException("Package preview is missing.");
        AssertTenant(tenant, p.TenantId, "Package plan");
        if (p.Id != id || p.IntegrityDigest != RecoveryDigest(p)) throw new SafetyViolationException("Package preview integrity failed.");
        return p;
    }
    public void SavePackageRun(PackagePublishRun p)
    {
        p.IntegrityDigest = RecoveryDigest(p); WriteJsonAtomic(PackageFile(p.TenantId, "run", p.Id), p);
    }
    public IReadOnlyList<PackagePublishRun> LoadPackageRuns(string tenant)
    {
        if (!Directory.Exists(PackageDirectory(tenant))) return Array.Empty<PackagePublishRun>();
        return Directory.EnumerateFiles(PackageDirectory(tenant), "run-*.json").Select(file =>
        {
            var p = ReadJson<PackagePublishRun>(file) ?? throw new SafetyViolationException("Package evidence is empty.");
            AssertTenant(tenant, p.TenantId, "Package run");
            if (p.IntegrityDigest != RecoveryDigest(p) || file != PackageFile(tenant, "run", p.Id)) throw new SafetyViolationException("Package evidence integrity failed.");
            return p;
        }).OrderByDescending(p => p.StartedAt).ToList();
    }
    public void AssertNoUnknownPackageWrite(string tenant, string objectId)
    {
        if (LoadPackageRuns(tenant).Any(r => r.ObjectId == objectId && r.Steps.Any(s => s.Acceptance == WriteAcceptance.Unknown)))
            throw new SafetyViolationException("A package operation has unknown acceptance. Do not retry or activate this app. Reconcile the recorded app/content IDs first.");
    }
    public void SavePackageVerification(PackageVerification verification)
    {
        var file = PackageFile(verification.TenantId, "verification", verification.Id);
        if (File.Exists(file)) throw new SafetyViolationException("Package verification records cannot be replaced.");
        verification.IntegrityDigest = RecoveryDigest(verification);
        WriteJsonAtomic(file, verification);
    }
}
