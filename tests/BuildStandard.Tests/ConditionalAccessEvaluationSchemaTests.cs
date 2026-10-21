using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ConditionalAccessEvaluationSchemaTests
{
    internal static byte[] Response()=>Encoding.UTF8.GetBytes("{\"value\":[{\"id\":\""+TestData.Emergency+"\",\"displayName\":\"Synthetic policy\",\"state\":\"enabled\",\"policyApplies\":true,\"analysisReasons\":\"notSet\"}]}");
    internal static ConditionalAccessEvaluationReport Valid(byte[]? response=null)
    {
        response??=Response();var scenario=new ConditionalAccessScenario(TestData.Operator,TestData.TenantA,"windows","browser");
        var r=new ConditionalAccessEvaluationReport{Id=TestData.Emergency,TenantId=TestData.TenantA,AccountObjectId=TestData.Operator,ClientId=TestData.Emergency,
            StartedAt="2026-10-20T00:00:00.000Z",CompletedAt="2026-10-20T00:00:01.000Z",ToolkitVersion="1.1.0-preview.19",Scopes=["Policy.Read.All"],Scenario=scenario,
            RequestSha256=ConditionalAccessScenarioCodec.Prepare(scenario).Sha256,ResponseSha256=CanonicalJson.Sha256Hex(response),ResponseBytes=response.Length,Status="Complete"};
        r.IntegrityDigest=EvidenceIntegrity.Compute(r);return r;
    }
    private static byte[] Bytes(ConditionalAccessEvaluationReport r)=>Encoding.UTF8.GetBytes(ToolkitJson.Serialize(r));
    [Fact] public void Original_source_bytes_are_bound_to_separate_typed_metadata_without_base64_or_reformatting()
    {var raw=Response();var r=Valid(raw);var read=ConditionalAccessEvaluationSchema.Read(Bytes(r),raw,TestData.TenantA);Assert.Equal(r.IntegrityDigest,read.IntegrityDigest);Assert.Equal(CanonicalJson.Sha256Hex(raw),read.ResponseSha256);Assert.DoesNotContain("value",ToolkitJson.Serialize(read),StringComparison.Ordinal);}
    [Fact] public void A_different_expected_tenant_is_refused_even_with_consistent_digests()
    {var r=Valid();Assert.Throws<TenantMismatchException>(()=>ConditionalAccessEvaluationSchema.Read(Bytes(r),Response(),TestData.TenantB));}
    [Theory][InlineData("kind")][InlineData("schema")][InlineData("adapter")][InlineData("id")][InlineData("actor")][InlineData("client")][InlineData("resource")]
    [InlineData("source-mode")][InlineData("time")][InlineData("negative-interval")][InlineData("over-budget")][InlineData("request-digest")]
    [InlineData("response-digest")][InlineData("response-length")][InlineData("status")][InlineData("scope-null")][InlineData("scope-value-null")][InlineData("duplicate-scope")]
    [InlineData("scenario-null")][InlineData("mac-scenario")]
    public void Resealed_inconsistent_metadata_cannot_be_imported_as_a_success(string defect)
    {
        var r=Valid();switch(defect)
        {
            case "kind":r.Kind="tenantSnapshot";break;case "schema":r.SchemaVersion=2;break;case "adapter":r.AdapterVersion="unknown/1";break;
            case "id":r.Id="";break;case "actor":r.AccountObjectId="unknown";break;case "client":r.ClientId="unknown";break;case "resource":r.Resource="ExchangeOnline";break;
            case "source-mode":r.SourceMode="trusted-import";break;case "time":r.StartedAt="yesterday";break;case "negative-interval":r.CompletedAt="2026-10-19T23:00:00.000Z";break;
            case "over-budget":r.CompletedAt="2026-10-20T00:02:01.000Z";break;case "request-digest":r.RequestSha256=new string('a',64);break;
            case "response-digest":r.ResponseSha256=new string('a',64);break;case "response-length":r.ResponseBytes++;break;case "status":r.Status="VerifiedProtection";break;
            case "scope-null":r.Scopes=null!;break;case "scope-value-null":r.Scopes=[null!];break;case "duplicate-scope":r.Scopes.Add("policy.read.all");break;
            case "scenario-null":r.Scenario=null!;break;case "mac-scenario":r.Scenario=r.Scenario with{DevicePlatform="macOS"};break;
        }
        r.IntegrityDigest=EvidenceIntegrity.Compute(r);Assert.ThrowsAny<ToolkitException>(()=>ConditionalAccessEvaluationSchema.Read(Bytes(r),Response(),TestData.TenantA));
    }
    [Fact] public void A_resealed_complete_claim_cannot_hide_an_unknown_source_result()
    {var raw=Encoding.UTF8.GetBytes("{\"value\":[{\"id\":\""+TestData.Emergency+"\",\"state\":\"future\",\"policyApplies\":null}]}");var r=Valid(raw);Assert.Throws<ConfigurationException>(()=>ConditionalAccessEvaluationSchema.Read(Bytes(r),raw));r.Status="Partial";r.IntegrityDigest=EvidenceIntegrity.Compute(r);Assert.Equal("Partial",ConditionalAccessEvaluationSchema.Read(Bytes(r),raw).Status);}
    [Theory][InlineData("extra")][InlineData("nested-extra")][InlineData("null")][InlineData("version-string")][InlineData("numeric-mode")][InlineData("case-mode")][InlineData("missing-id")][InlineData("scenario-bool-string")][InlineData("response-file-path")]
    public void Unknown_fields_nulls_and_coercions_are_refused(string defect)
    {
        var json=ToolkitJson.ParseObject(ToolkitJson.Serialize(Valid()));byte[]? raw=null;
        switch(defect){case "extra":json["extra"]=true;break;case "nested-extra":json["scenario"]!["extra"]=true;break;case "null":raw=Encoding.UTF8.GetBytes("null");break;
            case "version-string":json["schemaVersion"]="1";break;case "numeric-mode":json["permissionMode"]=0;break;case "case-mode":json["permissionMode"]="assessment";break;
            case "missing-id":json.Remove("id");break;case "scenario-bool-string":json["scenario"]!["deviceCompliant"]="false";break;case "response-file-path":json["responseFile"]="../../other.json";break;}
        Assert.ThrowsAny<ToolkitException>(()=>ConditionalAccessEvaluationSchema.Read(raw??Encoding.UTF8.GetBytes(json.ToJsonString()),Response()));
    }
    [Fact] public void Duplicate_properties_and_changed_original_source_cannot_be_silently_normalised()
    {
        var r=Valid();var json=ToolkitJson.Serialize(r).Replace("\"schemaVersion\": 1","\"schemaVersion\": 1, \"schemaVersion\": 1",StringComparison.Ordinal);
        Assert.Throws<ConfigurationException>(()=>ConditionalAccessEvaluationSchema.Read(Encoding.UTF8.GetBytes(json),Response()));
        var changed=Response().Concat(new byte[]{32}).ToArray();Assert.ThrowsAny<ToolkitException>(()=>ConditionalAccessEvaluationSchema.Read(Bytes(r),changed));
    }
    [Fact] public void Seal_checks_the_same_semantics_and_cannot_upgrade_a_partial_source()
    {var r=Valid();r.IntegrityDigest="";ConditionalAccessEvaluationSchema.Seal(r,Response());Assert.True(EvidenceIntegrity.Verify(r,r.IntegrityDigest));Assert.Equal(r.IntegrityDigest,ConditionalAccessEvaluationSchema.Read(Bytes(r),Response()).IntegrityDigest);}
}
