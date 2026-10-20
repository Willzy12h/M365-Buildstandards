using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class DeploymentCoverageSelectionTests
{
    internal sealed class Fixture
    {
        public StandardCatalogue Standard {get;}=new(){Release="2026.10.15",IntegrityDigest=new string('a',64),Collections=new(){["compliance"]=new(){Api="v1.0",Path="/deviceManagement/deviceCompliancePolicies",Scope="DeviceManagementConfiguration.Read.All"}},Controls=[new(){Id="CMP-WIN-001",Name="Windows compliance",Collection="compliance"}]};
        public TenantSession Session {get;}=new(){TenantId=TestData.TenantA,AccountObjectId=TestData.Operator,OperatorObjectId=TestData.Operator,ClientId=TestData.Emergency,Mode=SessionMode.Assessment,TenantVerified=true,OperatorVerified=true,Scopes=["DeviceManagementConfiguration.Read.All"]};
        public TenantSnapshot Snapshot {get;}
        public ManagedObjectMappings Mappings {get;}
        public DeploymentRun Run {get;}
        public Fixture()
        {
            var payload=new JsonObject{["displayName"]="CMP - Core - Windows"};var after=(JsonObject)payload.DeepClone();after["id"]=TestData.Office;
            Run=new(){Id=Guid.NewGuid().ToString("D"),TenantId=TestData.TenantA,Results=[new(){ControlId="CMP-WIN-001",Collection="compliance",ObjectId=TestData.Office,PlannedAction="Create",WriteAcceptance=WriteAcceptance.Accepted,Configuration=ConfigurationVerification.Pass,WrittenPayload=payload,AfterObject=after,PayloadDigest=CanonicalJson.Sha256(payload),ReadbackDigest=CanonicalJson.Sha256(after),WrittenAt="2026-10-19T09:00:00Z"}]};Run.IntegrityDigest=EvidenceIntegrity.Compute(Run);
            Snapshot=new(){Id=Guid.NewGuid().ToString("D"),TenantId=TestData.TenantA,CapturedAt="2026-10-19T10:00:00Z",StandardRelease=Standard.Release,Collections=new(){["compliance"]=new(){Status=CaptureStatus.Collected,Api="v1.0",Path="/deviceManagement/deviceCompliancePolicies",Count=1,Items=[after]}}};Seal();
            Mappings=new(){TenantId=TestData.TenantA,ByControl=new(){["CMP-WIN-001"]=new(){ControlId="CMP-WIN-001",Collection="compliance",ObjectId=TestData.Office,RunId=Run.Id,LastApplied=payload,LastAppliedDigest=CanonicalJson.Sha256(payload)}}};
        }
        public void Seal()=>Snapshot.IntegrityDigest=EvidenceIntegrity.Compute(Snapshot);
        public DeploymentCoverageSelection Prepare()=>DeploymentCoverageSelection.Prepare(Standard,Snapshot,Mappings,[Run],Session,"CMP-WIN-001",[TestData.Operator],[TestData.Mam]);
    }
    [Fact] public void Selection_freezes_exact_control_ownership_and_typed_pilot_namespaces_without_write_authority()
    {var f=new Fixture();var selection=f.Prepare();Assert.Equal("CMP-WIN-001",selection.ControlId);Assert.Equal(TestData.Office,selection.ObjectId);Assert.Equal(CoverageFamily.ClassicCompliance,selection.Family);Assert.Equal(f.Snapshot.Id,selection.SourceSnapshotId);Assert.Equal(f.Snapshot.IntegrityDigest,selection.SourceSnapshotDigest);Assert.Equal(f.Standard.IntegrityDigest,selection.StandardDigest);Assert.Single(selection.PilotUserObjectIds);Assert.Single(selection.PilotDirectoryDeviceObjectIds);Assert.Contains("not assignment approval",selection.Limitations);f.Standard.Controls[0].Name="Changed";f.Mappings.ByControl["CMP-WIN-001"].ObjectId=TestData.Operator;Assert.Equal("Windows compliance",selection.ControlName);Assert.Equal(TestData.Office,selection.ObjectId);}
    [Theory][InlineData("tenant")][InlineData("operator")][InlineData("unverified")][InlineData("mode")]
    public void Unverified_or_other_context_is_refused(string defect)
    {var f=new Fixture();switch(defect){case "tenant":f.Session.TenantId=TestData.TenantB;break;case "operator":f.Session.OperatorObjectId=TestData.Office;break;case "unverified":f.Session.TenantVerified=false;break;case "mode":f.Session.Mode=(SessionMode)99;break;}Assert.ThrowsAny<ToolkitException>(()=>f.Prepare());}
    [Theory][InlineData("modified")][InlineData("unhashed")][InlineData("failed")][InlineData("partial")][InlineData("path")][InlineData("api")][InlineData("release")]
    public void Source_identity_integrity_and_registered_collection_are_required(string defect)
    {var f=new Fixture();switch(defect){case "modified":f.Snapshot.ClientLabel="edited";break;case "unhashed":f.Snapshot.IntegrityDigest="";break;case "failed":f.Snapshot.Collections["compliance"].Status=CaptureStatus.Error;f.Seal();break;case "partial":f.Snapshot.Collections["compliance"].DetailIncomplete=true;f.Seal();break;case "path":f.Snapshot.Collections["compliance"].Path="/users";f.Seal();break;case "api":f.Snapshot.Collections["compliance"].Api="beta";f.Seal();break;case "release":f.Snapshot.StandardRelease="other";f.Seal();break;}Assert.ThrowsAny<ToolkitException>(()=>f.Prepare());}
    [Theory][InlineData("mapping-only")][InlineData("same-name")][InlineData("uncertain")][InlineData("future-proof")][InlineData("modified-run")][InlineData("duplicate-owner")]
    public void Reuse_existing_ownership_provenance_not_names_or_mapping_alone(string defect)
    {var f=new Fixture();switch(defect){case "mapping-only":f.Mappings.ByControl["CMP-WIN-001"].RunId=Guid.NewGuid().ToString();break;case "same-name":f.Mappings.ByControl.Clear();break;case "uncertain":f.Run.Results[0].WriteAcceptance=WriteAcceptance.Unknown;f.Run.IntegrityDigest=EvidenceIntegrity.Compute(f.Run);break;case "future-proof":f.Run.Results[0].WrittenAt="2026-10-20T10:00:00Z";f.Run.IntegrityDigest=EvidenceIntegrity.Compute(f.Run);break;case "modified-run":f.Run.Results[0].ObjectId=TestData.Operator;break;case "duplicate-owner":f.Mappings.ByControl["DUP"]=f.Mappings.ByControl["CMP-WIN-001"];break;}Assert.ThrowsAny<ToolkitException>(()=>f.Prepare());}
    [Theory][InlineData("unverified-standard")][InlineData("standard-path")][InlineData("standard-api")][InlineData("standard-scope")]
    public void Unverified_catalogue_or_route_changes_are_not_silently_rebound(string defect)
    {var f=new Fixture();switch(defect){case "unverified-standard":f.Standard.IntegrityDigest="";break;case "standard-path":f.Standard.Collections["compliance"].Path="/users";break;case "standard-api":f.Standard.Collections["compliance"].Api="beta";break;case "standard-scope":f.Standard.Collections["compliance"].Scope="Directory.Read.All";break;}Assert.ThrowsAny<ToolkitException>(()=>f.Prepare());}
    [Fact] public void Scoped_before_evidence_is_allowed_for_reporting_but_does_not_become_deployment_evidence()
    {var f=new Fixture();Assert.False(f.Snapshot.Complete);Assert.Contains("not complete deployment",f.Prepare().Limitations);}
    [Fact] public void Invalid_or_duplicate_pilot_ids_are_refused_in_their_explicit_namespace()
    {var f=new Fixture();Assert.Throws<ConfigurationException>(()=>DeploymentCoverageSelection.Prepare(f.Standard,f.Snapshot,f.Mappings,[f.Run],f.Session,"CMP-WIN-001",["friendly-name"],[]));Assert.Throws<ConfigurationException>(()=>DeploymentCoverageSelection.Prepare(f.Standard,f.Snapshot,f.Mappings,[f.Run],f.Session,"CMP-WIN-001",[TestData.Operator,TestData.Operator],[]));}
}
