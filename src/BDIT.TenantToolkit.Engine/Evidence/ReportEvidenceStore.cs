using System.Text;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Reports;

namespace BDIT.TenantToolkit.Engine.Evidence;

public sealed partial class EvidenceStore
{
    private string ReportEvidenceFile(string tenantId, string reportId)
    {
        if (!Guid.TryParseExact(reportId, "D", out var id) || id == Guid.Empty) throw new ConfigurationException("Invalid report evidence ID.");
        return Path.Combine(TenantDirectory(tenantId), "report-evidence", id.ToString("D") + ".json");
    }

    /// <summary>Reuse the existing durable immutable-record writer, separately from snapshots/assessments/plans.</summary>
    public string SaveReport(ReportEvidence report)
    {
        var json = ReportEvidenceSchema.Serialize(report);
        ReportEvidenceSchema.Read(json, report.TenantId);
        var file = ReportEvidenceFile(report.TenantId, report.Id);
        // Preserve required explicit null fields when using the shared atomic writer.
        CreateRecordFile(file, System.Text.Json.Nodes.JsonNode.Parse(json)!, "report evidence");
        return file;
    }

    public ReportEvidence? LoadReport(string tenantId, string reportId)
    {
        var file = ReportEvidenceFile(tenantId, reportId);
        if (!File.Exists(file)) return null;
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > ReportEvidenceSchema.MaximumBytes) throw new ConfigurationException("Report evidence exceeds its reader limit.");
        using var reader = new BinaryReader(stream);
        var bytes = reader.ReadBytes(ReportEvidenceSchema.MaximumBytes + 1);
        if (bytes.Length > ReportEvidenceSchema.MaximumBytes) throw new ConfigurationException("Report evidence exceeds its reader limit.");
        var report = ReportEvidenceSchema.Read(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'), tenantId);
        if (!string.Equals(report.Id, reportId, StringComparison.OrdinalIgnoreCase)) throw new IntegrityException("The report file contains a different record ID.");
        return report;
    }
}
