using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows.Input;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Graph;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecyclePageTests
{
    private static PropertyInfo Property(object page,string name){var result=page.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);Assert.NotNull(result);return result;}
    private static object Page(ConnectedReportsFixture f)
    {
        var type=typeof(ShellViewModel).Assembly.GetType("BuildStandard.App.ViewModels.LifecycleViewModel");Assert.NotNull(type);
        var shell=new ShellViewModel(f.Workspace);shell.Navigate("lifecycle");Assert.IsType(type,shell.CurrentPage);return shell.CurrentPage;
    }
    [Fact]public void Native_lifecycle_navigation_does_not_authenticate_or_read_and_default_write_is_explained()
    {
        using var f=new ConnectedReportsFixture();var page=Page(f);Assert.Contains(RouteCatalog.Items,item=>item.Key=="lifecycle"&&item.Group=="Build standard");
        Assert.False((bool)Property(page,"CanApply").GetValue(page)!);Assert.False(string.IsNullOrWhiteSpace((string)Property(page,"ApplyGuidance").GetValue(page)!));
        Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);
    }
    [Theory][InlineData("SelectedUserId")][InlineData("SelectedGroupId")][InlineData("SelectedSkuId")]
    public void Clearing_native_selection_is_safe_and_invalidates_explicit_reviews(string selected)
    {
        using var f=new ConnectedReportsFixture();var page=Page(f);
        foreach(var name in new[]{"ProtectedReviewed","DownstreamReviewed","PreservationReviewed"})Property(page,name).SetValue(page,true);
        Property(page,selected).SetValue(page,TestData.Office);Property(page,selected).SetValue(page,null);
        Assert.Equal("",Property(page,selected).GetValue(page));Assert.False((bool)Property(page,"ProtectedReviewed").GetValue(page)!);
        Assert.False((bool)Property(page,"DownstreamReviewed").GetValue(page)!);Assert.False((bool)Property(page,"PreservationReviewed").GetValue(page)!);
        Assert.False(((ICommand)Property(page,"ApplyCommand").GetValue(page)!).CanExecute(null));Assert.Equal(0,f.Handler.Calls);
    }
    private sealed class Source(TenantSession session,LifecycleOperation operation,Action? before,Func<bool>? enabled=null) : ILifecycleReadSource
    {
        public string TenantId=>session.TenantId;public string AccountObjectId=>session.AccountObjectId;public string ClientId=>session.ClientId;public SessionMode Mode=>session.Mode;public LifecycleOperation Operation=>operation;public int RequestCount{get;private set;}
        public Task<LifecycleReadPage> ReadNextAsync(string id,CancellationToken ct){ct.ThrowIfCancellationRequested();RequestCount++;if(id=="currentRoles")before?.Invoke();return Task.FromResult(new LifecycleReadPage(id=="user"?[new JsonObject { ["id"]=TestData.Office,["displayName"]="Synthetic ordinary member",["userPrincipalName"]="member@synthetic.invalid",["userType"]="Member",["accountEnabled"]=enabled?.Invoke()??true,["onPremisesSyncEnabled"]=false }]:[],false));}
        public void Dispose(){}
    }
    [Theory][InlineData(false)][InlineData(true)]public async Task Preparation_is_explicit_and_late_client_change_never_admits_an_old_preview(bool change)
    {
        using var f=new ConnectedReportsFixture();var session=TestData.Session();session.TokenExpiresAt=Timestamps.Format(DateTimeOffset.UtcNow.AddMinutes(10));
        session.Scopes=["User.Read.All","Group.Read.All","Organization.Read.All","RoleManagement.Read.Directory","User.EnableDisableAccount.All"];
        f.Set(nameof(Workspace.Connection),new ConnectedTenant(session,new FakeGraphClient(f.Workspace.RequireStandard()),null));
        var snapshot=TestData.Snapshot(f.Workspace.RequireStandard(),capturedAt:DateTimeOffset.UtcNow);f.Workspace.Evidence.SaveSnapshot(snapshot);
        f.Set(nameof(Workspace.Snapshot),snapshot);f.Set(nameof(Workspace.SnapshotIsLive),true);
        var page=Page(f);Property(page,"SelectedUserId").SetValue(page,TestData.Office);
        Property(page,"SelectedActionId").SetValue(page,"disable-account");
        Func<LifecycleOperation,Action,ILifecycleReadSource> source=(operation,_)=>new Source(session,operation,change?()=>f.Workspace.ApplyProfileToSession(TestData.Profile(TestData.TenantB),false):null);
        Property(page,"SourceFactory").SetValue(page,source);
        var prepare=page.GetType().GetMethod("PreviewAsync");Assert.NotNull(prepare);
        var task=(Task)prepare.Invoke(page,null)!;
        if(change)await Assert.ThrowsAnyAsync<ToolkitException>(()=>task);else await task;
        Assert.Equal(!change,Property(page,"HasPreview").GetValue(page));
        if(!change){var details=(string)Property(page,"BeforeDetails").GetValue(page)!;Assert.Contains("Synthetic ordinary member",details);Assert.Contains("accountEnabled",details);Assert.Contains("User.EnableDisableAccount.All",details);}Assert.False((bool)Property(page,"CanApply").GetValue(page)!);
        Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);
    }
    [Fact] public void Access_shortcut_selects_one_action_without_authenticating_or_running_it()
    {
        using var f=new ConnectedReportsFixture();var shell=new ShellViewModel(f.Workspace);var page=shell.Page<LifecycleViewModel>();
        page.SelectedActionId="revoke-sessions";page.AccessCommand.Execute(null);
        Assert.IsType<ConnectViewModel>(shell.CurrentPage);
        Assert.Equal("revoke-sessions",shell.Page<ConnectViewModel>().SelectedLifecycleActionId);
        Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);Assert.False(page.CanApply);
    }
    [Fact] public void Empty_picker_selection_does_not_erase_a_manual_exact_target()
    {
        using var f=new ConnectedReportsFixture();var page=(LifecycleViewModel)Page(f);
        page.SelectedUserId=TestData.Office;page.SelectedUserChoice=null;
        Assert.Equal(TestData.Office,page.SelectedUserId);Assert.False(page.CanApply);
    }

    private sealed class LifecycleGraph : IGraphClient
    {
        public string TenantId=>TestData.TenantA;public SessionMode Mode=>SessionMode.Deployment;
        public bool Enabled=true;public int Writes;
        public Task<JsonObject> GetAsync(GraphApi api,string path,CancellationToken ct)=>throw new InvalidOperationException("No actual reads in native orchestration test");
        public Task<IReadOnlyList<JsonObject>> GetAllAsync(GraphApi api,string path,CancellationToken ct)=>throw new InvalidOperationException("No actual reads in native orchestration test");
        public Task<JsonObject> WriteAsync(GraphApi api,GraphWriteMethod method,string path,JsonObject payload,CancellationToken ct)=>throw new InvalidOperationException("Generic write forbidden");
        public Task<JsonObject> WriteReviewedAsync(GraphApi api,GraphWriteMethod method,string path,JsonObject payload,ReviewedWriteAdmission admission,CancellationToken ct)=>throw new InvalidOperationException("Generic write forbidden");
        public Task<JsonObject> ApplyLifecycleAsync(TenantSession session,LifecycleOperation operation,ReviewedWriteAdmission admission,CancellationToken ct)
        {_=LifecycleActionRegistry.RequireRegistered(operation);admission.AssertAllowed();Writes++;Enabled=false;return Task.FromResult(new JsonObject());}
    }
    [Fact]public async Task Native_orchestration_preserves_exact_review_durable_result_and_refuses_reuse_without_network()
    {
        using var f=new ConnectedReportsFixture();var session=TestData.Session();session.TokenExpiresAt=Timestamps.Format(DateTimeOffset.UtcNow.AddMinutes(10));
        session.Scopes=["User.Read.All","Group.Read.All","Organization.Read.All","RoleManagement.Read.Directory","User.EnableDisableAccount.All"];
        var graph=new LifecycleGraph();f.Workspace.Profile!.DeploymentClientId=session.ClientId;f.Set(nameof(Workspace.Connection),new ConnectedTenant(session,graph,null));
        var snapshot=TestData.Snapshot(f.Workspace.RequireStandard(),capturedAt:DateTimeOffset.UtcNow);f.Workspace.Evidence.SaveSnapshot(snapshot);f.Set(nameof(Workspace.Snapshot),snapshot);f.Set(nameof(Workspace.SnapshotIsLive),true);
        var page=(LifecycleViewModel)Page(f);page.SelectedUserId=TestData.Office;page.SelectedActionId="disable-account";
        page.SourceFactory=(operation,current)=>new Source(session,operation,current,()=>graph.Enabled);
        await page.PreviewAsync();Assert.True(page.HasPreview);Assert.Contains("accountEnabled",page.BeforeDetails);Assert.Equal(0,graph.Writes);
        f.Workspace.SetExperimentalChanges(true);
        page.Confirm=review=>{Assert.Same(page,review);Assert.Contains(TestData.Office,review.PreviewIdentity+review.PreviewSummary);review.ProtectedReviewed=true;review.DownstreamReviewed=true;return true;};
        await page.ApplyAsync();Assert.Equal(1,graph.Writes);Assert.False(page.HasPreview);Assert.Contains("Accepted",page.Outcome);Assert.Contains("Pass",page.Outcome);
        await page.LoadHistoryAsync();var attempt=Assert.Single(page.Attempts);Assert.Equal("Accepted",attempt.WriteAcceptance);Assert.Equal("Pass",attempt.Verification);
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>page.ApplyAsync());Assert.Equal(1,graph.Writes);Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);
    }

}
