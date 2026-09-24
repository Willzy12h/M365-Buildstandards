using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>Page behaviour that the review harness cannot see, because it only renders a state rather than changing one.</summary>
public sealed class PageViewModelTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _logger;
    private readonly ShellViewModel _shell;

    public PageViewModelTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson);
        _root.WriteManifest();
        _logger = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(_root.Paths, new ToolkitSettings(), _logger, diagnostics: true);
        workspace.Initialise();
        if (workspace.Standard is null)
            throw new InvalidOperationException("The synthetic standard did not load: " + workspace.StandardError);
        _shell = new ShellViewModel(workspace);
    }

    public void Dispose()
    {
        _logger.Dispose();
        _root.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The Plan page says how many controls are selected. It was computed from the rows but only re-read when the
    /// workspace changed, so ticking a control, or "Select eligible", left the count showing the old number.
    /// </summary>
    [Fact]
    public void The_plan_page_count_follows_each_tick()
    {
        var plan = _shell.Page<PlanViewModel>();
        Assert.NotEmpty(plan.Controls);
        var raised = new List<string?>();
        plan.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        plan.Controls[0].IsSelected = true;
        Assert.Contains(nameof(PlanViewModel.ContextText), raised);

        raised.Clear();
        plan.ClearSelectionCommand.Execute(null);
        Assert.Contains(nameof(PlanViewModel.ContextText), raised);
    }

    /// <summary>A refresh replaces the rows; the new rows must be followed too, not only the first set.</summary>
    [Fact]
    public void The_plan_page_count_follows_ticks_after_a_refresh()
    {
        var plan = _shell.Page<PlanViewModel>();
        plan.Refresh();
        var raised = new List<string?>();
        plan.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        plan.Controls[^1].IsSelected = true;

        Assert.Contains(nameof(PlanViewModel.ContextText), raised);
    }

    [Fact]
    public void The_assessment_filter_offers_every_status_in_the_table_wording()
    {
        var assessment = _shell.Page<AssessmentViewModel>();
        var keys = assessment.StatusFilters.Select(f => f.Key).ToList();

        foreach (var status in Enum.GetValues<BDIT.TenantToolkit.Core.Models.FindingStatus>())
            Assert.Contains(status.ToString(), keys);
        Assert.DoesNotContain(assessment.StatusFilters, f => f.Label == nameof(BDIT.TenantToolkit.Core.Models.FindingStatus.SettingsMatchNotEnforced));
        Assert.Equal("Actionable", assessment.FilterStatus);
    }

    [Fact]
    public void The_deviation_kind_is_offered_in_words_and_saved_by_key()
    {
        var deviations = _shell.Page<DeviationsViewModel>();

        Assert.Equal(new[] { "ApprovedDeviation", "NotApplicable" }, deviations.Kinds.Select(k => k.Key));
        Assert.Equal(new[] { "Approved deviation", "Not applicable" }, deviations.Kinds.Select(k => k.Label));
        Assert.Contains(deviations.Kinds, k => k.Key == deviations.Kind);
    }

    /// <summary>
    /// A comparison belongs to the client it was made for. Switching client used to leave client A's drift on screen,
    /// with its exports enabled, under client B.
    /// </summary>
    [Fact]
    public void Switching_client_clears_the_previous_clients_comparison()
    {
        var workspace = _shell.Workspace;
        var history = _shell.Page<HistoryViewModel>();
        workspace.ApplyProfileToSession(TestData.Profile(TestData.TenantA), save: false);
        typeof(HistoryViewModel).GetProperty(nameof(HistoryViewModel.Drift))!.SetValue(history, new BDIT.TenantToolkit.Core.Models.DriftReport());

        history.Refresh();
        Assert.NotNull(history.Drift);

        workspace.ApplyProfileToSession(TestData.Profile(TestData.TenantB), save: false);

        Assert.Null(history.Drift);
        Assert.False(history.ExportDriftHtmlCommand.CanExecute(null));
    }
}
