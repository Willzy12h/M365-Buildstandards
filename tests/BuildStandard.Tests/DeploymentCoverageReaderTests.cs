using System.Net;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Graph;
using BuildStandard.Graph.Auth;
using Xunit;
namespace BuildStandard.Tests;
public sealed class DeploymentCoverageReaderTests
{
    private static PreparedCoverageRead Request()=>DeploymentCoverageReads.Prepare(CoverageFamily.ClassicCompliance,TestData.Office,CoverageReadKind.ServiceResults);
    private sealed class Tokens:IAccessTokenProvider
    {
        public List<bool> Calls {get;}=[];public Func<CancellationToken,Task>? Before;public string Value="synthetic-token";
        public Task<string> GetAccessTokenAsync(CancellationToken ct)=>GetAccessTokenAsync(false,ct);
        public async Task<string> GetAccessTokenAsync(bool forceRefresh,CancellationToken ct){Calls.Add(forceRefresh);if(Before is not null)await Before(ct);return Value;}
    }
    private sealed class Handler:HttpMessageHandler
    {
        public List<(string Method,string Uri,string? Auth)> Sent {get;}=[];public Queue<HttpStatusCode> Statuses {get;}=[];public string Body="{\"value\":[]}";public Action? After;public Func<CancellationToken,Task>? Before;public Func<HttpContent>? Content;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {Sent.Add((request.Method.Method,request.RequestUri!.AbsoluteUri,request.Headers.Authorization?.ToString()));if(Before is not null)await Before(ct);After?.Invoke();return new(Statuses.Count>0?Statuses.Dequeue():HttpStatusCode.OK){Content=Content?.Invoke()??new StringContent(Body,Encoding.UTF8,"application/json")};}
    }
    private sealed class Fixture:IDisposable
    {
        public TenantSession Session {get;}=new(){TenantId=TestData.TenantA,AccountObjectId=TestData.Operator,OperatorObjectId=TestData.Operator,ClientId=TestData.Emergency,Mode=SessionMode.Assessment,TenantVerified=true,OperatorVerified=true,Scopes=["DeviceManagementConfiguration.Read.All"]};
        public Tokens Tokens {get;}=new();public Handler Handler {get;}=new();public bool Current=true;public DeploymentCoverageReader Reader {get;}
        public Fixture(TimeProvider? clock=null){Reader=new(new HttpClient(Handler),Tokens,TestData.TenantA,SessionMode.Assessment,Session,()=>Current,clock);}
        public void Dispose()=>Reader.Dispose();
    }
    [Fact] public async Task Only_the_compiled_GET_is_sent_with_the_original_verified_context()
    {using var f=new Fixture();var result=await f.Reader.ReadAsync(Request(),CancellationToken.None);var sent=Assert.Single(f.Handler.Sent);Assert.Equal("GET",sent.Method);Assert.Equal("https://graph.microsoft.com/v1.0"+Request().Path,sent.Uri);Assert.Equal("Bearer synthetic-token",sent.Auth);Assert.Empty(result.Rows);Assert.Single(f.Tokens.Calls);}
    [Theory][InlineData("tenant")][InlineData("account")][InlineData("operator")][InlineData("client")][InlineData("mode")][InlineData("verified")][InlineData("scope")][InlineData("context")]
    public async Task Identity_or_access_mismatch_refuses_before_acquisition(string defect)
    {using var f=new Fixture();switch(defect){case "tenant":f.Session.TenantId=TestData.TenantB;break;case "account":f.Session.AccountObjectId=TestData.Office;break;case "operator":f.Session.OperatorObjectId=TestData.Office;break;case "client":f.Session.ClientId=TestData.Office;break;case "mode":f.Session.Mode=SessionMode.Deployment;break;case "verified":f.Session.TenantVerified=false;break;case "scope":f.Session.Scopes.Clear();break;case "context":f.Current=false;break;}await Assert.ThrowsAnyAsync<ToolkitException>(()=>f.Reader.ReadAsync(Request(),CancellationToken.None));Assert.Empty(f.Tokens.Calls);Assert.Empty(f.Handler.Sent);}
    [Fact] public async Task Scope_or_context_change_during_acquisition_prevents_the_send()
    {using var f=new Fixture();f.Tokens.Before=_=>{f.Session.Scopes.Add("User.Read.All");return Task.CompletedTask;};await Assert.ThrowsAsync<TenantMismatchException>(()=>f.Reader.ReadAsync(Request(),CancellationToken.None));Assert.Empty(f.Handler.Sent);}
    [Fact] public async Task Changed_context_after_response_prevents_admission()
    {using var f=new Fixture();f.Handler.After=()=>f.Current=false;await Assert.ThrowsAsync<TenantMismatchException>(()=>f.Reader.ReadAsync(Request(),CancellationToken.None));Assert.Single(f.Handler.Sent);}
    [Theory][InlineData(302)][InlineData(403)][InlineData(429)][InlineData(500)]
    public async Task Failure_is_explicit_and_not_automatically_retried_or_followed(int status)
    {using var f=new Fixture();f.Handler.Statuses.Enqueue((HttpStatusCode)status);await Assert.ThrowsAsync<GraphRequestException>(()=>f.Reader.ReadAsync(Request(),CancellationToken.None));Assert.Single(f.Handler.Sent);}
    [Fact] public async Task Only_one_silent_401_renewal_is_permitted_for_the_same_request()
    {using var f=new Fixture();f.Handler.Statuses.Enqueue(HttpStatusCode.Unauthorized);await f.Reader.ReadAsync(Request(),CancellationToken.None);Assert.Equal(new[]{false,true},f.Tokens.Calls);Assert.Equal(2,f.Handler.Sent.Count);Assert.Equal(f.Handler.Sent[0].Uri,f.Handler.Sent[1].Uri);}
    [Fact] public async Task Repeated_401_is_a_failed_source_not_an_empty_success()
    {using var f=new Fixture();f.Handler.Statuses.Enqueue(HttpStatusCode.Unauthorized);f.Handler.Statuses.Enqueue(HttpStatusCode.Unauthorized);await Assert.ThrowsAsync<GraphRequestException>(()=>f.Reader.ReadAsync(Request(),CancellationToken.None));Assert.Equal(2,f.Handler.Sent.Count);}
    [Theory][InlineData("")][InlineData(" ")][InlineData("bad token")][InlineData("bad\r\nheader")]
    public async Task Malformed_token_never_reaches_HTTP(string token)
    {using var f=new Fixture();f.Tokens.Value=token;await Assert.ThrowsAsync<AuthenticationRequiredException>(()=>f.Reader.ReadAsync(Request(),CancellationToken.None));Assert.Empty(f.Handler.Sent);}
    [Fact] public async Task No_automatic_pagination_from_an_untrusted_link()
    {using var f=new Fixture();f.Handler.Body="{\"value\":[],\"@odata.nextLink\":\"https://evil.invalid/anything\"}";Assert.True((await f.Reader.ReadAsync(Request(),CancellationToken.None)).HasMore);Assert.Single(f.Handler.Sent);}
    [Fact] public async Task Pre_cancelled_read_never_acquires_or_sends()
    {using var f=new Fixture();using var c=new CancellationTokenSource();c.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Reader.ReadAsync(Request(),c.Token));Assert.Empty(f.Tokens.Calls);Assert.Empty(f.Handler.Sent);}
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Total_capture_budget_includes_non_cooperative_acquisition_or_headers(bool headers)
    {var clock=new ConnectAcquisitionBudgetTests.ManualTime();using var f=new Fixture(clock);var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);async Task Pending(CancellationToken _){entered.SetResult();await release.Task;}if(headers)f.Handler.Before=Pending;else f.Tokens.Before=Pending;var task=f.Reader.ReadAsync(Request(),CancellationToken.None);await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));clock.Advance(TimeSpan.FromMinutes(5));try{await Assert.ThrowsAsync<GraphRequestException>(()=>task.WaitAsync(TimeSpan.FromSeconds(1)));}finally{release.TrySetResult();}Assert.Equal(headers?1:0,f.Handler.Sent.Count);}
    [Fact] public async Task Concurrent_read_is_refused_without_another_send()
    {using var f=new Fixture();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);f.Tokens.Before=async _=>{entered.SetResult();await release.Task;};var first=f.Reader.ReadAsync(Request(),CancellationToken.None);await entered.Task;try{await Assert.ThrowsAsync<ConfigurationException>(()=>f.Reader.ReadAsync(Request(),CancellationToken.None));}finally{release.SetResult();}await first;Assert.Single(f.Handler.Sent);}
    [Fact] public async Task Disposed_reader_never_acquires_or_sends()
    {using var f=new Fixture();f.Reader.Dispose();await Assert.ThrowsAsync<ObjectDisposedException>(()=>f.Reader.ReadAsync(Request(),CancellationToken.None));Assert.Empty(f.Tokens.Calls);}
    [Fact] public async Task Cumulative_source_row_budget_cannot_reset_between_reads()
    {using var f=new Fixture();f.Handler.Body="{\"value\":["+string.Join(',',Enumerable.Range(0,3000).Select(i=>"{\"id\":\"row-"+i+"\"}"))+"]}";Assert.Equal(3000,(await f.Reader.ReadAsync(Request(),CancellationToken.None)).Rows.Count);await Assert.ThrowsAsync<ConfigurationException>(()=>f.Reader.ReadAsync(Request(),CancellationToken.None));}
    [Fact] public async Task Actual_stream_bytes_are_bounded_even_when_content_length_lies()
    {using var f=new Fixture();f.Handler.Content=()=>{var content=new ByteArrayContent(new byte[CoverageResponseCodec.MaximumBytes+1]);content.Headers.ContentLength=1;return content;};var error=await Assert.ThrowsAsync<ConfigurationException>(()=>f.Reader.ReadAsync(Request(),CancellationToken.None));Assert.Contains("streamed",error.Message);Assert.Single(f.Handler.Sent);}
    [Fact] public async Task Byte_budget_is_cumulative_across_source_reads()
    {using var f=new Fixture();f.Handler.Body="{\"value\":[],\"padding\":\""+new string('x',3*1024*1024)+"\"}";await f.Reader.ReadAsync(Request(),CancellationToken.None);var error=await Assert.ThrowsAsync<ConfigurationException>(()=>f.Reader.ReadAsync(Request(),CancellationToken.None));Assert.Contains("byte budget",error.Message);}
    [Fact] public async Task Page_budget_does_not_reset_on_the_next_source_read()
    {using var f=new Fixture();for(var i=0;i<100;i++)await f.Reader.ReadAsync(Request(),CancellationToken.None);await Assert.ThrowsAsync<ConfigurationException>(()=>f.Reader.ReadAsync(Request(),CancellationToken.None));Assert.Equal(100,f.Handler.Sent.Count);}
    [Fact] public async Task Requested_cancellation_bounds_a_non_cooperative_token_without_sending()
    {using var f=new Fixture();using var stop=new CancellationTokenSource();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);f.Tokens.Before=async _=>{entered.SetResult();await release.Task;};var read=f.Reader.ReadAsync(Request(),stop.Token);await entered.Task;stop.Cancel();try{await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>read.WaitAsync(TimeSpan.FromSeconds(1)));}finally{release.SetResult();}Assert.Empty(f.Handler.Sent);}
    private sealed class PendingStream:MemoryStream
    {
        public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<int> Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){Entered.TrySetResult();return new(Release.Task);}
    }
    [Fact] public async Task Non_cooperative_body_is_bounded_by_the_same_total_deadline()
    {var clock=new ConnectAcquisitionBudgetTests.ManualTime();using var f=new Fixture(clock);using var stream=new PendingStream();f.Handler.Content=()=>new StreamContent(stream);var read=f.Reader.ReadAsync(Request(),CancellationToken.None);await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));clock.Advance(TimeSpan.FromMinutes(5));try{await Assert.ThrowsAsync<GraphRequestException>(()=>read.WaitAsync(TimeSpan.FromSeconds(1)));}finally{stream.Release.TrySetResult(0);}Assert.Single(f.Handler.Sent);}

}
