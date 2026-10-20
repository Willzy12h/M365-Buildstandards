using System.Net;
using System.Text.Json.Nodes;
using BuildStandard.Core.Json;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;

/// <summary>Shared native/export tables. Configuration, membership and service observations never become effective deployment.</summary>
public static partial class DeploymentCoverageDocuments
{
    public static IReadOnlyList<Sheet> Sheets(DeploymentCoverageCapture capture)
    {
        var sheets=new List<Sheet>{Context(capture)};
        var sources=new Sheet("Source read states",["Source","Read state","Retained rows","Original interval","Read meaning","Detail"]);
        var pages=new Sheet("Original source pages",["Source","Page","Original SHA-256 (not a signature)","Bytes","Registered path","Read state"]);
        foreach(var source in capture.Sources)
        {
            var count=source.ModernExport?.Archive.Table.Rows.Count??source.Capture.Rows.Count;
            var meaning=count==0?(source.Capture.State==CoverageSourceState.Complete?"Checked successfully; no source rows returned":"Not established — no admitted rows; this is not an empty success"):"Retained source rows; inspect read state and limitations";
            sources.Add(source.Key,source.Capture.State,count,source.StartedAt+" to "+source.CompletedAt,meaning,source.Capture.Detail);
            var originals=source.Capture.OriginalPages;
            for(var i=0;i<originals.Count;i++)pages.Add(source.Key,i+1,CanonicalJson.Sha256Hex(originals[i]),originals[i].Length,source.Path,source.Capture.State);
        }
        sheets.Add(sources);sheets.Add(pages);
        var pilot=new Sheet("Selected pilot",["Identity namespace","Original object ID","Meaning"]);
        foreach(var id in capture.Selection.PilotUserObjectIds)pilot.Add("Directory user object ID",id,"Selected report scope only; not assignment approval or verified deployment");
        foreach(var id in capture.Selection.PilotDirectoryDeviceObjectIds)pilot.Add("Directory device object ID (not an Intune managed-device ID)",id,"Selected report scope only; no azureADDeviceId/managed-device join");
        sheets.Add(pilot);
        var configuration=new Sheet("Object configuration",["Property","Projected original value","Source state"]);
        var objectSource=capture.Sources.FirstOrDefault(s=>s.Key=="object");
        if(objectSource is not null)foreach(var row in objectSource.Capture.Rows)foreach(var property in row)configuration.Add(property.Key,Value(property.Value),objectSource.Capture.State);
        sheets.Add(configuration);
        sheets.AddRange(InterpretationSheets(capture.Sources,capture.Assignments,capture.Groups,capture.ComplianceObservations));
        foreach(var source in capture.Sources)if(source.ModernExport is { } modern)
        {sheets[0].Add("Original modern report",modern.Request.ReportName);sheets[0].Add("Original modern job ID",modern.JobId);sheets[0].Add("Modern source meaning",modern.Archive.Table.Limitations);sheets.Add(ModernRows(modern));}
        return sheets;
    }
    private static IReadOnlyList<Sheet> InterpretationSheets(IReadOnlyList<CoverageCapturedSource> sources,
        IReadOnlyList<CoverageAssignmentObservation> assignmentRows,IReadOnlyList<CoverageCapturedGroup> groupObservations,
        IReadOnlyList<ComplianceCoverageObservation> serviceObservations)
    {
        var sheets=new List<Sheet>();
        var assignments=new Sheet("Assignment configuration",["Assignment row ID","Target meaning","Raw target type","Group object ID","Raw group ID","Raw intent","Raw filter type","Raw filter ID","Explicit unfiltered target","Detail"]);
        foreach(var row in assignmentRows)assignments.Add(row.AssignmentRowId,row.TargetMeaning,Raw(row.RawTargetType),Raw(row.GroupObjectId),Raw(row.RawGroupId),Raw(row.RawIntent),Raw(row.RawFilterType),Raw(row.RawFilterId),row.UnfilteredTargetVerified,row.Detail);
        sheets.Add(assignments);
        var populations=new Sheet("Group populations",["Group name","Group object ID","Population read meaning","Population limitations"]);
        var members=new Sheet("Direct member observations",["Group name","Group object ID","Identity namespace","Object ID","Raw object type","Population read meaning","Detail"]);
        var pilotGroups=new Sheet("Pilot group observations",["Group name","Group object ID","Identity namespace","Pilot object ID","Direct membership observation","Detail"]);
        foreach(var group in groupObservations)
        {
            var metadata=sources.FirstOrDefault(s=>s.Key=="group:"+group.GroupObjectId+":metadata");
            var name=metadata?.Capture.Rows.FirstOrDefault()?["displayName"] is JsonValue v&&v.TryGetValue<string>(out var text)?Raw(text):"Unknown / not returned";
            populations.Add(name,group.GroupObjectId,group.Membership.PopulationState,group.Membership.Limitations);
            foreach(var row in group.Membership.Members)members.Add(name,group.GroupObjectId,row.IdentityNamespace,row.ObjectId,Raw(row.RawType),group.Membership.PopulationState,row.Detail);
            foreach(var row in group.Membership.Pilots)pilotGroups.Add(name,group.GroupObjectId,row.IdentityNamespace,row.ObjectId,row.Observation,row.Detail);
        }
        sheets.Add(populations);sheets.Add(members);sheets.Add(pilotGroups);
        var results=new Sheet("Service observations",["Policy report-row ID (not a managed-device ID)","Reported display name (not a join)","Raw service status","Raw service time","Observation","Freshness","Identity limitation","Detail"]);
        foreach(var row in serviceObservations)results.Add(Raw(row.ReportRowId),Raw(row.DeviceDisplayName),Raw(row.RawStatus),Raw(row.RawReportedAt),row.Observation,row.Freshness,row.IdentityMeaning,row.Detail);
        sheets.Add(results);
        return sheets;
    }
    public static string Html(DeploymentCoverageCapture capture,string companyName)
    {
        var html=RegisteredReportDocuments.StartDocument("Selected-control deployment coverage observations",companyName);
        html.Append("<p class=\"notice\">Configured assignments and point-in-time source observations, not effective targeting, not installation success and not deployment authority. Unknown is not false, zero or successful verification. This document does not refresh the tenant.</p>");
        foreach(var sheet in Sheets(capture))
        {html.Append("<h2>").Append(WebUtility.HtmlEncode(sheet.Name)).Append("</h2>");RegisteredReportDocuments.Table(html,sheet);}
        return html.Append("</body></html>").ToString();
    }
    public static string OriginalContext(DeploymentCoverageCapture capture)=>string.Join('\n',Context(capture).Rows.Skip(1).Select(row=>row[0]+": "+row[1]));
    private static Sheet Context(DeploymentCoverageCapture capture)
    {
        var selection=capture.Selection;var sheet=new Sheet("Original context",["Field","Recorded value"]);
        sheet.Add("Control",selection.ControlName);sheet.Add("Control ID",selection.ControlId);sheet.Add("Exact object ID",selection.ObjectId);sheet.Add("Collection",selection.Collection);
        sheet.Add("Tenant ID",selection.TenantId);sheet.Add("Engineer account object ID",selection.AccountObjectId);sheet.Add("Application registration",selection.ClientId);
        sheet.Add("Recorded permission mode",selection.PermissionMode);sheet.Add("Recorded scopes",string.Join(", ",selection.Scopes));
        sheet.Add("Standard release",selection.StandardRelease);sheet.Add("Original standard SHA-256",selection.StandardDigest);sheet.Add("Selected control digest",selection.ControlDigest);
        sheet.Add("Source snapshot ID",selection.SourceSnapshotId);sheet.Add("Source snapshot digest",selection.SourceSnapshotDigest);sheet.Add("Source snapshot time",selection.SourceCapturedAt);
        sheet.Add("Original creation run ID",selection.SourceRunId);sheet.Add("Original creation run digest",selection.SourceRunDigest);
        sheet.Add("Candidate observation",capture.CandidateState);sheet.Add("Supported reads finished",capture.SupportedReadsComplete);sheet.Add("Context admission at collection completion",capture.IsCurrentAtCompletion);
        sheet.Add("Evidence status","Original read result; live Microsoft behaviour unverified. This projection does not establish that evidence was saved or its source authenticated.");
        sheet.Add("Scope limitation",selection.Limitations);sheet.Add("Read limitation",capture.Limitations);return sheet;
    }
    private static string Raw(string? value)=>value is null?"Unknown / not returned":value.Length==0?"Returned empty text":value;
    private static string Value(JsonNode? value)=>value is null?"Unknown / not returned":value is JsonValue scalar&&scalar.TryGetValue<string>(out var text)?Raw(text):value.ToJsonString();
}
