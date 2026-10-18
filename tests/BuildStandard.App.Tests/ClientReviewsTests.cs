using System.Text;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Graph;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ClientReviewsTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _log;
    private readonly Workspace _workspace;
    private readonly ShellViewModel _shell;
    private readonly ClientReviewsViewModel _page;
    public ClientReviewsTests()
    {
        _root.WriteStandard("test.json",TestData.StandardJson);_root.WriteManifest();
        new EvidenceStore(_root.Paths,NullLog.Instance).SaveProfiles([TestData.Profile()]);
        _log=new(_root.Paths.LogsDirectory,LogLevel.Debug);_workspace=new(_root.Paths,new ToolkitSettings(),_log,true);_workspace.Initialise();
        _shell=new(_workspace);_page=new(_shell);
    }
    public void Dispose(){_log.Dispose();_root.Dispose();}
    private static byte[] Csv()=>Encoding.UTF8.GetBytes("Microsoft ID,Company name\n"+TestData.TenantB+",Second client\n");
    [Fact] public void Native_route_reaches_the_existing_client_review_instance()
    {
        Assert.Contains(RouteCatalog.Items,r=>r.Key=="clients");_shell.Navigate("clients");Assert.IsType<ClientReviewsViewModel>(_shell.CurrentPage);
    }
    [Fact] public async Task Offline_scan_keeps_connection_and_selected_workspace_unchanged()
    {
        await _page.ScanAsync();Assert.Single(_page.Clients);Assert.Null(_workspace.Connection);Assert.Null(_workspace.Profile);Assert.False(_workspace.Busy);
    }
    [Fact] public async Task Import_requires_selection_then_saves_local_profiles_without_connecting()
    {
        await _page.PreviewImportAsync(Csv());Assert.NotNull(_page.ImportPreview);var row=Assert.Single(_page.ImportRows);Assert.False(row.IsSelected);
        await Assert.ThrowsAsync<ConfigurationException>(_page.SaveImportAsync);row.IsSelected=true;await _page.SaveImportAsync();
        Assert.Equal(2,_workspace.Evidence.LoadProfiles().Count);Assert.Null(_workspace.Profile);Assert.Null(_workspace.Connection);Assert.Null(_page.ImportPreview);
    }
    [Fact] public async Task A_stale_import_retains_the_preview_and_does_not_erase_other_engineers_changes()
    {
        await _page.PreviewImportAsync(Csv());_page.ImportRows[0].IsSelected=true;
        var updated=TestData.Profile();updated.Notes="Another engineer saved this";_workspace.Evidence.SaveProfiles([updated]);
        await Assert.ThrowsAsync<ConfigurationException>(_page.SaveImportAsync);
        Assert.NotNull(_page.ImportPreview);Assert.True(_page.ImportRows[0].IsSelected);Assert.Equal(updated.Notes,Assert.Single(_workspace.Evidence.LoadProfiles()).Notes);
    }
    [Fact] public void Existing_profile_edits_refuse_an_intervening_profile_store_change()
    {
        var edit=TestData.Profile();var other=TestData.Profile(TestData.TenantB);_workspace.Evidence.SaveProfiles([edit,other]);
        edit.Company="Stale edit";Assert.Throws<ConfigurationException>(()=>_workspace.SaveProfile(edit));Assert.Equal(2,_workspace.Evidence.LoadProfiles().Count);
    }
    [Fact] public async Task Existing_profile_deletion_refuses_an_intervening_profile_store_change()
    {
        var id=_workspace.Profiles.Single().Id;_workspace.Evidence.SaveProfiles([TestData.Profile(),TestData.Profile(TestData.TenantB)]);
        await Assert.ThrowsAsync<ConfigurationException>(()=>_workspace.DeleteProfileAsync(id));Assert.Equal(2,_workspace.Evidence.LoadProfiles().Count);
    }
    [Fact] public async Task Review_selected_captures_a_result_without_activating_the_client()
    {
        await _page.ScanAsync();_page.Selected=_page.Clients.Single();await _page.ReviewSelectedAsync();
        Assert.Equal(TestData.TenantA,_page.Review!.TenantId);Assert.Null(_workspace.Profile);Assert.Null(_workspace.Connection);
    }
    [Fact] public async Task Opening_a_client_refuses_an_active_connection_without_any_service_calls()
    {
        await _page.ScanAsync();_page.Selected=_page.Clients.Single();var graph=new FakeGraphClient(_workspace.RequireStandard());
        var connection=new ConnectedTenant(TestData.Session(),graph,null);typeof(Workspace).GetProperty(nameof(Workspace.Connection))!.SetValue(_workspace,connection);
        await Assert.ThrowsAsync<ToolkitException>(_page.OpenSelectedAsync);Assert.Same(connection,_workspace.Connection);Assert.Empty(graph.Reads);Assert.Empty(graph.Writes);
    }
}
