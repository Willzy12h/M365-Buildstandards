using System.Reflection;
using BuildStandard.App;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CandidateAuthoringPageTests
{
    [Fact]public async Task Local_edit_validate_export_preserves_published_source_and_never_calls_a_tenant()
    {
        using var f = new ConnectedReportsFixture(); var shell = new ShellViewModel(f.Workspace);
        var page = shell.Page<StandardViewModel>().Authoring;
        var original = File.ReadAllBytes(f.Root.Paths.StandardsManifestFile); var standard = f.Workspace.Standard;
        await page.CreateAsync(); Assert.True(page.HasUnsavedChanges); Assert.False(page.ExportCandidateCommand.CanExecute(null));
        page.SelectedControl = page.Controls.Single(c => c.Id == "CA-001"); await page.LoadFieldsAsync();
        page.SelectedField = page.Fields.Single(p => p.Path == "/payload/displayName"); page.ValueText = "CA - Local candidate";
        await page.ApplyAsync(); Assert.False(page.HasPendingInput); await page.ValidateAsync(); Assert.True(page.ExportCandidateCommand.CanExecute(null));
        await page.ExportAsync(); Assert.False(page.HasUnsavedChanges); Assert.Contains("Checksums", page.Message);
        Assert.Equal(4, Directory.GetFiles(f.Root.Paths.ReportsDirectory).Length);
        Assert.Equal(original, File.ReadAllBytes(f.Root.Paths.StandardsManifestFile)); Assert.Same(standard, f.Workspace.Standard);
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }
    [Fact]public async Task Pending_input_and_unsaved_draft_block_replacement_export_and_window_close()
    {
        using var f = new ConnectedReportsFixture(false); var shell = new ShellViewModel(f.Workspace);
        var standard = shell.Page<StandardViewModel>(); var page = standard.Authoring;
        await page.CreateAsync(); page.SelectedControl = page.Controls.Single(c => c.Id == "CA-001"); await page.LoadFieldsAsync();
        page.SelectedField = page.Fields.Single(p => p.Path == "/payload/displayName"); page.ValueText = "CA - Pending";
        await Assert.ThrowsAnyAsync<ToolkitException>(page.ValidateAsync); await Assert.ThrowsAnyAsync<ToolkitException>(page.ExportAsync);
        await Assert.ThrowsAnyAsync<ToolkitException>(page.SaveAsync); await Assert.ThrowsAnyAsync<ToolkitException>(page.CreateAsync);
        Assert.False(standard.SelectReleaseCommand.CanExecute(null));
        var guard = typeof(MainWindow).GetMethod("ShowPendingCandidate", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True((bool)guard.Invoke(null, [shell])!); Assert.Equal("standard", shell.ActiveRouteKey);
        page.Discard(); Assert.False((bool)guard.Invoke(null, [shell])!); Assert.Empty(Directory.GetFiles(f.Root.Paths.ReportsDirectory));
    }
    [Fact]public async Task Context_change_revokes_editor_validation_but_preserves_a_saveable_draft()
    {
        using var f = new ConnectedReportsFixture(false); var page = new ShellViewModel(f.Workspace).Page<StandardViewModel>().Authoring;
        await page.CreateAsync(); await page.ValidateAsync();
        f.Workspace.ApplyProfileToSession(TestData.Profile(TestData.TenantB), save: false);
        Assert.False(page.ExportCandidateCommand.CanExecute(null)); Assert.Empty(page.Fields); Assert.True(page.HasUnsavedChanges);
        await Assert.ThrowsAnyAsync<ToolkitException>(page.ValidateAsync); await page.SaveAsync();
        Assert.False(page.HasUnsavedChanges); Assert.Contains("Draft saved:", page.Message);
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }
}
