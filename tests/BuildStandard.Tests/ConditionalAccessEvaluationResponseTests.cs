using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Reporting;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ConditionalAccessEvaluationResponseTests
{
    private static JsonObject Row()=>new(){["id"]=TestData.Operator,["displayName"]="Synthetic policy",["state"]="enabled",["policyApplies"]=true,["analysisReasons"]="notSet"};
    private static byte[] Bytes(JsonNode node)=>Encoding.UTF8.GetBytes(node.ToJsonString());
    private static JsonObject Envelope(JsonObject? row)=>new(){["value"]=new JsonArray(row)};
    [Fact] public void Known_applicability_is_hypothetical_only_and_preserves_original_response_identity()
    {
        var bytes=Bytes(Envelope(Row()));var p=ConditionalAccessEvaluationResponseCodec.Read(bytes);Assert.False(p.IsPartial);var row=Assert.Single(p.Rows);
        Assert.Equal(TestData.Operator,row.PolicyId);Assert.True(row.PolicyApplies);Assert.Equal("Applies",row.Meaning);Assert.Equal("notSet",row.AnalysisReasons);
        Assert.Equal(CanonicalJson.Sha256Hex(bytes),p.OriginalSha256);Assert.Contains("not a real sign-in",ConditionalAccessEvaluationResponseCodec.Limitations,StringComparison.OrdinalIgnoreCase);
    }
    [Theory] [InlineData("enabled")][InlineData("enabledForReportingButNotEnforced")]
    public void False_with_an_explicit_reason_is_retained_without_an_enforcement_claim(string state)
    {var r=Row();r["state"]=state;r["policyApplies"]=false;r["analysisReasons"]="users";var p=ConditionalAccessEvaluationResponseCodec.Read(Bytes(Envelope(r)));Assert.False(p.IsPartial);Assert.Equal("DoesNotApply",p.Rows[0].Meaning);Assert.False(p.Rows[0].PolicyApplies);}
    [Theory] [InlineData("missing-applies")][InlineData("null-applies")][InlineData("missing-state")][InlineData("null-state")]
    [InlineData("future-state")][InlineData("disabled")][InlineData("future-reason")][InlineData("null-reason")][InlineData("contradictory-reason")]
    public void Unknown_or_contradictory_values_never_become_a_negative_safe_result(string defect)
    {
        var r=Row();switch(defect)
        {
            case "missing-applies":r.Remove("policyApplies");break;case "null-applies":r["policyApplies"]=null;break;
            case "missing-state":r.Remove("state");break;case "null-state":r["state"]=null;break;case "future-state":r["state"]="futureEnabled";break;
            case "disabled":r["state"]="disabled";break;case "future-reason":r["analysisReasons"]="futureReason";break;
            case "null-reason":r["analysisReasons"]=null;break;case "contradictory-reason":r["analysisReasons"]="users";break;
        }
        var p=ConditionalAccessEvaluationResponseCodec.Read(Bytes(Envelope(r)));Assert.True(p.IsPartial);Assert.Equal("Unknown",p.Rows[0].Meaning);Assert.NotEmpty(p.ReadLimitations);
        if(defect=="future-state")Assert.Equal("futureEnabled",p.Rows[0].State);
    }
    [Fact] public void An_explicit_valid_empty_array_is_distinct_from_missing_data()
    {var p=ConditionalAccessEvaluationResponseCodec.Read(Bytes(new JsonObject{["value"]=new JsonArray()}));Assert.Empty(p.Rows);Assert.False(p.IsPartial);}
    [Theory] [InlineData("empty")][InlineData("null-root")][InlineData("array-root")][InlineData("missing-value")][InlineData("null-value")]
    [InlineData("null-row")][InlineData("non-object-row")][InlineData("wrong-subtype")][InlineData("bool-string")][InlineData("bool-number")]
    [InlineData("missing-id")][InlineData("wrong-id")][InlineData("duplicate-id")][InlineData("duplicate-property")][InlineData("oversize")]
    [InlineData("too-many")][InlineData("long-name")][InlineData("non-string-reason")][InlineData("unsafe-context")][InlineData("malformed-next")]
    public void Malformed_or_over_limit_reads_cannot_become_empty_success(string defect)
    {
        var r=Row();var body=Envelope(r);byte[]? raw=null;
        switch(defect)
        {
            case "empty":raw=[];break;case "null-root":raw=Encoding.UTF8.GetBytes("null");break;case "array-root":raw=Encoding.UTF8.GetBytes("[]");break;
            case "missing-value":body.Remove("value");break;case "null-value":body["value"]=null;break;case "null-row":body["value"]![0]=null;break;
            case "non-object-row":body["value"]![0]="row";break;case "wrong-subtype":r["@odata.type"]="#microsoft.graph.user";break;
            case "bool-string":r["policyApplies"]="false";break;case "bool-number":r["policyApplies"]=0;break;
            case "missing-id":r.Remove("id");break;case "wrong-id":r["id"]="not-a-policy";break;
            case "duplicate-id":body["value"]!.AsArray().Add(r.DeepClone());break;
            case "duplicate-property":raw=Encoding.UTF8.GetBytes("{\"value\":[],\"value\":[]}");break;
            case "oversize":raw=new byte[5*1024*1024+1];break;
            case "too-many":body["value"]=new JsonArray(Enumerable.Range(0,1001).Select(_=>{var unique=Row();unique["id"]=Guid.NewGuid().ToString();return unique;}).ToArray());break;
            case "long-name":r["displayName"]=new string('x',513);break;case "non-string-reason":r["analysisReasons"]=new JsonArray();break;
            case "unsafe-context":body["@odata.context"]="https://other.invalid/metadata";break;case "malformed-next":body["@odata.nextLink"]=true;break;
        }
        Assert.Throws<ConfigurationException>(()=>ConditionalAccessEvaluationResponseCodec.Read(raw??Bytes(body)));
    }
    [Fact] public void A_next_link_is_not_followed_or_mistaken_for_a_complete_array()
    {var b=Envelope(Row());b["@odata.nextLink"]="https://other.invalid/never-follow";var p=ConditionalAccessEvaluationResponseCodec.Read(Bytes(b));Assert.True(p.IsPartial);Assert.Single(p.Rows);Assert.Contains(p.ReadLimitations,x=>x.Contains("further",StringComparison.OrdinalIgnoreCase));}
    [Fact] public void Policy_metadata_is_not_treated_as_commands_or_additional_response_rows()
    {var r=Row();r["conditions"]=new JsonObject{["futureField"]="uninterpreted"};r["@odata.type"]="#microsoft.graph.whatIfAnalysisResult";var p=ConditionalAccessEvaluationResponseCodec.Read(Bytes(Envelope(r)));Assert.Single(p.Rows);Assert.False(p.IsPartial);}
    [Theory] [InlineData("notEnoughInformation")][InlineData("invalidCondition")][InlineData("emptyPolicy")][InlineData("invalidPolicy")][InlineData("policyNotEnabled")]
    public void A_service_false_caused_by_incomplete_or_invalid_evaluation_remains_unknown(string reason)
    {var r=Row();r["policyApplies"]=false;r["analysisReasons"]=reason;var p=ConditionalAccessEvaluationResponseCodec.Read(Bytes(Envelope(r)));Assert.True(p.IsPartial);Assert.False(p.Rows[0].PolicyApplies);Assert.Equal("Unknown",p.Rows[0].Meaning);}
    [Fact] public void One_thousand_distinct_valid_rows_are_within_the_bound()
    {var rows=Enumerable.Range(0,1000).Select(_=>{var r=Row();r["id"]=Guid.NewGuid().ToString();return r;}).ToArray();var p=ConditionalAccessEvaluationResponseCodec.Read(Bytes(new JsonObject{["value"]=new JsonArray(rows)}));Assert.Equal(1000,p.Rows.Count);Assert.False(p.IsPartial);}

}
