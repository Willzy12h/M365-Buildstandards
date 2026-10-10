using System.Text;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Reports;

namespace BDIT.TenantToolkit.Engine.Evidence;

public sealed partial class EvidenceStore
{
    /// <summary>Exchange report records live apart from Graph reports, snapshots and the configuration Exchange capture.</summary>
    public const string ExchangeReportEvidenceFolder = "exchange-report-evidence";

    private string ExchangeReportEvidenceFile(string tenantId, string reportId)
    {
        if (!Guid.TryParseExact(reportId, "D", out var id) || id == Guid.Empty) throw new ConfigurationException("Invalid Exchange report evidence ID.");
        return Path.Combine(TenantDirectory(tenantId), ExchangeReportEvidenceFolder, id.ToString("D") + ".json");
    }

    /// <summary>Validates, then creates the record once. An existing ID is never replaced.</summary>
    public string SaveExchangeReport(ExchangeReportEvidence report)
    {
        var json = ExchangeReportEvidenceSchema.Serialize(report);
        ExchangeReportEvidenceSchema.Read(json, report.TenantId);
        var file = ExchangeReportEvidenceFile(report.TenantId, report.Id);
        // Preserve required explicit null fields when using the shared atomic writer.
        CreateRecordFile(file, System.Text.Json.Nodes.JsonNode.Parse(json)!, "Exchange report evidence");
        return file;
    }

    public ExchangeReportEvidence? LoadExchangeReport(string tenantId, string reportId)
    {
        var file = ExchangeReportEvidenceFile(tenantId, reportId);
        if (!File.Exists(file)) return null;
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > ExchangeReportEvidenceSchema.MaximumBytes) throw new ConfigurationException("Exchange report evidence exceeds its reader limit.");
        using var reader = new BinaryReader(stream);
        var bytes = reader.ReadBytes(ExchangeReportEvidenceSchema.MaximumBytes + 1);
        if (bytes.Length > ExchangeReportEvidenceSchema.MaximumBytes) throw new ConfigurationException("Exchange report evidence exceeds its reader limit.");
        string json;
        try { json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿'); }
        catch (DecoderFallbackException) { throw new ConfigurationException("Exchange report evidence is not valid UTF-8."); }
        var report = ExchangeReportEvidenceSchema.Read(json, tenantId);
        if (!string.Equals(report.Id, reportId, StringComparison.OrdinalIgnoreCase)) throw new IntegrityException("The report file contains a different record ID.");
        return report;
    }
}
