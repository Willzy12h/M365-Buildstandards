using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Exchange;
using BuildStandard.Engine.Execution;
using BuildStandard.Graph;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ClientHandoffQolTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _logger;
    private readonly Workspace _workspace;
    private readonly ShellViewModel _shell;
    private readonly TenantProfile _a, _b;
    public ClientHandoffQolTests()
    {
        _root.WriteStandard("2026.09.12.json", File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.09.12.json"))));
        _root.WriteManifest(); _logger = new(_root.Paths.LogsDirectory, LogLevel.Debug);
        _workspace = new(_root.Paths, new ToolkitSettings(), _logger, true); _workspace.Initialise();
        _a = TestData.Profile(); _a.Domain = ExchangeTestData.Domain;
        _b = TestData.Profile(TestData.TenantB); _b.Id = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
        _workspace.SaveProfile(_a); _workspace.SaveProfile(_b); _workspace.ApplyProfileToSession(_a, save: false);
        _shell = new(_workspace);
    }
    private void Set(string name, object? value) => typeof(Workspace).GetProperty(name)!.SetValue(_workspace, value);
    private FakeGraphClient Connect()
    {
        var session = TestData.Session(mode: SessionMode.Assessment); session.PrimaryDomain = ExchangeTestData.Domain;
        var graph = new FakeGraphClient(_workspace.RequireStandard()) { Mode = SessionMode.Assessment };
        Set(nameof(Workspace.Connection), new ConnectedTenant(session, graph, null)); return graph;
    }
    private TenantSnapshot OpenGraph()
    {
        var capture = TestData.Snapshot(_workspace.RequireStandard()); _workspace.Evidence.SaveSnapshot(capture);
        _workspace.LoadStoredSnapshot(capture.Id); return capture;
    }
    private TenantSnapshot OpenExchange()
    {
        var capture = ExchangeEvidenceImporter.Snapshot(ExchangeTestData.Capture(), _a, _workspace.RequireStandard());
        _workspace.Evidence.SaveSnapshot(capture); _workspace.LoadStoredSnapshot(capture.Id); return capture;
    }

    [Fact]
    public void Live_Graph_and_reopened_Exchange_assessment_explicitly_retain_mixed_sources()
    {
        var graphClient = Connect(); var graph = OpenGraph(); Set(nameof(Workspace.SnapshotIsLive), true);
        var exchange = OpenExchange(); var page = _shell.Page<AssessmentViewModel>();
        Assert.Contains("Live full capture", page.ViewStateText); Assert.Contains("Exchange: historical", page.ViewStateText);
        Assert.Contains("Exchange: historical", _shell.EvidenceStatus); Assert.Contains(graph.CapturedAt, page.ViewStateText);
        Assert.Contains(exchange.Id, _shell.DetailsText); Assert.Contains(exchange.CapturedAt, _shell.DetailsText);
        Assert.True(_workspace.SnapshotIsLive); Assert.False(_workspace.ExchangeCapturedByTool); Assert.Empty(graphClient.Reads); Assert.Empty(graphClient.Writes);
        page.Search = "synthetic unmatched filter"; Assert.Contains("No findings match", page.ViewStateText);
        Assert.Contains("Exchange: historical", page.ViewStateText);
    }

    [Fact]
    public async Task Exchange_only_tool_capture_is_not_labelled_a_historical_assessment()
    {
        var graph = Connect(); var runner = new SyntheticRunner(); await _workspace.CaptureExchangeAsync(true, runner);
        Assert.Null(_workspace.Snapshot); Assert.True(_workspace.ExchangeCapturedByTool); Assert.NotNull(_workspace.Assessment);
        var text = _shell.Page<AssessmentViewModel>().ViewStateText;
        Assert.Contains("Supplemental Exchange capture", text); Assert.DoesNotContain("Historical evidence review", text);
        Assert.Contains(_workspace.ExchangeSnapshot!.CapturedAt, text); Assert.Equal(1, runner.Calls);
        Assert.Contains("Supplemental Exchange capture", _shell.EvidenceStatus); Assert.Empty(graph.Reads); Assert.Empty(graph.Writes);
    }

    [Fact]
    public void Reopened_Exchange_only_assessment_remains_historical_with_original_time()
    {
        var capture = OpenExchange(); var text = _shell.Page<AssessmentViewModel>().ViewStateText;
        Assert.Contains("Historical Exchange evidence", text); Assert.Contains(capture.CapturedAt, text);
        Assert.Contains(capture.Id, _shell.DetailsText); Assert.Null(_workspace.Plan); Assert.False(_workspace.SnapshotIsLive);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Deleting_active_saved_client_clears_local_target_and_transient_authority_but_preserves_files(bool connected)
    {
        FakeGraphClient? graph = connected ? Connect() : null; var capture = OpenGraph(); OpenExchange();
        if (connected) Set(nameof(Workspace.SnapshotIsLive), true);
        Set(nameof(Workspace.Access), new AccessReport()); Set(nameof(Workspace.Licences), new LicenceInventory());
        Set(nameof(Workspace.Plan), new DeploymentPlan { PlanDigest = new string('a', 64) });
        Set(nameof(Workspace.AcknowledgedSnapshotId), capture.Id); Set(nameof(Workspace.LastRun), new DeploymentRun());
        Set(nameof(Workspace.Control), new DeploymentControl()); Set(nameof(Workspace.InterruptedNotice), "Synthetic old client notice");
        _workspace.SaveManualCheck("CA-001", "Pass", "Synthetic evidence retained after saved-client removal.");
        _workspace.Evidence.SaveRun(new DeploymentRun { Id = "cccccccc-cccc-4ccc-8ccc-cccccccccccc", TenantId = _a.TenantId, Release = _workspace.RequireStandard().Release, ToolkitVersion = _workspace.Version, StartedAt = "2026-10-01T10:00:00Z", EndedAt = "2026-10-01T10:00:00Z", Status = RunStatus.Completed, Results = new() });
        _workspace.Evidence.SaveDeviations(_a.TenantId, new[] { new Deviation { Id = "dddddddd-dddd-4ddd-8ddd-dddddddddddd", TenantId = _a.TenantId, ControlId = "CA-001", Kind = DeviationKind.ApprovedDeviation, Owner = "Synthetic owner", Reason = "Synthetic retained evidence", ApprovedBy = "Synthetic reviewer", Reference = "Synthetic decision", ReviewBy = "2099-01-01" } });
        var files = Directory.GetFiles(_root.Paths.TenantDirectory(_a.TenantId), "*.json", SearchOption.AllDirectories)
            .ToDictionary(file => file, File.ReadAllBytes);
        var binding = typeof(ExperimentalOperationGuard).GetField("_binding", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        binding.SetValue(_workspace.ExperimentalGuard, "synthetic old authority marker");
        await _workspace.DeleteProfileAsync(_a.Id);
        Assert.Null(_workspace.Profile); Assert.Null(_workspace.Connection); Assert.Null(_workspace.ApplicationSetup);
        Assert.Null(_workspace.Snapshot); Assert.Null(_workspace.ExchangeSnapshot); Assert.False(_workspace.SnapshotIsLive); Assert.False(_workspace.ExchangeCapturedByTool);
        Assert.Null(_workspace.Assessment); Assert.Null(_workspace.Plan); Assert.Null(_workspace.AcknowledgedSnapshotId);
        Assert.Null(_workspace.Access); Assert.Null(_workspace.Licences); Assert.Null(_workspace.LastRun); Assert.Null(_workspace.Control); Assert.Empty(_workspace.InterruptedNotice);
        Assert.Null(binding.GetValue(_workspace.ExperimentalGuard));
        Assert.DoesNotContain(_workspace.Profiles, p => p.Id == _a.Id); Assert.DoesNotContain(_workspace.Evidence.LoadProfiles(), p => p.Id == _a.Id);
        Assert.All(files, file => Assert.Equal(file.Value, File.ReadAllBytes(file.Key)));
        Assert.Contains("No client evidence selected", _shell.EvidenceStatus); Assert.False(_shell.HeaderHasDetails);
        Assert.Null(_shell.Page<ManualChecksViewModel>().Selected); Assert.Null(_shell.Page<DeviationsViewModel>().Selected);
        if (graph is not null) { Assert.Empty(graph.Reads); Assert.Empty(graph.Writes); }
    }

    [Fact]
    public async Task Deleting_another_saved_client_preserves_active_identity_evidence_and_approval()
    {
        var graph = Connect(); OpenGraph(); OpenExchange();
        var plan = new DeploymentPlan { PlanDigest = new string('a', 64) }; Set(nameof(Workspace.Plan), plan);
        Set(nameof(Workspace.AcknowledgedSnapshotId), _workspace.Snapshot!.Id);
        var profile = _workspace.Profile; var connection = _workspace.Connection; var capture = _workspace.Snapshot;
        var exchange = _workspace.ExchangeSnapshot; var assessment = _workspace.Assessment; var ack = _workspace.AcknowledgedSnapshotId;
        var files = _workspace.Evidence.ListSnapshots(_a.TenantId).ToDictionary(s => s.File, s => File.ReadAllBytes(s.File));
        var binding = typeof(ExperimentalOperationGuard).GetField("_binding", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        binding.SetValue(_workspace.ExperimentalGuard, "synthetic unchanged authority marker");
        await _workspace.DeleteProfileAsync(_b.Id);
        Assert.Same(profile, _workspace.Profile); Assert.Same(connection, _workspace.Connection); Assert.Same(capture, _workspace.Snapshot);
        Assert.Same(exchange, _workspace.ExchangeSnapshot); Assert.Same(assessment, _workspace.Assessment); Assert.Same(plan, _workspace.Plan);
        Assert.Equal(ack, _workspace.AcknowledgedSnapshotId); Assert.Single(_workspace.Evidence.LoadProfiles());
        Assert.Equal("synthetic unchanged authority marker", binding.GetValue(_workspace.ExperimentalGuard));
        Assert.All(files, file => Assert.Equal(file.Value, File.ReadAllBytes(file.Key))); Assert.Empty(graph.Reads); Assert.Empty(graph.Writes);
    }

    [Fact]
    public async Task Busy_direct_profile_delete_refuses_before_saved_list_or_evidence_changes()
    {
        var capture = OpenGraph(); var profile = _workspace.Profile;
        await _workspace.RunExclusiveAsync("Synthetic operation", async _ =>
            await Assert.ThrowsAsync<BuildStandard.Core.ToolkitException>(() => _workspace.DeleteProfileAsync(_a.Id)));
        Assert.Same(profile, _workspace.Profile); Assert.Equal(capture.Id, _workspace.Snapshot?.Id); Assert.Equal(2, _workspace.Evidence.LoadProfiles().Count);
    }

    private sealed class SyntheticRunner : IExchangeCaptureRunner
    {
        public int Calls;
        public Task<string> CaptureAsync(string tenantId, string referenceDomain, string account, bool includePurview, IProgress<string>? progress, CancellationToken ct)
        { Calls++; return Task.FromResult(ToolkitJson.Serialize(ExchangeTestData.Capture())); }
    }
    public void Dispose() { _logger.Dispose(); _root.Dispose(); }
}
