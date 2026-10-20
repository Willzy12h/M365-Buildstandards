using System.Text;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CollaborationReportsPageTests
{
    [Fact]public async Task Group_picker_uses_bounded_name_lookup_and_requires_explicit_selection()
    {
        using var f=new ConnectedReportsFixture();f.Workspace.Session!.Scopes.Add("Group.Read.All");
        f.Handler.Response=(_,_)=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"value\":[{\"id\":\""+TestData.TenantB+"\",\"displayName\":\"Synthetic group\"}]}")};
        var page=Page(f);page.GroupQuery="Synthetic";page.Acknowledged=true;
        await page.SearchGroupsAsync();Assert.Single(page.GroupMatches);Assert.Null(page.SelectedGroup);Assert.Equal("",page.CollaborationGroups);
        Assert.False(page.Acknowledged);Assert.Equal(1,f.Handler.Calls);Assert.Contains("/groups?",f.Handler.Routes.Single());
        page.SelectedGroup=page.GroupMatches.Single();page.UseSelectedGroup();Assert.Equal(TestData.TenantB,page.CollaborationGroups);
        Assert.Equal(1,f.Handler.Calls);Assert.False(page.HasPendingOriginal);
    }
    [Fact]public async Task Group_picker_query_A_B_A_discards_late_results()
    {
        using var f=new ConnectedReportsFixture();f.Workspace.Session!.Scopes.Add("Group.Read.All");
        var page=Page(f);page.GroupQuery="Synthetic";page.Acknowledged=true;
        f.Handler.BeforeResponse=()=>{page.GroupQuery="Other";page.GroupQuery="Synthetic";};
        f.Handler.Response=(_,_)=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"value\":[]}")};
        await Assert.ThrowsAnyAsync<ToolkitException>(page.SearchGroupsAsync);Assert.Equal(1,f.Handler.Calls);Assert.Empty(page.GroupMatches);
    }
    [Theory][InlineData("scope")][InlineData("ack")][InlineData("query")]
    public async Task Group_picker_invalid_access_or_inputs_never_requests(string fault)
    {
        using var f=new ConnectedReportsFixture();if(fault!="scope")f.Workspace.Session!.Scopes.Add("Group.Read.All");
        var page=Page(f);page.GroupQuery=fault=="query"?"x":"Synthetic";page.Acknowledged=fault!="ack";
        await Assert.ThrowsAnyAsync<ToolkitException>(page.SearchGroupsAsync);Assert.Equal(0,f.Handler.Calls);Assert.Empty(page.GroupMatches);
    }
    [Fact]public async Task Group_picker_failed_read_is_not_an_empty_success()
    {
        using var f=new ConnectedReportsFixture();f.Workspace.Session!.Scopes.Add("Group.Read.All");
        f.Handler.Response=(_,_)=>new HttpResponseMessage(HttpStatusCode.Forbidden){Content=new StringContent("{\"error\":{\"code\":\"Forbidden\"}}")};
        var page=Page(f);page.GroupQuery="Synthetic";page.Acknowledged=true;
        await Assert.ThrowsAnyAsync<ToolkitException>(page.SearchGroupsAsync);
        Assert.Empty(page.GroupMatches);Assert.Contains("Could not complete",page.GroupLookupStatus);Assert.False(page.Acknowledged);
    }
    [Fact]public async Task Group_picker_connection_replacement_invalidates_selection_without_a_second_request()
    {
        using var f=new ConnectedReportsFixture();f.Workspace.Session!.Scopes.Add("Group.Read.All");
        f.Handler.Response=(_,_)=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"value\":[{\"id\":\""+TestData.TenantB+"\",\"displayName\":\"Synthetic group\"}]}")};
        var page=Page(f);page.GroupQuery="Synthetic";page.Acknowledged=true;await page.SearchGroupsAsync();page.SelectedGroup=page.GroupMatches.Single();
        f.ReplaceConnection();Assert.ThrowsAny<ToolkitException>(page.UseSelectedGroup);Assert.Equal("",page.CollaborationGroups);Assert.Equal(1,f.Handler.Calls);
    }
    [Fact]public async Task Stopping_group_lookup_cannot_be_reported_as_successfully_empty()
    {
        using var f=new ConnectedReportsFixture();f.Workspace.Session!.Scopes.Add("Group.Read.All");
        var page=Page(f);page.GroupQuery="Synthetic";page.Acknowledged=true;
        f.Handler.BeforeResponse=()=>{Assert.True(page.StopCommand.CanExecute(null));page.StopCommand.Execute(null);};
        f.Handler.Response=(_,_)=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"value\":[]}")};
        await Assert.ThrowsAnyAsync<ToolkitException>(page.SearchGroupsAsync);
        Assert.Equal(1,f.Handler.Calls);Assert.Empty(page.GroupMatches);Assert.Null(page.SelectedGroup);
        Assert.DoesNotContain("Lookup completed",page.GroupLookupStatus);Assert.False(page.Acknowledged);
    }
    private sealed class Source(CollaborationReadSelection selection,Action? before=null):ICollaborationReportSource
    {
        public string TenantId=>TestData.TenantA;public CollaborationReadSelection Selection=>selection;public int Reads;
        public Task<OperationalReportPage>ReadCollaborationNextAsync(CollaborationReadKind kind,string target,CancellationToken ct)
        {ct.ThrowIfCancellationRequested();Reads++;before?.Invoke();return Task.FromResult(new OperationalReportPage([],false));}
    }
    private static OperationalReportsViewModel Page(ConnectedReportsFixture f,Action? before=null)
    {
        f.Workspace.Session!.Scopes.Add("Sites.Read.All");
        f.Controller.CollaborationSourceFactory=(_,selection,_)=>new Source(selection,before);
        return new(new ShellViewModel(f.Workspace),f.Controller){SelectedReportId="collaboration-inventory",CollaborationSearch="*"};
    }
    [Fact]public async Task Deliberate_search_keeps_original_and_exposes_blocked_limits_without_tenant_calls()
    {
        using var f=new ConnectedReportsFixture();var reads=0;var page=Page(f,()=>reads++);page.Acknowledged=true;
        await page.ReadAsync();Assert.True(page.HasPendingOriginal);Assert.Contains("collaboration",page.Outcome);
        await page.SavePendingOriginalAsync();var saved=OperationalContextFile.ReadFile(page.LastExport,TestData.TenantA);
        Assert.Equal("*",saved.Report.CollaborationSelection!.Search);Assert.Equal(0,saved.Report.CollaborationSelection.ReturnedCounts.Single());
        Assert.Contains("OneDrive",page.Notice);Assert.Contains("FullControl",page.AccessPreview);Assert.Equal(1,reads);Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);
    }
    [Fact]public void Native_choice_and_access_preview_explain_content_scope_and_default_off()
    {
        using var f=new ConnectedReportsFixture();var shell=new ShellViewModel(f.Workspace);var connect=shell.Page<ConnectViewModel>();var setup=shell.Page<ApplicationSetupViewModel>();
        Assert.False(connect.IncludeCollaborationSites);Assert.False(connect.IncludeCollaborationTeams);Assert.False(setup.IncludeCollaborationSites);Assert.False(setup.IncludeCollaborationTeams);
        connect.IncludeCollaborationSites=true;connect.IncludeCollaborationTeams=true;
        Assert.Contains("Sites.Read.All",connect.OptionalAccessSummary);Assert.Contains("TeamMember.Read.All",connect.OptionalAccessSummary);
        setup.IncludeCollaborationSites=true;Assert.Contains("Sites.Read.All",setup.OptionalAccessSummary);
        Assert.Contains(Page(f).ReportChoices,c=>c.Id=="collaboration-inventory");Assert.Equal(0,f.Tokens.Calls);
    }
    [Theory][InlineData("search")][InlineData("site")][InlineData("group")][InlineData("members")][InlineData("owners")][InlineData("libraries")]
    public void Changing_exact_selection_invalidates_acknowledgement(string field)
    {
        using var f=new ConnectedReportsFixture();var page=Page(f);page.Acknowledged=true;
        if(field=="search")page.CollaborationSearch="other";if(field=="site")page.CollaborationSites="site";if(field=="group")page.CollaborationGroups=TestData.TenantA;
        if(field=="members")page.IncludeMembers=true;if(field=="owners")page.IncludeOwners=true;if(field=="libraries")page.IncludeLibraries=true;
        Assert.False(page.Acknowledged);Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);
    }
    [Fact]public async Task Selection_A_B_A_during_original_retention_keeps_bytes_but_refuses_display()
    {
        using var f=new ConnectedReportsFixture();var page=Page(f);page.Acknowledged=true;
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.SerialiseOriginal=async report=>{entered.SetResult();await release.Task;return Encoding.UTF8.GetBytes(OperationalContextSchema.Serialise(report));};
        var reading=page.ReadAsync();var winner=await Task.WhenAny(entered.Task,reading,Task.Delay(TimeSpan.FromSeconds(10)));
        try{Assert.Same(entered.Task,winner);page.CollaborationSearch="other";page.CollaborationSearch="*";Assert.True(f.Workspace.Busy);}
        finally{release.TrySetResult();await Assert.ThrowsAnyAsync<ToolkitException>(()=>reading);}
        Assert.True(page.HasPendingOriginal);Assert.Contains("No current",page.Outcome);await page.SavePendingOriginalAsync();Assert.Equal("*",OperationalContextFile.ReadFile(page.LastExport,TestData.TenantA).Report.CollaborationSelection!.Search);
    }
    [Theory][InlineData("scope")][InlineData("ack")][InlineData("empty")][InlineData("unregistered")]
    public async Task Invalid_selection_access_or_acknowledgement_never_requests(string fault)
    {
        using var f=new ConnectedReportsFixture();var reads=0;var page=Page(f,()=>reads++);
        if(fault=="scope")f.Workspace.Session!.Scopes.Remove("Sites.Read.All");if(fault=="empty")page.CollaborationSearch="";
        if(fault=="unregistered"){page.CollaborationSearch="";page.CollaborationSites="/users/id/drive";}
        page.Acknowledged=fault!="ack";await Assert.ThrowsAnyAsync<ToolkitException>(page.ReadAsync);Assert.Equal(0,reads);Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);
    }
}
