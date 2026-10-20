using System.Net;
using System.Text;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;
public static class UsageReviewDocuments
{
    public const string PrivacyNotice="Privacy provenance is Unknown. Report identity text is unverified: no directory/licence join, inactivity conclusion or licence-removal recommendation is established. Missing activity is not zero activity. Review source access, refresh dates and reported periods separately.";
    public static IReadOnlyList<Sheet> Sheets(UsageReviewReport report,bool suppliedFile=false)
    {
        var parsed=UsageReviewSchema.Project(report).ToDictionary(p=>p.SourceId,StringComparer.Ordinal);
        var metadata=new Sheet("Usage report provenance",["Field","Value"])
            .Add("Record ID",report.Id).Add("Tenant ID (recorded)",report.TenantId).Add("Account object ID (recorded)",report.AccountObjectId??"Unknown / not recorded")
            .Add("Source mode (recorded)",report.SourceMode).Add("Read status",report.Status).Add("Started at",report.StartedAt).Add("Ended at",report.EndedAt)
            .Add("Toolkit version",report.ToolkitVersion).Add("Integrity digest",report.IntegrityDigest).Add("Privacy and identity",PrivacyNotice)
            .Add("Evidence meaning","Historical report observations, never complete deployment before-evidence, approval, job completion or an entitlement check.");
        if(suppliedFile)metadata.Add("Supplied-file provenance",RegisteredReportDocuments.SuppliedFileNotice);
        var sheets=new List<Sheet>{metadata};
        foreach(var source in report.Sources)
        {
            var definition=UsageReportRegistry.Find(source.SourceId);
            metadata.Add("Source",definition.Name).Add("Registered route",definition.Route(source.Period)).Add("Source read status",source.Status)
                .Add("Read detail",source.Reason??"Read completed; privacy/identity remain unverified").Add("Requested period",source.Period)
                .Add("Original CSV SHA-256",source.CsvSha256??"Unknown / no admitted CSV").Add("Download host",source.DownloadHost??"Not recorded")
                .Add("Privacy provenance",source.PrivacyProvenance);
            if(!parsed.TryGetValue(source.SourceId,out var data))continue;
            metadata.Add("Projected data rows",data.Rows.Count).Add("Extra column names (values excluded from projection)",string.Join("; ",data.ExtraColumns));
            if(!definition.Columns.Contains("Report Period",StringComparer.Ordinal))metadata.Add("Returned period", "Not supplied by this CSV schema; requested period alone is not returned interval evidence.");
            var table=new Sheet(definition.Name,definition.Columns.Select(name=>name is "User Id" or "User Principal Name"?name+" (unverified report text)":name));
            foreach(var row in data.Rows)table.Rows.Add(definition.Columns.Select(name=>row.Cells[name].State=="Value"?row.Cells[name].Value!:name.Contains("Activity Date",StringComparison.Ordinal)?"No date reported — activity unknown":"Not reported — unknown").ToArray());
            sheets.Add(table);
        }
        return sheets;
    }
    public static string Html(UsageReviewReport report,string company,bool suppliedFile=false)
    {
        static string E(string value)=>WebUtility.HtmlEncode(value);
        var html=new StringBuilder("<!doctype html><html lang=\"en-GB\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>Usage review</title><style>body{font-family:Segoe UI,sans-serif;margin:2rem;color:#173740}table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccdcdc;padding:.5rem;text-align:left;vertical-align:top;white-space:pre-wrap}.table{overflow-x:auto}th{background:#e5f2f1}</style><body>");
        html.Append("<h1>Usage review</h1><p>").Append(E(company)).Append("</p><p>").Append(E(PrivacyNotice)).Append("</p>");
        foreach(var sheet in Sheets(report,suppliedFile))
        {
            html.Append("<h2>").Append(E(sheet.Name)).Append("</h2><div class=\"table\"><table><thead><tr>");
            foreach(var cell in sheet.Rows[0])html.Append("<th>").Append(E(cell)).Append("</th>");
            html.Append("</tr></thead><tbody>");
            foreach(var row in sheet.Rows.Skip(1)){html.Append("<tr>");foreach(var cell in row)html.Append("<td>").Append(E(cell)).Append("</td>");html.Append("</tr>");}
            html.Append("</tbody></table></div>");
        }
        return html.Append("</body></html>").ToString();
    }
}
