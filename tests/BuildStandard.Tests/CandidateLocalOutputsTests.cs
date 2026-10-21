using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Standards;
using BuildStandard.Engine.Standards.Authoring;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CandidateLocalOutputsTests
{
    private static (CandidateLocalOutputs Outputs,StandardCandidateDraft Draft,CandidateValidationTicket Ticket) Setup(TempRoot root)
    {root.WriteStandard("test.json",TestData.StandardJson);root.WriteManifest();var loader=new CandidateBaselineLoader(root.Paths,NullLog.Instance);var draft=loader.Create("test.json","Synthetic <script> example");var service=new CandidateAuthoringService(loader);return(new(root.Paths,service),draft,CandidateValidationTicket.Create(service,draft));}
    [Fact]public void Export_keeps_catalogue_draft_and_encoded_comparison_separate_without_publishing()
    {
        using var root=new TempRoot();var(o,d,t)=Setup(root);var manifest=File.ReadAllBytes(root.Paths.StandardsManifestFile);var standard=File.ReadAllBytes(Path.Combine(root.Paths.StandardsDirectory,"test.json"));var files=o.Export(d,t);
        Assert.Equal("Candidate",StandardsLoader.Parse(File.ReadAllText(files.Catalogue),"candidate.json").Status);Assert.Equal(d.ContentDigest,CandidateDraftCodec.Read(File.ReadAllText(files.Draft)).ContentDigest);
        var html=File.ReadAllText(files.Comparison);Assert.Contains("&lt;script&gt;",html);Assert.DoesNotContain("<script>",html);Assert.Contains("Assignment",html);Assert.Contains("not a published standard",html);
        foreach(var line in File.ReadAllLines(files.Checksums)){var parts=line.Split("  ",2);Assert.Equal(parts[0],CanonicalJson.Sha256Hex(File.ReadAllBytes(Path.Combine(root.Paths.ReportsDirectory,parts[1]))));}
        Assert.Equal(manifest,File.ReadAllBytes(root.Paths.StandardsManifestFile));Assert.Equal(standard,File.ReadAllBytes(Path.Combine(root.Paths.StandardsDirectory,"test.json")));Assert.Equal(2,Directory.GetFiles(root.Paths.StandardsDirectory).Length);
    }
    [Fact]public void Unresolved_draft_is_saveable_to_new_files_without_candidate_export()
    {
        using var root=new TempRoot();var(o,d,_)=Setup(root);var hash=new string('a',64);d.Imports.Add(new(hash,"CA-001","conditionalAccess"));d.Unresolved.Add(new(hash,"CA-001","/payload/conditions","Unknown imported target"));CandidateDraftCodec.Seal(d);
        var first=o.ExportDraft(d);var original=File.ReadAllBytes(first);var second=o.ExportDraft(d);Assert.NotEqual(first,second);Assert.Equal(original,File.ReadAllBytes(first));Assert.Single(CandidateDraftCodec.Read(File.ReadAllText(first)).Unresolved);
    }
    [Fact]public void Stale_ticket_writes_nothing()
    {using var root=new TempRoot();var(o,d,t)=Setup(root);d.Title="Changed";CandidateDraftCodec.Seal(d);Assert.ThrowsAny<ToolkitException>(()=>o.Export(d,t));Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));}
    [Fact]public void Cancellation_after_first_output_reports_incomplete_paths_without_deleting_files()
    {
        using var root=new TempRoot();var(o,d,t)=Setup(root);var previous=Path.Combine(root.Paths.ReportsDirectory,"previous.txt");File.WriteAllText(previous,"Keep");using var ct=new CancellationTokenSource();
        var failure = Assert.Throws<IOException>(() => o.Export(d, t, ct.Token, new InlineProgress(_ => ct.Cancel())));
        Assert.IsAssignableFrom<OperationCanceledException>(failure.InnerException);
        var created = Directory.GetFiles(root.Paths.ReportsDirectory).Single(path => path != previous);
        Assert.Contains(created, failure.Message); Assert.Contains("No complete export", failure.Message);
        Assert.Equal("Keep", File.ReadAllText(previous));
        Assert.DoesNotContain(Directory.GetFiles(root.Paths.ReportsDirectory), path => path.EndsWith(".sha256", StringComparison.Ordinal));
    }
    [Fact]public void Cancellation_never_deletes_a_replacement_at_an_output_path()
    {
        using var root = new TempRoot(); var (outputs, draft, ticket) = Setup(root);
        using var cancellation = new CancellationTokenSource(); string? replaced = null;
        var failure = Record.Exception(() => outputs.Export(draft, ticket, cancellation.Token, new InlineProgress(_ =>
        {
            replaced = Directory.GetFiles(root.Paths.ReportsDirectory).Single();
            File.Delete(replaced); File.WriteAllText(replaced, "Unrelated replacement"); cancellation.Cancel();
        })));
        Assert.NotNull(failure); Assert.NotNull(replaced);
        Assert.True(File.Exists(replaced), "Cancellation must not delete a replacement owned by another writer.");
        Assert.Equal("Unrelated replacement", File.ReadAllText(replaced));
        Assert.Contains(replaced, failure.Message);
    }
    [Fact]public void A_failed_output_location_does_not_change_the_original_catalogue()
    {
        using var root=new TempRoot();var(o,d,t)=Setup(root);Directory.Delete(root.Paths.ReportsDirectory);File.WriteAllText(root.Paths.ReportsDirectory,"Not a folder");var original=File.ReadAllBytes(Path.Combine(root.Paths.StandardsDirectory,"test.json"));
        Assert.ThrowsAny<IOException>(()=>o.Export(d,t));Assert.Equal("Not a folder",File.ReadAllText(root.Paths.ReportsDirectory));Assert.Equal(original,File.ReadAllBytes(Path.Combine(root.Paths.StandardsDirectory,"test.json")));
    }
    private sealed class InlineProgress(Action<string> action):IProgress<string>{public void Report(string value)=>action(value);}
}
