using System.Reflection;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.App.Views;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Graph;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class Preview17WorkflowTests
{
    [Theory]
    [InlineData("approval")]
    [InlineData("profile tenant")]
    [InlineData("session tenant")]
    [InlineData("operator")]
    [InlineData("client")]
    [InlineData("assessment")]
    [InlineData("unverified")]
    [InlineData("empty plan")]
    [InlineData("digest")]
    public void Reviewed_deployment_refuses_absent_or_changed_approval_context(string change)
    {
        var profile = TestData.Profile(); var session = TestData.Session(); var plan = Plan();
        Assert.True(ConfirmTenantDialog.CanApprove(profile, plan, session, true));
        switch (change)
        {
            case "profile tenant": profile.TenantId = TestData.TenantB; break;
            case "session tenant": session.TenantId = TestData.TenantB; break;
            case "operator": session.OperatorObjectId = TestData.Emergency; break;
            case "client": session.ClientId = TestData.Emergency; break;
            case "assessment": session.Mode = SessionMode.Assessment; break;
            case "unverified": session.OperatorVerified = false; break;
            case "empty plan": plan.Rows.Clear(); break;
            case "digest": plan.PlanDigest = ""; break;
        }
        Assert.False(ConfirmTenantDialog.CanApprove(profile, plan, session, change != "approval"));
        Assert.False(ConfirmTenantDialog.CanApprove(profile, plan, null, true));
    }
    private static DeploymentPlan Plan() => new()
    {
        TenantId = TestData.TenantA, ClientId = TestData.ClientId, OperatorObjectId = TestData.Operator, PlanDigest = new string('a', 64),
        Rows = new() { new PlanRow { Action = PlanAction.Create, ControlId = "SYNTHETIC" } }
    };

    [Fact]
    public void Direct_setup_navigation_carries_verified_tenant_and_profile_application_IDs()
    {
        using var root = new TempRoot(); root.WriteStandard("test.json", TestData.StandardJson); root.WriteManifest();
        using var log = new ToolkitLogger(root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(root.Paths, new ToolkitSettings(), log, true); workspace.Initialise();
        var shell = new ShellViewModel(workspace);
        var profile = TestData.Profile(); profile.AssessmentClientId = TestData.ClientId; profile.DeploymentClientId = TestData.Emergency;
        typeof(Workspace).GetProperty(nameof(Workspace.Profile))!.SetValue(workspace, profile);
        typeof(Workspace).GetProperty(nameof(Workspace.Connection))!.SetValue(workspace, new ConnectedTenant(TestData.Session(), new FakeGraphClient(TestData.Standard()), null));
        shell.Navigate("setup");
        var setup = shell.Page<ApplicationSetupViewModel>();
        Assert.Equal(profile.TenantId, setup.TenantId);
        Assert.Equal(profile.AssessmentClientId, setup.AssessmentClientId); Assert.Equal(profile.DeploymentClientId, setup.DeploymentClientId);
        Assert.True(setup.QuickSetupCommand.CanExecute(null)); Assert.False(setup.CreateCommand.CanExecute(null));
        Assert.Empty(setup.ExistingApplications);
    }

    [Fact]
    public void Unknown_plan_controls_remain_disabled_with_a_concrete_next_action()
    {
        using var root = new TempRoot(); root.WriteStandard("test.json", TestData.StandardJson); root.WriteManifest();
        using var log = new ToolkitLogger(root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(root.Paths, new ToolkitSettings(), log, true); workspace.Initialise();
        var control = workspace.RequireStandard().Controls.First(c => c.HasRecipe);
        typeof(Workspace).GetProperty(nameof(Workspace.Assessment))!.SetValue(workspace, new AssessmentResult
        {
            Findings = new() { new ControlFinding { ControlId = control.Id, Status = FindingStatus.UnableToAssess, Reason = "Synthetic denied read." } }
        });
        var shell = new ShellViewModel(workspace); var vm = shell.Page<PlanViewModel>(); vm.Refresh();
        var row = vm.Controls.Single(c => c.ControlId == control.Id);
        Assert.Contains("Synthetic denied read", row.SelectionGuidance);
        Assert.Contains("2 · Configuration", row.SelectionGuidance);
        Assert.False(row.Eligible);
        vm.SelectEligibleCommand.Execute(null);
        Assert.False(row.IsSelected);
    }
}
