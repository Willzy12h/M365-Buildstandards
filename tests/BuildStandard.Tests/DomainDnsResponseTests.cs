using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Exchange;
using Xunit;

namespace BuildStandard.Tests;

public sealed class DomainDnsResponseTests
{
    private static JsonObject Response() => new()
    {
        ["name"]="example.invalid",["type"]="TXT",["state"]="Observed",
        ["startedAt"]="2026-10-18T12:00:00Z",["completedAt"]="2026-10-18T12:00:01Z",
        ["resolver"]="Windows configured DNS resolver",["adapterVersion"]="1",
        ["answers"]=new JsonArray(Txt("example.invalid", "v=spf1 ", "-all"))
    };
    private static JsonObject Txt(string name, params string[] chunks) => new()
    {
        ["owner"]=name,["type"]="TXT",["section"]="Answer",["ttl"]=300,
        ["textChunks"]=new JsonArray(chunks.Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray())
    };
    private static JsonObject Alias(string name,string target) => new()
        { ["owner"]=name,["type"]="CNAME",["section"]="Answer",["target"]=target };
    private static DomainDnsObservation Parse(JsonObject value,DomainDnsType type=DomainDnsType.TXT)
        =>DomainDnsResponse.Parse(value.ToJsonString(),new("example.invalid",type));

