using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using BuildStandard.Graph;
using BuildStandard.Graph.Auth;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ConditionalAccessEvaluationServiceTests
{
    private sealed class Tokens:IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken ct)=>Task.FromResult("synthetic-token");
        public Task<string> GetAccessTokenAsync(bool forceRefresh,CancellationToken ct)=>GetAccessTokenAsync(ct);
    }
    private sealed class Handler:HttpMessageHandler
    {
        public int Sends;public Action? After;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {Sends++;After?.Invoke();return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(ConditionalAccessEvaluationSchemaTests.Response())});}
    }
    private sealed class LookupGraph:IGraphClient
    {
        public string TenantId=>TestData.TenantA;public SessionMode Mode=>SessionMode.Assessment;
        public List<string> Reads {get;}=[];public List<string> Writes {get;}=[];
        private JsonObject _result=new();public Func<string,CancellationToken,Task>? BeforeRead;
        public void SetSingleton(string path,JsonObject result)=>_result=(JsonObject)result.DeepClone();
        public async Task<JsonObject> GetAsync(GraphApi api,string path,CancellationToken ct)
        {Reads.Add(path);if(BeforeRead is not null)await BeforeRead(path,ct);return (JsonObject)_result.DeepClone();}
        public Task<IReadOnlyList<JsonObject>> GetAllAsync(GraphApi api,string path,CancellationToken ct)=>throw new InvalidOperationException("Unbounded lookup is forbidden.");
        public Task<JsonObject> WriteAsync(GraphApi api,GraphWriteMethod method,string path,JsonObject body,CancellationToken ct)=>throw new InvalidOperationException("No writes.");
        public Task<JsonObject> WriteReviewedAsync(GraphApi api,GraphWriteMethod method,string path,JsonObject body,BuildStandard.Core.Safety.ReviewedWriteAdmission admission,CancellationToken ct)=>throw new InvalidOperationException("No writes.");
    }
    private sealed class Fixture:IDisposable
    {
        public readonly TenantSession Session=new(){TenantId=TestData.TenantA,AccountObjectId=TestData.Operator,OperatorObjectId=TestData.Operator,
            ClientId=TestData.Emergency,Mode=SessionMode.Assessment,TenantVerified=true,OperatorVerified=true,Scopes=["Policy.Read.All","User.Read.All"]};
        public readonly LookupGraph Graph=new();
        public readonly Handler Http=new();public readonly ConditionalAccessEvaluator Evaluator;public bool Current=true;
        public JsonObject Member=>ToolkitJson();
        private static JsonObject ToolkitJson()=>new(){["id"]=TestData.Operator,["userType"]="Member",["creationType"]=null,["externalUserState"]=null,["accountEnabled"]=true};
        public Fixture(TimeProvider? clock=null){Evaluator=new(new HttpClient(Http),new Tokens(),TestData.TenantA,SessionMode.Assessment,Session,()=>Current,clock);Set(Member);}
        public void Set(JsonObject member,bool next=false)=>Graph.SetSingleton("/users",new(){["value"]=new JsonArray(member),["@odata.nextLink"]=next?"https://graph.microsoft.com/v1.0/users?$skiptoken=secret":null});
        public Task<ConditionalAccessEvaluationCapture> Run(TimeProvider? clock=null,CancellationToken ct=default)=>new ConditionalAccessEvaluationService(clock).CaptureAsync(Graph,Evaluator,Session,new(TestData.Operator,TestData.TenantA,"windows","browser"),()=>Current,ct);
        public void Dispose()=>Evaluator.Dispose();
    }
    [Fact] public async Task Fresh_exact_member_lookup_precedes_evaluation_and_preserves_both_original_files()
    {
        using var f=new Fixture();var read=await f.Run();var path=Assert.Single(f.Graph.Reads);Assert.StartsWith("/users?",path);Assert.Contains(TestData.Operator,path);Assert.Contains("$top=2",path);Assert.Equal(1,f.Http.Sends);
        var r=ConditionalAccessEvaluationSchema.Read(read.MetadataBytes,read.OriginalResponseBytes,TestData.TenantA);
        Assert.Equal(TestData.Emergency,r.ClientId);Assert.Equal(SessionMode.Assessment,r.PermissionMode);Assert.Equal("Complete",r.Status);
        var bytes=read.OriginalResponseBytes;bytes[0]=0;Assert.NotEqual(0,read.OriginalResponseBytes[0]);read.Metadata.TenantId=TestData.TenantB;Assert.Equal(TestData.TenantA,read.Metadata.TenantId);
        Assert.Empty(f.Graph.Writes);
    }
    [Theory][InlineData("guest")][InlineData("unknown-type")][InlineData("missing-type")][InlineData("external-state")][InlineData("invitation")][InlineData("missing-origin")][InlineData("wrong-id")][InlineData("next-page")][InlineData("missing-enabled")][InlineData("bool-string")]
    public async Task Unsupported_or_incomplete_member_lookup_never_starts_evaluation(string defect)
    {
        using var f=new Fixture();var m=f.Member;switch(defect){case "guest":m["userType"]="Guest";break;case "unknown-type":m["userType"]="future";break;case "missing-type":m.Remove("userType");break;
            case "external-state":m["externalUserState"]="Accepted";break;case "invitation":m["creationType"]="Invitation";break;case "missing-origin":m.Remove("creationType");break;
            case "wrong-id":m["id"]=TestData.Emergency;break;case "missing-enabled":m.Remove("accountEnabled");break;case "bool-string":m["accountEnabled"]="true";break;}
        f.Set(m,defect=="next-page");await Assert.ThrowsAnyAsync<ToolkitException>(()=>f.Run());Assert.Equal(0,f.Http.Sends);Assert.Empty(f.Graph.Writes);
    }
    [Theory][InlineData("empty")][InlineData("null")][InlineData("duplicate")]
    public async Task An_unavailable_or_ambiguous_exact_target_is_not_replaced_with_another_user(string defect)
    {
        using var f=new Fixture();f.Graph.SetSingleton("/users",new(){["value"]=defect=="null"?null:defect=="empty"?new JsonArray():new JsonArray(f.Member,f.Member)});
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>f.Run());Assert.Equal(0,f.Http.Sends);
    }
    [Theory][InlineData("tenant")][InlineData("account")][InlineData("client")][InlineData("scope")][InlineData("unverified")][InlineData("context")]
    public async Task Invalid_initial_context_is_refused_before_any_read(string defect)
    {
        using var f=new Fixture();switch(defect){case "tenant":f.Session.TenantId=TestData.TenantB;break;case "account":f.Session.AccountObjectId="";break;case "client":f.Session.ClientId="";break;
            case "scope":f.Session.Scopes.Remove("User.Read.All");break;case "unverified":f.Session.OperatorVerified=false;break;case "context":f.Current=false;break;}
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>f.Run());Assert.Empty(f.Graph.Reads);Assert.Equal(0,f.Http.Sends);
    }
    [Fact] public async Task Context_change_after_lookup_blocks_evaluation()
    {using var f=new Fixture();f.Graph.BeforeRead=(_,_)=>{f.Current=false;return Task.CompletedTask;};await Assert.ThrowsAnyAsync<ToolkitException>(()=>f.Run());Assert.Equal(0,f.Http.Sends);}
    [Fact] public async Task Context_change_after_evaluation_blocks_result_admission()
    {using var f=new Fixture();f.Http.After=()=>f.Current=false;await Assert.ThrowsAnyAsync<ToolkitException>(()=>f.Run());Assert.Equal(1,f.Http.Sends);}
    [Fact] public async Task A_stopped_lookup_that_ignores_cancellation_cannot_produce_a_late_send()
    {
        using var f=new Fixture();using var stop=new CancellationTokenSource();var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Graph.BeforeRead=(_,_)=>{stop.Cancel();return pending.Task;};
        try{await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Run(ct:stop.Token).WaitAsync(TimeSpan.FromSeconds(1)));Assert.Equal(0,f.Http.Sends);}
        finally{pending.TrySetResult();}
    }
    [Fact] public async Task Overall_budget_includes_the_member_read_even_if_it_ignores_cancellation()
    {
        var clock=new ConnectAcquisitionBudgetTests.ManualTime();using var f=new Fixture(clock);var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Graph.BeforeRead=(_,_)=>pending.Task;var run=f.Run(clock);clock.Advance(TimeSpan.FromSeconds(121));
        try{var error=await Assert.ThrowsAsync<GraphRequestException>(()=>run.WaitAsync(TimeSpan.FromSeconds(1)));Assert.Equal("timeout",error.ErrorCode);Assert.Equal(0,f.Http.Sends);}
        finally{pending.TrySetResult();}
    }
}
