using BuildStandard.Core;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class DeploymentCoverageWorkflowTests
{
    [Fact] public async Task Successful_original_is_saved_once_without_another_query()
    {var e=await DeploymentCoverageEvidenceTests.Evidence();var reads=0;var saves=0;var flow=new DeploymentCoverageWorkflow(_=>{saves++;return "saved.zip";},()=>"A");var result=await flow.ReadAsync(_=>{reads++;return Task.FromResult(e);},"A",TestData.TenantA,true,default);Assert.Equal("saved.zip",result.File);Assert.Null(result.NotSavedReason);Assert.Null(flow.Pending);Assert.Equal(1,reads);Assert.Equal(1,saves);}
    [Fact] public async Task Failed_save_retains_original_and_blocks_replacement_reads()
    {var e=await DeploymentCoverageEvidenceTests.Evidence();var flow=new DeploymentCoverageWorkflow(_=>throw new IOException("Synthetic disk refusal"),()=>"A");var result=await flow.ReadAsync(_=>Task.FromResult(e),"A",TestData.TenantA,true,default);Assert.Equal("",result.File);Assert.Contains("NOT SAVED",result.NotSavedReason!);Assert.Same(e,flow.Pending);await Assert.ThrowsAsync<SafetyViolationException>(()=>flow.ReadAsync(_=>throw new InvalidOperationException("Must not read"),"A",TestData.TenantA,true,default));}
    [Theory][InlineData("changed")][InlineData("stopped")]
    public async Task Context_change_or_stop_after_capture_retains_original_before_refusing_current_admission(string change)
    {var e=await DeploymentCoverageEvidenceTests.Evidence();var context="A";using var stop=new CancellationTokenSource();var saves=0;var flow=new DeploymentCoverageWorkflow(_=>{saves++;return "saved";},()=>context);var task=flow.ReadAsync(_=>{if(change=="changed")context="B";else stop.Cancel();return Task.FromResult(e);},"A",TestData.TenantA,true,stop.Token);if(change=="changed")await Assert.ThrowsAsync<TenantMismatchException>(()=>task);else await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task);Assert.Same(e,flow.Pending);Assert.Equal(0,saves);}
    [Theory][InlineData("context")][InlineData("tenant")][InlineData("ack")]
    public async Task Invalid_start_does_not_invoke_a_collector(string defect)
    {var flow=new DeploymentCoverageWorkflow(_=>"saved",()=>"A");var reads=0;await Assert.ThrowsAnyAsync<ToolkitException>(()=>flow.ReadAsync(_=>{reads++;throw new InvalidOperationException();},defect=="context"?"B":"A",defect=="tenant"?"../bad":TestData.TenantA,defect!="ack",default));Assert.Equal(0,reads);}
    [Fact] public async Task Historical_originals_cannot_impersonate_new_connected_collection()
    {var e=await DeploymentCoverageEvidenceTests.Evidence();var imported=DeploymentCoverageEvidence.Read(e.MetadataBytes,e.OriginalPages,TestData.TenantA);var flow=new DeploymentCoverageWorkflow(_=>throw new InvalidOperationException("Must not save"),()=>"A");await Assert.ThrowsAsync<SafetyViolationException>(()=>flow.ReadAsync(_=>Task.FromResult(imported),"A",TestData.TenantA,true,default));Assert.Null(flow.Pending);}
    [Theory][InlineData("wrong-id")][InlineData("no-ack")]
    public async Task Recovery_requires_exact_original_identity_and_acknowledgement(string defect)
    {var e=await DeploymentCoverageEvidenceTests.Evidence();var flow=new DeploymentCoverageWorkflow(_=>throw new IOException(),()=>"A");await flow.ReadAsync(_=>Task.FromResult(e),"A",TestData.TenantA,true,default);Assert.Throws<SafetyViolationException>(()=>flow.DiscardPending(defect=="wrong-id"?TestData.Office:e.Metadata.Id,defect!="no-ack"));Assert.Same(e,flow.Pending);}
    [Fact] public async Task Original_JSON_export_preserves_bytes_and_resolves_pending_without_requery()
    {using var root=new TempRoot();var e=await DeploymentCoverageEvidenceTests.Evidence();var flow=new DeploymentCoverageWorkflow(_=>throw new IOException(),()=>"A");await flow.ReadAsync(_=>Task.FromResult(e),"A",TestData.TenantA,true,default);var file=flow.ExportOriginal(new ReportExporter(root.Paths,""),e.Metadata.Id,true);Assert.Null(flow.Pending);Assert.Equal(e.MetadataBytes,DeploymentCoverageBundle.ReadFile(file,TestData.TenantA).MetadataBytes);}
    [Fact] public async Task Noncooperative_collector_cannot_hold_stop_forever_or_save_a_late_result()
    {var e=await DeploymentCoverageEvidenceTests.Evidence();using var stop=new CancellationTokenSource();var late=new TaskCompletionSource<DeploymentCoverageEvidence>(TaskCreationOptions.RunContinuationsAsynchronously);var saves=0;var flow=new DeploymentCoverageWorkflow(_=>{saves++;return "saved";},()=>"A");try{await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>flow.ReadAsync(_=>{stop.Cancel();return late.Task;},"A",TestData.TenantA,true,stop.Token).WaitAsync(TimeSpan.FromSeconds(1)));}finally{late.SetResult(e);}Assert.Equal(0,saves);Assert.Null(flow.Pending);}
}
