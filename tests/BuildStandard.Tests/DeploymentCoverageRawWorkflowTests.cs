using BuildStandard.Core;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class DeploymentCoverageRawWorkflowTests
{
    [Fact] public async Task Raw_capture_is_saved_once_with_the_same_frozen_original_ID()
    {var raw=await new DeploymentCoverageServiceTests.Fixture().Capture();var saves=0;var reads=0;var flow=new DeploymentCoverageWorkflow(e=>{saves++;Assert.Equal(raw.Id,e.Metadata.Id);return "saved.zip";},()=>"A");var result=await flow.ReadCaptureAsync(_=>{reads++;return Task.FromResult(raw);},"A",TestData.TenantA,true,default);Assert.Equal("saved.zip",result.File);Assert.NotNull(result.Evidence);Assert.Null(result.NotSavedReason);Assert.False(flow.HasPendingOriginal);Assert.Equal(1,reads);Assert.Equal(1,saves);}
    [Fact] public async Task Oversize_retains_exact_raw_original_and_blocks_both_new_read_interfaces()
    {var raw=await CoverageOriginalFilesTests.Oversize();var flow=new DeploymentCoverageWorkflow(_=>throw new InvalidOperationException("Must not save an invalid envelope"),()=>"A");var result=await flow.ReadCaptureAsync(_=>Task.FromResult(raw),"A",TestData.TenantA,true,default);Assert.Null(result.Evidence);Assert.Equal("",result.File);Assert.Contains("NOT SAVED",result.NotSavedReason!);Assert.Same(raw,flow.PendingCapture);Assert.Null(flow.Pending);Assert.True(flow.HasPendingOriginal);await Assert.ThrowsAsync<SafetyViolationException>(()=>flow.ReadCaptureAsync(_=>throw new InvalidOperationException("Must not read"),"A",TestData.TenantA,true,default));await Assert.ThrowsAsync<SafetyViolationException>(()=>flow.ReadAsync(_=>throw new InvalidOperationException("Must not read"),"A",TestData.TenantA,true,default));}
    [Fact] public async Task Context_change_after_read_retains_original_without_attributing_it_to_the_new_client()
    {var raw=await new DeploymentCoverageServiceTests.Fixture().Capture();var context="A";var saves=0;var flow=new DeploymentCoverageWorkflow(_=>{saves++;return "saved";},()=>context);await Assert.ThrowsAsync<TenantMismatchException>(()=>flow.ReadCaptureAsync(_=>{context="B";return Task.FromResult(raw);},"A",TestData.TenantA,true,default));Assert.Same(raw,flow.PendingCapture);Assert.True(flow.HasPendingOriginal);Assert.Equal(0,saves);Assert.Equal(raw.Id,flow.Pending!.Metadata.Id);}
    [Fact] public async Task Stop_after_a_complete_raw_result_keeps_its_original_for_recovery()
    {using var stop=new CancellationTokenSource();var raw=await new DeploymentCoverageServiceTests.Fixture().Capture();var flow=new DeploymentCoverageWorkflow(_=>throw new InvalidOperationException("Must not save"),()=>"A");await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>flow.ReadCaptureAsync(_=>{stop.Cancel();return Task.FromResult(raw);},"A",TestData.TenantA,true,stop.Token));Assert.Same(raw,flow.PendingCapture);Assert.True(flow.HasPendingOriginal);}
    [Fact] public async Task Stop_waits_for_the_owned_partial_handoff_then_retains_originals_without_saving_or_replay()
    {
        using var stop=new CancellationTokenSource();
        var fixture=new DeploymentCoverageServiceTests.Fixture();
        var reading=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var captured=new TaskCompletionSource<DeploymentCoverageCapture>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Override=async (request,token)=>
        {
            if(request.Path.EndsWith("/assignments"))
            {reading.TrySetResult();await Task.Delay(Timeout.Infinite,token);}
            return BuildStandard.Core.Reporting.CoverageResponseCodec.Read(System.Text.Encoding.UTF8.GetBytes("{\"id\":\""+TestData.Office+"\"}"),request);
        };
        var saves=0;
        var flow=new DeploymentCoverageWorkflow(_=>{saves++;return "must-not-save";},()=>"A");
        var run=flow.ReadCaptureAsync(async token=>
        {var raw=await fixture.Capture(token);captured.TrySetResult(raw);await release.Task;return raw;},"A",TestData.TenantA,true,stop.Token);
        try
        {
            await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));stop.Cancel();
            var raw=await captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(raw.Sources[0].Capture.OriginalPages);
            Assert.False(run.IsCompleted); // Stop must not abandon the owned producer's already collected originals.
            release.TrySetResult();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>run);
            Assert.Same(raw,flow.PendingCapture);Assert.True(flow.HasPendingOriginal);Assert.Equal(0,saves);
            await Assert.ThrowsAsync<SafetyViolationException>(()=>flow.ReadCaptureAsync(_=>throw new InvalidOperationException("No replay"),"A",TestData.TenantA,true,default));
        }
        finally
        {release.TrySetResult();try{await run;}catch(OperationCanceledException){}}
    }
    [Fact] public async Task Owned_service_settles_promptly_on_Stop_even_when_an_underlying_source_ignores_it()
    {
        using var stop=new CancellationTokenSource();var fixture=new DeploymentCoverageServiceTests.Fixture();
        var reading=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never=new TaskCompletionSource<BuildStandard.Core.Reporting.CoverageReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Override=(request,_)=>
        {
            if(request.Path.EndsWith("/assignments")){reading.TrySetResult();return never.Task;}
            return Task.FromResult(BuildStandard.Core.Reporting.CoverageResponseCodec.Read(System.Text.Encoding.UTF8.GetBytes("{\"id\":\""+TestData.Office+"\"}"),request));
        };
        var saves=0;var flow=new DeploymentCoverageWorkflow(_=>{saves++;return "must-not-save";},()=>"A");
        var run=flow.ReadCaptureAsync(fixture.Capture,"A",TestData.TenantA,true,stop.Token);
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(flow.HasPendingOriginal);Assert.NotNull(flow.PendingCapture);Assert.Single(flow.PendingCapture.Sources[0].Capture.OriginalPages);
        Assert.Equal(2,fixture.Reads.Count);Assert.Equal(0,saves);
        never.TrySetException(new IOException("Synthetic late source failure"));
    }
    [Fact] public async Task Failed_save_keeps_both_raw_and_conforming_original_until_explicit_recovery()
    {var raw=await new DeploymentCoverageServiceTests.Fixture().Capture();var flow=new DeploymentCoverageWorkflow(_=>throw new IOException(),()=>"A");var result=await flow.ReadCaptureAsync(_=>Task.FromResult(raw),"A",TestData.TenantA,true,default);Assert.Contains("NOT SAVED",result.NotSavedReason!);Assert.Same(raw,flow.PendingCapture);Assert.Equal(raw.Id,flow.Pending!.Metadata.Id);flow.DiscardPending(raw.Id,true);Assert.False(flow.HasPendingOriginal);}
    [Fact] public async Task Oversize_can_export_manual_original_files_but_cannot_become_a_larger_JSON_report()
    {using var root=new TempRoot();var raw=await CoverageOriginalFilesTests.Oversize();var flow=new DeploymentCoverageWorkflow(_=>throw new InvalidOperationException(),()=>"A");await flow.ReadCaptureAsync(_=>Task.FromResult(raw),"A",TestData.TenantA,true,default);var exporter=new ReportExporter(root.Paths,"");Assert.Throws<SafetyViolationException>(()=>flow.ExportOriginal(exporter,raw.Id,true));Assert.True(flow.HasPendingOriginal);var folder=flow.ExportOriginalFiles(exporter,raw.Id,true);Assert.True(File.Exists(Path.Combine(folder,"COPY-VERIFIED.txt")));Assert.False(flow.HasPendingOriginal);Assert.Equal(raw.Sources[0].Capture.OriginalPages[0],File.ReadAllBytes(Path.Combine(folder,"page-000.json")));}
    [Theory][InlineData("id")][InlineData("ack")]
    public async Task Manual_original_copy_requires_the_exact_original_ID_and_acknowledgement(string defect)
    {using var root=new TempRoot();var raw=await CoverageOriginalFilesTests.Oversize();var flow=new DeploymentCoverageWorkflow(_=>throw new InvalidOperationException(),()=>"A");await flow.ReadCaptureAsync(_=>Task.FromResult(raw),"A",TestData.TenantA,true,default);Assert.Throws<SafetyViolationException>(()=>flow.ExportOriginalFiles(new(root.Paths,""),defect=="id"?TestData.Office:raw.Id,defect!="ack"));Assert.True(flow.HasPendingOriginal);Assert.Empty(Directory.EnumerateFileSystemEntries(root.Paths.ReportsDirectory));}
    [Fact] public async Task Failed_manual_copy_does_not_clear_the_only_retained_original()
    {using var root=new TempRoot();var raw=await CoverageOriginalFilesTests.Oversize();var flow=new DeploymentCoverageWorkflow(_=>throw new InvalidOperationException(),()=>"A");await flow.ReadCaptureAsync(_=>Task.FromResult(raw),"A",TestData.TenantA,true,default);Directory.Delete(root.Paths.ReportsDirectory);File.WriteAllText(root.Paths.ReportsDirectory,"Synthetic refusal");Assert.ThrowsAny<IOException>(()=>flow.ExportOriginalFiles(new(root.Paths,""),raw.Id,true));Assert.Same(raw,flow.PendingCapture);Assert.True(flow.HasPendingOriginal);}
}
