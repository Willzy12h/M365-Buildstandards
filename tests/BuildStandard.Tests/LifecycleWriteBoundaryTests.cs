using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Graph;
using BuildStandard.Graph.Auth;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleWriteBoundaryTests
{
    private sealed class Tokens : IAccessTokenProvider
    {public Action? Before;public int Calls;public Task<string> GetAccessTokenAsync(CancellationToken ct)=>GetAccessTokenAsync(false,ct);public Task<string> GetAccessTokenAsync(bool forceRefresh,CancellationToken ct){Calls++;Before?.Invoke();return Task.FromResult("synthetic-token");}}
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls; public HttpStatusCode Status=HttpStatusCode.OK;public string Body="{}";public bool Throw;public string? Method,Path,Content;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message,CancellationToken ct)
        {Calls++;Method=message.Method.Method;Path=message.RequestUri!.AbsolutePath;Content=message.Content is null?null:await message.Content.ReadAsStringAsync(ct);if(Throw)throw new HttpRequestException("synthetic interruption");return new(Status){Content=new StringContent(Body)};}
    }
    private sealed class Fixture : IDisposable
    {
        public TenantSession Session=TestData.Session();public Handler Handler=new();public Tokens Tokens=new();public HttpClient Http;public GraphClient Graph;
        public Fixture(){Http=new(Handler);Graph=new(Http,Tokens,TestData.TenantA,SessionMode.Deployment,GraphRouteAllowList.FromStandard(TestData.Standard()),new(){Sleep=false},NullLog.Instance);Session.TokenExpiresAt=Timestamps.Format(DateTimeOffset.UtcNow.AddMinutes(10));Session.Scopes=["User.Read.All","GroupMember.ReadWrite.All","LicenseAssignment.ReadWrite.All","User.EnableDisableAccount.All","User.RevokeSessions.All"];}
        public Task<JsonObject> Send(LifecycleOperation operation,Action? assert=null,CancellationToken ct=default)
        {
            var method=typeof(GraphClient).GetMethod("ApplyLifecycleAsync");Assert.NotNull(method);var clock=new FixedClock();var admission=new ReviewedWriteAdmission(clock.UtcNow,clock.UtcNow,TimeSpan.FromMinutes(20),TimeSpan.FromMinutes(10),clock,assert??(()=>{}),()=>ct.IsCancellationRequested);
            try{return (Task<JsonObject>)method.Invoke(Graph,new object[]{Session,operation,admission,ct})!;}
            catch(TargetInvocationException error)when(error.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        }
        public void Dispose()=>Http.Dispose();
    }
    public static IEnumerable<object[]> Actions=>LifecycleActionRegistry.ActionIds.Select(id=>new object[]{id});
    private static LifecycleOperation Operation(string id)=>LifecycleActionRegistry.Create(id,TestData.Office,id.EndsWith("member",StringComparison.Ordinal)?TestData.Mam:null,id.EndsWith("license",StringComparison.Ordinal)?TestData.ClientId:null);
    [Theory][MemberData(nameof(Actions))]public async Task Only_registered_exact_method_route_body_is_sent_once(string action)
    {using var f=new Fixture();var operation=Operation(action);await f.Send(operation);Assert.Equal(1,f.Handler.Calls);Assert.Equal(operation.Method,f.Handler.Method);Assert.Equal("/v1.0"+operation.Path,f.Handler.Path);if(operation.PayloadJson is null)Assert.Null(f.Handler.Content);else Assert.True(JsonNode.DeepEquals(JsonNode.Parse(operation.PayloadJson),JsonNode.Parse(f.Handler.Content!)));}
    [Theory][InlineData("mode")][InlineData("tenant")][InlineData("scope")][InlineData("expired")][InlineData("account")][InlineData("path")][InlineData("late-account")][InlineData("late-scope")][InlineData("late-mode")][InlineData("late-context")][InlineData("stop")]
    public async Task Refused_binding_or_stop_means_known_not_sent_and_zero_requests(string fault)
    {
        using var f=new Fixture();var operation=Operation("disable-account");Action current=()=>{};using var stop=new CancellationTokenSource();
        switch(fault)
        {
            case "mode":f.Session.Mode=SessionMode.Assessment;break;case "tenant":f.Session.TenantId=TestData.TenantB;break;
            case "scope":f.Session.Scopes.Clear();break;case "expired":f.Session.TokenExpiresAt=Timestamps.Format(DateTimeOffset.UtcNow.AddMinutes(-1));break;
            case "account":f.Session.OperatorObjectId=TestData.Office;break;case "path":operation=operation with{Path="/users/"+TestData.Office+"/delete"};break;
            case "late-account":f.Tokens.Before=()=>f.Session.AccountObjectId=TestData.Office;break;
            case "late-scope":f.Tokens.Before=()=>f.Session.Scopes.Clear();break;
            case "late-mode":f.Tokens.Before=()=>f.Session.Mode=SessionMode.Assessment;break;
            case "late-context":current=()=>throw new TenantMismatchException("synthetic context changed");break;
            case "stop":f.Tokens.Before=stop.Cancel;break;
        }
        await Assert.ThrowsAsync<WriteNotSentException>(()=>f.Send(operation,current,stop.Token));Assert.Equal(0,f.Handler.Calls);
    }
    [Theory][InlineData(false)][InlineData(true)]public async Task Boolean_revoke_reply_is_preserved_exactly(bool accepted)
    {using var f=new Fixture();f.Handler.Body=accepted?"true":"false";var reply=await f.Send(Operation("revoke-sessions"));Assert.Equal(accepted,reply["value"]!.GetValue<bool>());}
    [Theory][InlineData("transport")][InlineData("500")][InlineData("408")][InlineData("malformed")]
    public async Task After_handoff_failure_is_uncertain_and_never_retried(string fault)
    {using var f=new Fixture();f.Handler.Throw=fault=="transport";if(fault=="500")f.Handler.Status=HttpStatusCode.InternalServerError;if(fault=="408")f.Handler.Status=HttpStatusCode.RequestTimeout;if(fault=="malformed")f.Handler.Body="not-json";await Assert.ThrowsAsync<AmbiguousWriteException>(()=>f.Send(Operation("disable-account")));Assert.Equal(1,f.Handler.Calls);Assert.Equal(1,f.Tokens.Calls);}
}
