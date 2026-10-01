using System.Reflection;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using BDIT.TenantToolkit.Graph.Setup;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ConnectionGuidanceTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _log;
    private readonly Workspace _workspace;
    private readonly ShellViewModel _shell;
    public ConnectionGuidanceTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson); _root.WriteManifest();
        _log = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        _workspace = new Workspace(_root.Paths, new ToolkitSettings(), _log, true);
        _workspace.Initialise(); _shell = new ShellViewModel(_workspace);
    }
    public void Dispose() { _log.Dispose(); _root.Dispose(); }

    [Fact]
    public void Quick_connect_requires_discovery_then_an_explicit_confirmation_and_cancel_preserves_the_profile()
    {
        var vm = _shell.Page<ConnectViewModel>();
        var profile = _workspace.SaveProfile(TestData.Profile());
        vm.Selected = profile;
        Assert.False(vm.ConfirmQuickConnectCommand.CanExecute(null));
        typeof(ConnectViewModel).GetField("_discovered", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm,
            new DiscoveredTenant(TestData.TenantB, "Other synthetic client", "other.example", "engineer@other.example", TestData.Operator, false));
        Assert.True(vm.ConfirmQuickConnectCommand.CanExecute(null));
        Assert.Contains(TestData.TenantB, vm.DiscoveredTenantText, StringComparison.Ordinal);
        vm.CancelQuickConnectCommand.Execute(null);
        Assert.False(vm.ConfirmQuickConnectCommand.CanExecute(null));
        Assert.Equal(profile.TenantId, vm.EditTenantId);
        Assert.Same(profile, Assert.Single(_workspace.Profiles));
        Assert.Null(_workspace.Connection);
    }

    [Fact]
    public void Choosing_another_saved_profile_discards_the_discovery_confirmation()
    {
        var vm = _shell.Page<ConnectViewModel>();
        typeof(ConnectViewModel).GetField("_discovered", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm,
            new DiscoveredTenant(TestData.TenantB, "Other", "other.example", "engineer@other.example", TestData.Operator, false));
        vm.Selected = TestData.Profile();
        Assert.False(vm.HasDiscoveredTenant); Assert.False(vm.ConfirmQuickConnectCommand.CanExecute(null));
    }

    [Fact]
    public void Partner_sign_in_requires_an_explicit_customer_and_disabling_fallback_disables_quick_discovery()
    {
        var vm = _shell.Page<ConnectViewModel>();
        Assert.False(vm.ConnectPartnerCommand.CanExecute(null));
        vm.EditCompany = "Synthetic client"; vm.EditTenantId = TestData.TenantA;
        Assert.True(vm.ConnectPartnerCommand.CanExecute(null));
        vm.EditTenantId = "organizations";
        Assert.False(vm.ConnectPartnerCommand.CanExecute(null));
        _workspace.Settings.AllowMicrosoftGraphPowerShellFallback = false;
        Assert.False(vm.QuickConnectCommand.CanExecute(null));
    }

    [Fact]
    public void Missing_deployment_ID_opens_setup_for_the_current_profile_without_sign_in_or_tenant_calls()
    {
        var profile = TestData.Profile();
        typeof(Workspace).GetProperty(nameof(Workspace.Profile))!.SetValue(_workspace, profile);
        var connect = _shell.Page<ConnectViewModel>();
        connect.Selected = TestData.Profile(TestData.TenantB);
        var deploy = _shell.Page<DeployViewModel>();
        Assert.Equal("Configure deployment application", deploy.DeploymentButtonText);
        deploy.EnableDeploymentCommand.Execute(null);
        var setup = _shell.Page<ApplicationSetupViewModel>();
        Assert.Equal(profile.TenantId, setup.TenantId);
        Assert.Equal(profile.TenantId, connect.EditTenantId);
        Assert.Null(_workspace.Connection); Assert.Null(_workspace.ApplicationSetup);
    }

    [Fact]
    public void Returning_to_setup_keeps_created_IDs_only_for_the_same_tenant()
    {
        var setup = _shell.Page<ApplicationSetupViewModel>();
        setup.TenantId = TestData.TenantA;
        setup.AssessmentClientId = TestData.ClientId; setup.DeploymentClientId = TestData.Emergency;
        var connect = _shell.Page<ConnectViewModel>();
        connect.OpenSetupForProfile(TestData.Profile());
        Assert.Equal(TestData.Emergency, setup.DeploymentClientId);
        connect.OpenSetupForProfile(TestData.Profile(TestData.TenantB));
        Assert.Empty(setup.AssessmentClientId); Assert.Empty(setup.DeploymentClientId);
    }

    [Fact]
    public void Session_reuse_requires_the_same_verified_tenant_mode_application_and_complete_profile()
    {
        var profile = TestData.Profile();
        var session = TestData.Session(mode: SessionMode.Assessment);
        session.ClientId = ToolkitSettings.MicrosoftGraphPowerShellClientId;
        var graph = new FakeGraphClient(TestData.Standard()) { Mode = SessionMode.Assessment };
        var connection = new ConnectedTenant(session, graph, null);
        typeof(Workspace).GetProperty(nameof(Workspace.Profile))!.SetValue(_workspace, profile);
        typeof(Workspace).GetProperty(nameof(Workspace.Connection))!.SetValue(_workspace, connection);
        Assert.True(_workspace.CanReuseConnection(TestData.Profile(), SessionMode.Assessment));
        Assert.False(_workspace.CanReuseConnection(TestData.Profile(), SessionMode.Deployment));
        Assert.False(_workspace.CanReuseConnection(TestData.Profile(TestData.TenantB), SessionMode.Assessment));
        Assert.False(_workspace.CanReuseConnection(TestData.Profile(office: TestData.Emergency), SessionMode.Assessment));
        _workspace.Settings.AssessmentClientId = TestData.ClientId;
        Assert.False(_workspace.CanReuseConnection(TestData.Profile(), SessionMode.Assessment));
        _workspace.Settings.AssessmentClientId = "";
        session.OperatorVerified = false;
        Assert.False(_workspace.CanReuseConnection(TestData.Profile(), SessionMode.Assessment));
        session.OperatorVerified = true;
        foreach (var page in _shell.NavItems) _shell.Navigate(page.Key);
        Assert.Same(connection, _workspace.Connection);
        Assert.Empty(graph.Reads); Assert.Empty(graph.Writes);
    }

    private sealed class NoTokens : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken ct) => throw new InvalidOperationException("No authentication in a UI handoff test.");
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) => GetAccessTokenAsync(ct);
    }

    [Fact]
    public void Setup_opened_directly_initialises_a_fresh_connection_form_only_for_its_verified_tenant()
    {
        var vm = _shell.Page<ConnectViewModel>();
        vm.Selected = TestData.Profile(TestData.TenantB);
        Assert.Throws<TenantMismatchException>(() => vm.UseApplicationIds(TestData.TenantA, TestData.ClientId, TestData.Emergency));
        using var http = new System.Net.Http.HttpClient();
        var setup = new ApplicationSetupService(http, new NoTokens(), new SignInOutcome
        { TenantId = TestData.TenantA, AccountObjectId = TestData.Operator }, NullLog.Instance);
        typeof(Workspace).GetProperty(nameof(Workspace.ApplicationSetup))!.SetValue(_workspace, setup);
        vm.UseApplicationIds(TestData.TenantA, TestData.ClientId, TestData.Emergency, "Verified synthetic client");
        Assert.Equal(TestData.TenantA, vm.EditTenantId);
        Assert.Equal("Verified synthetic client", vm.EditCompany);
        Assert.Equal(TestData.Emergency, vm.EditDeploymentClientId);
        Assert.Empty(vm.EditEmergencyIds); Assert.Empty(vm.ExclusionAccounts);
        Assert.False(vm.RememberConnection); Assert.Empty(_workspace.Profiles);
        Assert.Null(_workspace.Connection);
    }

    [Theory]
    [InlineData(false, false, false, "configuration issues")]
    [InlineData(true, false, false, "Approve assessment permissions")]
    [InlineData(true, true, false, "Users and groups")]
    [InlineData(true, true, true, "Connect read-only now")]
    public void Access_guidance_names_the_next_unfinished_step(bool configuration, bool consent, bool assigned, string expected)
    {
        var check = new ApplicationPermissionValidation { ConfigurationValid = configuration, ConsentComplete = consent, EngineerAssignmentConfirmed = assigned };
        Assert.Contains(expected, ApplicationSetupViewModel.NextAction(check, SessionMode.Assessment), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}", "Not reported")]
    [InlineData("{\"state\":null}", "Unknown")]
    [InlineData("{\"state\":\"\"}", "Unknown")]
    [InlineData("{\"state\":true}", "Unknown")]
    [InlineData("{\"state\":\"enabled\"}", "enabled")]
    [InlineData("{\"state\":\"disabled\"}", "disabled")]
    public void Object_state_never_shows_an_ambiguous_blank_or_infers_compliance(string json, string expected)
    {
        var row = new ObjectRow { Item = JsonNode.Parse(json)!.AsObject() };
        Assert.Equal(expected, row.State);
        Assert.Contains("not a compliance result", row.StateExplanation, StringComparison.Ordinal);
    }
}
