using System.Net;
using System.Text;
using BuildStandard.Core.Reporting;

namespace BuildStandard.Engine.Reports;

/// <summary>One native/offline/export projection; applicability is never enforcement or write authority.</summary>
public static class ConditionalAccessEvaluationDocuments
{
    public static IReadOnlyList<Sheet> Sheets(ConditionalAccessEvaluationCapture capture)
    {
        var report=capture.Metadata;var result=ConditionalAccessEvaluationResponseCodec.Read(capture.OriginalResponseBytes);
        var request=ConditionalAccessScenarioCodec.Prepare(report.Scenario);
        var metadata=new Sheet("Context and limitations",["Field","Recorded value"]);
        metadata.Add("Evidence admission",capture.IsHistorical?"Historical / unverified supplied evidence":"Original read receipt; live behaviour unverified");
        metadata.Add("Limitation",ConditionalAccessEvaluationBundle.Notice);metadata.Add("Limitation",ConditionalAccessEvaluationResponseCodec.Limitations);
        metadata.Add("Tenant ID",report.TenantId);metadata.Add("Engineer account ID",report.AccountObjectId);metadata.Add("Application registration",report.ClientId);
        metadata.Add("Resource",report.Resource);metadata.Add("Recorded source mode",report.SourceMode);metadata.Add("Recorded permission mode",report.PermissionMode);
        metadata.Add("Recorded scopes",string.Join(", ",report.Scopes));metadata.Add("Started at",report.StartedAt);metadata.Add("Completed at",report.CompletedAt);
        metadata.Add("Read status",report.Status);metadata.Add("Toolkit version",report.ToolkitVersion);metadata.Add("Adapter",report.AdapterVersion);
        metadata.Add("Scenario user ID",report.Scenario.UserObjectId);metadata.Add("Scenario application ID",report.Scenario.ApplicationId);
        metadata.Add("Device platform",report.Scenario.DevicePlatform);metadata.Add("Client type",report.Scenario.ClientAppType);
        metadata.Add("IP address",report.Scenario.IpAddress??"Omitted — not false or none");metadata.Add("Country",report.Scenario.Country??"Omitted — not inferred");
        metadata.Add("Sign-in risk",report.Scenario.SignInRisk??"Omitted — not none");metadata.Add("User risk",report.Scenario.UserRisk??"Omitted — not none");
        metadata.Add("Device compliant",report.Scenario.DeviceCompliant is null?"Omitted — not false":report.Scenario.DeviceCompliant.Value?"true":"false");
        metadata.Add("Omitted conditions",string.Join(", ",request.OmittedConditions));metadata.Add("Request SHA-256",report.RequestSha256);
        metadata.Add("Original response SHA-256",report.ResponseSha256);metadata.Add("Integrity digest (not a signature)",report.IntegrityDigest);
        foreach(var limitation in result.ReadLimitations)metadata.Add("Read limitation",limitation);
        var rows=new Sheet("Policy applicability",["Policy name","Policy ID","Raw policy state","Raw policy applies","Raw analysis reason","Meaning"]);
        foreach(var row in result.Rows)rows.Add(row.DisplayName??"Unknown / not returned",row.PolicyId,row.State??"Unknown / not returned",
            row.PolicyApplies is null?"Unknown / not returned":row.PolicyApplies.Value?"true":"false",row.AnalysisReasons??"Unknown / not returned",Meaning(row.Meaning));
        return [metadata,rows];
    }
    public static string OriginalContext(ConditionalAccessEvaluationCapture capture)
    {
        var rows=Sheets(capture)[0].Rows.Skip(1).ToArray();
        string[] priority=["Evidence admission","Scenario user ID","Scenario application ID","Device platform","Client type","Engineer account ID","Tenant ID","Started at","Completed at","Read status"];
        return string.Join("\n",rows.OrderBy(row=>{var index=Array.IndexOf(priority,row[0]);return index<0?priority.Length:index;}).Select(row=>row[0]+": "+row[1]));
    }
    public static string Html(ConditionalAccessEvaluationCapture capture,string companyName)
    {
        var sheets=Sheets(capture);var report=capture.Metadata;
        var html=RegisteredReportDocuments.StartDocument("Conditional Access hypothetical evaluation",companyName);
        html.Append("<p class=\"notice\">").Append(WebUtility.HtmlEncode(ConditionalAccessEvaluationResponseCodec.Limitations)).Append("</p>");
        html.Append("<p>Read status: ").Append(WebUtility.HtmlEncode(report.Status)).Append(". This is not proof of successful access or protection.</p>");
        if(sheets[1].Rows.Count==1)html.Append("<p>Evaluation completed; no policy results returned. This is not proof of enforcement or absence of applicable policies.</p>");
        foreach(var sheet in sheets){html.Append("<h2>").Append(WebUtility.HtmlEncode(sheet.Name)).Append("</h2>");RegisteredReportDocuments.Table(html,sheet);}
        return html.Append("</body></html>").ToString();
    }
    private static string Meaning(string meaning)=>meaning switch
    {
        "Applies"=>"Microsoft reports this policy applies to the supplied scenario; not proof of enforcement",
        "DoesNotApply"=>"Microsoft reports this policy does not apply to the supplied scenario; not a blanket access result",
        _=>"Unknown — could not establish applicability from the returned result"
    };
}
