using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;

namespace BuildStandard.Engine.Evidence;

public sealed partial class EvidenceStore
{
    /// <summary>Schema 2 shares the immutable report-record location, not snapshots or deployment evidence.</summary>
    public string SaveApplicationReport(ApplicationReportEvidence report)
    {
        var json = ApplicationReportEvidenceSchema.Serialise(report);
        ApplicationReportEvidenceSchema.Read(json, report.TenantId);
        var file = ReportEvidenceFile(report.TenantId, report.Id);
        ReportEnvelopeDispatcher.RefuseLinks(file);
        CreateRecordFile(file, JsonNode.Parse(json)!, "report evidence"); return file;
    }
    public ApplicationReportEvidence? LoadApplicationReport(string tenantId, string reportId)
    {
        var file = ReportEvidenceFile(tenantId, reportId);
        if (!File.Exists(file)) return null;
        var envelope = ReportEnvelopeDispatcher.ReadFile(file, tenantId);
        var report = envelope.Schema2 ?? throw new ConfigurationException("Expected an application schema 2 report record.");
        if (report.Id != reportId) throw new IntegrityException("The application report file contains a different record ID.");
        return report;
    }
}
