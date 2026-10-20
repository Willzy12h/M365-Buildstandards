using System.Reflection;
using BuildStandard.App;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CandidateAuthoringPageTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Native_policy_import_preserves_assignment_uncertainty_and_requires_validation(bool foreign)
    {
        using var f = new ConnectedReportsFixture(false);
        f.Root.WriteStandard("policy-base.json", File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.09.5.json"))));
        f.Root.WriteManifest(); f.Workspace.SelectRelease("policy-base.json");
        var shell = new ShellViewModel(f.Workspace); var page = shell.Page<StandardViewModel>().Authoring;
        await page.CreateAsync(); page.SelectedControl = page.Controls.Single(c => c.Id == "CFG-WIN-002");
        var source = page.SelectedControl.Payload!.DeepClone(); source["omaSettings"]![2]!["value"] = 22;
        if (foreign) source["omaSettings"]![0]!["displayName"] = TestData.Office;
        var file = Path.Combine(f.Root.Root, "policy.json"); File.WriteAllText(file, source.ToJsonString());
        var method = typeof(CandidateAuthoringViewModel).GetMethod("ImportPolicyAsync");
        Assert.NotNull(method);
        await (Task)method.Invoke(page, [file, "CFG - Native candidate - Windows", null])!;
        Assert.True(page.HasUnsavedChanges); Assert.False(page.ExportCandidateCommand.CanExecute(null));
        await page.SaveAsync(); var draftFile = Directory.GetFiles(f.Root.Paths.ReportsDirectory).Single();
        var draft = BuildStandard.Engine.Standards.Authoring.CandidateDraftCodec.ReadFile(draftFile);
        Assert.Equal(foreign ? 1 : 0, draft.Unresolved.Count); Assert.Equal("Unknown", draft.AssignmentObservations.Single().Status);
        if (foreign) await Assert.ThrowsAnyAsync<Exception>(page.ValidateAsync);
        else { await page.ValidateAsync(); Assert.True(page.ExportCandidateCommand.CanExecute(null)); }
        Assert.Equal("2026.09.5", f.Workspace.Standard!.Release); Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }
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
    [Theory][InlineData("load")][InlineData("import")][InlineData("select")][InlineData("command")]
    public async Task Every_existing_baseline_replacement_preserves_unsaved_authoring(string route)
    {
        using var f = new ConnectedReportsFixture(false);
        var imported = TestData.Standard(); imported.Release = "import-" + new string('a', 24); imported.Status = "Candidate";
        var import = new BuildStandard.Engine.Standards.DevicePolicyImport(imported, new string('b', 64), []);
        f.Workspace.UseImportedStandard(import); var filename = f.Workspace.LocalCandidates().Single();
        f.Workspace.SelectRelease("test.json");
        var shell = new ShellViewModel(f.Workspace); var editor = shell.Page<StandardViewModel>().Authoring;
        await editor.CreateAsync(); var original = f.Workspace.Standard;
        var files = Directory.GetFiles(f.Root.Paths.TenantDirectory(f.Workspace.Profile!.TenantId), "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, File.ReadAllBytes);
        switch (route)
        {
            case "load": Assert.ThrowsAny<ToolkitException>(() => f.Workspace.LoadLocalCandidate(filename)); break;
            case "import": Assert.ThrowsAny<ToolkitException>(() => f.Workspace.UseImportedStandard(import)); break;
            case "select": Assert.False(f.Workspace.TrySelectStandard("test.json")); break;
            case "command":
                var automation = shell.Page<AutomationViewModel>(); automation.SavedCandidate = filename;
                Assert.False(automation.LoadCandidateCommand.CanExecute(null));
                Assert.Contains("save or explicitly discard", automation.ImportGuidance);
                automation.LoadCandidateCommand.Execute(null); Assert.True(shell.HasError); break;
        }
        Assert.Same(original, f.Workspace.Standard); Assert.True(editor.HasUnsavedChanges);
        foreach (var (path, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
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
    [Fact]public async Task Saved_draft_with_unapplied_input_blocks_replacement_until_reset()
    {
        using var f = new ConnectedReportsFixture(false); var shell = new ShellViewModel(f.Workspace);
        var editor = shell.Page<StandardViewModel>().Authoring;
        await editor.CreateAsync(); await editor.SaveAsync(); Assert.False(editor.HasUnsavedChanges);
        editor.SelectedControl = editor.Controls.Single(c => c.Id == "CA-001"); await editor.LoadFieldsAsync();
        editor.SelectedField = editor.Fields.Single(p => p.Path == "/payload/displayName"); editor.ValueText = "CA - Pending input";
        var original = f.Workspace.Standard;
        Assert.False(f.Workspace.TrySelectStandard("test.json")); Assert.Same(original, f.Workspace.Standard);
        editor.ResetInputCommand.Execute(null); Assert.False(editor.HasUnsavedChanges);
        Assert.True(f.Workspace.TrySelectStandard("test.json"));
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }
    [Fact]public async Task Another_editor_cannot_replace_the_first_editors_unsaved_guard()
    {
        using var f = new ConnectedReportsFixture(false); var first = new ShellViewModel(f.Workspace);
        var editor = first.Page<StandardViewModel>().Authoring; await editor.CreateAsync();
        _ = new ShellViewModel(f.Workspace); var original = f.Workspace.Standard;
        Assert.False(f.Workspace.TrySelectStandard("test.json")); Assert.Same(original, f.Workspace.Standard);
        editor.Discard(); Assert.True(f.Workspace.TrySelectStandard("test.json"));
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }
}
