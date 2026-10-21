using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Graph;
using BuildStandard.Graph.Auth;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleReadTransportTests
{
    private const string User = "11111111-1111-4111-8111-111111111111";
    private sealed class Tokens : IAccessTokenProvider
    {
        public int Calls; public int Renewals; public Action? Before;
        public Task<string> GetAccessTokenAsync(CancellationToken token) => GetAccessTokenAsync(false,token);
        public Task<string> GetAccessTokenAsync(bool forceRefresh,CancellationToken token) { token.ThrowIfCancellationRequested(); Calls++; if(forceRefresh) Renewals++; Before?.Invoke(); return Task.FromResult("synthetic-token"); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls; public List<Uri> Seen = []; public Func<int,Uri,HttpResponseMessage>? Reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Assert.Equal(HttpMethod.Get,request.Method); Assert.Equal("graph.microsoft.com",request.RequestUri!.Host);
            Calls++; Seen.Add(request.RequestUri); return Task.FromResult(Reply?.Invoke(Calls,request.RequestUri) ?? Json("{\"id\":\""+User+"\"}"));
        }
    }
    private static HttpResponseMessage Json(string text,HttpStatusCode code=HttpStatusCode.OK) => new(code) { Content = new StringContent(text,Encoding.UTF8,"application/json") };
    private sealed class Fixture : IDisposable
    {
        public TenantSession Session = TestData.Session(); public Tokens Tokens = new(); public Handler Handler = new(); public HttpClient Http;
        public Fixture() { Http=new(Handler); Session.TokenExpiresAt=Timestamps.Format(DateTimeOffset.UtcNow.AddHours(1)); Session.Scopes=["User.Read.All","Group.Read.All","RoleManagement.Read.Directory","Organization.Read.All"]; }
        public object Source()
        {
            var type=typeof(GraphClient).Assembly.GetType("BuildStandard.Graph.Lifecycle.LifecycleReadTransport"); Assert.NotNull(type);
            try { return Activator.CreateInstance(type,BindingFlags.NonPublic|BindingFlags.Instance,null,new object[] {Http,Tokens,Session,LifecycleActionRegistry.Create("disable-account",User),(Action)(()=>{}),false},null)!; }
            catch(TargetInvocationException error) when(error.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        public void Dispose()=>Http.Dispose();
    }
    private static async Task<object> Page(object source,string id,CancellationToken token=default)
    {
        Task task;
        try { task=(Task)source.GetType().GetMethod("ReadNextAsync")!.Invoke(source,new object[] {id,token})!; }
        catch(TargetInvocationException error) when(error.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        await task; return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }
    [Fact] public async Task Navigation_acquires_no_token_and_registered_user_read_has_one_fixed_target()
    {
        using var f=new Fixture(); using var source=(IDisposable)f.Source(); Assert.Equal(0,f.Tokens.Calls); Assert.Equal(0,f.Handler.Calls);
        var page=await Page(source,"user"); Assert.Equal(1,f.Handler.Calls); Assert.Contains("/users/"+User,f.Handler.Seen[0].AbsolutePath);
        Assert.False((bool)page.GetType().GetProperty("HasMore")!.GetValue(page)!);
    }
    [Theory][InlineData("unknown-source")][InlineData("cancel")][InlineData("disposed")][InlineData("changed-account")]
    public async Task Unknown_context_and_cancelled_reads_send_nothing(string fault)
    {
        using var f=new Fixture(); using var source=(IDisposable)f.Source();
        if(fault=="disposed") source.Dispose(); if(fault=="changed-account") f.Tokens.Before=()=>f.Session.AccountObjectId=User;
        var failure = await Record.ExceptionAsync(()=>Page(source,fault=="unknown-source"?"unregistered":"user",fault=="cancel"?new CancellationToken(true):default));
        Assert.True(failure is ToolkitException or OperationCanceledException or ObjectDisposedException, failure?.GetType().FullName ?? "No refusal");
        Assert.Equal(0,f.Handler.Calls);
    }
    [Theory][InlineData("foreign")][InlineData("filter")][InlineData("duplicate-query")]
    public async Task Continuation_cannot_change_resource_principal_filter_or_query_meaning(string fault)
    {
        using var f=new Fixture(); f.Handler.Reply=(_,uri)=>
        {
            var next=fault=="foreign"?"https://other.invalid/v1.0/roleManagement/directory/roleAssignments":uri.AbsoluteUri+"&$skiptoken=next";
            if(fault=="filter") next=next.Replace(User,TestData.Operator,StringComparison.Ordinal);
            if(fault=="duplicate-query") next+="&$filter=principalId%20eq%20'"+User+"'";
            return Json(new JsonObject { ["value"]=new JsonArray(),["@odata.nextLink"]=next }.ToJsonString());
        };
        using var source=(IDisposable)f.Source(); await Assert.ThrowsAnyAsync<ToolkitException>(()=>Page(source,"currentRoles")); Assert.Equal(1,f.Handler.Calls);
    }
    [Fact] public async Task One_silent_401_renewal_and_no_other_automatic_retry()
    {
        using var f=new Fixture(); f.Handler.Reply=(call,_)=>call==1?Json("{}",HttpStatusCode.Unauthorized):Json("{\"id\":\""+User+"\"}");
        using var source=(IDisposable)f.Source(); await Page(source,"user"); Assert.Equal(2,f.Handler.Calls); Assert.Equal(1,f.Tokens.Renewals);
        f.Handler.Reply=(_,_)=>Json("{}",HttpStatusCode.ServiceUnavailable);
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>Page(source,"currentRoles")); Assert.Equal(3,f.Handler.Calls);
    }
    [Fact] public async Task Actual_request_cap_includes_every_page_and_refuses_the_101st_send()
    {
        using var f=new Fixture(); f.Handler.Reply=(call,uri)=>
        {
            var start=uri.AbsoluteUri.Split("&$skiptoken=",StringSplitOptions.None)[0];
            return Json(new JsonObject { ["value"]=new JsonArray(),["@odata.nextLink"]=start+"&$skiptoken="+call }.ToJsonString());
        };
        using var source=(IDisposable)f.Source(); for(var index=0;index<100;index++) await Page(source,"currentRoles");
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>Page(source,"currentRoles")); Assert.Equal(100,f.Handler.Calls);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Graph_factory_reuses_only_the_matching_verified_connection_without_sign_in(bool wrongTenant)
    {
        using var f = new Fixture();
        var graph = new GraphClient(f.Http,f.Tokens,TestData.TenantA,f.Session.Mode,GraphRouteAllowList.ForReports(),new GraphClientOptions { Sleep=false },BuildStandard.Core.Diagnostics.NullLog.Instance);
        var method=typeof(GraphClient).GetMethod("ForLifecycleReads"); Assert.NotNull(method);
        if(wrongTenant) f.Session.TenantId=TestData.TenantB;
        if(wrongTenant)
        {
            var error=Assert.Throws<TargetInvocationException>(()=>method.Invoke(graph,new object[] {f.Session,LifecycleActionRegistry.Create("disable-account",User),(Action)(()=>{})}));
            Assert.IsAssignableFrom<ToolkitException>(error.InnerException);
        }
        else { using var source=(IDisposable)method.Invoke(graph,new object[] {f.Session,LifecycleActionRegistry.Create("disable-account",User),(Action)(()=>{})})!; }
        Assert.Equal(0,f.Tokens.Calls); Assert.Equal(0,f.Handler.Calls);
    }
    [Fact] public async Task Equivalent_query_space_encoding_preserves_the_exact_principal_filter()
    {
        using var f=new Fixture(); f.Handler.Reply=(call,uri)=>call==1
            ? Json(new JsonObject { ["value"]=new JsonArray(),["@odata.nextLink"]=uri.AbsoluteUri.Replace("%20","+",StringComparison.Ordinal)+"&$skiptoken=next" }.ToJsonString())
            : Json("{\"value\":[]}");
        using var source=(IDisposable)f.Source(); await Page(source,"currentRoles"); await Page(source,"currentRoles"); Assert.Equal(2,f.Handler.Calls);
    }
    [Theory][InlineData("rows")][InlineData("bytes")][InlineData("duplicate-json")][InlineData("missing-value")][InlineData("wrong-user")]
    public async Task Oversized_malformed_and_wrong_identity_responses_never_become_complete_empty_pages(string fault)
    {
        using var f = new Fixture(); f.Handler.Reply=(_,_)=> fault switch
        {
            "rows" => Json("{\"value\":["+string.Join(',',Enumerable.Repeat("{}",5001))+"]}"),
            "bytes" => Json(new string(' ',16*1024*1024+1)),
            "duplicate-json" => Json("{\"value\":[],\"value\":[]}"),
            "wrong-user" => Json("{\"id\":\""+TestData.Operator+"\"}"), _ => Json("{}")
        };
        using var source=(IDisposable)f.Source();
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>Page(source,fault=="wrong-user"?"user":"currentRoles")); Assert.Equal(1,f.Handler.Calls);
    }
    [Theory][InlineData("scope")][InlineData("audience")][InlineData("lifetime")]
    public void Missing_read_access_or_unknown_lifetime_refuses_before_token_or_network(string fault)
    {
        using var f = new Fixture();
        if(fault=="lifetime") f.Session.TokenExpiresAt="";
        else { f.Session.Scopes.Remove("User.Read.All"); if(fault=="audience") f.Session.Scopes.Add("https://other.invalid/User.Read.All"); }
        Assert.ThrowsAny<ToolkitException>(()=>f.Source()); Assert.Equal(0,f.Tokens.Calls); Assert.Equal(0,f.Handler.Calls);
    }
}