    [Fact] public void Txt_chunks_join_only_within_each_record_and_missing_ttl_stays_unknown()
    {
        var response=Response();response["answers"]!.AsArray().Add(Txt("EXAMPLE.invalid.","other policy"));
        response["answers"]![1]!.AsObject().Remove("ttl");
        var result=Parse(response);Assert.Equal(DomainDnsState.Observed,result.State);Assert.Equal(2,result.Answers.Count);
        Assert.Equal("v=spf1 -all",result.Answers[0].Text);Assert.Equal(2,result.Answers[0].TextChunks!.Count);
        Assert.Equal("other policy",result.Answers[1].Text);Assert.Null(result.Answers[1].Ttl);
        Assert.Equal("EXAMPLE.invalid.",result.Answers[1].Owner);
    }
    [Fact] public void Connected_alias_chain_admits_mx_and_preserves_raw_values_and_preference()
    {
        var response=Response();response["type"]="MX";response["answers"]=new JsonArray(
            Alias("example.invalid","alias.example.invalid."),
            new JsonObject { ["owner"]="ALIAS.example.invalid",["type"]="MX",["section"]="Answer",["ttl"]=0,["exchange"]="gateway.example.invalid.",["preference"]=10 });
        var result=Parse(response,DomainDnsType.MX);Assert.Equal(2,result.Answers.Count);
        Assert.Equal("gateway.example.invalid.",result.Answers[1].Exchange);Assert.Equal(10,result.Answers[1].Preference);Assert.Equal(0,result.Answers[1].Ttl);
    }
    [Theory] [InlineData("NameError",9003)] [InlineData("NoData",9501)]
    public void Native_negative_does_not_invent_absence_at_original_owner(string state,int code)
    {
        var response=Response();response["state"]=state;response["nativeErrorCode"]=code;response["answers"]=new JsonArray();
        var result=Parse(response);Assert.Equal(state,result.State.ToString());Assert.Null(result.NegativeOwner);
        Assert.Contains("unknown",result.Reason!,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void Negative_owner_is_admitted_only_when_bound_through_observed_aliases()
    {
        var response=Response();response["state"]="NameError";response["nativeErrorCode"]=9003;
        response["answers"]=new JsonArray(Alias("example.invalid","missing.example.invalid"));response["negativeOwner"]="missing.example.invalid";
        Assert.Equal("missing.example.invalid",Parse(response).NegativeOwner);
        response["negativeOwner"]="unrelated.invalid";Assert.Throws<ConfigurationException>(()=>Parse(response));
    }
    [Theory] [InlineData("Failed")] [InlineData("Limited")] [InlineData("Cancelled")]
    public void Incomplete_states_retain_prior_answers_without_claiming_a_complete_read(string state)
    {
        var response=Response();response["state"]=state;response["reason"]="Collection interrupted";
        var result=Parse(response);Assert.Equal(state,result.State.ToString());Assert.Single(result.Answers);
    }
    [Theory]
    [InlineData("wrong-name")][InlineData("wrong-type")][InlineData("unknown-state")][InlineData("state-number")]
    [InlineData("missing-name")][InlineData("missing-section")][InlineData("wrong-section")][InlineData("null-owner")]
    [InlineData("wrong-owner")][InlineData("wrong-answer-type")][InlineData("null-chunk")][InlineData("no-chunks")]
    [InlineData("bad-ttl")][InlineData("empty-answers")][InlineData("null-answers")][InlineData("bad-start")]
    [InlineData("reverse-time")][InlineData("offset-time")][InlineData("unknown-field")][InlineData("unknown-answer-field")]
    [InlineData("unbound-negative")][InlineData("wrong-native-code")][InlineData("success-with-error")]
    [InlineData("alias-loop")][InlineData("alias-branch")][InlineData("unrelated-alias")][InlineData("too-many-aliases")]
    [InlineData("mixed-records")][InlineData("missing-adapter")][InlineData("invented-resolver")]
    public void Unbound_malformed_or_ambiguous_output_is_refused_in_full(string defect)
    {
        var r=Response();var answer=r["answers"]![0]!.AsObject();
        switch(defect)
        {
            case "wrong-name":r["name"]="other.invalid";break;
            case "wrong-type":r["type"]="MX";break;
            case "unknown-state":r["state"]="Present";break;
            case "state-number":r["state"]=0;break;
            case "missing-name":r.Remove("name");break;
            case "missing-section":answer.Remove("section");break;
            case "wrong-section":answer["section"]="Additional";break;
            case "null-owner":answer["owner"]=null;break;
            case "wrong-owner":answer["owner"]="other.invalid";break;
            case "wrong-answer-type":answer["type"]="AAAA";break;
            case "null-chunk":answer["textChunks"]=new JsonArray((JsonNode?)null);break;
            case "no-chunks":answer["textChunks"]=new JsonArray();break;
            case "bad-ttl":answer["ttl"]=-1;break;
            case "empty-answers":r["answers"]=new JsonArray();break;
            case "null-answers":r["answers"]=null;break;
            case "bad-start":r["startedAt"]="yesterday";break;
            case "reverse-time":r["completedAt"]="2026-10-18T11:00:00Z";break;
            case "offset-time":r["startedAt"]="2026-10-18T12:00:00+01:00";break;
            case "unknown-field":r["mystery"]=true;break;
            case "unknown-answer-field":answer["mystery"]=true;break;
            case "unbound-negative":r["negativeOwner"]="other.invalid";break;
            case "wrong-native-code":r["state"]="NameError";r["nativeErrorCode"]=9501;r["answers"]=new JsonArray();break;
            case "success-with-error":r["nativeErrorCode"]=9003;break;
            case "alias-loop":r["answers"]=new JsonArray(Alias("example.invalid","a.invalid"),Alias("a.invalid","example.invalid"),Txt("a.invalid","v=spf1 -all"));break;
            case "alias-branch":r["answers"]=new JsonArray(Alias("example.invalid","a.invalid"),Alias("example.invalid","b.invalid"),Txt("a.invalid","v=spf1 -all"));break;
            case "unrelated-alias":r["answers"]!.AsArray().Add(Alias("unrelated.invalid","other.invalid"));break;
            case "too-many-aliases":
                var rows=new JsonArray();var previous="example.invalid";
                for(var i=0;i<11;i++){var next=$"alias{i}.invalid";rows.Add(Alias(previous,next));previous=next;}
                rows.Add(Txt(previous,"v=spf1 -all"));r["answers"]=rows;break;
            case "mixed-records":r["answers"]!.AsArray().Add(Txt("unrelated.invalid","v=spf1 -all"));break;
            case "missing-adapter":r.Remove("adapterVersion");break;
            case "invented-resolver":r["resolver"]="Authoritative DNSSEC verified";break;
        }
        Assert.Throws<ConfigurationException>(()=>Parse(r));
    }
    [Fact] public void Duplicate_fields_and_output_limits_refuse_without_truncation()
    {
        var json=Response().ToJsonString();
        Assert.Throws<ConfigurationException>(()=>DomainDnsResponse.Parse(json.Replace("\"type\":\"TXT\"","\"type\":\"MX\",\"type\":\"TXT\""),new("example.invalid",DomainDnsType.TXT)));
        Assert.Throws<ConfigurationException>(()=>DomainDnsResponse.Parse(new string(' ',65537),new("example.invalid",DomainDnsType.TXT)));
        var r=Response();r["answers"]=new JsonArray(Enumerable.Range(0,101).Select(_=>(JsonNode?)Txt("example.invalid","value")).ToArray());
        Assert.Throws<ConfigurationException>(()=>Parse(r));
    }
    [Theory] [InlineData("")][InlineData("null")][InlineData("{}")][InlineData("[]")]
    public void Missing_output_is_not_absence(string json)
        =>Assert.Throws<ConfigurationException>(()=>DomainDnsResponse.Parse(json,new("example.invalid",DomainDnsType.TXT)));
}
