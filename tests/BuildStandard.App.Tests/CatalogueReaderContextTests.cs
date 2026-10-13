using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CatalogueReaderContextTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _log;
    private readonly Workspace _workspace;
    private readonly ShellViewModel _shell;
    public CatalogueReaderContextTests()
    {
        _root.WriteStandard("2026.09.12.json", File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../standards/2026.09.12.json"))));
        _root.WriteManifest(); _log = new(_root.Paths.LogsDirectory, LogLevel.Debug);
        _workspace = new(_root.Paths,new ToolkitSettings(),_log,true); _workspace.Initialise();
        _workspace.ApplyProfileToSession(TestData.Profile(),false); _shell=new(_workspace);
    }
    [Fact] public async Task Client_replacement_hides_reference_clears_pending_and_refuses_late_callbacks_without_authentication()
    {
        var document=new CatalogueDocumentService(_root.Paths).Create(_workspace.RequireStandard());
        var reader=new CatalogueReaderViewModel(_shell); var closed=0; reader.Closing+=()=>closed++;
        await reader.OpenAsync(document,"CA-001",_workspace.CapturePresentationContext(),CancellationToken.None);
        reader.Ready("synthetic unit Runtime");
        reader.SetPendingReference(document.RendererReferences.First(u=>new Uri(u).Host=="learn.microsoft.com"));
        Assert.NotEmpty(reader.PendingReference);
        _workspace.ApplyProfileToSession(TestData.Profile(TestData.TenantB),false);
        Assert.Equal("Superseded",reader.State); Assert.False(reader.IsCurrent); Assert.Equal(1,closed);
        Assert.Empty(reader.PendingReference); Assert.False(reader.SaveCommand.CanExecute(null));
        reader.Ready("synthetic late version"); reader.ObserveAnchor("CA-001");
        Assert.Equal("Superseded",reader.State); Assert.Empty(reader.OpenedAt);
        Assert.Null(_workspace.Connection); Assert.Null(_workspace.ApplicationSetup); Assert.Null(_workspace.Plan);
        reader.Close();
    }
    [Fact] public async Task Failed_reader_refuses_a_late_reference_and_has_no_launch_authority()
    {
        var document=new CatalogueDocumentService(_root.Paths).Create(_workspace.RequireStandard());
        var reader=new CatalogueReaderViewModel(_shell);
        await reader.OpenAsync(document,"CA-001",_workspace.CapturePresentationContext(),CancellationToken.None);
        reader.Ready("synthetic unit Runtime");
        var link=document.RendererReferences.First(u=>new Uri(u).Host=="learn.microsoft.com");
        reader.SetPendingReference(link); Assert.True(reader.OpenReferenceCommand.CanExecute(null));
        reader.Failed("Failed","Synthetic renderer failure"); reader.SetPendingReference(link);
        Assert.Empty(reader.PendingReference); Assert.False(reader.OpenReferenceCommand.CanExecute(null));
        Assert.False(reader.FindCommand.CanExecute(null)); Assert.False(reader.ZoomInCommand.CanExecute(null));
        reader.Close(); Assert.Null(_workspace.Connection);
    }
    [Fact] public async Task A_late_generation_does_not_replace_the_engineers_new_route_in_the_same_client()
    {
        var document=new CatalogueDocumentService(_root.Paths).Create(_workspace.RequireStandard());
        _shell.Navigate("standard"); var epoch=_shell.NavigationEpoch;
        _shell.Navigate("reports"); var current=_shell.CurrentPage;
        await _shell.ShowCatalogueReferenceAsync(document,"CA-001",_workspace.CapturePresentationContext(),epoch);
        Assert.Same(current,_shell.CurrentPage); Assert.Null(_workspace.Connection);
    }
    [Fact] public async Task Returning_without_a_context_change_reuses_the_native_control_and_filters()
    {
        var standard=_shell.Page<StandardViewModel>(); standard.Search="CA-001"; standard.Selected=Assert.Single(standard.Controls);
        var selected=standard.Selected!.Id; var document=new CatalogueDocumentService(_root.Paths).Create(_workspace.RequireStandard());
        await _shell.ShowCatalogueReferenceAsync(document,selected,_workspace.CapturePresentationContext());
        var reader=Assert.IsType<CatalogueReaderViewModel>(_shell.CurrentPage);
        _shell.Navigate("standard"); Assert.Same(standard,_shell.CurrentPage); Assert.Equal(selected,standard.Selected?.Id);
        Assert.Equal("CA-001",standard.Search); Assert.Equal("Closed",reader.State); Assert.False(reader.IsCurrent);
        Assert.Null(_workspace.Connection);
    }
    public void Dispose() { _log.Dispose(); _root.Dispose(); }
}
