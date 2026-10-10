// Synthetic local journey regressions; Windows/net10.0-windows required.
// Recovery preview injection is fixture setup, not evidence that a preview was authorised.
using System.Diagnostics;
using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Graph;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class JourneyIsolationExtendedTests : IDisposable
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

    public JourneyIsolationExtendedTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson);
        _root.WriteStandard("test2.json", TestData.StandardJson.Replace("test.1", "test.2", StringComparison.Ordinal));
        _root.WriteManifest();
        _logger = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(_root.Paths, new ToolkitSettings(), _logger, diagnostics: true);
        workspace.Initialise();
        workspace.SelectRelease("test.json");
        Assert.NotNull(workspace.Standard);
        _shell = new ShellViewModel(workspace);
    }

    private static TenantProfile Profile(string tenant, string office = TestData.Office)
    {
        var profile = TestData.Profile(tenant, office: office);
        profile.Id = tenant == TestData.TenantA ? AId : BId;
        return profile;
    }

    private void Select(string tenant) => Workspace.ApplyProfileToSession(Profile(tenant), save: false);

    private void SeedChecks()
    {
        Select(TestData.TenantA); Workspace.SaveManualCheck(Control, "Pass", ANote);
        Select(TestData.TenantB); Workspace.SaveManualCheck(Control, "Fail", BNote);
        Select(TestData.TenantA);
    }

    private void SeedDeviation(string tenant, string reason)
    {
        Workspace.Evidence.SaveDeviations(tenant, new[] { new Deviation
        {
            Id = tenant == TestData.TenantA ? AId : BId, TenantId = tenant, ControlId = Control,
            Kind = DeviationKind.ApprovedDeviation, Reason = reason,
            ApprovedBy = tenant == TestData.TenantA ? "Synthetic A approver" : "Synthetic B approver",
            Owner = "Synthetic owner", ReviewBy = "2099-01-01", Reference = "Synthetic decision"
        }});
    }

    private void SelectBothEditors()
    {
        SeedChecks(); SeedDeviation(TestData.TenantA, ANote);
        var manual = _shell.Page<ManualChecksViewModel>();
        var deviations = _shell.Page<DeviationsViewModel>();
        manual.Refresh(); deviations.Refresh();
        manual.Selected = manual.Rows.Single(r => r.ControlId == Control);
        deviations.Selected = Assert.Single(deviations.Items);
        manual.EditNote = "Unsaved manual draft in the original review context.";
        deviations.Reason = "Unsaved exception draft in the original review context.";
    }

    private void AssertClearedEditors()
    {
        var manual = _shell.Page<ManualChecksViewModel>();
        var deviations = _shell.Page<DeviationsViewModel>();
        Assert.Null(manual.Selected); Assert.Equal("Pending", manual.EditStatus); Assert.Empty(manual.EditNote);
        Assert.Null(deviations.Selected); Assert.Empty(deviations.ControlId); Assert.Empty(deviations.Reason);
        Assert.Empty(deviations.Reference); Assert.Empty(deviations.Owner);
        // Offline fixture has no verified current approver: no A attribution may carry forward.
        Assert.Empty(deviations.ApprovedBy);
    }

    [Fact]
    public void A_stale_manual_row_cannot_be_reselected_and_saved_after_switching_to_B()
    {
        SeedChecks();
        var page = _shell.Page<ManualChecksViewModel>();
        var stale = page.Rows.Single(r => r.ControlId == Control);
        page.Selected = stale; Select(TestData.TenantB);
        page.Selected = stale; page.SaveCommand.Execute(null);
        Assert.Equal(BNote, Workspace.Evidence.LoadManualChecks(TestData.TenantB).Checks[Control].Note);
        Assert.Equal("Fail", Workspace.Evidence.LoadManualChecks(TestData.TenantB).Checks[Control].Status);
    }

    [Fact]
    public void A_stale_exception_cannot_be_reselected_and_saved_after_switching_to_B()
    {
        SeedDeviation(TestData.TenantA, ANote); SeedDeviation(TestData.TenantB, BNote);
        Select(TestData.TenantA);
        var page = _shell.Page<DeviationsViewModel>();
        var stale = Assert.Single(page.Items);
        page.Selected = stale; Select(TestData.TenantB);
        page.Selected = stale; page.SaveCommand.Execute(null);
        var retained = Assert.Single(Workspace.Evidence.LoadDeviations(TestData.TenantB));
        Assert.Equal(BId, retained.Id); Assert.Equal(BNote, retained.Reason);
        Assert.Equal("Synthetic B approver", retained.ApprovedBy);
        _shell.ClearErrorCommand.Execute(null);
        page.DeleteCommand.Execute(null); // Stale selection must be refused before opening a native modal.
        Assert.NotEmpty(_shell.ErrorMessage);
        Assert.Equal(BNote, Assert.Single(Workspace.Evidence.LoadDeviations(TestData.TenantB)).Reason);
    }

    [Fact]
    public void Loading_another_verified_standard_invalidates_both_editor_drafts()
    {
        SelectBothEditors();
        var originalDigest = Workspace.RequireStandard().IntegrityDigest;
        Workspace.SelectRelease("test2.json");
        Assert.NotEqual(originalDigest, Workspace.RequireStandard().IntegrityDigest);
        AssertClearedEditors();
        Assert.Equal(ANote, Workspace.Evidence.LoadManualChecks(TestData.TenantA).Checks[Control].Note);
        Assert.Equal(ANote, Assert.Single(Workspace.Evidence.LoadDeviations(TestData.TenantA)).Reason);
    }

    [Fact]
    public void Material_client_inputs_invalidate_both_editor_drafts()
    {
        SelectBothEditors();
        var previous = ReviewedClientScope.Digest(Workspace.Profile!);
        Workspace.ApplyProfileToSession(Profile(TestData.TenantA, office: TestData.Emergency), save: false);
        Assert.NotEqual(previous, ReviewedClientScope.Digest(Workspace.Profile!));
        AssertClearedEditors();
    }

    [Fact]
    public void Relabelling_the_same_client_preserves_both_unsaved_editor_drafts()
    {
        SelectBothEditors();
        var profile = Profile(TestData.TenantA); var previous = ReviewedClientScope.Digest(Workspace.Profile!);
        profile.Company = "Revised local display label"; profile.Notes = "A harmless handoff note";
        Workspace.ApplyProfileToSession(profile, save: false);
        Assert.Equal(previous, ReviewedClientScope.Digest(Workspace.Profile!));
        Assert.Equal("Unsaved manual draft in the original review context.", _shell.Page<ManualChecksViewModel>().EditNote);
        Assert.Equal("Unsaved exception draft in the original review context.", _shell.Page<DeviationsViewModel>().Reason);
        Assert.Equal(Control, _shell.Page<ManualChecksViewModel>().Selected?.ControlId);
        Assert.Equal(AId, _shell.Page<DeviationsViewModel>().Selected?.Id);
    }

    private async Task<RecoveryViewModel> ConnectedRegister()
    {
        Select(TestData.TenantA);
        Workspace.Evidence.SaveRun(new DeploymentRun
        {
            Id = AId, TenantId = TestData.TenantA, Release = Workspace.RequireStandard().Release,
            StartedAt = "2026-10-10T00:00:00Z", EndedAt = "2026-10-10T00:01:00Z",
            ToolkitVersion = Workspace.Version, Status = RunStatus.ReviewRequired,
            Results = new() { new RunResult
            {
                ControlId = Control, Name = "Synthetic uncertain candidate", Collection = "conditionalAccess",
                PlannedAction = nameof(PlanAction.Create), ObjectId = AId,
                Status = ResultStatus.Error, WriteAcceptance = WriteAcceptance.Unknown,
                Configuration = ConfigurationVerification.Unknown, Reason = "Synthetic uncertain result."
            }}
        });
        ReplaceSyntheticConnection();
        var page = _shell.Page<RecoveryViewModel>(); page.Refresh();
        var command = Assert.IsType<AsyncCommand>(page.LoadRegisterCommand);
        command.Execute(null); var elapsed = Stopwatch.StartNew();
        while (command.IsRunning && elapsed.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
        Assert.False(command.IsRunning); Assert.Empty(_shell.ErrorMessage);
        page.Selected = Assert.Single(page.Changes);
        return page;
    }

    private void ReplaceSyntheticConnection()
    {
        // Matches existing WorkspaceTests fixture technique; no real auth or Graph calls.
        typeof(Workspace).GetProperty(nameof(Workspace.Connection))!.SetValue(Workspace,
            new ConnectedTenant(TestData.Session(), new FakeGraphClient(Workspace.RequireStandard()), authenticator: null));
    }

    private static void SeedApproval(RecoveryViewModel page)
    {
        typeof(RecoveryViewModel).GetProperty(nameof(RecoveryViewModel.Plan))!.SetValue(page,
            new RecoveryPlan { Id = AId, TenantId = TestData.TenantA, ControlId = Control });
        page.Approved = true; page.ReviewedDrift = true; page.TypedTenant = TestData.TenantA;
        page.HistoricalAcknowledged = true;
    }

    private static void AssertNoApproval(RecoveryViewModel page)
    {
        Assert.Null(page.Plan); Assert.False(page.Approved); Assert.False(page.ReviewedDrift);
        Assert.Empty(page.TypedTenant); Assert.False(page.HistoricalAcknowledged);
    }

    [Fact]
    public async Task Disconnect_retains_local_register_rows_and_clears_recovery_authority()
    {
        var page = await ConnectedRegister(); SeedApproval(page);
        await Workspace.DisconnectAsync(); // Actual existing public transition is the action under test.
        Assert.Null(Workspace.Connection); Assert.Single(page.Changes); AssertNoApproval(page);
        Assert.False(page.PreviewDeleteCommand.CanExecute(null));
        Assert.False(page.ExecuteCommand.CanExecute(null));
        Assert.False(page.ReverifyDeploymentCommand.CanExecute(null));
    }

    [Fact]
    public async Task Same_tenant_reconnection_clears_recovery_authority()
    {
        var page = await ConnectedRegister(); SeedApproval(page);
        ReplaceSyntheticConnection(); page.Refresh();
        Assert.Single(page.Changes); AssertNoApproval(page);
    }

    [Fact]
    public async Task Ordinary_refresh_in_the_same_connection_preserves_recovery_review()
    {
        var page = await ConnectedRegister(); SeedApproval(page);
        var plan = page.Plan; page.Refresh();
        Assert.Single(page.Changes); Assert.Same(plan, page.Plan);
        Assert.True(page.Approved); Assert.True(page.ReviewedDrift);
        Assert.Equal(TestData.TenantA, page.TypedTenant); Assert.True(page.HistoricalAcknowledged);
    }

    public void Dispose() { _logger.Dispose(); _root.Dispose(); }
}
