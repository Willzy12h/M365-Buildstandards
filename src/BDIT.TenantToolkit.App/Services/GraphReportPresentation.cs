using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;
using Envelope = BDIT.TenantToolkit.Engine.Reports.ReportEnvelopeDispatcher.Envelope;

namespace BDIT.TenantToolkit.App.Services;

/// <summary>Presentation choices only. The paired registries still decide supported schemas, routes and permissions.</summary>
public sealed class GraphReportPresentation
{
    private GraphReportPresentation(int schema, string id) { SchemaVersion = schema; Id = id; }
    public int SchemaVersion { get; }
    public string Id { get; }
    public string Name => SchemaVersion == 1 ? GraphReportRegistry.Find(Id).Name : ApplicationReportRegistry.Find(SchemaVersion, Id).Name;
    public bool DateRange => SchemaVersion == 1 && GraphReportRegistry.Find(Id).DateRange;
    public bool Expiry => SchemaVersion == 2 && Id == "application-expiry";
    public string Limitations => SchemaVersion == 1 ? GraphReportRegistry.Find(Id).Limitations : ApplicationReportRegistry.Find(SchemaVersion, Id).Limitations;
    public static IReadOnlyList<GraphReportPresentation> Choices { get; } = Array.AsReadOnly(
        GraphReportRegistry.Definitions.Select(d => new GraphReportPresentation(1, d.Id))
            .Concat(ApplicationReportRegistry.Definitions.Select(d => new GraphReportPresentation(2, d.Id))).ToArray());
    public static GraphReportPresentation Find(int schema, string id) => Choices.SingleOrDefault(c => c.SchemaVersion == schema && c.Id == id)
        ?? throw new ConfigurationException("Choose a registered report schema and ID pair.");
    public string Scopes(TenantSession? session) => string.Join("\n", SchemaVersion == 1
        ? GraphReportRegistry.Find(Id).Routes.Select(r => Line(r.Path, r.Scope, session is not null && GraphReportRegistry.HasAccess(session, r)))
        : ApplicationReportRegistry.Find(SchemaVersion, Id).Routes.Select(r => Line(r.Path, r.Scope, session is not null && ApplicationReportRegistry.HasAccess(session, r))));
    private static string Line(string path, string scope, bool available) => path + " requires " + scope
        + (available ? " — available in this session." : " — unavailable in this session.");
    public static GraphReportPresentation For(Envelope evidence) => Find(evidence.SchemaVersion, evidence.Schema1?.ReportId ?? evidence.Schema2!.ReportId);
    public static string RecordId(Envelope evidence) => evidence.Schema1?.Id ?? evidence.Schema2!.Id;
    public static string StartedAt(Envelope evidence) => evidence.Schema1?.StartedAt ?? evidence.Schema2!.StartedAt;
    public static string EndedAt(Envelope evidence) => evidence.Schema1?.EndedAt ?? evidence.Schema2!.EndedAt;
    public static string Status(Envelope evidence) => evidence.Schema1?.Status ?? evidence.Schema2!.Status;
    public static IReadOnlyList<ReportSection> Sections(Envelope evidence) => evidence.Schema1?.Sections ?? evidence.Schema2!.Sections;
    public static IReadOnlyList<Sheet> Sheets(Envelope evidence, bool supplied) => evidence.Schema1 is { } legacy
        ? RegisteredReportDocuments.Sheets(legacy, supplied) : ApplicationReportDocuments.Sheets(evidence.Schema2!, supplied);
    public static void Validate(Envelope evidence, string tenant)
    {
        if (evidence.Schema1 is { } legacy) ReportEvidenceSchema.Validate(legacy, tenant);
        else ApplicationReportEvidenceSchema.Validate(evidence.Schema2!, tenant);
    }
}
