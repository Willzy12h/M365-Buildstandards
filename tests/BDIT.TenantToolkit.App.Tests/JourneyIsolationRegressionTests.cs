using System.Diagnostics;
using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class JourneyIsolationRegressionTests : IDisposable
{
    private const string Control = "CA-001";
    private const string AId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private const string BId = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
    private const string ANote = "Synthetic client A evidence; never belongs to B.";
    private const string BNote = "Synthetic client B evidence must remain unchanged.";
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _logger;
    private readonly ShellViewModel _shell;
    private Workspace Workspace => _shell.Workspace;

    public JourneyIsolationRegressionTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson);
        _root.WriteManifest();
        _logger = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(_root.Paths, new ToolkitSettings(), _logger, diagnostics: true);
        workspace.Initialise();
        Assert.NotNull(workspace.Standard);
        _shell = new ShellViewModel(workspace);
    }

    private static TenantProfile Profile(string tenant)
    {
        var profile = TestData.Profile(tenant);
        // TestData.Profile otherwise uses the same profile ID for both tenants.
        profile.Id = tenant == TestData.TenantA ? AId : BId;
        profile.Company = tenant == TestData.TenantA ? "Synthetic A" : "Synthetic B";
        return profile;
    }

    private void Select(string tenant) => Workspace.ApplyProfileToSession(Profile(tenant), save: false);

    private void SeedChecks()
    {
        Select(TestData.TenantA); Workspace.SaveManualCheck(Control, "Pass", ANote);
        Select(TestData.TenantB); Workspace.SaveManualCheck(Control, "Fail", BNote);
        Select(TestData.TenantA);
    }

    private void SeedDeviation(string tenant, string reason, string approver)
    {
        Workspace.Evidence.SaveDeviations(tenant, new[] { new Deviation
        {
            Id = tenant == TestData.TenantA ? AId : BId,
            TenantId = tenant, ControlId = Control, Kind = DeviationKind.ApprovedDeviation,
            Reason = reason, ApprovedBy = approver, Owner = "Synthetic security owner",
            ReviewBy = "2099-01-01", Reference = "Synthetic decision reference"
        }});
    }

    [Fact]
    public void Switching_client_cannot_save_client_A_manual_values_under_B()
    {
        SeedChecks();
        var page = _shell.Page<ManualChecksViewModel>();
        page.Selected = page.Rows.Single(r => r.ControlId == Control);
        Assert.Equal(ANote, page.EditNote);
        Select(TestData.TenantB);
        // Deliberately call Execute even if a repair disables the button. RelayCommand does not
        // enforce CanExecute, so the handler must also protect the context.
        page.SaveCommand.Execute(null);
        var retained = Workspace.Evidence.LoadManualChecks(TestData.TenantB).Checks[Control];
        Assert.Equal("Fail", retained.Status);
        Assert.Equal(BNote, retained.Note); // Current source overwrites this with ANote.
        Assert.Equal(ANote, Workspace.Evidence.LoadManualChecks(TestData.TenantA).Checks[Control].Note);
    }

    [Fact]
    public void Null_selection_after_a_client_switch_clears_manual_editor_values()
    {
        SeedChecks();
        var page = _shell.Page<ManualChecksViewModel>();
        page.Selected = page.Rows.Single(r => r.ControlId == Control);
        Select(TestData.TenantB);
        // Model WPF clearing SelectedItem when the collection is replaced.
        page.Selected = null;
        Assert.Empty(page.EditNote);
        Assert.Equal("Pending", page.EditStatus);
        page.SaveCommand.Execute(null);
        Assert.Equal(BNote, Workspace.Evidence.LoadManualChecks(TestData.TenantB).Checks[Control].Note);
    }

    [Fact]
    public void Ordinary_same_context_manual_refresh_preserves_an_unsaved_draft()
    {
        SeedChecks();
        var page = _shell.Page<ManualChecksViewModel>();
        page.Selected = page.Rows.Single(r => r.ControlId == Control);
        page.EditStatus = "Unknown"; page.EditNote = "Synthetic draft still being checked.";
        page.Refresh();
        Assert.Equal("Unknown", page.EditStatus);
        Assert.Equal("Synthetic draft still being checked.", page.EditNote);
        Assert.Equal(Control, page.Selected?.ControlId);
        Assert.Equal(ANote, Workspace.Evidence.LoadManualChecks(TestData.TenantA).Checks[Control].Note);
    }

    [Fact]
    public void Switching_client_cannot_save_client_A_approved_exception_under_B()
    {
        SeedDeviation(TestData.TenantA, ANote, "Synthetic A approver");
        SeedDeviation(TestData.TenantB, BNote, "Synthetic B approver");
        Select(TestData.TenantA);
        var page = _shell.Page<DeviationsViewModel>();
        page.Selected = Assert.Single(page.Items);
        Select(TestData.TenantB);
        page.SaveCommand.Execute(null);
        var retained = Assert.Single(Workspace.Evidence.LoadDeviations(TestData.TenantB));
        Assert.Equal(BId, retained.Id);
        Assert.Equal(BNote, retained.Reason); // Current source saves ANote under B.
        Assert.Equal("Synthetic B approver", retained.ApprovedBy);
        Assert.Equal(ANote, Assert.Single(Workspace.Evidence.LoadDeviations(TestData.TenantA)).Reason);
    }

    [Fact]
    public void Clearing_grid_selection_does_not_make_old_deviation_fields_a_new_B_exception()
    {
        SeedDeviation(TestData.TenantA, ANote, "Synthetic A approver");
        SeedDeviation(TestData.TenantB, BNote, "Synthetic B approver");
        Select(TestData.TenantA);
        var page = _shell.Page<DeviationsViewModel>();
        page.Selected = Assert.Single(page.Items);
        Select(TestData.TenantB);
        page.Selected = null; // Native binding may do this during Items.Clear.
        page.SaveCommand.Execute(null);
        var retained = Assert.Single(Workspace.Evidence.LoadDeviations(TestData.TenantB));
        Assert.Equal(BId, retained.Id);
        Assert.Equal(BNote, retained.Reason);
        Assert.Equal("Synthetic B approver", retained.ApprovedBy);
    }

    [Fact]
    public void Ordinary_same_context_deviation_refresh_preserves_an_unsaved_draft()
    {
        SeedDeviation(TestData.TenantA, ANote, "Synthetic A approver");
        Select(TestData.TenantA);
        var page = _shell.Page<DeviationsViewModel>();
        page.Selected = Assert.Single(page.Items);
        page.Reason = "Synthetic same-context draft awaiting review.";
        page.Refresh();
        Assert.Equal("Synthetic same-context draft awaiting review.", page.Reason);
        Assert.Equal("Synthetic A approver", page.ApprovedBy);
        Assert.Equal(ANote, Assert.Single(Workspace.Evidence.LoadDeviations(TestData.TenantA)).Reason);
    }

    [Fact]
    public async Task Local_register_rows_survive_offline_load_completion_and_navigation()
    {
        Select(TestData.TenantA);
        var run = new DeploymentRun
        {
            Id = AId, TenantId = TestData.TenantA, Release = Workspace.RequireStandard().Release,
            StartedAt = "2026-10-10T00:00:00Z", EndedAt = "2026-10-10T00:01:00Z",
            ToolkitVersion = ToolkitVersion.Current, Status = RunStatus.ReviewRequired,
            Results = new() { new RunResult
            {
                ControlId = Control, Name = "Synthetic uncertain candidate", Collection = "conditionalAccess",
                PlannedAction = nameof(PlanAction.Create), ObjectId = AId,
                Status = ResultStatus.Error, WriteAcceptance = WriteAcceptance.Unknown,
                Configuration = ConfigurationVerification.Unknown, Reason = "Synthetic uncertain result."
            }}
        };
        Workspace.Evidence.SaveRun(run); // Computes integrity; do not write raw JSON or use a real executor.
        Assert.Single(Workspace.Recovery.Register(TestData.TenantA)); // Separates storage/fixture errors.
        var page = _shell.Page<RecoveryViewModel>();
        Assert.True(page.LoadRegisterCommand.CanExecute(null));
        var command = Assert.IsType<AsyncCommand>(page.LoadRegisterCommand);
        command.Execute(null);
        var elapsed = Stopwatch.StartNew();
        while (command.IsRunning && elapsed.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
        Assert.False(command.IsRunning, "Local register command did not complete within its test budget.");
        Assert.Empty(_shell.ErrorMessage);
        var row = Assert.Single(page.Changes); // Current source returns zero after finally/Notify/Refresh.
        Assert.Equal(WriteAcceptance.Unknown, row.Acceptance);
        page.Selected = row;
        _shell.Navigate("overview"); _shell.Navigate("recovery");
        Assert.Single(page.Changes);
        Assert.Null(page.Plan); Assert.False(page.Approved); Assert.Empty(page.TypedTenant);
        Assert.False(page.PreviewDeleteCommand.CanExecute(null));
        Assert.False(page.ExecuteCommand.CanExecute(null));
        Assert.False(page.ReverifyDeploymentCommand.CanExecute(null));
        Assert.Null(Workspace.Connection); Assert.Null(Workspace.ApplicationSetup);
        Select(TestData.TenantB);
        Assert.Empty(page.Changes); Assert.Null(page.Selected); Assert.Null(page.Plan);
    }

    public void Dispose() { _logger.Dispose(); _root.Dispose(); }
}
