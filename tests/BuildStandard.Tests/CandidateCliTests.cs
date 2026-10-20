using System.Text.Json.Nodes;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Engine.Standards.Authoring;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CandidateCliTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Offline_policy_import_saves_a_separate_draft_and_exposes_unresolved_state(bool foreign)
    {
        using var root = new TempRoot();
        root.WriteStandard("base.json", BuildStandard.Core.Json.ToolkitJson.Serialize(DevicePolicyImportTests.Shipped())); root.WriteManifest();
        var manifest = File.ReadAllBytes(root.Paths.StandardsManifestFile);
        var loader = new CandidateBaselineLoader(root.Paths, NullLog.Instance); var draft = loader.Create("base.json", "Imported candidate");
        var input = Path.Combine(root.Root, "draft.json"); File.WriteAllText(input, CandidateDraftCodec.Serialize(draft));
        var source = loader.Load(draft).FindControl("CFG-WIN-002")!.Payload!.DeepClone();
        source["omaSettings"]![2]!["value"] = 22;
        if (foreign) source["omaSettings"]![0]!["displayName"] = TestData.Office;
        var policy = Path.Combine(root.Root, "policy.json"); File.WriteAllText(policy, source.ToJsonString());
        var result = await RegisteredReportCliTests.Run("candidate", "--action", "import", "--input", input,
            "--control", "CFG-WIN-002", "--source", policy, "--name", "CFG - Candidate - Windows", "--root", root.Root);
        Assert.True(result.Exit == 0, result.Output + result.Error);
        var output = JsonNode.Parse(result.Output)!;
        Assert.Equal(foreign ? "Unresolved" : "ReadyForValidation", output["status"]!.GetValue<string>());
        var saved = output["draft"]!.GetValue<string>(); Assert.NotEqual(input, saved);
        var imported = CandidateDraftCodec.ReadFile(saved); Assert.Equal(foreign ? 1 : 0, imported.Unresolved.Count);
        var validation = await RegisteredReportCliTests.Run("candidate", "--action", "validate", "--input", saved, "--root", root.Root);
        Assert.Equal(foreign ? 2 : 0, validation.Exit);
        Assert.Equal(manifest, File.ReadAllBytes(root.Paths.StandardsManifestFile));
        Assert.Equal(draft.ContentDigest, CandidateDraftCodec.ReadFile(input).ContentDigest);
    }
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
    [Theory][InlineData("valid", 0)][InlineData("duplicate", 2)][InlineData("not-string", 2)][InlineData("unknown-parameter", 2)]
    public async Task Import_mapping_file_is_strict_and_uses_existing_typed_parameters(string mode, int expected)
    {
        using var root = new TempRoot();
        root.WriteStandard("base.json", BuildStandard.Core.Json.ToolkitJson.Serialize(DevicePolicyImportTests.Shipped())); root.WriteManifest();
        var loader = new CandidateBaselineLoader(root.Paths, NullLog.Instance); var draft = loader.Create("base.json", "Mapped candidate");
        var input = Path.Combine(root.Root, "draft.json"); File.WriteAllText(input, CandidateDraftCodec.Serialize(draft));
        var source = loader.Load(draft).FindControl("CFG-WIN-002")!.Payload!.DeepClone(); source["omaSettings"]![0]!["displayName"] = TestData.Office;
        var policy = Path.Combine(root.Root, "policy.json"); File.WriteAllText(policy, source.ToJsonString());
        const string path = "/payload/omaSettings/0/displayName";
        var mapping = Path.Combine(root.Root, "mapping.json"); File.WriteAllText(mapping, mode switch
        {
            "duplicate" => "{\"" + path + "\":\"officeLocationId\",\"" + path + "\":\"pilotGroupId\"}",
            "not-string" => "{\"" + path + "\":false}",
            "unknown-parameter" => "{\"" + path + "\":\"notDeclared\"}",
            _ => "{\"" + path + "\":\"officeLocationId\"}"
        });
        var result = await RegisteredReportCliTests.Run("candidate", "--action", "import", "--input", input, "--control", "CFG-WIN-002",
            "--source", policy, "--name", "CFG - Candidate - Windows", "--map", mapping, "--root", root.Root);
        Assert.True(result.Exit == expected, result.Output + result.Error);
        if (expected == 0) Assert.Equal("ReadyForValidation", JsonNode.Parse(result.Output)!["status"]!.GetValue<string>());
        else Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
    }
}
