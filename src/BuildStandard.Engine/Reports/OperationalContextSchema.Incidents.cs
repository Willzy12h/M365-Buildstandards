using System.Text.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;
public static partial class OperationalContextSchema
{
    public const string IncidentNotice = "Incident context only, not proof of tenant safety or complete Defender coverage. Availability depends on licensed and connected workloads and delegated roles. Business Premium/P1 does not imply P2 Identity Protection. Retention is unavailable unless returned by Microsoft. Resolved is a source workflow state, not verified remediation. No alert content or response action is included.";
    internal static bool KnownIncidentValue(string field, string? value) => field switch
    {
        "severity" => value is "unknown" or "informational" or "low" or "medium" or "high",
        "status" => value is "active" or "resolved" or "inProgress" or "redirected" or "awaitingAction",
        "classification" => value is "unknown" or "falsePositive" or "truePositive" or "informationalExpectedActivity",
        "determination" => value is "unknown" or "apt" or "malware" or "securityPersonnel" or "securityTesting" or "unwantedSoftware"
            or "other" or "multiStagedAttack" or "compromisedUser" or "phishing" or "maliciousUserActivity" or "clean"
            or "insufficientData" or "confirmedActivity" or "lineOfBusinessApplication",
        _ => false
    };
    internal static SecurityIncidentReadSelection IncidentWindow(OperationalContextReport report)
    {
        if (report.IncidentSelection is not { } selection || !Utc(selection.StartUtc, out var start)
            || !Utc(selection.EndUtc, out var end) || !Utc(report.AsOfUtc, out var asOf)) throw Bad("Missing or invalid frozen UTC incident interval.");
        return new(start, end, asOf);
    }
    private static void ValidateIncidents(OperationalContextReport report, DateTimeOffset asOf)
    {
        if (report.CollaborationSelection is not null || report.CustomerTenantId is not null || report.PartnerCoverage is not null)
            throw Bad("Incident context cannot claim collaboration or partner/customer coverage.");
        var window = IncidentWindow(report);
        if (!Utc(report.EndedAt, out var captureEnd)) throw Bad("Invalid incident capture end.");
        var expected = SourceDeclarations(report.ReportId);
        if (report.Sources is null || report.Sources.Count != 1 || report.Sources[0] is not { } source
            || source.Api != expected[0].Api || source.RegisteredRoute != expected[0].RegisteredRoute || source.Reference != expected[0].Reference)
            throw Bad("Incident source declarations differ from the registered surface.");
        if (report.Sections is not { Count: 1 } || report.Sections[0] is not { } section || section.Id != "security-incidents"
            || section.Rows is null || section.Rows.Count > OperationalReportRegistry.MaximumRows || section.Limitations is null
            || !section.Limitations.Contains(IncidentNotice, StringComparer.Ordinal)) throw Bad("Missing incident section or required coverage limitations.");
        RequireState(section.Status, section.Error); foreach (var limitation in section.Limitations) Text(limitation);
        if (section.Status is ReportReadState.NotAttempted or ReportReadState.Failed or ReportReadState.Cancelled && section.Rows.Count != 0)
            throw Bad("Retained rows after an incomplete read must remain Partial.");
        var rows = new List<SecurityIncidentRow>();
        foreach (var raw in section.Rows)
        {
            SecurityIncidentRow row;
            try { row = raw?.Deserialize<SecurityIncidentRow>(RegisteredReportJson.Strict) ?? throw Bad("Null incident row."); }
            catch (JsonException) { throw Bad("Incident row contains unsupported fields or types; private content is not retained."); }
            RequireState(row.ReadStatus, row.Error);
            if (row.ReadStatus is not (ReportReadState.Collected or ReportReadState.Partial)) throw Bad("Returned incident rows must be observed complete or partial metadata.");
            foreach (var text in new[] { row.Id, row.Name, row.Error, row.Severity, row.Status, row.Classification, row.Determination, row.RedirectIncidentId, row.IncidentWebUrl }) Text(text);
            if (row.Id is not null && !Identity(row.Id) || row.RedirectIncidentId is not null && !Identity(row.RedirectIncidentId)) throw Bad("Malformed incident identity.");
            if (row.PriorityScore is < 0 or > 100) throw Bad("Incident priority is outside Microsoft's documented 0 to 100 range.");
            DateTimeOffset created = default, modified = default;
            if (row.CreatedAt is not null && !Utc(row.CreatedAt, out created) || row.ModifiedAt is not null && !Utc(row.ModifiedAt, out modified)) throw Bad("Malformed incident time.");
            var incomplete = row.Id is null || string.IsNullOrWhiteSpace(row.Name) || row.CreatedAt is null || row.ModifiedAt is null
                || created < window.StartUtc || created >= window.EndUtc || modified < created || modified > captureEnd
                || !KnownIncidentValue("severity", row.Severity) || !KnownIncidentValue("status", row.Status)
                || !KnownIncidentValue("classification", row.Classification) || !KnownIncidentValue("determination", row.Determination)
                || row.Status == "redirected" && row.RedirectIncidentId is null;
            if (incomplete && row.ReadStatus != ReportReadState.Partial) throw Bad("Unknown or inconsistent incident observations cannot become complete results.");
            rows.Add(row);
        }
        foreach (var duplicate in rows.Where(r => r.Id is not null).GroupBy(r => r.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            if (duplicate.Any(r => r.ReadStatus != ReportReadState.Partial)) throw Bad("Duplicate incident identities must remain explicitly partial.");
        if (section.Status == ReportReadState.Collected && rows.Any(r => r.ReadStatus != ReportReadState.Collected)) throw Bad("Incident section status conceals partial observations.");
    }
}
