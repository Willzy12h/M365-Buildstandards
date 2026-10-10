using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>Shared typed tables for CLI/desktop/HTML/CSV/XLSX; child metadata gets separate rows, never giant cells.</summary>
public static class ApplicationReportDocuments
{
    public static IReadOnlyList<Sheet> Sheets(ApplicationReportEvidence report, bool suppliedFile = false)
    {
        ApplicationReportEvidenceSchema.Validate(report, report.TenantId);
        var definition = ApplicationReportRegistry.Find(2, report.ReportId);
        var provenance = new Sheet("Report provenance", ["Field", "Value"]);
        if (suppliedFile) provenance.Add("Export provenance", RegisteredReportDocuments.SuppliedFileNotice);
        provenance.Add("Report", definition.Name); provenance.Add("Report ID", report.ReportId); provenance.Add("Envelope schema", "2");
        provenance.Add("Record ID", report.Id); provenance.Add("Tenant ID", report.TenantId); provenance.Add("Recorded account object ID", report.AccountObjectId ?? "Unknown historical account");
        provenance.Add("Resource", report.Resource); provenance.Add("Source mode", report.SourceMode); provenance.Add("Started at", report.StartedAt);
        provenance.Add("Fixed collection end", report.EndedAt); provenance.Add("Read status", report.Status); provenance.Add("Toolkit version", report.ToolkitVersion);
        provenance.Add("Adapter version", report.ModuleVersion); provenance.Add("Integrity digest", report.IntegrityDigest);
        provenance.Add("Horizon days", report is ApplicationExpiryEvidence expiry ? expiry.Parameters.HorizonDays.ToString(System.Globalization.CultureInfo.InvariantCulture) : "Not applicable — no consent date/horizon filter");
        foreach (var s in report.Sources)
            provenance.Add("Source " + s.RegisteredRoute, s.Status + " · " + s.StartedAt + " to " + s.EndedAt + " · " + s.Rows + " retained metadata entries · " + s.ObjectsAttempted + " child objects attempted · " + (s.Error ?? "Read completed") + " · " + s.Reference);
        foreach (var limitation in report.Limitations) provenance.Add("Limitation", limitation);
        var sheets = new List<Sheet> { provenance };
        foreach (var section in report.Sections)
        {
            provenance.Add("Section " + section.Id, section.Status + " · " + section.Rows.Count + " recorded rows · " + (section.Error ?? "Read completed"));
            foreach (var limitation in section.Limitations) provenance.Add("Section limitation", limitation);
            var rowType = definition.Sections.Single(s => s.Id == section.Id).RowType;
            var columns = Columns(rowType).Where(c => c is not ("credentials" or "owners" or "appRoles")).ToArray();
            var sheet = new Sheet(section.Id, columns.Select(Label));
            foreach (var row in section.Rows) sheet.Rows.Add(columns.Select(c => Value(row[c])).ToArray());
            sheets.Add(sheet);
        }
        if (report is ApplicationExpiryEvidence)
        {
            foreach (var (name, field, type) in new[] { ("Credentials", "credentials", typeof(ApplicationCredentialMetadata)), ("Owners", "owners", typeof(ApplicationOwnerMetadata)) })
            {
                var columns = Columns(type); var sheet = new Sheet(name, new[] { "Directory object ID", "Object kind" }.Concat(columns.Select(Label)));
                foreach (var row in report.Sections.SelectMany(s => s.Rows))
                    foreach (var child in row[field]!.AsArray().OfType<JsonObject>())
                        sheet.Rows.Add(new[] { Value(row["id"]), Value(row["objectKind"]) }.Concat(columns.Select(c => Value(child[c]))).ToArray());
                sheets.Add(sheet);
            }
        }
        else
        {
            var columns = Columns(typeof(ApplicationRoleMetadata)); var sheet = new Sheet("Role definitions", new[] { "Resource service principal ID" }.Concat(columns.Select(Label)));
            foreach (var row in report.Sections[0].Rows)
                foreach (var child in row["appRoles"]!.AsArray().OfType<JsonObject>())
                    sheet.Rows.Add(new[] { Value(row["id"]) }.Concat(columns.Select(c => Value(child[c]))).ToArray());
            sheets.Add(sheet);
        }
        return sheets;
    }
    public static string Html(ApplicationReportEvidence report, string companyName, bool suppliedFile = false)
    {
        var sheets = Sheets(report, suppliedFile); var definition = ApplicationReportRegistry.Find(2, report.ReportId);
        var html = new StringBuilder("<!doctype html><html lang=\"en-GB\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\"><style>body{font:15px system-ui;margin:24px;color:#172c3e}table{border-collapse:collapse;margin:12px 0}th,td{border:1px solid #ccd6df;padding:8px;white-space:pre-wrap;overflow-wrap:anywhere}.table{overflow:auto}.notice{padding:12px;background:#fff0cd}</style><title>");
        html.Append(E(definition.Name)).Append("</title></head><body><p>").Append(E(companyName)).Append("</p><h1>").Append(E(definition.Name)).Append("</h1>");
        html.Append("<p class=\"notice\">Read status: ").Append(E(report.Status)).Append(". Separate metadata report; never complete deployment before-evidence. Unknown values do not prove absence. No data is refreshed by export.</p>");
        if (suppliedFile) html.Append("<p class=\"notice\">").Append(E(RegisteredReportDocuments.SuppliedFileNotice)).Append("</p>");
        foreach (var section in report.Sections)
        {
            html.Append("<p>").Append(E(section.Id)).Append(": ").Append(E(section.Status)).Append(" · ").Append(section.Rows.Count).Append(" recorded rows. ").Append(E(section.Error)).Append("</p>");
            if (section.Rows.Count == 0) html.Append("<p>").Append(section.Status == ReportReadState.Collected ? "Checked successfully; no objects returned." : "No complete object list is available; this is not an empty successful check.").Append("</p>");
        }
        foreach (var sheet in sheets)
        {
            html.Append("<h2>").Append(E(sheet.Name)).Append("</h2><div class=\"table\"><table><thead><tr>");
            foreach (var cell in sheet.Rows[0]) html.Append("<th>").Append(E(cell)).Append("</th>");
            html.Append("</tr></thead><tbody>");
            foreach (var row in sheet.Rows.Skip(1)) { html.Append("<tr>"); foreach (var cell in row) html.Append("<td>").Append(E(cell)).Append("</td>"); html.Append("</tr>"); }
            html.Append("</tbody></table></div>");
        }
        return html.Append("</body></html>").ToString();
    }
    private static string[] Columns(Type type) => RegisteredReportJson.Strict.GetTypeInfo(type).Properties.Select(p => p.Name).ToArray();
    private static string Value(JsonNode? node) => node is null ? "Unknown / not returned" : node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node.ToJsonString();
    private static string Label(string value) => char.ToUpperInvariant(value[0]) + string.Concat(value.Skip(1).Select(c => char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
}
