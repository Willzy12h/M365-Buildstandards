using System.Net;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Graph;
using BuildStandard.Graph.Auth;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ConditionalAccessEvaluatorTests
{
    private const string Endpoint="https://graph.microsoft.com/v1.0/identity/conditionalAccess/evaluate";
    private static ConditionalAccessScenario Scenario()=>new(TestData.Operator,TestData.TenantA,"windows","browser");
    private static string ValidResponse=>"{\"value\":[{\"id\":\""+TestData.Emergency+"\",\"displayName\":\"Synthetic\",\"state\":\"enabled\",\"policyApplies\":true,\"analysisReasons\":\"notSet\"}]}";
    private sealed class Tokens:IAccessTokenProvider
    {
        public List<bool> Refreshes {get;}=[];
        public Func<CancellationToken,Task>? Before;
        public string Value="synthetic-token";
        public Task<string> GetAccessTokenAsync(CancellationToken ct)=>GetAccessTokenAsync(false,ct);
        public async Task<string> GetAccessTokenAsync(bool forceRefresh,CancellationToken ct)
        {Refreshes.Add(forceRefresh);if(Before is not null)await Before(ct);return Value;}
    }
    private sealed record Sent(string Method,string Uri,string Body,string? Auth);
    private sealed class Handler:HttpMessageHandler
    {
        public List<Sent> Sent {get;}=[];
        public Queue<(HttpStatusCode,string)> Responses {get;}=new();
        public Action? AfterResponse;
        public Func<CancellationToken,Task>? BeforeResponse;
        public Func<HttpContent>? Content;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Sent.Add(new(request.Method.Method,request.RequestUri!.AbsoluteUri,await request.Content!.ReadAsStringAsync(ct),request.Headers.Authorization?.ToString()));
            var response=Responses.Count>0?Responses.Dequeue():(HttpStatusCode.OK,ValidResponse);
            if(BeforeResponse is not null)await BeforeResponse(ct);
            AfterResponse?.Invoke();return new(response.Item1){Content=Content?.Invoke()??new StringContent(response.Item2,Encoding.UTF8,"application/json")};
        }
    }
    private sealed class Fixture:IDisposable
    {
        public TenantSession Session {get;}=new(){TenantId=TestData.TenantA,AccountObjectId=TestData.Operator,OperatorObjectId=TestData.Operator,
            ClientId=TestData.Emergency,Mode=SessionMode.Assessment,TenantVerified=true,OperatorVerified=true,Scopes=["Policy.Read.All"]};
        public Tokens Tokens {get;}=new();public Handler Handler {get;}=new();public bool Current=true;
        public ConditionalAccessEvaluator Evaluator {get;}
        public Fixture(TimeProvider? clock=null,Action<TenantSession>? configure=null)
        {configure?.Invoke(Session);Evaluator=new(new HttpClient(Handler),Tokens,TestData.TenantA,SessionMode.Assessment,Session,()=>Current,clock);}
        public void Dispose()=>Evaluator.Dispose();
    }
    [Fact] public async Task Only_the_fixed_evaluation_POST_is_sent_in_assessment_with_the_frozen_typed_body()
    {
        using var f=new Fixture();var read=await f.Evaluator.EvaluateAsync(Scenario(),CancellationToken.None);var sent=Assert.Single(f.Handler.Sent);
        Assert.Equal("POST",sent.Method);Assert.Equal(Endpoint,sent.Uri);Assert.Equal("Bearer synthetic-token",sent.Auth);
        Assert.Equal(ConditionalAccessScenarioCodec.Prepare(Scenario()).Bytes,Encoding.UTF8.GetBytes(sent.Body));Assert.Single(f.Tokens.Refreshes);
        Assert.Equal(TestData.TenantA,read.TenantId);Assert.Equal(TestData.Operator,read.AccountObjectId);Assert.Single(read.Result.Rows);
        Assert.Equal(CanonicalJson.Sha256Hex(read.OriginalResponseBytes),read.Result.OriginalSha256);
        var bytes=read.OriginalResponseBytes;bytes[0]=0;Assert.NotEqual(0,read.OriginalResponseBytes[0]);
    }
    [Theory][InlineData("tenant")][InlineData("account")][InlineData("operator")][InlineData("unverified")][InlineData("mode")][InlineData("scope")][InlineData("context")]
    public async Task Wrong_identity_or_access_is_refused_before_token_acquisition(string defect)
    {
        using var f=new Fixture();switch(defect){case "tenant":f.Session.TenantId=TestData.TenantB;break;case "account":f.Session.AccountObjectId=TestData.Emergency;break;
            case "operator":f.Session.OperatorObjectId=TestData.Emergency;break;case "unverified":f.Session.OperatorVerified=false;break;case "mode":f.Session.Mode=SessionMode.Deployment;break;
            case "scope":f.Session.Scopes.Clear();break;case "context":f.Current=false;break;}
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>f.Evaluator.EvaluateAsync(Scenario(),CancellationToken.None));Assert.Empty(f.Tokens.Refreshes);Assert.Empty(f.Handler.Sent);
    }
    [Theory][InlineData("account")][InlineData("scope")][InlineData("context")]
    public async Task A_change_while_acquiring_a_token_prevents_the_send(string defect)
    {
        using var f=new Fixture();f.Tokens.Before=_=>{if(defect=="account")f.Session.AccountObjectId=TestData.Emergency;else if(defect=="scope")f.Session.Scopes.Add("Mail.Send");else f.Current=false;return Task.CompletedTask;};
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>f.Evaluator.EvaluateAsync(Scenario(),CancellationToken.None));Assert.Empty(f.Handler.Sent);
    }
    [Fact] public async Task Cancellation_after_a_synchronous_token_return_still_prevents_send()
    {
        using var f=new Fixture();using var stop=new CancellationTokenSource();f.Tokens.Before=_=>{stop.Cancel();return Task.CompletedTask;};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Evaluator.EvaluateAsync(Scenario(),stop.Token));Assert.Empty(f.Handler.Sent);
    }
    [Fact] public async Task A_late_response_cannot_be_admitted_after_context_change()
    {using var f=new Fixture();f.Handler.AfterResponse=()=>f.Current=false;await Assert.ThrowsAnyAsync<ToolkitException>(()=>f.Evaluator.EvaluateAsync(Scenario(),CancellationToken.None));Assert.Single(f.Handler.Sent);}
    [Fact] public async Task A_401_has_one_silent_renewal_with_the_same_request_and_no_interactive_path()
    {
        using var f=new Fixture();f.Handler.Responses.Enqueue((HttpStatusCode.Unauthorized,""));var read=await f.Evaluator.EvaluateAsync(Scenario(),CancellationToken.None);
        Assert.Equal(new[]{false,true},f.Tokens.Refreshes);Assert.Equal(2,f.Handler.Sent.Count);Assert.Equal(f.Handler.Sent[0].Body,f.Handler.Sent[1].Body);Assert.Single(read.Result.Rows);
    }
    [Theory][InlineData(401)][InlineData(429)][InlineData(500)][InlineData(503)][InlineData(302)][InlineData(307)]
    public async Task Errors_and_redirects_are_not_retried_as_general_reads(int status)
    {
        using var f=new Fixture();f.Handler.Responses.Enqueue(((HttpStatusCode)status,"{}"));if(status==401)f.Handler.Responses.Enqueue((HttpStatusCode.Unauthorized,"{}"));
        var error=await Assert.ThrowsAsync<GraphRequestException>(()=>f.Evaluator.EvaluateAsync(Scenario(),CancellationToken.None));Assert.Equal(status,error.StatusCode);Assert.Equal(status==401?2:1,f.Handler.Sent.Count);
    }
    [Fact] public async Task A_malformed_result_does_not_produce_a_successful_empty_report_or_retry()
    {using var f=new Fixture();f.Handler.Responses.Enqueue((HttpStatusCode.OK,"{}"));await Assert.ThrowsAsync<ConfigurationException>(()=>f.Evaluator.EvaluateAsync(Scenario(),CancellationToken.None));Assert.Single(f.Handler.Sent);}
    [Fact] public async Task A_pre_cancelled_request_makes_no_token_or_HTTP_call()
    {using var f=new Fixture();using var stop=new CancellationTokenSource();stop.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Evaluator.EvaluateAsync(Scenario(),stop.Token));Assert.Empty(f.Tokens.Refreshes);Assert.Empty(f.Handler.Sent);}
    [Theory][InlineData(true)][InlineData(false)]
    public async Task The_total_deadline_includes_acquisition_even_if_timer_delivery_is_delayed(bool deliverTimers)
    {
        var clock=new ConnectAcquisitionBudgetTests.ManualTime();using var f=new Fixture(clock);f.Tokens.Before=_=>{clock.Advance(TimeSpan.FromSeconds(121),deliverTimers);return Task.CompletedTask;};
        var error=await Assert.ThrowsAsync<GraphRequestException>(()=>f.Evaluator.EvaluateAsync(Scenario(),CancellationToken.None));Assert.Equal("timeout",error.ErrorCode);Assert.Empty(f.Handler.Sent);
    }
    [Fact] public async Task A_disposed_evaluator_has_no_token_or_send_authority()
    {using var f=new Fixture();f.Evaluator.Dispose();await Assert.ThrowsAsync<ObjectDisposedException>(()=>f.Evaluator.EvaluateAsync(Scenario(),CancellationToken.None));Assert.Empty(f.Tokens.Refreshes);Assert.Empty(f.Handler.Sent);}
    [Theory][InlineData("")][InlineData(" ")][InlineData("invalid\nheader")]
    public async Task An_unusable_token_is_refused_as_authentication_failure_without_sending(string token)
    {using var f=new Fixture();f.Tokens.Value=token;await Assert.ThrowsAsync<AuthenticationRequiredException>(()=>f.Evaluator.EvaluateAsync(Scenario(),CancellationToken.None));Assert.Empty(f.Handler.Sent);}
    [Theory][InlineData("account")][InlineData("client")]
    public async Task Whitespace_in_an_initial_actor_identity_is_not_a_verified_context(string field)
    {using var f=new Fixture(configure:s=>{if(field=="account"){s.AccountObjectId+="\n";s.OperatorObjectId=s.AccountObjectId;}else s.ClientId+=" ";});await Assert.ThrowsAnyAsync<ToolkitException>(()=>f.Evaluator.EvaluateAsync(Scenario(),CancellationToken.None));Assert.Empty(f.Tokens.Refreshes);Assert.Empty(f.Handler.Sent);}
    private sealed class ProbeStream():MemoryStream(new byte[6*1024*1024])
    {
        public int ReadBytes;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
        {var count=await base.ReadAsync(buffer,ct);ReadBytes+=count;return count;}
    }
    [Fact] public async Task Streaming_stops_at_the_bound_even_when_Content_Length_is_false()
    {
        using var f=new Fixture();var stream=new ProbeStream();f.Handler.Content=()=>{var c=new StreamContent(stream);c.Headers.ContentLength=0;return c;};
        await Assert.ThrowsAsync<ConfigurationException>(()=>f.Evaluator.EvaluateAsync(Scenario(),CancellationToken.None));
        Assert.InRange(stream.ReadBytes,1,5*1024*1024+16*1024);Assert.Single(f.Handler.Sent);
    }

    [Fact] public async Task A_non_cooperative_token_acquisition_cannot_outlive_the_overall_budget_or_send_late()
    {
        var clock=new ConnectAcquisitionBudgetTests.ManualTime();using var f=new Fixture(clock);
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Tokens.Before=async _=>{started.TrySetResult();await release.Task;};
        var pending=f.Evaluator.EvaluateAsync(Scenario(),CancellationToken.None);await started.Task;clock.Advance(TimeSpan.FromSeconds(121));
        try{var error=await Assert.ThrowsAsync<GraphRequestException>(()=>pending.WaitAsync(TimeSpan.FromSeconds(1)));Assert.Equal("timeout",error.ErrorCode);}
        finally{release.TrySetResult();try{await pending;}catch(GraphRequestException){}}
        Assert.Empty(f.Handler.Sent);
    }

    private sealed class PausingStream:MemoryStream
    {
        public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<int> Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default)
        {Started.TrySetResult();return new(Release.Task);}
    }
    [Fact] public async Task A_body_stream_that_ignores_cancellation_cannot_outlive_the_overall_budget()
    {
        var clock=new ConnectAcquisitionBudgetTests.ManualTime();using var f=new Fixture(clock);var body=new PausingStream();f.Handler.Content=()=>new StreamContent(body);
        var pending=f.Evaluator.EvaluateAsync(Scenario(),CancellationToken.None);await body.Started.Task;clock.Advance(TimeSpan.FromSeconds(121));
        try{var error=await Assert.ThrowsAsync<GraphRequestException>(()=>pending.WaitAsync(TimeSpan.FromSeconds(1)));Assert.Equal("timeout",error.ErrorCode);}
        finally{body.Release.TrySetResult(0);try{await pending;}catch(GraphRequestException){}}
        Assert.Single(f.Handler.Sent);
    }

    [Fact] public async Task A_headers_read_that_ignores_cancellation_cannot_outlive_the_overall_budget()
    {
        var clock=new ConnectAcquisitionBudgetTests.ManualTime();using var f=new Fixture(clock);var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Handler.BeforeResponse=async _=>{started.TrySetResult();await release.Task;};var pending=f.Evaluator.EvaluateAsync(Scenario(),CancellationToken.None);await started.Task;clock.Advance(TimeSpan.FromSeconds(121));
        try{var error=await Assert.ThrowsAsync<GraphRequestException>(()=>pending.WaitAsync(TimeSpan.FromSeconds(1)));Assert.Equal("timeout",error.ErrorCode);}
        finally{release.TrySetResult();try{await pending;}catch(GraphRequestException){}}
        Assert.Single(f.Handler.Sent);
    }

}
