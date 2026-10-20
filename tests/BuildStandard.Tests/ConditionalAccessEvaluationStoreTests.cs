using BuildStandard.Core;
using BuildStandard.Engine.Evidence;
using Xunit;
namespace BuildStandard.Tests;

public sealed class ConditionalAccessEvaluationStoreTests
{
    [Fact] public void Original_report_is_durable_separate_from_snapshots_and_loaded_as_historical()
    {
        using var root=new TempRoot();var store=new EvidenceStore(root.Paths,BuildStandard.Core.Diagnostics.NullLog.Instance);var capture=ConditionalAccessEvaluationExportTests.Capture();
        var file=store.SaveConditionalAccessEvaluation(capture);Assert.True(File.Exists(file));Assert.Contains("ca-evaluations",file);
        var read=store.LoadConditionalAccessEvaluation(TestData.TenantA,capture.Metadata.Id)!;
        Assert.Equal(capture.MetadataBytes,read.MetadataBytes);Assert.Equal(capture.OriginalResponseBytes,read.OriginalResponseBytes);Assert.True(read.IsHistorical);
        Assert.Empty(store.ListSnapshots(TestData.TenantA));Assert.Null(store.LoadConditionalAccessEvaluation(TestData.TenantB,capture.Metadata.Id));
        Assert.DoesNotContain(Directory.EnumerateFiles(root.Paths.TenantsDirectory,"*",SearchOption.AllDirectories),f=>f.EndsWith(".tmp",StringComparison.Ordinal));
    }
    [Fact] public void Existing_ID_is_never_replaced_even_with_identical_bytes()
    {
        using var root=new TempRoot();var store=new EvidenceStore(root.Paths,BuildStandard.Core.Diagnostics.NullLog.Instance);var capture=ConditionalAccessEvaluationExportTests.Capture();var file=store.SaveConditionalAccessEvaluation(capture);var original=File.ReadAllBytes(file);
        Assert.Throws<SafetyViolationException>(()=>store.SaveConditionalAccessEvaluation(capture));Assert.Equal(original,File.ReadAllBytes(file));
    }
    [Theory][InlineData("../elsewhere")][InlineData("")][InlineData("00000000-0000-0000-0000-000000000000")]
    public void Path_identifiers_are_refused_before_filesystem_use(string id)
    {using var root=new TempRoot();var store=new EvidenceStore(root.Paths,BuildStandard.Core.Diagnostics.NullLog.Instance);Assert.Throws<ConfigurationException>(()=>store.LoadConditionalAccessEvaluation(TestData.TenantA,id));}
    [Fact] public void A_different_record_cannot_be_moved_under_a_familiar_filename()
    {
        using var root=new TempRoot();var store=new EvidenceStore(root.Paths,BuildStandard.Core.Diagnostics.NullLog.Instance);var c=ConditionalAccessEvaluationExportTests.Capture();var file=store.SaveConditionalAccessEvaluation(c);
        File.Move(file,Path.Combine(Path.GetDirectoryName(file)!,TestData.Office+".json.zip"));
        Assert.Throws<IntegrityException>(()=>store.LoadConditionalAccessEvaluation(TestData.TenantA,TestData.Office));
    }
}
