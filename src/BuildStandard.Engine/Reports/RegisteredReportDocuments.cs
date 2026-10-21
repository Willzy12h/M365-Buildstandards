using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;

namespace BuildStandard.Engine.Reports;

/// <summary>Offline visibility over validated report evidence; no re-collection or current-state inference.</summary>
public static partial class RegisteredReportDocuments
{
    public const string SuppliedFileNotice = "Offline supplied file: source, account and live-mode claims have not been independently verified. No live reads were performed for this export. A digest detects modification; it does not authenticate the source.";
    public static IReadOnlyList<Sheet> Sheets(ExchangeReportEvidence report)
    {
        ExchangeReportEvidenceSchema.Validate(report, report.TenantId);
        var definition = ExchangeReportRegistry.Find(report.ReportId);
        var metadata = new Sheet("Report provenance", ["Field", "Value"]);
        metadata.Add("Report", definition.Name); metadata.Add("Report ID", report.ReportId); metadata.Add("Record ID", report.Id);
        metadata.Add("Tenant ID", report.TenantId); metadata.Add("Observed account", report.ObservedAccountUpn ?? "Unknown");
        metadata.Add("Source mode", report.SourceMode); metadata.Add("Read status", report.Status);
        metadata.Add("Run ID",report.RunId);metadata.Add("Resource",report.Resource);metadata.Add("Initiating account object ID",report.InitiatingAccountObjectId ?? "Unknown");
        metadata.Add("Toolkit",report.ToolkitVersion);metadata.Add("Adapter",report.AdapterVersion);metadata.Add("Module",report.ModuleVersion);metadata.Add("Runtime",report.RuntimeVersion);
        metadata.Add("Script SHA-256",report.ScriptSha256);metadata.Add("Manifest SHA-256",report.ManifestSha256);metadata.Add("Runner SHA-256",report.RunnerTemplateSha256);
        foreach(var parameter in report.Parameters)metadata.Add($"Parameter {parameter.Name} ({parameter.Type})",parameter.Value);
        foreach(var command in report.SourceCommands)metadata.Add("Source command",command);
        metadata.Add("Started at", report.StartedAt); metadata.Add("Ended at", report.EndedAt);
        metadata.Add("Integrity digest", report.IntegrityDigest);
        var sheets = new List<Sheet> { metadata };
        foreach (var section in report.Sections)
        {
            metadata.Add("Section " + section.Id, section.Status + " · " + section.Rows.Count + " returned rows · " + (section.Error ?? "Read completed"));
            foreach (var limitation in report.Limitations.Concat(section.Limitations)) metadata.Add("Limitation", limitation);
            var columns = ExchangeReportEvidenceSchema.Columns(definition.Sections.Single(s => s.Id == section.Id).RowType)
                .OrderBy(k => k == "id" ? 0 : k == "name" ? 1 : k == "readStatus" ? 2 : k == "error" ? 3 : 4).ToArray();
            var sheet = new Sheet(section.Id, columns.Select(c => c == "id" ? "Exchange mailbox GUID" : Label(c)));
            foreach (var row in section.Rows)
                sheet.Rows.Add(columns.Select(c => ExchangeValue(row,c)).ToArray());
            sheets.Add(sheet);
        }
        return sheets;
    }
    private static string ExchangeValue(JsonObject row,string column)
    {
        if (row[column] is not null) return Value(row[column]);
        if (column == "error" && row["readStatus"]?.GetValue<string>() == ReportReadState.Collected) return "No read error";
        if ((column is "forwardingAddress" or "forwardingSmtpAddress")
            && row[column + "ReadStatus"]?.GetValue<string>() == ReportReadState.Collected) return "Not set (observed)";
        return Value(null);
    }
    public static IReadOnlyList<Sheet> Sheets(ReportEvidence report, bool suppliedFile = false)
    {
        ReportEvidenceSchema.Validate(report, report.TenantId);
        var metadata = new Sheet("Report provenance", ["Field", "Value"]);
        if (suppliedFile) metadata.Add("Export provenance", SuppliedFileNotice);
        metadata.Add("Report", GraphReportRegistry.Find(report.ReportId).Name);
        metadata.Add("Report ID", report.ReportId); metadata.Add("Record ID", report.Id);
        metadata.Add("Tenant ID", report.TenantId); metadata.Add("Recorded account object ID", report.AccountObjectId ?? "Unknown historical account");
        metadata.Add("Resource", report.Resource); metadata.Add("Source mode", report.SourceMode);
        metadata.Add("Started at", report.StartedAt); metadata.Add("Ended at", report.EndedAt);
        metadata.Add("Read status", report.Status); metadata.Add("Toolkit version", report.ToolkitVersion); metadata.Add("Adapter version", report.ModuleVersion);
        metadata.Add("Requested start", report.Parameters.Start ?? "Not applicable"); metadata.Add("Requested end", report.Parameters.End ?? "Not applicable");
        if (GraphReportRegistry.Find(report.ReportId).DateRange) metadata.Add("Available retention", "Unknown — successful API pagination does not prove historical retention");
        metadata.Add("Integrity digest", report.IntegrityDigest);
        foreach (var source in report.Sources) metadata.Add("Registered source", source.Api + " " + source.RegisteredRoute + " · " + source.Reference);
        foreach (var limitation in report.Limitations) metadata.Add("Limitation", limitation);
        var sheets = new List<Sheet> { metadata };
        foreach (var section in report.Sections)
        {
            metadata.Add("Section " + section.Id, section.Status + " · " + section.Rows.Count + " recorded rows · " + (section.Error ?? "Read completed"));
            foreach (var limitation in section.Limitations) metadata.Add("Section limitation", limitation);
            var columns = Columns(report.ReportId, section);
            var sheet = new Sheet(section.Id, columns.Select(Label));
            foreach (var row in section.Rows) sheet.Rows.Add(columns.Select(c => Value(row[c])).ToArray());
            sheets.Add(sheet);
        }
        return sheets;
    }

