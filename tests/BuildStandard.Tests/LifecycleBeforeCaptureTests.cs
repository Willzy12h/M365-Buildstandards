using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleBeforeCaptureTests
{
    private const string User="11111111-1111-4111-8111-111111111111";
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; }=new(2026,10,21,9,0,0,TimeSpan.Zero); }
    private sealed class Source : ILifecycleReadSource
    {
        public string TenantId { get; set; }=TestData.TenantA;
        public string AccountObjectId=>TestData.Operator;
        public string ClientId=>TestData.ClientId;
        public SessionMode Mode=>SessionMode.Deployment;
        public LifecycleOperation Operation { get; set; }=LifecycleActionRegistry.Create("disable-account",User);
        public int RequestCount { get; private set; }
        public Func<string,LifecycleReadPage>? Reply;
        public Action<string>? Before;
        public Task<LifecycleReadPage> ReadNextAsync(string id,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); RequestCount++; Before?.Invoke(id);
            return Task.FromResult(Reply?.Invoke(id)??new LifecycleReadPage(id=="user"?[new JsonObject {
                ["id"]=User,["displayName"]="Synthetic target",["userPrincipalName"]="target@synthetic.invalid",["userType"]="Member",["accountEnabled"]=true,["onPremisesSyncEnabled"]=false,
                ["passwordProfile"]=new JsonObject { ["password"]="never-persist" },
                ["assignedLicenses"]=new JsonArray(new JsonObject { ["skuId"]=TestData.ClientId,["disabledPlans"]=new JsonArray(),["secret"]="never-persist" }) }]:[],false));
        }
        public void Dispose() { }
    }
    private static async Task<object> Capture(Source source,Clock clock,TenantSession? session=null,Action? current=null)
    {
        var type=typeof(BuildStandard.Engine.Execution.ReviewedChangeService).Assembly.GetType("BuildStandard.Engine.Execution.LifecycleBeforeCaptureService"); Assert.NotNull(type);
        var service=Activator.CreateInstance(type,clock)!; var method=type.GetMethod("CaptureAsync"); Assert.NotNull(method);
        if(session is null) { session=TestData.Session(); session.Scopes=["User.Read.All","Group.Read.All","RoleManagement.Read.Directory","Organization.Read.All"]; }
        Task task;
        try { task=(Task)method.Invoke(service,new object[] {source,session,LifecycleActionRegistry.Create("disable-account",User),current??(()=>{}),CancellationToken.None})!; }
        catch(TargetInvocationException error) when(error.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        await task; return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }
    private static JsonObject Observation(object captured)=>(JsonObject)captured.GetType().GetMethod("GetObservation")!.Invoke(captured,null)!;
    [Fact] public async Task Completed_before_capture_keeps_exact_source_binding_and_drops_unrelated_nested_private_content()
    {
        using var source=new Source(); var result=await Capture(source,new Clock()); var value=Observation(result);
        Assert.True(value["complete"]!.GetValue<bool>()); Assert.Equal(TestData.TenantA,value["tenantId"]!.GetValue<string>());
        Assert.Equal(User,value["user"]!["id"]!.GetValue<string>()); Assert.Equal(4,source.RequestCount);
        var bytes=(byte[])result.GetType().GetMethod("GetOriginalBytes")!.Invoke(result,null)!;
        Assert.DoesNotContain("never-persist",Encoding.UTF8.GetString(bytes)); Assert.DoesNotContain("passwordProfile",Encoding.UTF8.GetString(bytes));
        value["user"]!["id"]=TestData.Operator; Assert.Equal(User,Observation(result)["user"]!["id"]!.GetValue<string>());
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Failed_or_cancelled_later_read_preserves_earlier_rows_without_empty_safe_completion(bool cancelled)
    {
        using var source=new Source(); source.Before=id=>{if(id=="currentRoles"){if(cancelled)throw new OperationCanceledException();throw new HttpRequestException("private-server-detail");}};
        var value=Observation(await Capture(source,new Clock())); Assert.False(value["complete"]!.GetValue<bool>());
        Assert.Equal(User,value["user"]!["id"]!.GetValue<string>()); Assert.False(value.ContainsKey("currentRoles"));
        Assert.DoesNotContain("private-server-detail",value.ToJsonString()); Assert.Equal(2,source.RequestCount);
    }
    [Fact] public async Task Late_context_change_retains_original_binding_and_does_not_admit_the_new_page()
    {
        using var source=new Source(); var changed=false; source.Before=id=>{if(id=="currentRoles")changed=true;};
        var result=await Capture(source,new Clock(),current:()=>{if(changed)throw new TenantMismatchException("Changed");});
        var value=Observation(result); Assert.False(value["complete"]!.GetValue<bool>()); Assert.Equal(TestData.Operator,value["accountObjectId"]!.GetValue<string>());
        Assert.Equal(User,value["user"]!["id"]!.GetValue<string>()); Assert.False(value.ContainsKey("currentRoles"));
    }
    [Theory][InlineData("tenant")][InlineData("operation")]
    public async Task Wrong_source_identity_or_action_is_refused_before_any_read(string fault)
    {
        using var source=new Source(); if(fault=="tenant")source.TenantId=TestData.TenantB;else source.Operation=LifecycleActionRegistry.Create("enable-account",User);
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>Capture(source,new Clock())); Assert.Equal(0,source.RequestCount);
    }
    [Fact] public async Task Clock_budget_expiry_cannot_turn_a_late_page_into_complete_before_evidence()
    {
        using var source=new Source(); var clock=new Clock(); source.Before=_=>clock.UtcNow=clock.UtcNow.AddMinutes(6);
        var value=Observation(await Capture(source,clock)); Assert.False(value["complete"]!.GetValue<bool>()); Assert.False(value.ContainsKey("user")); Assert.Equal(1,source.RequestCount);
    }
    [Fact] public async Task Mismatched_verified_operator_claim_is_refused_before_reads()
    {
        using var source=new Source(); var session=TestData.Session(); session.OperatorObjectId=User;
        session.Scopes=["User.Read.All","Group.Read.All","RoleManagement.Read.Directory","Organization.Read.All"];
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>Capture(source,new Clock(),session)); Assert.Equal(0,source.RequestCount);
    }
    [Fact] public async Task Missing_required_returned_read_scope_is_not_replaced_by_source_claims()
    {
        using var source=new Source(); await Assert.ThrowsAnyAsync<ToolkitException>(()=>Capture(source,new Clock(),TestData.Session())); Assert.Equal(0,source.RequestCount);
    }
    [Fact] public async Task Http_access_failure_retains_status_without_private_server_body()
    {
        using var source=new Source(); source.Before=id=>{if(id=="currentRoles")throw new GraphRequestException(403,"GET","/synthetic",null,"never-persist-server-body");};
        var value=Observation(await Capture(source,new Clock())); Assert.False(value["complete"]!.GetValue<bool>());
        Assert.Equal(403,value["sources"]!["currentRoles"]!["httpStatus"]?.GetValue<int>()); Assert.DoesNotContain("never-persist-server-body",value.ToJsonString());
    }
    [Fact] public async Task Structured_private_content_in_a_scalar_field_is_refused_before_persistence()
    {
        using var source=new Source(); source.Reply=_=>new LifecycleReadPage([new JsonObject { ["id"]=User,["displayName"]=new JsonObject { ["private"]="never-persist-structure" } }],false);
        var value=Observation(await Capture(source,new Clock())); Assert.False(value["complete"]!.GetValue<bool>()); Assert.False(value.ContainsKey("user"));
        Assert.DoesNotContain("never-persist-structure",value.ToJsonString());
    }
    [Fact] public async Task Original_token_expiry_is_frozen_before_read_only_renewal()
    {
        using var source=new Source(); var clock=new Clock(); var session=TestData.Session();
        session.Scopes=["User.Read.All","Group.Read.All","RoleManagement.Read.Directory","Organization.Read.All"];
        var original=Timestamps.Format(clock.UtcNow.AddMinutes(10)); session.TokenExpiresAt=original;
        source.Before=id=>{if(id=="currentRoles")session.TokenExpiresAt=Timestamps.Format(clock.UtcNow.AddHours(1));};
        var value=Observation(await Capture(source,clock,session)); Assert.True(value["complete"]!.GetValue<bool>());
        Assert.Equal(original,value["originalTokenExpiresAt"]?.GetValue<string>());
    }
}
