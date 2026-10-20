using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;

namespace BuildStandard.Engine.Reports;

/// <summary>Offline visibility over validated report evidence; no re-collection or current-state inference.</summary>
public static class RegisteredReportDocuments
{
    public const string SuppliedFileNotice = "Offline supplied file: source, account and live-mode claims have not been independently verified. No live reads were performed for this export. A digest detects modification; it does not authenticate the source.";
    public static IReadOnlyList<Sheet> Sheets(ExchangeReportEvidence report, bool suppliedFile = false)
    {
        ExchangeReportEvidenceSchema.Validate(report, report.TenantId);
        var definition = ExchangeReportRegistry.Find(report.ReportId);
        var metadata = new Sheet("Report provenance", ["Field", "Value"]);
        if (suppliedFile) metadata.Add("Export provenance", SuppliedFileNotice);
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
        if(definition.ReportSchemaVersion==2)
        {
            metadata.Add("Identity meaning",report.ReportId=="exo-transport-rules" ? "Policy IDs are original Exchange transport-rule GUIDs. sha256 diagnostic keys identify local scope observations, never service IDs or execution authority." : "sha256 keys identify local observation tuples, not Microsoft service IDs, ownership, permissions or execution targets. Source namespaces remain separate.");
            metadata.Add("Registered limitation",definition.Limitations);
            if(report.ReportId=="exo-inbox-rules")metadata.Add("Hidden rule scope",report.Parameters.Single(p=>p.Name=="IncludeHidden").Value=="false"
                ?"Hidden rules omitted; no complete all-rules inventory or incident verdict is claimed."
                :"Hidden rules requested; source/RBAC/row limits still apply and this is not an incident verdict.");
            if (report.ReportId == "exo-inbox-rules") metadata.Add("Local review cues",
                "Forwarding recipients, DeleteMessage=true and MarkAsRead=true are review cues, including on recorded disabled rules. Unknown values stay visible. Cues do not establish maliciousness, effective behaviour or protection; hidden-rule scope still applies.");
        }
        var sheets = new List<Sheet> { metadata };
        foreach (var section in report.Sections)
        {
            metadata.Add("Section " + section.Id, section.Status + " · " + section.Rows.Count + " returned rows · " + (section.Error ?? "Read completed"));
            foreach (var limitation in report.Limitations.Concat(section.Limitations)) metadata.Add("Limitation", limitation);
            var columns = ExchangeReportEvidenceSchema.Columns(definition.Sections.Single(s => s.Id == section.Id).RowType)
                .OrderBy(k => definition.ReportSchemaVersion == 2 ? FamilyColumnPriority(section.Id, k)
                    : k == "id" ? 0 : k == "name" ? 1 : k == "readStatus" ? 2 : k == "error" ? 3 : 4).ToArray();
            var ruleCues = report.ReportId == "exo-inbox-rules" && section.Id == "rules";
            var headings = columns.Select(c => c == "id" ? definition.ReportSchemaVersion==2 ? report.ReportId=="exo-transport-rules" && section.Id=="policies" ? "Exchange transport-rule GUID" : "Local observation key (not a service ID)" : "Exchange mailbox GUID" : Label(c)).ToList();
            if (ruleCues) headings.InsertRange(1, ["Local review cue status", "Local review cues"]);
            var sheet = new Sheet(section.Id, headings);
            foreach (var row in section.Rows)
            {
                var cells = columns.Select(c => ExchangeValue(row,c,definition.ReportSchemaVersion == 2)).ToList();
                if (ruleCues)
                {
                    var cue = RuleReviewCue(row);
                    cells.InsertRange(1, [cue.Status, cue.Detail]);
                }
                sheet.Rows.Add(cells.ToArray());
            }
            sheets.Add(sheet);
        }
        return sheets;
    }
    // Presentation order only: retain every registered column and the unchanged original evidence/JSON.
    // Work fields lead the native and human-readable export table; local hashes remain available at the end.
    private static int FamilyColumnPriority(string section, string column)
    {
        if (column == "id") return 99;
        string[] priority = section switch
        {
            "permissions" => ["principal", "rights", "readStatus", "permissionKind", "sharingPermissionFlags", "isInherited", "deny", "error"],
            "policies" => ["name", "state", "mode", "priority", "readStatus", "activationDate", "expiryDate", "error"],
            "members" => ["name", "sourceIdentity", "recipientTypeDetails", "primarySmtpAddress", "readStatus", "error"],
            "rules" => ["name", "enabled", "readStatus", "priority", "error"],
            _ => ["name", "readStatus", "lookupReadStatus", "folderReadStatus", "permissionReadStatus", "membershipReadStatus", "childReadStatus", "error", "permissionReads", "managedBy"]
        };
        var index = Array.IndexOf(priority, column);
        return index < 0 ? 80 : index;
    }
    private static (string Status, string Detail) RuleReviewCue(JsonObject row)
    {
        var cues = new List<string>();
        foreach (var name in new[] { "forwardTo", "forwardAsAttachmentTo", "redirectTo" })
            if (row[name]!["state"]!.GetValue<string>() == "Value"
                && row[name]!["value"] is JsonArray { Count: > 0 })
                cues.Add(Label(name) + " recipients reported");
        if (row["deleteMessage"]!["state"]!.GetValue<string>() == "Value"
            && row["deleteMessage"]!["value"]!.GetValue<bool>()) cues.Add("DeleteMessage reported true");
        if (row["markAsRead"]!["state"]!.GetValue<string>() == "Value"
            && row["markAsRead"]!["value"]!.GetValue<bool>()) cues.Add("MarkAsRead reported true");
        var unknown = row["readStatus"]!.GetValue<string>() != ReportReadState.Collected;
        var status = (cues.Count > 0, unknown) switch
        {
            (true, true) => "Reported cues and unknowns — review",
            (true, false) => "Reported cues — review context",
            (false, true) => "Unable to classify all cues",
            _ => "No selected cues reported — not a safety verdict"
        };
        if (cues.Count == 0) cues.Add("No positive cues established from returned values");
        var enabled = row["enabled"]!;
        cues.Add(enabled["state"]!.GetValue<string>() == "Value"
            ? enabled["value"]!.GetValue<bool>() ? "Recorded enabled; effect unverified" : "Recorded disabled; effect unverified"
            : "Enabled state unknown; effect unverified");
        if (unknown) cues.Add("Unknown values / unresolved references: " + row["error"]!.GetValue<string>());
        return (status, string.Join(" · ", cues));
    }
    private static string ExchangeValue(JsonObject row,string column,bool family)
    {
        if (family && column == "name" && row[column] is JsonValue label && label.TryGetValue<string>(out var name)
            && string.IsNullOrWhiteSpace(name))
            return name.Length == 0 ? "Empty name (reported)" : "Whitespace name (reported)";
        if (family && (column is "principal" or "sourceIdentity") && row[column] is JsonObject principal) return ReferenceValue(principal);
        if (family && column == "permissionReads" && row[column] is JsonArray reads)
            return string.Join("\n", reads.Select(node =>
            {
                var read = node!.AsObject(); var kind = read["kind"]!.GetValue<string>();
                if (!read["selected"]!.GetValue<bool>()) return kind + ": Not selected — not attempted";
                return kind + ": " + read["status"]!.GetValue<string>()
                    + " · returned entries: " + (read["returnedEntries"] is null ? "Unknown" : Value(read["returnedEntries"]))
                    + " · excluded entries: " + (read["excludedEntries"] is null ? "Unknown" : Value(read["excludedEntries"]))
                    + (read["error"] is null ? "" : " · " + read["error"]!.GetValue<string>());
            }));
        if (family && row[column] is JsonObject observed && observed["state"] is JsonValue)
            return ObservedValue(observed);
        if (row[column] is not null) return Value(row[column]);
        if (column == "error" && row["readStatus"]?.GetValue<string>() == ReportReadState.Collected) return "No read error";
        if ((column is "forwardingAddress" or "forwardingSmtpAddress")
            && row[column + "ReadStatus"]?.GetValue<string>() == ReportReadState.Collected) return "Not set (observed)";
        return Value(null);
    }
    // Called only after strict report2 validation. This is shared presentation, never evidence mutation or inference.
    private static string ObservedValue(JsonObject observed)
    {
        var state = observed["state"]!.GetValue<string>();
        if (state != "Value")
        {
            var meaning = state switch
            {
                "ReturnedNull" => "returned null", "Missing" => "not returned", "Failed" => "read failed",
                "NotAttempted" => "not attempted", "Unsupported" => "unsupported",
                _ => "unclassified"
            };
            return $"Unknown — {meaning}: {observed["reason"]!.GetValue<string>()}";
        }
        var value = observed["value"];
        if (value is JsonArray references)
        {
            if (references.Count == 0) return "No values returned (reported empty list)";
            if (references.All(item => item is JsonValue scalar && scalar.TryGetValue<string>(out _)))
                return string.Join("; ", references.Select(Value)) + " (reported)";
            return string.Join("; ", references.Select(reference => ReferenceValue(reference!.AsObject())));
        }
        if (value is JsonValue scalar && scalar.TryGetValue<bool>(out var flag))
            return flag ? "True (reported)" : "False (reported)";
        if(value is JsonValue textValue && textValue.TryGetValue<string>(out var text) && text.Length==0)return "Empty text (reported)";
        return Value(value) + " (reported)";
    }

