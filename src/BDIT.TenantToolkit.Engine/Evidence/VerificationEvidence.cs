using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Evidence;

public sealed partial class EvidenceStore
{
    public void SaveVerification(WriteVerification record)
    {
        record.IntegrityDigest = RecoveryDigest(record);
        WriteJsonAtomic(Path.Combine(TenantDirectory(record.TenantId), "verification", SafeId(record.Id) + ".json"), record);
    }

    public IReadOnlyList<WriteVerification> LoadVerifications(string tenantId)
    {
        var directory = Path.Combine(TenantDirectory(tenantId), "verification");
        if (!Directory.Exists(directory)) return Array.Empty<WriteVerification>();
        return Directory.EnumerateFiles(directory, "*.json").Select(file =>
        {
            var item = ReadJson<WriteVerification>(file) ?? throw new IntegrityException("Empty verification evidence.");
            AssertTenant(tenantId, item.TenantId, "Verification evidence");
            if (Path.GetFileNameWithoutExtension(file) != item.Id || RecoveryDigest(item) != item.IntegrityDigest)
                throw new IntegrityException("Verification evidence was renamed or modified.");
            return item;
        }).OrderByDescending(v => v.At, StringComparer.Ordinal).ToList();
    }

    public bool HasVerification(string tenantId, string kind, string runId, string digest, string controlId) =>
        LoadVerifications(tenantId).Any(v => v.Verified && v.SourceKind == kind && v.SourceRunId == runId
            && v.SourceDigest == digest && v.ControlId == controlId);

    public static bool IsHistoricalCandidate(DeploymentRun run, RunResult item) => run.ToolkitVersion == "1.0.0"
        && item.RecordedWriteAcceptance is null && item.Status == ResultStatus.Completed
        && item.PlannedAction is nameof(PlanAction.Create) or nameof(PlanAction.Update)
        && ProfileValidator.IsGuid(item.ObjectId ?? "");

    public bool HasAcceptedWrite(DeploymentRun run, RunResult item) => item.WriteAcceptance == WriteAcceptance.Accepted
        || (IsHistoricalCandidate(run, item) && LoadVerifications(run.TenantId).Any(v => v.Verified && v.HistoricalAcknowledged
            && v.SourceKind == "Deployment" && v.SourceRunId == run.Id && v.SourceDigest == run.IntegrityDigest && v.ControlId == item.ControlId));
}
