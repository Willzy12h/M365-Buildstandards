using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>Explicit schema/ID routing; schema 1 entry points continue to refuse application-report schema 2.</summary>
public static class ReportEnvelopeDispatcher
{
    public sealed class Envelope
    {
        internal Envelope(ReportEvidence legacy) { Schema1 = legacy; }
        internal Envelope(ApplicationReportEvidence application) { Schema2 = application; }
        public ReportEvidence? Schema1 { get; }
        public ApplicationReportEvidence? Schema2 { get; }
        public int SchemaVersion => Schema1 is not null ? 1 : 2;
        public string Export(ReportExporter exporter, ExportFormat format, bool suppliedFile = false) => Schema1 is { } legacy
            ? exporter.ExportReport(legacy, format, suppliedFile) : exporter.ExportApplicationReport(Schema2!, format, suppliedFile);
    }
    /// <summary>Bounded UTF-8 file admission for stored/supplied envelopes; never reseals or changes source claims.</summary>
    public static Envelope ReadFile(string file, string expectedTenantId)
    {
        RefuseLinks(file);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > ReportEvidenceSchema.MaximumBytes) throw new ConfigurationException("Report evidence exceeds its 32 MiB reader limit.");
        using var reader = new BinaryReader(stream);
        var bytes = reader.ReadBytes(ReportEvidenceSchema.MaximumBytes + 1);
        if (bytes.Length > ReportEvidenceSchema.MaximumBytes) throw new ConfigurationException("Report evidence exceeds its 32 MiB reader limit.");
        string json;
        try { json = new System.Text.UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (System.Text.DecoderFallbackException) { throw new ConfigurationException("Report evidence is not valid UTF-8."); }
        return Read(json, expectedTenantId);
    }
    internal static void RefuseLinks(string file)
    {
        var full = Path.GetFullPath(file);
        if (File.Exists(full) && File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint)) throw new ConfigurationException("Report evidence links are refused.");
        var parent = Directory.GetParent(full);
        while (parent is not null)
        {
            if (parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new ConfigurationException("Report evidence parent links are refused.");
            parent = parent.Parent;
        }
    }
    public static Envelope Read(string json, string expectedTenantId)
    {
        using var document = RegisteredReportJson.Parse(json);
        var (schema, id) = RegisteredReportJson.Inspect(document.RootElement);
        if (schema == 1) { GraphReportRegistry.Find(id); return new(ReportEvidenceSchema.Read(json, expectedTenantId)); }
        if (schema == 2) { ApplicationReportRegistry.Find(schema, id); return new(ApplicationReportEvidenceSchema.Read(json, expectedTenantId)); }
        throw new ConfigurationException("Unsupported Graph report envelope schema.");
    }
}
