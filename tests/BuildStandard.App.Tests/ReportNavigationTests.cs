using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core.Json;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ReportNavigationTests
{
    [Fact]
    public void Grouped_navigation_preserves_every_existing_key_step_and_page_and_exposes_both_new_pages()
    {
        using var f = new ConnectedReportsFixture(false); var shell = new ShellViewModel(f.Workspace);
        var originalKeys = new[] { "overview", "connect", "setup", "configuration", "assessment", "deviations", "plan", "deploy", "automation", "recovery", "history", "jobs", "checks", "scripts", "standard", "settings" };
        foreach (var key in originalKeys) Assert.Single(shell.NavItems, n => n.Key == key);
        Assert.Equal(TestData.NativeRouteKeys.Count, shell.NavItems.Count);
        Assert.Single(shell.NavItems, n => n.Key == "run-reports" && n.Group == "Scripts");
        Assert.Single(shell.NavItems, n => n.Key == "mailbox-capacity" && n.Group == "Reports");
        Assert.Single(shell.NavItems, n => n.Key == "home");
        Assert.IsType<HomeViewModel>(shell.CurrentPage);
        Assert.Equal(new[] { "Workspace", "Build standard", "Reports", "Scripts", "Settings" }, shell.NavItems.Select(n => n.Group).Distinct());
        Assert.Equal(new[] { "home", "connect", "overview" }, shell.NavItems.Where(n => n.Group == "Workspace").Select(n => n.Key));
        Assert.Equal(new[] { "reports", "ca-evaluation", "coverage", "naming", "mailbox-capacity" }, shell.NavItems.Where(n => n.Group == "Reports").Select(n => n.Key));
        foreach (var group in shell.NavItems.GroupBy(n => n.Group))
        {
            Assert.True(group.First().IsGroupStart);
            Assert.All(group.Skip(1), n => Assert.False(n.IsGroupStart));
        }
        var numbered = shell.NavItems.Where(n => int.TryParse(n.Step, out _)).ToDictionary(n => n.Key, n => n.Step);
        Assert.Equal("1", numbered["configuration"]); Assert.Equal("2", numbered["assessment"]); Assert.Equal("3", numbered["deviations"]);
        Assert.Equal("4", numbered["plan"]); Assert.Equal("5", numbered["deploy"]); Assert.Equal("6", numbered["automation"]);
        var seen = new HashSet<object>();
        foreach (var nav in shell.NavItems)
        {
            shell.NavigateCommand.Execute(nav);
            Assert.NotNull(shell.CurrentPage); Assert.True(seen.Add(shell.CurrentPage));
            Assert.Equal(nav.Key, Assert.Single(shell.NavItems, n => n.IsActive).Key);
        }
        shell.Navigate("reports"); Assert.Same(shell.Page<GraphReportsViewModel>(), shell.CurrentPage);
        shell.Navigate("naming"); Assert.Same(shell.Page<NamingAuditViewModel>(), shell.CurrentPage);
        shell.Navigate("mailbox-capacity"); Assert.Same(shell.Page<MailboxCapacityViewModel>(), shell.CurrentPage);
        shell.Navigate("ca-evaluation"); Assert.Same(shell.Page<ConditionalAccessEvaluationViewModel>(), shell.CurrentPage);
        shell.Navigate("coverage"); Assert.Same(shell.Page<DeploymentCoverageViewModel>(), shell.CurrentPage);
        shell.Navigate("scripts"); Assert.Same(shell.Page<ScriptsViewModel>(), shell.CurrentPage);
        shell.Navigate("run-reports"); Assert.Same(shell.Page<RunReportsViewModel>(), shell.CurrentPage);
        // Run reports sits directly after the Copy-only library it extends.
        var operate = shell.NavItems.Where(n => n.Group == "Scripts").Select(n => n.Key).ToList();
        Assert.Equal(operate.IndexOf("scripts") + 1, operate.IndexOf("run-reports"));
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }

    [Fact]
    public void New_page_navigation_performs_no_reads_and_keeps_existing_capture_plan_and_acknowledgement_exact()
    {
        using var f = new ConnectedReportsFixture();
        var capture = TestData.Snapshot(f.Workspace.RequireStandard());
        f.Set(nameof(Workspace.Snapshot), capture); f.Set(nameof(Workspace.AcknowledgedSnapshotId), capture.Id);
        var shell = new ShellViewModel(f.Workspace); var before = ToolkitJson.Serialize(f.Workspace.Snapshot); var plan = f.Workspace.Plan;
        foreach (var key in new[] { "reports", "ca-evaluation", "naming", "run-reports", "mailbox-capacity", "configuration", "reports", "mailbox-capacity", "run-reports", "ca-evaluation", "coverage", "naming" }) shell.Navigate(key);
        Assert.Empty(shell.Page<NamingAuditViewModel>().Rows);
        Assert.Equal(before, ToolkitJson.Serialize(f.Workspace.Snapshot)); Assert.Same(plan, f.Workspace.Plan);
        Assert.Equal(capture.Id, f.Workspace.AcknowledgedSnapshotId);
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }
}
