using System.Net;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Graph;
using BuildStandard.Graph.Auth;
using Xunit;
namespace BuildStandard.Tests;
public sealed class IntuneExportReaderTests
{
    private static PreparedIntuneExportRequest Request()=>IntuneExportRequest.Prepare(CoverageFamily.Application,TestData.Office);
    private const string Address="https://synthetic.blob.core.windows.net/reports/source.zip?sig=synthetic-not-a-secret";
    private sealed class Tokens:IAccessTokenProvider
    {public List<bool> Calls=[];public Func<CancellationToken,Task>? Before;public Task<string> GetAccessTokenAsync(CancellationToken ct)=>GetAccessTokenAsync(false,ct);public async Task<string> GetAccessTokenAsync(bool forceRefresh,CancellationToken ct){Calls.Add(forceRefresh);if(Before is not null)await Before(ct);return "synthetic-token";}}
    private sealed class Handler:HttpMessageHandler
    {
        public List<(string Method,string Address,string? Auth,string? Cookie,byte[] Body)> Sent=[];
        public Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>>? Response;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {Sent.Add((request.Method.Method,request.RequestUri!.AbsoluteUri,request.Headers.Authorization?.ToString(),request.Headers.Contains("Cookie")?string.Join(';',request.Headers.GetValues("Cookie")):null,request.Content is null?[]:await request.Content.ReadAsByteArrayAsync(ct)));return await Response!(request,ct);}
    }
    private sealed class Fixture:IDisposable
    {
        public TenantSession Session=new(){TenantId=TestData.TenantA,AccountObjectId=TestData.Operator,OperatorObjectId=TestData.Operator,ClientId=TestData.Emergency,Mode=SessionMode.Deployment,TenantVerified=true,OperatorVerified=true,Scopes=["DeviceManagementApps.ReadWrite.All"]};
        public Tokens Tokens=new();public Handler Graph=new(),Download=new();public bool Current=true;
        public ConnectAcquisitionBudgetTests.ManualTime Clock=new();public IntuneExportReader Reader;
        public Fixture(bool ack=true)
        {Graph.Response=(_,_)=>Task.FromResult(Job("completed"));Download.Response=(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent([1,2,3])});Reader=new(new HttpClient(Graph),new HttpClient(Download),Tokens,TestData.TenantA,SessionMode.Deployment,Session,()=>Current,ack,Clock);}
        public static HttpResponseMessage Job(string status="notStarted",HttpStatusCode code=HttpStatusCode.Created)
            =>new(code){Content=new ByteArrayContent(IntuneExportJobCodecTests.Body(Request(),status:status,url:status=="completed"?Address:null))};
        public void Dispose()=>Reader.Dispose();
    }
    [Fact] public async Task Fixed_POST_and_token_free_download_return_only_sanitised_original_receipt()
    {using var f=new Fixture();var result=await f.Reader.ReadAsync(Request(),default);var sent=Assert.Single(f.Graph.Sent);Assert.Equal("POST",sent.Method);Assert.Equal("https://graph.microsoft.com/v1.0/deviceManagement/reports/exportJobs",sent.Address);Assert.Equal(Request().Bytes,sent.Body);Assert.Equal("Bearer synthetic-token",sent.Auth);var download=Assert.Single(f.Download.Sent);Assert.Equal("GET",download.Method);Assert.Null(download.Auth);Assert.Null(download.Cookie);Assert.Empty(download.Body);Assert.Equal(new byte[]{1,2,3},result.OriginalArchiveBytes);Assert.Equal(TestData.TenantA,result.TenantId);Assert.Equal(Request().Sha256,result.RequestSha256);Assert.DoesNotContain("sig=",result.ToString());}
    [Theory][InlineData("tenant")][InlineData("account")][InlineData("operator")][InlineData("client")][InlineData("assessment")][InlineData("scope")][InlineData("verified")][InlineData("context")][InlineData("ack")]
    public async Task Original_actor_purpose_and_deliberate_broader_access_gate_precede_all_acquisition(string defect)
    {using var f=new Fixture(defect!="ack");switch(defect){case "tenant":f.Session.TenantId=TestData.TenantB;break;case "account":f.Session.AccountObjectId=TestData.Office;break;case "operator":f.Session.OperatorObjectId=TestData.Office;break;case "client":f.Session.ClientId=TestData.Office;break;case "assessment":f.Session.Mode=SessionMode.Assessment;break;case "scope":f.Session.Scopes.Clear();break;case "verified":f.Session.TenantVerified=false;break;case "context":f.Current=false;break;}await Assert.ThrowsAnyAsync<ToolkitException>(()=>f.Reader.ReadAsync(Request(),default));Assert.Empty(f.Tokens.Calls);Assert.Empty(f.Graph.Sent);Assert.Empty(f.Download.Sent);}
    [Fact] public async Task Identity_change_during_acquisition_prevents_a_POST()
    {using var f=new Fixture();f.Tokens.Before=_=>{f.Session.AccountObjectId=TestData.Office;return Task.CompletedTask;};await Assert.ThrowsAsync<TenantMismatchException>(()=>f.Reader.ReadAsync(Request(),default));Assert.Empty(f.Graph.Sent);}
    [Fact] public async Task Context_change_after_create_prevents_download_and_does_not_replay()
    {using var f=new Fixture();f.Graph.Response=(_,_)=>{f.Current=false;return Task.FromResult(Fixture.Job("completed"));};await Assert.ThrowsAsync<TenantMismatchException>(()=>f.Reader.ReadAsync(Request(),default));Assert.Empty(f.Download.Sent);await Assert.ThrowsAsync<SafetyViolationException>(()=>f.Reader.ReadAsync(Request(),default));Assert.Single(f.Graph.Sent);}
    [Theory][InlineData(302)][InlineData(403)][InlineData(429)][InlineData(500)][InlineData(408)]
    public async Task Create_failures_are_not_retried_or_followed_and_cannot_be_replayed(int code)
    {using var f=new Fixture();f.Graph.Response=(_,_)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)code));await Assert.ThrowsAnyAsync<ToolkitException>(()=>f.Reader.ReadAsync(Request(),default));await Assert.ThrowsAsync<SafetyViolationException>(()=>f.Reader.ReadAsync(Request(),default));Assert.Single(f.Graph.Sent);Assert.Empty(f.Download.Sent);}
    [Theory][InlineData(302)][InlineData(403)][InlineData(429)][InlineData(500)]
    public async Task Download_refusals_have_no_redirect_or_authenticated_fallback(int code)
    {using var f=new Fixture();f.Download.Response=(_,_)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)code));var error=await Assert.ThrowsAnyAsync<ToolkitException>(()=>f.Reader.ReadAsync(Request(),default));Assert.Single(f.Graph.Sent);Assert.Single(f.Download.Sent);Assert.Null(f.Download.Sent[0].Auth);Assert.DoesNotContain("synthetic-not-a-secret",error.Message);}
    [Fact] public async Task One_silent_401_reuses_exact_create_body_without_consent_or_interactive_auth()
    {using var f=new Fixture();f.Graph.Response=(_,_)=>Task.FromResult(f.Graph.Sent.Count==1?new HttpResponseMessage(HttpStatusCode.Unauthorized):Fixture.Job("completed"));await f.Reader.ReadAsync(Request(),default);Assert.Equal(new[]{false,true},f.Tokens.Calls);Assert.Equal(2,f.Graph.Sent.Count);Assert.Equal(f.Graph.Sent[0].Body,f.Graph.Sent[1].Body);}
    [Fact] public async Task Polling_waits_ten_seconds_and_preserves_created_job_identity()
    {using var f=new Fixture();f.Graph.Response=(r,_)=>Task.FromResult(Fixture.Job(r.Method==HttpMethod.Post?"notStarted":"completed",r.Method==HttpMethod.Post?HttpStatusCode.Created:HttpStatusCode.OK));var task=f.Reader.ReadAsync(Request(),default);Assert.Single(f.Graph.Sent);f.Clock.Advance(TimeSpan.FromSeconds(9));Assert.Single(f.Graph.Sent);f.Clock.Advance(TimeSpan.FromSeconds(1));var result=await task.WaitAsync(TimeSpan.FromSeconds(2));Assert.Equal("report_opaque-1",result.JobId);Assert.Equal("https://graph.microsoft.com/v1.0/deviceManagement/reports/exportJobs/report_opaque-1",f.Graph.Sent[1].Address);Assert.Equal("GET",f.Graph.Sent[1].Method);}
    [Fact] public async Task Larger_RetryAfter_is_honoured_within_the_original_overall_budget()
    {using var f=new Fixture();f.Graph.Response=(r,_)=>{var response=Fixture.Job(r.Method==HttpMethod.Post?"notStarted":"completed",r.Method==HttpMethod.Post?HttpStatusCode.Created:HttpStatusCode.OK);response.Headers.RetryAfter=new(TimeSpan.FromSeconds(20));return Task.FromResult(response);};var task=f.Reader.ReadAsync(Request(),default);f.Clock.Advance(TimeSpan.FromSeconds(10));Assert.Single(f.Graph.Sent);f.Clock.Advance(TimeSpan.FromSeconds(10));await task.WaitAsync(TimeSpan.FromSeconds(2));Assert.Equal(2,f.Graph.Sent.Count);}
    [Fact] public async Task Download_transport_errors_do_not_echo_the_signed_url_or_replay_the_job()
    {using var f=new Fixture();f.Download.Response=(_,_)=>Task.FromException<HttpResponseMessage>(new HttpRequestException(Address));var error=await Assert.ThrowsAnyAsync<ToolkitException>(()=>f.Reader.ReadAsync(Request(),default));Assert.DoesNotContain("synthetic-not-a-secret",error.Message);Assert.DoesNotContain("blob.core",error.Message);Assert.Single(f.Graph.Sent);}
    [Fact] public async Task Five_minute_budget_bounds_a_noncooperative_token_without_sending()
    {using var f=new Fixture();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);f.Tokens.Before=_=>{entered.SetResult();return release.Task;};var task=f.Reader.ReadAsync(Request(),default);await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));f.Clock.Advance(TimeSpan.FromMinutes(5));try{await Assert.ThrowsAnyAsync<ToolkitException>(()=>task.WaitAsync(TimeSpan.FromSeconds(1)));}finally{release.SetResult();}Assert.Empty(f.Graph.Sent);}
    [Fact] public async Task Stop_during_an_ambiguous_create_does_not_replay_or_claim_a_configuration_write()
    {using var f=new Fixture();using var stop=new CancellationTokenSource();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);f.Graph.Response=(_,_)=>{entered.SetResult();return release.Task;};var task=f.Reader.ReadAsync(Request(),stop.Token);await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));stop.Cancel();try{var error=await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task.WaitAsync(TimeSpan.FromSeconds(1)));Assert.Contains("report job",error.Message,StringComparison.OrdinalIgnoreCase);}finally{release.SetResult(Fixture.Job());}Assert.Single(f.Graph.Sent);Assert.Empty(f.Download.Sent);}
    [Fact] public async Task Oversized_RetryAfter_ends_at_the_original_budget_instead_of_throwing_a_timer_range_error()
    {using var f=new Fixture();f.Graph.Response=(_,_)=>{var response=Fixture.Job();response.Headers.RetryAfter=new(TimeSpan.FromDays(50));return Task.FromResult(response);};var task=f.Reader.ReadAsync(Request(),default);f.Clock.Advance(TimeSpan.FromMinutes(5));await Assert.ThrowsAsync<GraphRequestException>(()=>task.WaitAsync(TimeSpan.FromSeconds(1)));Assert.Single(f.Graph.Sent);Assert.Empty(f.Download.Sent);}
    [Fact] public async Task Actual_download_bytes_are_counted_even_with_a_false_small_content_length()
    {using var f=new Fixture();f.Download.Response=(_,_)=>{var content=new ByteArrayContent(new byte[5*1024*1024+1]);content.Headers.ContentLength=1;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});};var error=await Assert.ThrowsAsync<ConfigurationException>(()=>f.Reader.ReadAsync(Request(),default));Assert.Contains("streamed",error.Message);Assert.Single(f.Download.Sent);}
    [Fact] public async Task The_single_silent_renewal_budget_is_shared_across_create_and_status_requests()
    {using var f=new Fixture();f.Graph.Response=(r,_)=>Task.FromResult(f.Graph.Sent.Count==1||r.Method==HttpMethod.Get?new HttpResponseMessage(HttpStatusCode.Unauthorized):Fixture.Job());var task=f.Reader.ReadAsync(Request(),default);f.Clock.Advance(TimeSpan.FromSeconds(10));await Assert.ThrowsAsync<GraphRequestException>(()=>task.WaitAsync(TimeSpan.FromSeconds(1)));Assert.Equal(new[]{false,true,false},f.Tokens.Calls);Assert.Equal(3,f.Graph.Sent.Count);Assert.Empty(f.Download.Sent);}
    [Fact] public async Task Unknown_job_completion_is_not_polled_or_downloaded_as_empty_success()
    {using var f=new Fixture();f.Graph.Response=(_,_)=>Task.FromResult(Fixture.Job("futureState"));await Assert.ThrowsAsync<ConfigurationException>(()=>f.Reader.ReadAsync(Request(),default));Assert.Single(f.Graph.Sent);Assert.Empty(f.Download.Sent);}
    private sealed class PendingStream:MemoryStream
    {public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource<int> Release=new(TaskCreationOptions.RunContinuationsAsynchronously);public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){Entered.TrySetResult();return new(Release.Task);}}
    [Fact] public async Task A_noncooperative_download_stream_is_bounded_by_the_original_budget()
    {using var f=new Fixture();using var stream=new PendingStream();f.Download.Response=(_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(stream)});var task=f.Reader.ReadAsync(Request(),default);await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));f.Clock.Advance(TimeSpan.FromMinutes(5));try{await Assert.ThrowsAsync<GraphRequestException>(()=>task.WaitAsync(TimeSpan.FromSeconds(1)));}finally{stream.Release.TrySetResult(0);}Assert.Single(f.Graph.Sent);Assert.Single(f.Download.Sent);}
    private sealed class TrackedContent:ByteArrayContent
    {public TaskCompletionSource Retired=new(TaskCreationOptions.RunContinuationsAsynchronously);public TrackedContent():base(new byte[]{1}){}protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing)Retired.TrySetResult();}}
    [Fact] public async Task A_late_owned_response_is_retired_after_stop_without_downloading_or_replaying()
    {using var f=new Fixture();using var stop=new CancellationTokenSource();var late=new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);f.Graph.Response=(_,_)=>{entered.SetResult();return late.Task;};var task=f.Reader.ReadAsync(Request(),stop.Token);await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));stop.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task);var content=new TrackedContent();late.SetResult(new HttpResponseMessage(HttpStatusCode.Created){Content=content});await content.Retired.Task.WaitAsync(TimeSpan.FromSeconds(1));Assert.Empty(f.Download.Sent);Assert.Single(f.Graph.Sent);}
}
