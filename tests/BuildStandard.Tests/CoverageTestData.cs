using System.Reflection;
using System.Text;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
namespace BuildStandard.Tests;

/// <summary>Synthetic original documents only. Reflection seeds UI recovery states; it is never a product import-to-live API.</summary>
internal static class CoverageTestData
{
    internal static DeploymentCoverageEvidence Original(string tenant,string account,string client,string objectId,string record,bool includeGroup=false)
    {
        const string start="2026-10-20T00:00:00.000Z",end="2026-10-20T00:00:01.000Z";
        var scope=new CoverageReportScope{ControlId="CMP-HISTORY-001",ControlName="Original historical control A",ControlDigest=new string('a',64),ObjectId=objectId,Collection="compliance",Family=CoverageFamily.ClassicCompliance,
            SourceSnapshotId="98989898-9898-4989-8989-989898989898",SourceSnapshotDigest=new string('b',64),SourceCapturedAt="2026-10-19T00:00:00Z",SourceRunId="97979797-9797-4979-8979-979797979797",SourceRunDigest=new string('c',64),PilotUserObjectIds=includeGroup?[account]:[],StandardRelease="2026.10.15",StandardDigest=new string('d',64)};
        var r=new DeploymentCoverageReport{Id=record,TenantId=tenant,AccountObjectId=account,ClientId=client,Scopes=["DeviceManagementConfiguration.Read.All"],ToolkitVersion="synthetic-ui",StartedAt=start,CompletedAt=end,Selection=scope};
        if(includeGroup)r.Scopes.Add("Group.Read.All");
        var pages=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        Add("object",CoverageReadKind.Object,"{\"id\":\""+objectId+"\",\"displayName\":\"Observed synthetic policy\"}",[objectId]);
        const string group="96969696-9696-4969-8969-969696969696";
        Add("assignments",CoverageReadKind.Assignments,includeGroup?"{\"value\":[{\"id\":\"assignment-1\",\"target\":{\"@odata.type\":\"#microsoft.graph.groupAssignmentTarget\",\"groupId\":\""+group+"\",\"deviceAndAppManagementAssignmentFilterType\":\"none\",\"deviceAndAppManagementAssignmentFilterId\":null}}]}":"{\"value\":[]}",includeGroup?["assignment-1"]:[]);
        Add("service-results",CoverageReadKind.ServiceResults,"{\"value\":[{\"id\":\"opaque-policy-row\",\"status\":null}]}",["opaque-policy-row"]);
        if(includeGroup)
        {
            AddGroup(false,"{\"id\":\""+group+"\",\"displayName\":\"Synthetic direct pilot group\",\"groupTypes\":[],\"membershipRuleProcessingState\":null,\"visibility\":\"Private\"}",[group]);
            AddGroup(true,"{\"value\":[{\"id\":\""+account+"\",\"@odata.type\":\"#microsoft.graph.user\"}]}",[account]);
        }
        r.IntegrityDigest=EvidenceIntegrity.Compute(r);return DeploymentCoverageEvidence.Read(Encoding.UTF8.GetBytes(ToolkitJson.Serialize(r)),pages,tenant);
        void AddGroup(bool members,string json,List<string> ids)
        {var request=DeploymentCoverageReads.Group(group,members);var bytes=Encoding.UTF8.GetBytes(json);var name=$"page-{pages.Count:D3}.json";pages.Add(name,bytes);r.Sources.Add(new(){Key="group:"+group+":"+(members?"members":"metadata"),Path=request.Path,Scope=request.Scope,StartedAt=start,CompletedAt=end,State=CoverageSourceState.Complete,Detail="Synthetic direct-membership source; not effective targeting.",RetainedRowIds=ids,Pages=[new(){File=name,Sha256=CanonicalJson.Sha256Hex(bytes),Bytes=bytes.Length}]});}
        void Add(string key,CoverageReadKind kind,string json,List<string> ids)
        {var request=DeploymentCoverageReads.Prepare(scope.Family,scope.ObjectId,kind);var bytes=Encoding.UTF8.GetBytes(json);var name=$"page-{pages.Count:D3}.json";pages.Add(name,bytes);r.Sources.Add(new(){Key=key,Path=request.Path,Scope=request.Scope,StartedAt=start,CompletedAt=end,State=CoverageSourceState.Complete,Detail="Synthetic original read; no live behaviour asserted.",RetainedRowIds=ids,Pages=[new(){File=name,Sha256=CanonicalJson.Sha256Hex(bytes),Bytes=bytes.Length}]});}
    }
    internal static DeploymentCoverageCapture OversizeForUi(DeploymentCoverageEvidence original)
    {
        var metadata=original.Metadata;
        var scope=(DeploymentCoverageSelection)Activator.CreateInstance(typeof(DeploymentCoverageSelection),nonPublic:true)!;
        foreach(var target in typeof(DeploymentCoverageSelection).GetProperties().Where(p=>p.GetSetMethod(true) is not null))
        {
            var header=typeof(DeploymentCoverageReport).GetProperty(target.Name);var selected=typeof(CoverageReportScope).GetProperty(target.Name);
            if(header is not null)target.SetValue(scope,header.GetValue(metadata));else if(selected is not null)target.SetValue(scope,selected.GetValue(metadata.Selection));
        }
        var sources=original.Sources().ToArray();var source=sources[0];
        var prefix="{\"id\":\""+scope.ObjectId+"\",\"padding\":\"";const string suffix="\"}";
        var bytes=Encoding.UTF8.GetBytes(prefix+new string('x',DeploymentCoverageEvidence.MaximumBytes-256-prefix.Length-suffix.Length)+suffix);
        var constructor=typeof(CoverageSourceCapture).GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance).Single();
        var capture=(CoverageSourceCapture)constructor.Invoke([CoverageSourceState.Complete,source.Capture.Rows,new byte[][]{bytes},"Synthetic oversized original source, not current live evidence.",false]);
        sources[0]=source with{Capture=capture};
        return new(scope,sources,[],[],[],"Unknown",false,false,"Synthetic raw material exceeds the saved-report bound; manual recovery only."){Id=metadata.Id};
    }
}
