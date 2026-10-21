using System.Text;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CoverageSourceCollectorTests
{
    private static PreparedCoverageRead Request()=>DeploymentCoverageReads.Prepare(CoverageFamily.ClassicCompliance,TestData.Office,CoverageReadKind.ServiceResults);
    private static CoverageReadResponse Page(PreparedCoverageRead request,string id,string? cursor=null)=>CoverageResponseCodec.Read(Encoding.UTF8.GetBytes("{\"value\":[{\"id\":\""+id+"\"}]"+(cursor is null?"":",\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0"+request.Path.Split('?')[0]+"?$skiptoken="+cursor+"\"")+"}"),request);
    [Fact] public async Task Exact_admitted_pages_form_a_complete_source_not_deployment_verification()
    {var seen=new List<string>();var result=await new CoverageSourceCollector().CollectAsync(Request(),(r,_)=>{seen.Add(r.Path);return Task.FromResult(Page(r,seen.Count==1?"first":"second",seen.Count==1?"next":null));},()=>true,CancellationToken.None);Assert.Equal(CoverageSourceState.Complete,result.State);Assert.Equal(2,result.Rows.Count);Assert.Equal(2,result.OriginalPages.Count);Assert.EndsWith("?$skiptoken=next",seen[1]);Assert.Contains("not effective",result.Detail);}
    [Fact] public async Task Unfollowable_link_retains_original_page_as_partial_not_complete()
    {var response=CoverageResponseCodec.Read(Encoding.UTF8.GetBytes("{\"value\":[{\"id\":\"first\"}],\"@odata.nextLink\":\"https://evil.invalid/next\"}"),Request());var calls=0;var result=await new CoverageSourceCollector().CollectAsync(Request(),(_,_)=>{calls++;return Task.FromResult(response);},()=>true,CancellationToken.None);Assert.Equal(CoverageSourceState.Partial,result.State);Assert.Single(result.Rows);Assert.Single(result.OriginalPages);Assert.Equal(1,calls);}
    [Fact] public async Task Same_cursor_loop_is_not_requeried_and_prior_rows_survive()
    {var calls=0;var result=await new CoverageSourceCollector().CollectAsync(Request(),(r,_)=>Task.FromResult(Page(r,"row-"+(++calls),"same")),()=>true,CancellationToken.None);Assert.Equal(2,calls);Assert.Equal(CoverageSourceState.Partial,result.State);Assert.Equal(2,result.Rows.Count);Assert.Contains("loop",result.Detail);}
    [Fact] public async Task Duplicate_identity_across_pages_is_explicit_partial_and_not_double_counted()
    {var calls=0;var result=await new CoverageSourceCollector().CollectAsync(Request(),(r,_)=>Task.FromResult(Page(r,"same",++calls==1?"next":null)),()=>true,CancellationToken.None);Assert.Equal(CoverageSourceState.Partial,result.State);Assert.Single(result.Rows);Assert.Equal(2,result.OriginalPages.Count);Assert.Contains("duplicate",result.Detail);}
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Failure_is_failed_or_partial_not_an_empty_success(bool afterPage)
    {var calls=0;var result=await new CoverageSourceCollector().CollectAsync(Request(),(r,_)=>afterPage&&calls++==0?Task.FromResult(Page(r,"first","next")):Task.FromException<CoverageReadResponse>(new HttpRequestException("sensitive URL must not be echoed")),()=>true,CancellationToken.None);Assert.Equal(afterPage?CoverageSourceState.Partial:CoverageSourceState.Failed,result.State);Assert.Equal(afterPage?1:0,result.Rows.Count);Assert.DoesNotContain("sensitive",result.Detail);}
    [Fact] public async Task Context_change_during_read_keeps_original_but_never_admits_it_as_current()
    {var current=true;var result=await new CoverageSourceCollector().CollectAsync(Request(),(r,_)=>{current=false;return Task.FromResult(Page(r,"first"));},()=>current,CancellationToken.None);Assert.Equal(CoverageSourceState.ContextChanged,result.State);Assert.Single(result.OriginalPages);Assert.False(result.IsCurrent);}
    [Fact] public async Task Context_change_before_read_sends_nothing()
    {var calls=0;var result=await new CoverageSourceCollector().CollectAsync(Request(),(r,_)=>{calls++;return Task.FromResult(Page(r,"first"));},()=>false,CancellationToken.None);Assert.Equal(CoverageSourceState.ContextChanged,result.State);Assert.Equal(0,calls);}
    [Fact] public async Task A_stopped_read_keeps_prior_rows_with_stopped_status()
    {using var stop=new CancellationTokenSource();var calls=0;var result=await new CoverageSourceCollector().CollectAsync(Request(),(r,_)=>{if(++calls==2){stop.Cancel();return Task.FromCanceled<CoverageReadResponse>(stop.Token);}return Task.FromResult(Page(r,"first","next"));},()=>true,stop.Token);Assert.Equal(CoverageSourceState.Stopped,result.State);Assert.Single(result.Rows);}
    [Fact] public async Task No_more_than_100_pages_are_read()
    {var calls=0;var result=await new CoverageSourceCollector().CollectAsync(Request(),(r,_)=>Task.FromResult(Page(r,"row-"+(++calls),"next"+calls)),()=>true,CancellationToken.None);Assert.Equal(100,calls);Assert.Equal(CoverageSourceState.Partial,result.State);Assert.Equal(100,result.Rows.Count);}
    [Fact] public async Task Non_cooperative_source_cannot_outlive_the_five_minute_budget()
    {var clock=new ConnectAcquisitionBudgetTests.ManualTime();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<CoverageReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);var task=new CoverageSourceCollector(clock).CollectAsync(Request(),(_,_)=>{entered.SetResult();return release.Task;},()=>true,CancellationToken.None);await entered.Task;clock.Advance(TimeSpan.FromMinutes(5));try{var result=await task.WaitAsync(TimeSpan.FromSeconds(1));Assert.Equal(CoverageSourceState.Failed,result.State);Assert.Contains("deadline",result.Detail);}finally{release.TrySetResult(Page(Request(),"late"));}}
    [Fact] public async Task Original_bytes_and_rows_are_not_mutable_through_the_result()
    {var result=await new CoverageSourceCollector().CollectAsync(Request(),(r,_)=>Task.FromResult(Page(r,"first")),()=>true,CancellationToken.None);result.OriginalPages[0][0]=0;result.Rows[0]["id"]="other";Assert.NotEqual(0,result.OriginalPages[0][0]);Assert.Equal("first",result.Rows[0]["id"]!.GetValue<string>());}
}
