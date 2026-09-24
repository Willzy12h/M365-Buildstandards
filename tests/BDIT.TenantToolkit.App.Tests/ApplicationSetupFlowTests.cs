using System.Reflection;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Graph.Setup;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// The guided setup runs creation, both permission approvals and the grant check from one button. These are the
/// decisions that let it go on from one stage to the next, and the gate in front of it. None of them contacts a tenant.
/// </summary>
public sealed class ApplicationSetupFlowTests : IDisposable
{
    private const string Assessment = "33333333-3333-4333-8333-333333333333";
    private const string Deployment = "44444444-4444-4444-8444-444444444444";
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _logger;
    private readonly ShellViewModel _shell;

    public ApplicationSetupFlowTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson);
        _root.WriteManifest();
        _logger = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(_root.Paths, new ToolkitSettings(), _logger, diagnostics: true);
        workspace.Initialise();
        _shell = new ShellViewModel(workspace);
    }

    public void Dispose()
    {
        _logger.Dispose();
        _root.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("11111111-1111-4111-8111", false)]
    [InlineData("contoso.onmicrosoft.com", false)]
    [InlineData(TestData.TenantA, true)]
    [InlineData(" " + TestData.TenantA + " ", true)]
    public void Signing_in_needs_only_the_full_tenant_ID(string tenant, bool enabled)
    {
        var setup = _shell.Page<ApplicationSetupViewModel>();
        setup.TenantId = tenant;

        Assert.Equal(enabled, setup.ConnectCommand.CanExecute(null));
    }

    [Fact]
    public void The_engineer_running_setup_is_assigned_unless_they_choose_otherwise()
    {
        Assert.True(_shell.Page<ApplicationSetupViewModel>().AssignOperator);
    }

    /// <summary>
    /// The one button that writes to the tenant and asks Microsoft for consent is enabled only by a reviewed plan, the
    /// approval tick and the plan's tenant ID typed in full. Removing either condition fails this test.
    /// </summary>
    [Fact]
    public void Creating_and_granting_waits_for_the_approval_and_the_typed_tenant_ID()
    {
        var setup = _shell.Page<ApplicationSetupViewModel>();
        setup.TenantId = TestData.TenantA;
        Assert.False(setup.CreateCommand.CanExecute(null));

        typeof(ApplicationSetupViewModel).GetField("_plan", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(setup, new ApplicationSetupPlan { TenantId = TestData.TenantA, TenantName = "Synthetic client" });
        setup.Confirmation = TestData.TenantA;
        Assert.False(setup.CreateCommand.CanExecute(null));

        setup.PermissionsApproved = true;
        foreach (var refused in new[] { "", TestData.TenantA[..30], TestData.TenantB, TestData.TenantA + "0", "Synthetic client" })
        {
            setup.Confirmation = refused;
            Assert.False(setup.CreateCommand.CanExecute(null), refused);
        }

        setup.Confirmation = "  " + TestData.TenantA.ToUpperInvariant() + " ";
        Assert.True(setup.CreateCommand.CanExecute(null));
    }

    [Fact]
    public void Approval_is_asked_for_only_after_every_write_was_confirmed()
    {
        Assert.True(ApplicationSetupViewModel.ReadyForApproval(Completed()));

        var uncertain = Completed();
        uncertain.Status = "Review required";
        Assert.False(ApplicationSetupViewModel.ReadyForApproval(uncertain));

        var stopped = Completed();
        stopped.Status = "Stopped — review evidence";
        Assert.False(ApplicationSetupViewModel.ReadyForApproval(stopped));

        var noEvidence = Completed();
        noEvidence.AfterComplete = false;
        Assert.False(ApplicationSetupViewModel.ReadyForApproval(noEvidence));

        var nameMatchOnly = Completed();
        nameMatchOnly.Rows[1].ClientId = "";
        Assert.False(ApplicationSetupViewModel.ReadyForApproval(nameMatchOnly));
    }

    [Fact]
    public void Approvals_follow_the_actual_grants_in_order()
    {
        var both = ApplicationSetupViewModel.ApprovalsNeeded(Validation(assessmentGranted: false, deploymentGranted: false));
        Assert.Null(both.Blocker);
        Assert.Equal(new[] { SessionMode.Assessment, SessionMode.Deployment }, both.Modes.ToArray());

        var deploymentOnly = ApplicationSetupViewModel.ApprovalsNeeded(Validation(assessmentGranted: true, deploymentGranted: false));
        Assert.Equal(new[] { SessionMode.Deployment }, deploymentOnly.Modes.ToArray());

        var none = ApplicationSetupViewModel.ApprovalsNeeded(Validation(assessmentGranted: true, deploymentGranted: true));
        Assert.Null(none.Blocker);
        Assert.Empty(none.Modes);
    }

    [Fact]
    public void An_application_needing_attention_is_never_sent_for_approval()
    {
        var validation = Validation(assessmentGranted: false, deploymentGranted: false);
        validation.Rows[1].ConfigurationValid = false;

        var result = ApplicationSetupViewModel.ApprovalsNeeded(validation);

        Assert.Empty(result.Modes);
        Assert.NotNull(result.Blocker);
        Assert.Contains("deployment application needs attention", result.Blocker, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_check_stops_the_sequence()
    {
        var validation = Validation(assessmentGranted: false, deploymentGranted: false);
        validation.Rows.RemoveAt(0);

        var result = ApplicationSetupViewModel.ApprovalsNeeded(validation);

        Assert.Empty(result.Modes);
        Assert.NotNull(result.Blocker);
        Assert.Contains("could not be checked", result.Blocker, StringComparison.Ordinal);
    }

    private static ApplicationSetupResult Completed() => new()
    {
        Status = ApplicationSetupService.CompletedStatus, AfterComplete = true,
        Rows =
        {
            new ApplicationSetupItemResult { Mode = SessionMode.Assessment, ClientId = Assessment, Status = "Configured — consent pending" },
            new ApplicationSetupItemResult { Mode = SessionMode.Deployment, ClientId = Deployment, Status = "Configured — consent pending" }
        }
    };

    private static ApplicationSetupValidation Validation(bool assessmentGranted, bool deploymentGranted) => new()
    {
        Rows =
        {
            new ApplicationPermissionValidation { Mode = SessionMode.Assessment, ClientId = Assessment, ConfigurationValid = true, ConsentComplete = assessmentGranted },
            new ApplicationPermissionValidation { Mode = SessionMode.Deployment, ClientId = Deployment, ConfigurationValid = true, ConsentComplete = deploymentGranted }
        }
    };
}
