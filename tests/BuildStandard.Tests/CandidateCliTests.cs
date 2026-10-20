using System.Text.Json.Nodes;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Engine.Standards.Authoring;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CandidateCliTests
{
    [Fact]public async Task Offline_create_validate_export_use_the_shared_draft_without_publishing()
    {
        using var root=new TempRoot();root.WriteStandard("test.json",TestData.StandardJson);root.WriteManifest();var original=File.ReadAllBytes(root.Paths.StandardsManifestFile);
        var created=await RegisteredReportCliTests.Run("candidate","--action","create","--catalogue","test.json","--title","Synthetic candidate","--root",root.Root);Assert.True(created.Exit==0,created.Output+created.Error);
        var file=JsonNode.Parse(created.Output)!["draft"]!.GetValue<string>();var draft=CandidateDraftCodec.Read(File.ReadAllText(file));
        var validated=await RegisteredReportCliTests.Run("candidate","--action","validate","--input",file,"--root",root.Root);Assert.True(validated.Exit==0,validated.Error);Assert.Equal(draft.ContentDigest,JsonNode.Parse(validated.Output)!["draftDigest"]!.GetValue<string>());
        var exported=await RegisteredReportCliTests.Run("candidate","--action","export","--input",file,"--reviewed-digest",draft.ContentDigest,"--root",root.Root);Assert.True(exported.Exit==0,exported.Error);Assert.True(File.Exists(JsonNode.Parse(exported.Output)!["catalogue"]!.GetValue<string>()));Assert.Equal(original,File.ReadAllBytes(root.Paths.StandardsManifestFile));
    }
    [Theory][InlineData("permission")][InlineData("duplicate")][InlineData("stale")][InlineData("oversize")]
    public async Task Unsupported_options_or_invalid_drafts_are_refused(string fault)
    {
        using var root=new TempRoot();root.WriteStandard("test.json",TestData.StandardJson);root.WriteManifest();var loader=new CandidateBaselineLoader(root.Paths,NullLog.Instance);var draft=loader.Create("test.json","Synthetic");var file=Path.Combine(root.Root,"draft.json");File.WriteAllText(file,CandidateDraftCodec.Serialize(draft));
        var args=new List<string>{"candidate","--action","export","--input",file,"--reviewed-digest",fault=="stale"?new string('b',64):draft.ContentDigest,"--root",root.Root};
        if(fault=="permission")args.AddRange(["--scope","Directory.Read.All"]);if(fault=="duplicate")args.AddRange(["--input",file]);if(fault=="oversize")using(var stream=File.OpenWrite(file))stream.SetLength(CandidateDraftCodec.MaximumBytes+1);
        var result=await RegisteredReportCliTests.Run(args.ToArray());Assert.Equal(2,result.Exit);Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
    }
}
