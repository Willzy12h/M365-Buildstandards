using System.Net;
namespace BuildStandard.Engine.Reports;

public static partial class DeploymentCoverageDocuments
{
    /// <summary>Original evidence view; no historical projection becomes a live scope, effect claim or job completion.</summary>
    public static IReadOnlyList<Sheet> Sheets(DeploymentCoverageEvidence evidence)
    {
        var r=evidence.Metadata;var s=r.Selection;var context=new Sheet("Original context",["Field","Recorded value"]);
        context.Add("Evidence",evidence.IsHistorical?"Historical / unverified import":"Original unsigned source observations");
        context.Add("Meaning","Configuration and source observations; not effective targeting, installation, complete deployment before-evidence or authority to act");
        context.Add("Report ID",r.Id);context.Add("Toolkit / adapter",r.ToolkitVersion+" / "+r.AdapterVersion);
        context.Add("Original source mode",r.SourceMode);context.Add("Tenant ID",r.TenantId);context.Add("Engineer account object ID",r.AccountObjectId);context.Add("Application registration",r.ClientId);
        context.Add("Original permission mode",r.PermissionMode);context.Add("Original scopes",string.Join(", ",r.Scopes));context.Add("Original interval",r.StartedAt+" to "+r.CompletedAt);
        context.Add("Control",s.ControlName);context.Add("Control ID",s.ControlId);context.Add("Exact object ID",s.ObjectId);context.Add("Collection",s.Collection);
        context.Add("Original control digest",s.ControlDigest);context.Add("Standard release",s.StandardRelease);context.Add("Original standard SHA-256",s.StandardDigest);
        context.Add("Source snapshot ID",s.SourceSnapshotId);context.Add("Original snapshot SHA-256",s.SourceSnapshotDigest);context.Add("Source capture time",s.SourceCapturedAt);
        context.Add("Source run ID",s.SourceRunId);context.Add("Original run SHA-256",s.SourceRunDigest);
        context.Add("Integrity limitation","Fingerprints detect modification, not source authentication. Import does not sign in, refresh evidence, approve or replay a change, clear uncertainty or complete a job.");
        var states=new Sheet("Source read states",["Source","Read state","Retained rows","Original interval","Read meaning","Detail"]);
        var raw=new Sheet("Admitted source observations",["Source","Raw row identity","Property","Original value","Read state","Meaning"]);
        var pages=new Sheet("Original source pages",["Source","File","Original SHA-256 (not a signature)","Bytes","Registered path"]);
        foreach(var source in evidence.Sources())
        {
            var rows=source.Capture.Rows;
            var meaning=rows.Count==0?(source.Capture.State==CoverageSourceState.Complete?"Checked successfully; no source rows returned":"Not established — no admitted rows; this is not an empty success"):"Retained source observations; not proof of effective deployment";
            states.Add(source.Key,source.Capture.State,rows.Count,source.StartedAt+" to "+source.CompletedAt,meaning,source.Capture.Detail);
            foreach(var row in rows)foreach(var property in row)
                raw.Add(source.Key,row["id"]!.GetValue<string>(),property.Key,Value(property.Value),source.Capture.State,"Raw source namespaces/statuses retained; no name, UPN or device-ID join and no effect inferred");
            foreach(var page in r.Sources.Single(x=>x.Key==source.Key).Pages)pages.Add(source.Key,page.File,page.Sha256,page.Bytes,source.Path);
        }
        var pilot=new Sheet("Selected pilot",["Identity namespace","Original object ID","Meaning"]);
        foreach(var id in s.PilotUserObjectIds)pilot.Add("Directory user object ID",id,"Report scope only; not assignment approval");
        foreach(var id in s.PilotDirectoryDeviceObjectIds)pilot.Add("Directory device object ID (not an Intune managed-device ID)",id,"Report scope only; no device namespace guess");
        return new[]{context,states,pilot,pages,raw};
    }
    public static string Html(DeploymentCoverageEvidence evidence,string companyName)
    {
        var html=RegisteredReportDocuments.StartDocument("Original selected-control coverage observations",companyName);
        html.Append("<p class=\"notice\">Historical or unsigned observations, not proof of effective protection. This document does not refresh the tenant. Unknown is not false, zero or successful verification.</p>");
        foreach(var sheet in Sheets(evidence)){html.Append("<h2>").Append(WebUtility.HtmlEncode(sheet.Name)).Append("</h2>");RegisteredReportDocuments.Table(html,sheet);}
        return html.Append("</body></html>").ToString();
    }
}
