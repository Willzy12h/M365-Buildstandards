using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class DeploymentCoverageEvidenceTests
{
    internal static async Task<DeploymentCoverageEvidence> Evidence()
    {var f=new DeploymentCoverageServiceTests.Fixture();f.GroupAssignment();return DeploymentCoverageEvidence.Create(await f.Capture());}
    [Fact] public async Task Original_bytes_metadata_and_retained_row_admission_round_trip_without_resealing()
    {var evidence=await Evidence();var read=DeploymentCoverageEvidence.Read(evidence.MetadataBytes,evidence.OriginalPages,TestData.TenantA);Assert.True(read.IsHistorical);Assert.Equal(evidence.MetadataBytes,read.MetadataBytes);Assert.Equal("deploymentCoverageReport",read.Metadata.Kind);Assert.Equal(5,read.Metadata.Sources.Count);Assert.Equal(TestData.Operator,read.Metadata.Selection.PilotUserObjectIds[0]);foreach(var pair in evidence.OriginalPages)Assert.Equal(pair.Value,read.OriginalPages[pair.Key]);}
    [Fact] public async Task Metadata_and_source_collections_are_defensive()
    {var e=await Evidence();var bytes=e.MetadataBytes;bytes[0]=0;e.Metadata.Selection.ControlName="changed";var pages=e.OriginalPages;pages.First().Value[0]=0;Assert.NotEqual(0,e.MetadataBytes[0]);Assert.NotEqual("changed",e.Metadata.Selection.ControlName);Assert.NotEqual(0,e.OriginalPages.First().Value[0]);}
    [Fact] public async Task Wrong_tenant_originals_never_enter_another_clients_report()
    {var e=await Evidence();Assert.Throws<TenantMismatchException>(()=>DeploymentCoverageEvidence.Read(e.MetadataBytes,e.OriginalPages,TestData.TenantB));}
    [Fact] public async Task Changed_original_page_or_metadata_fingerprint_is_refused()
    {var e=await Evidence();var pages=e.OriginalPages.ToDictionary(p=>p.Key,p=>p.Value);pages.First().Value[^1]=(byte)' ';Assert.Throws<IntegrityException>(()=>DeploymentCoverageEvidence.Read(e.MetadataBytes,pages,TestData.TenantA));var node=JsonNode.Parse(e.MetadataBytes)!.AsObject();node["selection"]!["controlName"]="changed";Assert.Throws<IntegrityException>(()=>DeploymentCoverageEvidence.Read(Encoding.UTF8.GetBytes(node.ToJsonString()),e.OriginalPages,TestData.TenantA));}
    [Theory][InlineData("kind")][InlineData("schemaVersion")][InlineData("selection")][InlineData("sources")][InlineData("scopes")]
    public async Task Required_fields_cannot_be_omitted_even_if_the_default_would_look_supported(string property)
    {var e=await Evidence();var node=JsonNode.Parse(e.MetadataBytes)!.AsObject();node.Remove(property);Assert.ThrowsAny<ToolkitException>(()=>DeploymentCoverageEvidence.Read(Encoding.UTF8.GetBytes(node.ToJsonString()),e.OriginalPages,TestData.TenantA));}
    [Theory][InlineData("unknown")][InlineData("wrong-case")][InlineData("duplicate")][InlineData("numeric-enum")]
    public async Task Unsupported_fields_duplicate_properties_and_coerced_enums_are_refused(string defect)
    {var e=await Evidence();var node=JsonNode.Parse(e.MetadataBytes)!.AsObject();if(defect=="unknown")node["newAuthority"]=true;if(defect=="wrong-case"){node["Kind"]=node["kind"]!.DeepClone();node.Remove("kind");}if(defect=="numeric-enum")node["permissionMode"]=0;var json=node.ToJsonString();if(defect=="duplicate")json=json.Insert(1,"\"kind\":\"deploymentCoverageReport\",");Assert.ThrowsAny<ToolkitException>(()=>DeploymentCoverageEvidence.Read(Encoding.UTF8.GetBytes(json),e.OriginalPages,TestData.TenantA));}
    [Fact] public async Task Unexpected_missing_or_oversized_files_cannot_be_dropped_to_make_a_complete_report()
    {var e=await Evidence();var pages=e.OriginalPages.ToDictionary(p=>p.Key,p=>p.Value);pages.Add("extra.json",Encoding.UTF8.GetBytes("{}"));Assert.Throws<ConfigurationException>(()=>DeploymentCoverageEvidence.Read(e.MetadataBytes,pages,TestData.TenantA));pages=e.OriginalPages.ToDictionary(p=>p.Key,p=>p.Value);pages.Remove(pages.Keys.First());Assert.Throws<ConfigurationException>(()=>DeploymentCoverageEvidence.Read(e.MetadataBytes,pages,TestData.TenantA));Assert.Throws<ConfigurationException>(()=>DeploymentCoverageEvidence.Read(new byte[5*1024*1024+1],e.OriginalPages,TestData.TenantA));}
    [Fact] public async Task A_context_changed_page_is_retained_as_original_without_becoming_an_admitted_row()
    {var f=new DeploymentCoverageServiceTests.Fixture();f.Override=(r,_)=>{f.Current=false;return Task.FromResult(BuildStandard.Core.Reporting.CoverageResponseCodec.Read(Encoding.UTF8.GetBytes("{\"id\":\""+TestData.Office+"\"}"),r));};var e=DeploymentCoverageEvidence.Create(await f.Capture());var read=DeploymentCoverageEvidence.Read(e.MetadataBytes,e.OriginalPages,TestData.TenantA);Assert.Single(read.OriginalPages);Assert.Empty(read.Metadata.Sources[0].RetainedRowIds);Assert.Equal(CoverageSourceState.ContextChanged,read.Metadata.Sources[0].State);}
    [Fact] public async Task Failed_source_and_explicit_empty_success_keep_different_read_states()
    {var f=new DeploymentCoverageServiceTests.Fixture();var complete=DeploymentCoverageEvidence.Create(await f.Capture());Assert.Equal(CoverageSourceState.Complete,complete.Metadata.Sources.Single(s=>s.Key=="assignments").State);Assert.Single(complete.Metadata.Sources.Single(s=>s.Key=="assignments").Pages);f.Override=(_,_)=>Task.FromException<BuildStandard.Core.Reporting.CoverageReadResponse>(new HttpRequestException());var failed=DeploymentCoverageEvidence.Create(await f.Capture());Assert.Equal(CoverageSourceState.Failed,failed.Metadata.Sources[0].State);Assert.Empty(failed.Metadata.Sources[0].Pages);}
    private static byte[] Change(DeploymentCoverageEvidence evidence,Action<JsonObject> change)
    {var node=JsonNode.Parse(evidence.MetadataBytes)!.AsObject();change(node);node["integrityDigest"]="";node["integrityDigest"]=CanonicalJson.Sha256(node);return Encoding.UTF8.GetBytes(node.ToJsonString());}
    [Theory][InlineData("path")][InlineData("scope")][InlineData("detail")][InlineData("pages")][InlineData("retainedRowIds")]
    public async Task Explicit_null_source_fields_are_rejected_as_invalid_evidence_without_an_unhandled_error(string field)
    {var e=await Evidence();var bytes=Change(e,n=>n["sources"]![0]![field]=null);Assert.Throws<ConfigurationException>(()=>DeploymentCoverageEvidence.Read(bytes,e.OriginalPages,TestData.TenantA));}
    [Theory][InlineData("path")][InlineData("missing-rows")][InlineData("future-time")][InlineData("family")][InlineData("unknown-source")][InlineData("future-state")][InlineData("wrong-case-state")]
    public async Task A_recomputed_unsigned_digest_does_not_make_unsupported_or_inconsistent_metadata_valid(string defect)
    {var e=await Evidence();var bytes=Change(e,n=>{switch(defect){case "path":n["sources"]![0]!["path"]="/users";break;case "missing-rows":n["sources"]![0]!["retainedRowIds"]=new JsonArray();break;case "future-time":n["sources"]![0]!["completedAt"]="2099-01-01T00:00:00.000Z";break;case "family":n["selection"]!["collection"]="apps";break;case "unknown-source":n["sources"]![0]!["key"]="unregistered";break;case "future-state":n["sources"]![0]!["state"]="Future";break;case "wrong-case-state":n["sources"]![0]!["state"]="complete";break;}});Assert.ThrowsAny<ToolkitException>(()=>DeploymentCoverageEvidence.Read(bytes,e.OriginalPages,TestData.TenantA));}
    [Fact] public async Task Original_uppercase_directory_identity_is_preserved_without_changing_the_admitted_row_set()
    {var f=new DeploymentCoverageServiceTests.Fixture();f.GroupAssignment();const string original="ABCDEFAB-CDEF-4ABC-8ABC-ABCDEFABCDEF";f.Members=f.Members.Replace(TestData.Operator,original);var e=DeploymentCoverageEvidence.Create(await f.Capture());var members=e.Metadata.Sources.Single(s=>s.Key.EndsWith(":members"));Assert.Equal(original,Assert.Single(members.RetainedRowIds));Assert.True(DeploymentCoverageEvidence.Read(e.MetadataBytes,e.OriginalPages,TestData.TenantA).IsHistorical);}
    [Fact] public async Task Original_schema_records_toolkit_and_source_identity_without_import_rewriting_them()
    {var e=await Evidence();var node=JsonNode.Parse(e.MetadataBytes)!;Assert.Equal(BuildStandard.Engine.ToolkitVersion.Current,node["toolkitVersion"]?.GetValue<string>());Assert.Equal("live",node["sourceMode"]?.GetValue<string>());var imported=DeploymentCoverageEvidence.Read(e.MetadataBytes,e.OriginalPages,TestData.TenantA);Assert.True(imported.IsHistorical);Assert.Equal(e.MetadataBytes,imported.MetadataBytes);}
    [Fact] public async Task Projected_source_details_do_not_append_a_second_interpretation_notice_after_import()
    {var e=await Evidence();var read=DeploymentCoverageEvidence.Read(e.MetadataBytes,e.OriginalPages,TestData.TenantA);var originals=e.Metadata.Sources;var sources=read.Sources();Assert.Equal(originals.Select(s=>s.Detail),sources.Select(s=>s.Capture.Detail));}
}
