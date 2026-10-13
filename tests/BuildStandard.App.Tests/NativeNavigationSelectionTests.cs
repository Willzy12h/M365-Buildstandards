using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using Xunit;
namespace BuildStandard.Tests;
public sealed class NativeNavigationSelectionTests
{
    [Fact]
    public void Group_changes_keep_a_stable_item_source_and_the_exact_current_route()
    {
        using var root=new TempRoot(); root.WriteStandard("test.json",TestData.StandardJson); root.WriteManifest();
        using var log=new ToolkitLogger(root.Paths.LogsDirectory,LogLevel.Debug);
        var workspace=new Workspace(root.Paths,new ToolkitSettings(),log,true); workspace.Initialise();
        var shell=new ShellViewModel(workspace); var list=shell.VisibleNavItems;
        foreach(var key in new[]{"plan","reports","standard","scripts","settings","connect"})
        {
            shell.Navigate(key); Assert.Same(list,shell.VisibleNavItems);
            Assert.Equal(key,shell.ActiveRouteKey); Assert.Contains(list,n=>n.Key==key);
            var page=shell.CurrentPage; shell.ActiveRouteKey=""; shell.ActiveRouteKey=key;
            Assert.Same(page,shell.CurrentPage); Assert.Equal(key,shell.ActiveRouteKey);
        }
        Assert.Null(workspace.Connection); Assert.Null(workspace.ApplicationSetup);
    }
    [Fact]
    public void Item_source_refresh_cannot_turn_an_automatic_first_item_into_a_new_navigation()
    {
        using var root=new TempRoot(); root.WriteStandard("test.json",TestData.StandardJson); root.WriteManifest();
        using var log=new ToolkitLogger(root.Paths.LogsDirectory,LogLevel.Debug);
        var workspace=new Workspace(root.Paths,new ToolkitSettings(),log,true); workspace.Initialise();
        var shell=new ShellViewModel(workspace);
        shell.VisibleNavItems.CollectionChanged+=(_,_)=>shell.ActiveRouteKey=shell.VisibleNavItems.FirstOrDefault()?.Key??"";
        shell.Navigate("plan"); Assert.Equal("plan",shell.ActiveRouteKey); Assert.IsType<PlanViewModel>(shell.CurrentPage);
        shell.Navigate("reports"); Assert.Equal("reports",shell.ActiveRouteKey); Assert.IsType<GraphReportsViewModel>(shell.CurrentPage);
        Assert.Null(workspace.Connection); Assert.Null(workspace.Plan);
    }
}
