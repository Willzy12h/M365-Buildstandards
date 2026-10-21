using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
namespace BuildStandard.Engine.Reports;
public static partial class RegisteredReportDocuments
{
    public const string OperationalNotice = "Independent operational observations, not proof of service availability, incident cause, effective partner access or successful customer sign-in. Unknown is not false, zero or proof of absence. This report cannot authorise deployment and does not refresh the data.";
    public static IReadOnlyList<Sheet> OperationalSheets(OperationalContextReport report, bool suppliedFile = false)
    {
        _ = OperationalContextSchema.Serialise(report);
        var metadata = new Sheet("Operational provenance", ["Field", "Value"])
            .Add("Report", report.ReportId).Add("Record ID", report.Id).Add("Source tenant ID", report.TenantId)
            .Add("Recorded account object ID", report.AccountObjectId ?? "Unknown / not recorded").Add("Recorded application ID", report.ClientId ?? "Unknown / not recorded")
            .Add("Original access intent", report.AccessIntent).Add("Original permission mode", report.PermissionMode).Add("Recorded source mode", report.SourceMode)
            .Add("Read status", report.Status).Add("Frozen as-of", report.AsOfUtc).Add("Started at", report.StartedAt).Add("Ended at", report.EndedAt)
            .Add("Toolkit version", report.ToolkitVersion).Add("Adapter version", report.AdapterVersion).Add("Integrity digest", report.IntegrityDigest)
            .Add("Evidence meaning", OperationalNotice);
        if (report.ReportId == "partner-relationships")
        {
            var coverage=report.PartnerCoverage!;
            metadata.Add("Selected customer tenant ID",report.CustomerTenantId!).Add("Source/customer meaning","The connected source remains the partner; selected customer is a report parameter, not a customer connection or proof of access.")
                .Add("Parent inventory complete",coverage.ParentInventoryComplete.ToString()).Add("Bounded parent rows inspected",coverage.ParentRowsRead.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Add("Selected-customer rows",coverage.SelectedCustomerRows.ToString(System.Globalization.CultureInfo.InvariantCulture)).Add("Excluded other-customer rows",coverage.ExcludedOtherCustomerRows.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Add("Unattributable customer rows",coverage.UnattributableCustomerRows.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (report.ReportId == "collaboration-inventory")
        {
            var choice = report.CollaborationSelection!;
            metadata.Add("Collaboration coverage", OperationalContextSchema.CollaborationNotice).Add("Bounded site search", choice.Search ?? "Not selected");
            foreach (var id in choice.Sites) metadata.Add("Selected site", id);
            foreach (var id in choice.Groups) metadata.Add("Selected group/Team", id);
            for (var i = 0; i < report.Sections.Count; i++) metadata.Add("Returned count " + report.Sections[i].Id, choice.ReturnedCounts[i]?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown / source did not complete");
        }
        if (report.ReportId == "security-incidents")
        {
            metadata.Add("Created at or after (UTC)", report.IncidentSelection!.StartUtc)
                .Add("Created before (UTC)", report.IncidentSelection.EndUtc)
                .Add("Incident coverage", OperationalContextSchema.IncidentNotice);
        }
        if (suppliedFile) metadata.Add("Supplied-file provenance", SuppliedFileNotice);
        foreach (var source in report.Sources) metadata.Add("Registered source", source.Api + " " + source.RegisteredRoute + " · " + source.Reference);
        var sheets = new List<Sheet> { metadata };
        foreach (var section in report.Sections)
        {
            metadata.Add("Section " + section.Id, section.Status + " · " + section.Rows.Count + " recorded rows · " + (section.Error ?? "Read completed"));
            foreach (var limitation in section.Limitations) metadata.Add("Section limitation", limitation);
            if (section.Rows.Count == 0) metadata.Add("Empty section " + section.Id, section.Status == ReportReadState.Collected
                ? "Checked successfully; no objects returned." : "Could not check completely; this is not an empty successful result.");
            var type = report.ReportId == "collaboration-inventory"
                ? OperationalContextSchema.CollaborationSections(OperationalContextSchema.FrozenSelection(report.CollaborationSelection)).Single(s => s.Id == section.Id).RowType
                : section.Id switch
            {
                "health-overviews" => typeof(ServiceHealthOverviewRow), "health-issues" => typeof(ServiceHealthIssueRow),
                "security-incidents" => typeof(SecurityIncidentRow),
                "partner-relationships" => typeof(PartnerRelationshipRow), "partner-assignments" => typeof(PartnerAccessAssignmentRow),
                _ => throw new ConfigurationException("Unsupported operational report section.")
            };
            var columns = type.GetProperties().Select(property => ToolkitJson.Options.PropertyNamingPolicy!.ConvertName(property.Name))
                .OrderBy(key => key == "name" ? 0 : key == "id" ? 1 : key == "readStatus" ? 2 : key == "error" ? 3 : 4).ThenBy(key => key, StringComparer.Ordinal).ToArray();
            var sheet = new Sheet(section.Id, columns.Select(key => key == "status" ? report.ReportId == "security-incidents" ? "Reported incident workflow state" : "Reported service state" : key == "roleDefinitionIds" ? "Directly advertised role IDs (not effective access)" : Label(key)));
            foreach (var row in section.Rows) sheet.Rows.Add(columns.Select(key => key == "error" && row[key] is null && row["readStatus"]?.GetValue<string>() == ReportReadState.Collected ? "No read error" : Value(row[key])).ToArray());
            sheets.Add(sheet);
        }
        return sheets;
    }
    public static string OperationalHtml(OperationalContextReport report, string companyName, bool suppliedFile = false)
    {
        var html = StartDocument("Operational context — " + report.ReportId, companyName);
        html.Append("<p class=\"notice\">").Append(E(OperationalNotice)).Append("</p>");
        foreach (var sheet in OperationalSheets(report, suppliedFile))
        { html.Append("<h2>").Append(E(sheet.Name)).Append("</h2>"); Table(html, sheet); }
        return html.Append("</body></html>").ToString();
    }
}
