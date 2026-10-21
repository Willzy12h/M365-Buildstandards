using System.Reflection;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Engine.Evidence;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecyclePreparationTests
{
    internal sealed class Source : ILifecycleReadSource
    {
        public string TenantId=>TestData.TenantA;public string AccountObjectId=>TestData.Operator;public string ClientId=>TestData.ClientId;public SessionMode Mode=>SessionMode.Deployment;
        public LifecycleOperation Operation=>LifecycleActionRegistry.Create("disable-account",TestData.Office);public int RequestCount{get;private set;} public Action<string>? Before;public Func<bool>? ReadEnabled;
        public Task<LifecycleReadPage> ReadNextAsync(string source,CancellationToken ct)
        {RequestCount++;Before?.Invoke(source);return Task.FromResult(new LifecycleReadPage(source=="user"?[new JsonObject { ["id"]=TestData.Office,["displayName"]="Synthetic member",["userPrincipalName"]="member@synthetic.invalid",["userType"]="Member",["accountEnabled"]=ReadEnabled?.Invoke()??true,["onPremisesSyncEnabled"]=false }]:[],false));}
        public void Dispose(){}
    }
    internal sealed class Fixture : IDisposable
    {
        public TempRoot Root=new();public FixedClock Clock=new();public Source Source=new();public TenantSession Session=TestData.Session();public TenantProfile Profile=TestData.Profile();public StandardCatalogue Standard=TestData.Standard();public EvidenceStore Evidence;public TenantSnapshot Snapshot;
        public Fixture(){Evidence=new(Root.Paths,NullLog.Instance);Snapshot=TestData.Snapshot(Standard);Evidence.SaveSnapshot(Snapshot);Session.Scopes=["User.Read.All","Group.Read.All","RoleManagement.Read.Directory","Organization.Read.All","User.EnableDisableAccount.All"];Session.TokenExpiresAt=Timestamps.Format(Clock.UtcNow.AddMinutes(10));}
        public async Task<object> Prepare()
        {
            var type=typeof(BuildStandard.Engine.Execution.ReviewedChangeService).Assembly.GetType("BuildStandard.Engine.Execution.LifecyclePreparationService");Assert.NotNull(type);
            var service=Activator.CreateInstance(type,Evidence,Clock)!;var method=type.GetMethod("PreviewAsync");Assert.NotNull(method);Task task;
            try{task=(Task)method.Invoke(service,new object[]{Source,Session,Profile,Standard,Snapshot,Source.Operation,(Action)(()=>{}),CancellationToken.None})!;}
            catch(TargetInvocationException error)when(error.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
            await task;return task.GetType().GetProperty("Result")!.GetValue(task)!;
        }
        public void Dispose()=>Root.Dispose();
    }
    private static JsonObject Document(object preview)=>(JsonObject)preview.GetType().GetMethod("GetDocument")!.Invoke(preview,null)!;
    [Fact]public async Task Preview_requires_saved_complete_snapshot_and_retains_immutable_exact_source_without_approval()
    {
        using var f=new Fixture();var preview=await f.Prepare();var document=Document(preview);
        Assert.Equal("lifecycle-plan",document["kind"]?.GetValue<string>());Assert.Equal(1,document["schemaVersion"]?.GetValue<int>());
        Assert.Equal(f.Snapshot.Id,document["snapshotId"]?.GetValue<string>());Assert.Equal(TestData.Office,document["operation"]!["userId"]?.GetValue<string>());
        Assert.False(document["preservationRequired"]!.GetValue<bool>());Assert.False(document.ContainsKey("approved"));
        document["operation"]!["path"]="/other";Assert.NotEqual("/other",Document(preview)["operation"]!["path"]?.GetValue<string>());
        Assert.Single(Directory.GetFiles(Path.Combine(f.Evidence.TenantDirectory(TestData.TenantA),"lifecycle"),"plan-*.json"));
        Assert.Equal(4,f.Source.RequestCount);
    }
    [Theory][InlineData("snapshot-missing")][InlineData("snapshot-old")][InlineData("snapshot-partial")][InlineData("assessment")][InlineData("write-access")][InlineData("expiry")]
    public async Task Ineligible_context_is_refused_before_action_reads(string fault)
    {
        using var f=new Fixture();switch(fault)
        {
            case "snapshot-missing":Directory.Delete(Path.Combine(f.Evidence.TenantDirectory(TestData.TenantA),"snapshots"),true);break;
            case "snapshot-old":f.Clock.UtcNow=f.Clock.UtcNow.AddMinutes(21);f.Session.TokenExpiresAt=Timestamps.Format(f.Clock.UtcNow.AddMinutes(10));break;
            case "snapshot-partial":f.Snapshot.Complete=false;break;
            case "assessment":f.Session.Mode=SessionMode.Assessment;break;
            case "write-access":f.Session.Scopes.Remove("User.EnableDisableAccount.All");break;
            case "expiry":f.Session.TokenExpiresAt=Timestamps.Format(f.Clock.UtcNow.AddMinutes(-1));break;
        }
        await Assert.ThrowsAnyAsync<ToolkitException>(f.Prepare);Assert.Equal(0,f.Source.RequestCount);
    }
    [Theory][InlineData("profile")][InlineData("catalogue")][InlineData("failed-read")][InlineData("expired-during-read")]
    public async Task Late_changed_or_partial_evidence_cannot_create_a_reviewable_plan(string fault)
    {
        using var f=new Fixture();f.Source.Before=source=>{if(source!="currentRoles")return;switch(fault){case "profile":f.Profile.Parameters.EmergencyAccountIds.Add(TestData.Mam);break;case "catalogue":f.Standard.Release="changed";break;case "failed-read":throw new HttpRequestException("synthetic refusal");case "expired-during-read":f.Clock.UtcNow=f.Clock.UtcNow.AddMinutes(11);break;}};
        await Assert.ThrowsAnyAsync<ToolkitException>(f.Prepare);
        var dir=Path.Combine(f.Evidence.TenantDirectory(TestData.TenantA),"lifecycle");Assert.False(Directory.Exists(dir)&&Directory.EnumerateFiles(dir,"plan-*.json").Any());
    }
    [Fact]public async Task Failed_action_read_retains_partial_original_without_creating_approval_or_plan()
    {
        using var f=new Fixture();f.Source.Before=id=>{if(id=="currentRoles")throw new HttpRequestException("never-persist-private-error");};
        await Assert.ThrowsAnyAsync<ToolkitException>(f.Prepare);
        var dir=Path.Combine(f.Evidence.TenantDirectory(TestData.TenantA),"lifecycle");
        Assert.True(Directory.Exists(dir));var file=Assert.Single(Directory.GetFiles(dir,"before-*.json"));
        var value=JsonNode.Parse(File.ReadAllText(file))!.AsObject();Assert.False(value["complete"]!.GetValue<bool>());
        Assert.Equal(TestData.Office,value["user"]!["id"]?.GetValue<string>());Assert.DoesNotContain("never-persist-private-error",value.ToJsonString());
        Assert.Empty(Directory.GetFiles(dir,"plan-*.json"));
    }

}
