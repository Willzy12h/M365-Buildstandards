using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
namespace BuildStandard.Engine.Evidence;
public sealed partial class EvidenceStore
{
    /// <summary>Exact source-bound original. Never snapshot/before-evidence, approval or current effective access.</summary>
    public string SaveOperationalContext(byte[] original, string tenant, string id)
    {
        var admitted = OperationalContextFile.Read(original, tenant);
        if (!ReportValues.IsCanonicalGuid(id) || id != admitted.Report.Id)
            throw new IntegrityException("The operational record identity differs from its destination.");
        var bytes = admitted.CopyOriginal();
        var file = Path.Combine(TenantDirectory(tenant), "operational-context-evidence", id + ".json");
        lock (_gate)
        {
            ReportEnvelopeDispatcher.RefuseLinks(file);
            CreateRecordFile(file, admitted.Report, "operational context report", bytes);
            OperationalContextFile.VerifyOriginalExport(file, bytes);
        }
        return file;
    }
}