    private static string ReferenceValue(JsonObject item)
    {
        var source = item["namespace"]!.GetValue<string>();
        var label = source switch
        {
            "UnresolvedDisplayLabel" => "Unresolved display label", "RawIdentity" => "Raw identity",
            "ExchangeRecipientGuid" => "Exchange recipient GUID", "DistinguishedName" => "Distinguished name", "SpecialPrincipal" => "Special principal", _ => "Returned text"
        };
        var unresolved = source == "UnresolvedDisplayLabel" ? " (identity not resolved)" : "";
        var directoryId = item["externalDirectoryObjectId"]?.GetValue<string>();
        return $"{label}: {item["value"]!.GetValue<string>()}{unresolved}"
            + (directoryId is null ? "" : $" · Returned Entra object ID: {directoryId}");
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

    public static string ExchangeHtml(ExchangeReportEvidence report,string companyName,bool suppliedFile = false)
    {
        var sheets=Sheets(report,suppliedFile);
        return ExchangeHtmlCore(report, companyName, sheets);
    }
    public static string SuppliedExchangeHtml(ExchangeReportImport original, string companyName)
    {
        var report = original.ReadEvidence();
        var sheets = Sheets(report, suppliedFile: true);
        sheets[0].Add("Original file SHA-256", original.OriginalSha256);
        return ExchangeHtmlCore(report, companyName, sheets);
    }
    private static string ExchangeHtmlCore(ExchangeReportEvidence report, string companyName, IReadOnlyList<Sheet> sheets)
    {
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
