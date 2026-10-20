using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Standards.Authoring;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CandidateDraftCodecTests
{
    internal static StandardCandidateDraft Draft() => new()
    {
        Kind="standard-candidate-draft",SchemaVersion=1,Baseline=new("2026.10.15",new string('a',64),5),Title="Synthetic candidate",
        Changes=[new("CA-001","/recipe/payload/displayName",JsonValue.Create("CA - Old"),JsonValue.Create("CA - New"))],
        Imports=[],Unresolved=[],AssignmentObservations=[],ContentDigest=""
    };
    [Fact]public void Roundtrip_preserves_values_and_exact_baseline()
    {var draft=Draft();CandidateDraftCodec.Seal(draft);var read=CandidateDraftCodec.Read(CandidateDraftCodec.Serialize(draft));Assert.Equal(draft.Baseline,read.Baseline);Assert.Equal(draft.ContentDigest,read.ContentDigest);Assert.Equal("CA - New",read.Changes[0].Proposed!.GetValue<string>());}
    [Fact]public void Saved_time_is_not_candidate_identity()
    {var draft=Draft();CandidateDraftCodec.Seal(draft);var digest=draft.ContentDigest;draft.SavedAtUtc=DateTimeOffset.Parse("2026-10-20T00:00:00Z");CandidateDraftCodec.Seal(draft);Assert.Equal(digest,draft.ContentDigest);}
    [Fact]public void Unresolved_drafts_are_saveable_without_claiming_validation()
    {var draft=Draft();draft.Imports.Add(new(new string('b',64),"CA-001","conditionalAccess"));draft.Unresolved.Add(new(new string('b',64),"CA-001","/recipe/payload/conditions","Foreign reference needs mapping"));CandidateDraftCodec.Seal(draft);Assert.Single(CandidateDraftCodec.Read(CandidateDraftCodec.Serialize(draft)).Unresolved);}
    [Theory][InlineData("unknown")][InlineData("duplicate")][InlineData("missing")][InlineData("null")][InlineData("tamper")][InlineData("version")]
    public void Invalid_envelopes_are_refused(string fault)
    {
        var draft=Draft();CandidateDraftCodec.Seal(draft);var json=CandidateDraftCodec.Serialize(draft);var node=JsonNode.Parse(json)!.AsObject();
        switch(fault){case "unknown":node["permission"]="anything";break;case "duplicate":json=json.Insert(json.IndexOf('{')+1,"\"TITLE\":\"Other\",");break;case "missing":node.Remove("changes");break;case "null":node["changes"]=null;break;case "tamper":node["title"]="Tampered";break;case "version":node["schemaVersion"]=2;break;}
        Assert.Throws<ConfigurationException>(()=>CandidateDraftCodec.Read(fault=="duplicate"?json:node.ToJsonString()));
    }
    [Theory][InlineData("duplicate")][InlineData("ancestor")][InlineData("import")][InlineData("unresolved")][InlineData("bounds")][InlineData("digest")][InlineData("assignment")]
    public void Invalid_authored_input_cannot_be_sealed(string fault)
    {
        var draft=Draft();switch(fault){case "duplicate":draft.Changes.Add(draft.Changes[0] with{ControlId="ca-001"});break;case "ancestor":draft.Changes.Add(draft.Changes[0] with{Path="/recipe/payload"});break;case "import":draft.Imports.Add(new(new string('b',64),"CA-001",""));break;case "unresolved":draft.Unresolved.Add(new(new string('b',64),"CA-001","/a","Missing import"));break;case "bounds":draft.Changes=Enumerable.Range(0,1001).Select(i=>draft.Changes[0] with{Path="/recipe/payload/p"+i}).ToList();break;case "digest":draft.Baseline=draft.Baseline with{FileSha256="abc"};break;case "assignment":draft.AssignmentObservations.Add(new(new string('b',64),"CA-001","Empty","Not observed"));break;}
        Assert.Throws<ConfigurationException>(()=>CandidateDraftCodec.Seal(draft));
    }
    [Fact]public void Oversize_text_is_refused_before_json_parsing()=>Assert.Throws<ConfigurationException>(()=>CandidateDraftCodec.Read(new string(' ',CandidateDraftCodec.MaximumBytes+1)));
}
