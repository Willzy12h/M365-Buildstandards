using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;

public sealed class ConditionalAccessEvaluationWorkflowTests
{
    [Fact] public async Task Successful_original_is_saved_once_without_a_second_read()
    {
        var calls=0;var saves=0;var c=ConditionalAccessEvaluationExportTests.Capture();var flow=new ConditionalAccessEvaluationWorkflow(_=>{saves++;return "original.zip";},()=>"context");
        var outcome=await flow.ReadAsync(_=>{calls++;return Task.FromResult(c);},"context",TestData.TenantA,true,default);
        Assert.Equal("original.zip",outcome.File);Assert.Null(outcome.NotSavedReason);Assert.Null(flow.Pending);Assert.Equal(1,calls);Assert.Equal(1,saves);
    }
    [Fact] public async Task Failed_save_retains_exact_original_and_blocks_a_new_read()
    {
        var c=ConditionalAccessEvaluationExportTests.Capture();var reads=0;var flow=new ConditionalAccessEvaluationWorkflow(_=>throw new IOException("Synthetic disk refusal."),()=>"context");
        var outcome=await flow.ReadAsync(_=>{reads++;return Task.FromResult(c);},"context",TestData.TenantA,true,default);
        Assert.Equal("",outcome.File);Assert.Contains("not saved",outcome.NotSavedReason!,StringComparison.OrdinalIgnoreCase);Assert.Same(c,flow.Pending);
        await Assert.ThrowsAsync<SafetyViolationException>(()=>flow.ReadAsync(_=>{reads++;return Task.FromResult(c);},"context",TestData.TenantA,true,default));Assert.Equal(1,reads);
    }
    [Fact] public async Task A_context_change_after_capture_retains_the_original_for_separate_acknowledged_recovery()
    {
        using var root=new TempRoot();var store=new EvidenceStore(root.Paths,NullLog.Instance);var context="original";var c=ConditionalAccessEvaluationExportTests.Capture();var flow=new ConditionalAccessEvaluationWorkflow(store.SaveConditionalAccessEvaluation,()=>context);
        await Assert.ThrowsAsync<TenantMismatchException>(()=>flow.ReadAsync(_=>{context="different";return Task.FromResult(c);},"original",TestData.TenantA,true,default));
        Assert.Same(c,flow.Pending);var file=flow.SavePending(c.Metadata.Id,true);Assert.True(File.Exists(file));Assert.Null(flow.Pending);Assert.Contains(TestData.TenantA,file);
    }
    [Fact] public async Task A_context_change_during_save_does_not_claim_the_file_for_the_new_client()
    {
        var context="original";var c=ConditionalAccessEvaluationExportTests.Capture();var flow=new ConditionalAccessEvaluationWorkflow(_=>{context="different";return "saved-original.zip";},()=>context);
        await Assert.ThrowsAsync<TenantMismatchException>(()=>flow.ReadAsync(_=>Task.FromResult(c),"original",TestData.TenantA,true,default));Assert.Null(flow.Pending);
    }
    [Theory][InlineData("ack")][InlineData("context")][InlineData("tenant")]
    public async Task Invalid_start_never_calls_the_collector(string defect)
    {
        var calls=0;var flow=new ConditionalAccessEvaluationWorkflow(_=>"saved",()=>"context");
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>flow.ReadAsync(_=>{calls++;return Task.FromResult(ConditionalAccessEvaluationExportTests.Capture());},defect=="context"?"other":"context",defect=="tenant"?"../bad":TestData.TenantA,defect!="ack",default));Assert.Equal(0,calls);
    }
    [Fact] public async Task A_wrong_tenant_receipt_is_never_accepted_or_saved()
    {
        var saves=0;var flow=new ConditionalAccessEvaluationWorkflow(_=>{saves++;return "saved";},()=>"context");
        await Assert.ThrowsAsync<TenantMismatchException>(()=>flow.ReadAsync(_=>Task.FromResult(ConditionalAccessEvaluationExportTests.Capture()),"context",TestData.TenantB,true,default));Assert.Equal(0,saves);Assert.Null(flow.Pending);
    }
    [Fact] public async Task A_second_read_cannot_overlap_a_pending_first_read()
    {
        var pending=new TaskCompletionSource<ConditionalAccessEvaluationCapture>(TaskCreationOptions.RunContinuationsAsynchronously);var flow=new ConditionalAccessEvaluationWorkflow(_=>"saved",()=>"context");var first=flow.ReadAsync(_=>pending.Task,"context",TestData.TenantA,true,default);
        try{await Assert.ThrowsAsync<SafetyViolationException>(()=>flow.ReadAsync(_=>Task.FromResult(ConditionalAccessEvaluationExportTests.Capture()),"context",TestData.TenantA,true,default));}
        finally{pending.SetResult(ConditionalAccessEvaluationExportTests.Capture());await first;}
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Recovery_requires_exact_original_ID_and_explicit_acknowledgement(bool wrongId)
    {
        var c=ConditionalAccessEvaluationExportTests.Capture();var flow=new ConditionalAccessEvaluationWorkflow(_=>throw new IOException(),()=>"context");await flow.ReadAsync(_=>Task.FromResult(c),"context",TestData.TenantA,true,default);
        var id=wrongId?TestData.Office:c.Metadata.Id;Assert.Throws<SafetyViolationException>(()=>flow.SavePending(id,wrongId));Assert.Throws<SafetyViolationException>(()=>flow.DiscardPending(id,wrongId));Assert.Same(c,flow.Pending);
    }
    [Fact] public async Task Original_export_preserves_the_retained_pair_without_requerying()
    {
        using var root=new TempRoot();var c=ConditionalAccessEvaluationExportTests.Capture();var flow=new ConditionalAccessEvaluationWorkflow(_=>throw new IOException(),()=>"context");await flow.ReadAsync(_=>Task.FromResult(c),"context",TestData.TenantA,true,default);
        var file=flow.ExportOriginal(new ReportExporter(root.Paths,"Synthetic"),c.Metadata.Id,true);Assert.Null(flow.Pending);var read=ConditionalAccessEvaluationBundle.ReadFile(file,TestData.TenantA);Assert.Equal(c.MetadataBytes,read.MetadataBytes);Assert.Equal(c.OriginalResponseBytes,read.OriginalResponseBytes);
    }
    [Fact] public async Task Supplied_historical_bytes_cannot_be_admitted_as_a_new_connected_read()
    {
        var original=ConditionalAccessEvaluationExportTests.Capture();var historical=ConditionalAccessEvaluationBundle.Read(ConditionalAccessEvaluationBundle.Write(original),TestData.TenantA);
        var saves=0;var flow=new ConditionalAccessEvaluationWorkflow(_=>{saves++;return "saved";},()=>"context");
        await Assert.ThrowsAsync<SafetyViolationException>(()=>flow.ReadAsync(_=>Task.FromResult(historical),"context",TestData.TenantA,true,default));Assert.Equal(0,saves);Assert.Null(flow.Pending);
    }
    [Fact] public async Task Stop_while_the_collector_ignores_cancellation_is_bounded_and_cannot_save_a_late_result()
    {
        using var stop=new CancellationTokenSource();var pending=new TaskCompletionSource<ConditionalAccessEvaluationCapture>(TaskCreationOptions.RunContinuationsAsynchronously);var saves=0;
        var flow=new ConditionalAccessEvaluationWorkflow(_=>{saves++;return "saved";},()=>"context");
        try{await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>flow.ReadAsync(_=>{stop.Cancel();return pending.Task;},"context",TestData.TenantA,true,stop.Token).WaitAsync(TimeSpan.FromSeconds(1)));}
        finally{pending.SetResult(ConditionalAccessEvaluationExportTests.Capture());}
        Assert.Equal(0,saves);Assert.Null(flow.Pending);
    }
    [Fact] public async Task Stop_after_a_complete_original_is_returned_retains_it_separately_without_saving()
    {
        using var stop=new CancellationTokenSource();var c=ConditionalAccessEvaluationExportTests.Capture();var saves=0;var flow=new ConditionalAccessEvaluationWorkflow(_=>{saves++;return "saved";},()=>"context");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>flow.ReadAsync(_=>{stop.Cancel();return Task.FromResult(c);},"context",TestData.TenantA,true,stop.Token));
        Assert.Equal(0,saves);Assert.Same(c,flow.Pending);
    }
    [Fact] public async Task Failed_original_export_retains_the_only_unsaved_pair()
    {
        using var root=new TempRoot();var c=ConditionalAccessEvaluationExportTests.Capture();var flow=new ConditionalAccessEvaluationWorkflow(_=>throw new IOException(),()=>"context");await flow.ReadAsync(_=>Task.FromResult(c),"context",TestData.TenantA,true,default);
        Directory.Delete(root.Paths.ReportsDirectory);File.WriteAllText(root.Paths.ReportsDirectory,"Synthetic blocked destination");
        Assert.ThrowsAny<IOException>(()=>flow.ExportOriginal(new ReportExporter(root.Paths,""),c.Metadata.Id,true));Assert.Same(c,flow.Pending);
    }
    [Fact] public async Task Explicit_discard_only_clears_the_retained_report_and_cannot_run_anything()
    {var c=ConditionalAccessEvaluationExportTests.Capture();var flow=new ConditionalAccessEvaluationWorkflow(_=>throw new IOException(),()=>"context");await flow.ReadAsync(_=>Task.FromResult(c),"context",TestData.TenantA,true,default);flow.DiscardPending(c.Metadata.Id,true);Assert.Null(flow.Pending);}
}