    public static string Html(ReportEvidence report, string companyName, bool suppliedFile = false)
    {
        var sheets = Sheets(report, suppliedFile);
        var definition = GraphReportRegistry.Find(report.ReportId);
        var html = StartDocument(definition.Name,companyName);
        if (suppliedFile) html.Append("<p class=\"notice\">").Append(E(SuppliedFileNotice)).Append("</p>");
        html.Append("<p>Tenant ID: ").Append(E(report.TenantId)).Append(" · ").Append(E(report.SourceMode == "live" ? "Recorded live read" : "Historical report"))
            .Append(" · ").Append(E(report.StartedAt)).Append(" to ").Append(E(report.EndedAt)).Append("</p>");
        html.Append("<div class=\"notice\"><strong>Read status: ").Append(E(report.Status)).Append(".</strong> Separate report evidence; cannot authorise deployment. Unknown values are not zero, false or proof of absence. This document does not refresh the data.</div>");
        foreach (var limitation in report.Limitations) html.Append("<p>").Append(E(limitation)).Append("</p>");
        foreach (var section in report.Sections)
        {
            html.Append("<h2>").Append(E(section.Id)).Append("</h2><p>Read status: ").Append(E(section.Status)).Append(" · ").Append(section.Rows.Count).Append(" recorded rows.</p>");
            if (section.Error is not null) html.Append("<p class=\"notice\">").Append(E(section.Error)).Append("</p>");
            if (section.Rows.Count == 0) html.Append("<p>").Append(section.Status == ReportReadState.Collected ? "Checked successfully; no objects returned." : "Unable to report objects: this is not an empty successful check.").Append("</p>");
            var sheet = sheets.Single(s => s.Name == section.Id);
            Table(html, sheet);
        }
        html.Append("<h2>Source and integrity details</h2><p>A digest detects modification; unsigned source claims are not authenticated.</p>");
        Table(html, sheets[0]);
        return html.Append("</body></html>").ToString();
    }

    public static string ExchangeHtml(ExchangeReportEvidence report,string companyName)
    {
        var sheets=Sheets(report);
        var html=StartDocument(ExchangeReportRegistry.Find(report.ReportId).Name,companyName);
        html.Append("<p class=\"notice\">Read status: ").Append(E(report.Status)).Append(". Separate report evidence; cannot authorise deployment. Unknown is not false or proof of absence. A digest verifies integrity, not Microsoft acceptance. This export does not refresh the data.</p>");
        foreach(var sheet in sheets) { html.Append("<h2>").Append(E(sheet.Name)).Append("</h2>");Table(html,sheet); }
        return html.Append("</body></html>").ToString();
    }
    private static StringBuilder StartDocument(string title,string companyName)
    {
        var html = new StringBuilder("<!doctype html><html lang=\"en-GB\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\">");
        html.Append("<title>").Append(E(title)).Append("</title><style>body{font:15px system-ui;margin:32px;color:#172c3e}h1,h2{color:#145887}.table{overflow:auto}table{border-collapse:collapse;width:100%;margin:16px 0}th,td{border:1px solid #cad4dd;padding:10px;text-align:left;vertical-align:top}th{background:#eef3f8}td{white-space:pre-wrap;overflow-wrap:anywhere;min-width:110px}.notice{padding:14px;background:#fff0cd}.muted{color:#536477}</style></head><body>");
        html.Append("<p class=\"muted\">").Append(E(companyName)).Append("</p><h1>").Append(E(title)).Append("</h1>");
        return html;
    }

    private static string[] Columns(string reportId, ReportSection section) => GraphReportRegistry.Find(reportId).Sections.Single(s => s.Id == section.Id).RowType
        .GetProperties().Select(p => ToolkitJson.Options.PropertyNamingPolicy!.ConvertName(p.Name)).Distinct(StringComparer.Ordinal)
        .OrderBy(k => k == "name" ? 0 : k == "id" ? 1 : k == "readStatus" ? 2 : k == "error" ? 3 : 4).ThenBy(k => k, StringComparer.Ordinal).ToArray();
    private static string Label(string key) => key switch
    {
        "id" => "Object ID", "skuId" => "SKU ID", "osVersion" => "OS version", "isMfaRegistered" => "MFA registered",
        "isMfaCapable" => "MFA capable", "isSsprRegistered" => "SSPR registered", "error" => "Read detail",
        _ => char.ToUpperInvariant(key[0]) + string.Concat(key.Skip(1).Select(c => char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()))
    };
    private static string Value(JsonNode? value) => value is null ? "Unknown / not returned"
        : value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text
        : value is JsonArray array && array.Count == 0 ? "No recorded values — check read status" : value.ToJsonString();
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static void Table(StringBuilder html, Sheet sheet)
    {
        html.Append("<div class=\"table\"><table><thead><tr>");
        foreach (var cell in sheet.Rows[0]) html.Append("<th>").Append(E(cell)).Append("</th>");
        html.Append("</tr></thead><tbody>");
        foreach (var row in sheet.Rows.Skip(1))
        { html.Append("<tr>"); foreach (var cell in row) html.Append("<td>").Append(E(cell)).Append("</td>"); html.Append("</tr>"); }
        html.Append("</tbody></table></div>");
    }
}
