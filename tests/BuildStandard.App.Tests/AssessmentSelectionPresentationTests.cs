using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using Xunit;
namespace BuildStandard.Tests;
public sealed class AssessmentSelectionPresentationTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _log;
    private readonly Workspace _workspace;
    private readonly AssessmentViewModel _view;
    public AssessmentSelectionPresentationTests()
    {
        _root.WriteStandard("2026.09.12.json", File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.09.12.json"))));
        _root.WriteManifest(); _log = new(_root.Paths.LogsDirectory, LogLevel.Debug);
        _workspace = new(_root.Paths, new ToolkitSettings(), _log, true); _workspace.Initialise();
        _workspace.ApplyProfileToSession(TestData.Profile(), false);
        _view = new ShellViewModel(_workspace).Page<AssessmentViewModel>();
    }
    private void Open(string id)
    {
        var result = new AssessmentResult { Id = id, TenantId = TestData.TenantA,
            Findings = [new() { ControlId = "CA-001", Name = "Synthetic control", Status = FindingStatus.Missing }] };
        typeof(Workspace).GetProperty(nameof(Workspace.Assessment))!.SetValue(_workspace, result); _view.Refresh();
    }
    [Fact]
    public void Filtered_selection_is_explicit_and_new_result_clears_old_detail()
    {
        Open("original-assessment"); _view.Selected = _view.Findings.Single(); var selected = _view.Selected;
        _view.Search = "not-a-match";
        Assert.Empty(_view.Findings); Assert.Same(selected, _view.Selected); Assert.Contains("hidden", _view.HiddenSelectionText);
        _view.Search = ""; _view.Refresh(); Assert.Equal("CA-001", _view.Selected!.ControlId);
        Open("replacement-assessment"); Assert.Null(_view.Selected); Assert.Equal("", _view.ProposedJson);
        Assert.Equal(1, _view.Summary.NeedsReview); Assert.Equal(0, _view.Summary.Compliant);
    }
    [Fact]
    public async Task Late_export_keeps_original_bytes_but_cannot_announce_them_for_a_replaced_client()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(false);
        var originalFile = Path.Combine(_root.Paths.ReportsDirectory, "synthetic-original-export.txt");
        var export = _workspace.ExportAsync(() => { started.SetResult(); release.Wait(); File.WriteAllText(originalFile, "Original synthetic source"); return originalFile; });
        await started.Task;
        _workspace.ApplyProfileToSession(TestData.Profile(TestData.TenantB), false);
        release.Set();
        await Assert.ThrowsAsync<BuildStandard.Core.TenantMismatchException>(() => export);
        Assert.Equal("Original synthetic source", File.ReadAllText(originalFile));
        Assert.Equal(TestData.TenantB, _workspace.Profile!.TenantId);
    }
    public void Dispose() { _log.Dispose(); _root.Dispose(); }
}
