using System.Globalization;
using System.Windows;
using System.Windows.Media;
using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Reports;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// How statuses look in the grids. The converter works on words, so a status whose word changes, or a new status,
/// silently loses its colour; these pin every status a grid can show.
/// </summary>
public sealed class StatusPresentationTests
{
    private static readonly StatusToBrushConverter Converter = new();

    private static Color? ColourOf(string status) =>
        Converter.Convert(status, typeof(Brush), null, CultureInfo.InvariantCulture) is SolidColorBrush { Color.A: > 0 } brush ? brush.Color : null;

    [Fact]
    public void Every_assessment_status_has_a_colour()
    {
        var uncoloured = Enum.GetValues<FindingStatus>().Where(s => ColourOf(s.ToString()) is null).ToList();
        Assert.Empty(uncoloured);
    }

    [Fact]
    public void Every_plan_action_has_a_colour()
    {
        var uncoloured = Enum.GetValues<PlanAction>().Where(a => ColourOf(a.ToString()) is null).ToList();
        Assert.Empty(uncoloured);
    }

    [Fact]
    public void Every_run_outcome_has_a_colour()
    {
        foreach (var status in new[] { RunStatus.Running, RunStatus.Completed, RunStatus.Stopped, RunStatus.ReviewRequired, RunStatus.Interrupted, RunStatus.Error,
                     ResultStatus.InProgress, ResultStatus.Completed, ResultStatus.Error })
            Assert.True(ColourOf(status) is not null, $"'{status}' has no colour.");
    }

    [Fact]
    public void Failures_are_never_shown_in_the_colour_of_success()
    {
        var good = ColourOf("Compliant");
        foreach (var status in new[] { "Missing", RunStatus.Error, RunStatus.ReviewRequired, RunStatus.Interrupted, "Fail", "Blocked", "Conflict", "Drift", WriteAcceptance.Rejected })
            Assert.NotEqual(good, ColourOf(status));
    }

    [Fact]
    public void The_deploy_prerequisite_steps_read_as_ready_or_not()
    {
        Assert.Equal(ColourOf("Compliant"), ColourOf("Ready"));
        Assert.NotNull(ColourOf("Missing"));
        Assert.NotNull(ColourOf("Incomplete"));
    }

    [Fact]
    public void Shared_brushes_are_frozen()
    {
        foreach (var status in new[] { "Compliant", "Missing", "PartialMatch", "Create" })
            Assert.True(((Freezable)Converter.Convert(status, typeof(Brush), null, CultureInfo.InvariantCulture)).IsFrozen, status);
    }

    [Fact]
    public void A_filter_option_is_announced_by_its_label_not_its_key()
    {
        var option = new FilterOption(nameof(FindingStatus.SettingsMatchNotEnforced), StatusLabels.For(FindingStatus.SettingsMatchNotEnforced));
        Assert.Equal("Settings match, not enforced", option.ToString());
    }
}
