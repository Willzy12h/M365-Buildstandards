using System.Globalization;
using System.Net;
using System.Text;
namespace BuildStandard.Engine.Reports;

/// <summary>Display-only projections of admitted external claims; no verdict translation or active links.</summary>
public static class ExternalCheckDocuments
{
    public static IReadOnlyList<Sheet> Sheets(ExternalCheckImport source)
    {
        var evidence = source.Evidence;
        var provenance = new Sheet("Source claims", ["Field", "Value"]);
        provenance.Add("Assurance", source.AssuranceNotice);
        provenance.Add("Claimed tool", evidence.ToolName); provenance.Add("Claimed version", evidence.ToolVersion);
        provenance.Add("Claimed run", evidence.SourceRunId); provenance.Add("Claimed tenant", evidence.TenantId);
        provenance.Add("Claimed performed at", evidence.PerformedAt); provenance.Add("Source declared completeness", evidence.SourceState);
        provenance.Add("Original bytes SHA-256", source.OriginalSha256); provenance.Add("Source canonical digest", evidence.IntegrityDigest);
        provenance.Add("Meaning", "Source outcomes remain separate from native configuration assessment. Passed rows cannot make a failed or partial source run complete.");
        foreach (var outcome in new[] { "Passed", "Failed", "Skipped", "Error", "Unknown", "Unclassified" })
            provenance.Add("Claimed " + outcome, evidence.Rows.Count(r => r.DisplayOutcome == outcome).ToString(CultureInfo.InvariantCulture));
        var rows = new Sheet("External result claims", ["Source test ID", "Source outcome (raw)", "Display category", "Title", "Reason", "Reference text (not opened)"]);
        foreach (var row in evidence.Rows) rows.Add(row.TestId, row.Outcome, row.DisplayOutcome, row.Title, row.Reason ?? "Not supplied", row.Reference ?? "Not supplied");
        return [provenance, rows];
    }
    public static string Html(ExternalCheckImport source, string companyName)
    {
        var html = new StringBuilder("<!doctype html><html lang=\"en-GB\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\"><title>External check results — untrusted claims</title><style>body{font:15px system-ui;margin:24px;color:#172c3e}table{border-collapse:collapse;margin:12px 0;width:100%}th,td{border:1px solid #ccd6df;padding:8px;white-space:pre-wrap;overflow-wrap:anywhere}.table{overflow:auto}.notice{padding:12px;background:#fff0cd}</style></head><body>");
        html.Append("<p>").Append(E(companyName)).Append("</p><h1>External check results — untrusted source claims</h1><p class=\"notice\">").Append(E(source.AssuranceNotice)).Append("</p>");
        if (source.Evidence.Rows.Count == 0) html.Append("<p>The supplied source contains zero result rows. This is not proof that checks ran or that the client passed.</p>");
        foreach (var sheet in Sheets(source))
        {
            html.Append("<h2>").Append(E(sheet.Name)).Append("</h2><div class=\"table\"><table><thead><tr>");
            foreach (var cell in sheet.Rows[0]) html.Append("<th>").Append(E(cell)).Append("</th>");
            html.Append("</tr></thead><tbody>");
            foreach (var row in sheet.Rows.Skip(1)) { html.Append("<tr>"); foreach (var cell in row) html.Append("<td>").Append(E(cell)).Append("</td>"); html.Append("</tr>"); }
            html.Append("</tbody></table></div>");
        }
        return html.Append("</body></html>").ToString();
    }
    private static string E(string value) => WebUtility.HtmlEncode(value);
}
