using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Standards.Authoring;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CandidateBaselineLoaderTests
{
    [Fact]public void New_draft_binds_manifest_verified_bytes_without_modifying_the_source()
    {
        using var root=new TempRoot();root.WriteStandard("test.json",TestData.StandardJson);root.WriteManifest();
        var path=Path.Combine(root.Paths.StandardsDirectory,"test.json");var bytes=File.ReadAllBytes(path);var loader=new CandidateBaselineLoader(root.Paths,NullLog.Instance);
        var draft=loader.Create("test.json","Synthetic local candidate");var baseline=loader.Load(draft);
        Assert.Equal(CanonicalJson.Sha256Hex(bytes),draft.Baseline.FileSha256);Assert.Equal(baseline.Release,draft.Baseline.Release);Assert.Empty(draft.Changes);Assert.Equal(bytes,File.ReadAllBytes(path));
    }
    [Fact]public void Matching_release_text_cannot_rebind_to_changed_bytes_even_with_updated_manifest()
    {
        using var root=new TempRoot();root.WriteStandard("test.json",TestData.StandardJson);root.WriteManifest();var loader=new CandidateBaselineLoader(root.Paths,NullLog.Instance);var draft=loader.Create("test.json","Candidate");
        root.WriteStandard("test.json",TestData.StandardJson+"\n");root.WriteManifest();Assert.ThrowsAny<ToolkitException>(()=>loader.Load(draft));
    }
    [Fact]public void Moving_identical_manifest_registered_bytes_does_not_change_identity()
    {
        using var root=new TempRoot();root.WriteStandard("test.json",TestData.StandardJson);root.WriteManifest();var loader=new CandidateBaselineLoader(root.Paths,NullLog.Instance);var draft=loader.Create("test.json","Candidate");
        File.Move(Path.Combine(root.Paths.StandardsDirectory,"test.json"),Path.Combine(root.Paths.StandardsDirectory,"renamed.json"));root.WriteManifest();Assert.Equal(draft.Baseline.FileSha256,loader.Load(draft).IntegrityDigest);
    }
    [Theory][InlineData("release")][InlineData("schema")][InlineData("missing")][InlineData("tamper")]
    public void Missing_changed_or_mismatched_baseline_is_refused(string fault)
    {
        using var root=new TempRoot();root.WriteStandard("test.json",TestData.StandardJson);root.WriteManifest();var loader=new CandidateBaselineLoader(root.Paths,NullLog.Instance);var draft=loader.Create("test.json","Candidate");
        switch(fault){case "release":draft.Baseline=draft.Baseline with{Release="other"};CandidateDraftCodec.Seal(draft);break;case "schema":draft.Baseline=draft.Baseline with{CatalogueSchemaVersion=4};CandidateDraftCodec.Seal(draft);break;case "missing":File.Delete(Path.Combine(root.Paths.StandardsDirectory,"test.json"));break;case "tamper":root.WriteStandard("test.json",TestData.StandardJson+" ");break;}
        Assert.ThrowsAny<ToolkitException>(()=>loader.Load(draft));
    }
}
