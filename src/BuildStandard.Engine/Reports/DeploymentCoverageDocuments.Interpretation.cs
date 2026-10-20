using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using BuildStandard.Core.Safety;

namespace BuildStandard.Engine.Reports;

public static partial class DeploymentCoverageDocuments
{
    // Presentation-only original observations: no DeploymentCoverageSelection is created, no freshness clock is
    // restarted and no imported evidence can become a live collection scope, deployment plan or completion claim.
    private static (string Candidate,IReadOnlyList<Sheet> Sheets,IReadOnlyList<string> Limitations) RecordedInterpretation(
        DeploymentCoverageReport report,IReadOnlyList<CoverageCapturedSource> sources)
    {
        var assignments=new List<CoverageAssignmentObservation>();var groups=new List<CoverageCapturedGroup>();
        var compliance=new List<ComplianceCoverageObservation>();var limits=new List<string>();
        var obj=sources.SingleOrDefault(s=>s.Key=="object");var assignmentSource=sources.SingleOrDefault(s=>s.Key=="assignments");
        var objectRows=obj?.Capture.Rows;var originalObject=objectRows is {Count:1}?objectRows[0]:null;
        var candidate="Candidate state unknown";
        if(assignmentSource is not null)
            foreach(var row in assignmentSource.Capture.Rows)
            {
                try{assignments.Add(CoverageAssignmentObservation.Read(row));}
                catch(ConfigurationException){limits.Add("An original assignment could not be interpreted; its raw fields remain available. No complete assignment/population claim is made.");}
            }
        if(obj?.Capture.State==CoverageSourceState.Complete&&originalObject is not null)
        {
            if(report.Selection.Family==CoverageFamily.ConditionalAccess)
                candidate=ConditionalAccessMaterialState.HasPolicyDefinition(originalObject)?Text(originalObject,"state") switch
                {"disabled"=>"Observed disabled CA policy","enabled"=>"Observed enabled CA policy","enabledForReportingButNotEnforced"=>"Observed report-only CA policy",_=>"CA policy state unknown"}:"CA policy definition incomplete";
            else
            {
                candidate=assignmentSource?.Capture.State==CoverageSourceState.Complete&&limits.Count==0
                    ? assignments.Count==0?"Observed unassigned":"Observed assignment configuration; effectiveness unknown":"Assignment state unknown";
                if(assignmentSource?.Capture.State==CoverageSourceState.Complete&&originalObject["isAssigned"] is JsonValue flag
                    &&flag.TryGetValue<bool>(out var assigned)&&assigned!=(assignments.Count>0))
                {candidate="Assignment state unknown";limits.Add("The original assigned flag and separately collected assignment list conflict; no single assignment conclusion is inferred.");}
            }
        }
        var groupIds=assignments.Where(a=>a.CanObserveGroup).Select(a=>a.GroupObjectId!).Distinct(StringComparer.Ordinal).ToArray();
        if(groupIds.Length>32)limits.Add("The32-group observation limit excludes remaining populations; absence is not established for them.");
        foreach(var id in groupIds.Take(32))
        {
            var metadata=sources.SingleOrDefault(s=>s.Key=="group:"+id+":metadata");
            var admission=new CoverageGroupAdmission(id,false,"Original group metadata was not read completely; population support is unknown.");
            try{if(metadata?.Capture.State==CoverageSourceState.Complete)admission=CoverageGroupAdmission.Read(metadata.Capture.Rows.Single(),id);}
            catch(ToolkitException){limits.Add("Original group metadata could not establish a supported population; no membership absence is inferred.");}
            var members=sources.SingleOrDefault(s=>s.Key=="group:"+id+":members")?.Capture
                ??new CoverageSourceCapture(CoverageSourceState.NotAttempted,[],[],"No original direct-member read was returned.");
            CoverageMembershipResult membership;
            try{membership=CoverageMembershipProjection.Project(admission,members,report.Selection.PilotUserObjectIds,report.Selection.PilotDirectoryDeviceObjectIds);}
            catch(ConfigurationException)
            {
                limits.Add("Original direct-member rows could not be interpreted; raw rows remain available and pilot observations stay unknown.");
                membership=CoverageMembershipProjection.Project(admission,new(CoverageSourceState.Partial,[],[],"Original members could not be interpreted."),report.Selection.PilotUserObjectIds,report.Selection.PilotDirectoryDeviceObjectIds);
            }
            groups.Add(new(id,membership));
        }
        if(report.Selection.Family==CoverageFamily.ClassicCompliance&&sources.SingleOrDefault(s=>s.Key=="service-results") is { } service)
        {
            if(!Timestamps.TryParse(service.CompletedAt,out var collectedAt))throw new ConfigurationException("The original service interval is invalid; no freshness can be derived.");
            foreach(var row in service.Capture.Rows)
            {
                try{compliance.Add(ComplianceCoverageObservation.Read(row,collectedAt));}
                catch(ConfigurationException)
                {limits.Add("An original compliance row could not be interpreted; its raw source is retained.");compliance.Add(new(Text(row,"id"),null,null,null,"Unknown","Unknown — malformed original source","The recorded fields do not support a service conclusion; inspect the original source."));}
            }
        }
        return(candidate,InterpretationSheets(sources,assignments,groups,compliance),limits.AsReadOnly());
    }
    private static string? Text(JsonObject row,string key)=>row[key] is JsonValue value&&value.TryGetValue<string>(out var text)?text:null;
}
