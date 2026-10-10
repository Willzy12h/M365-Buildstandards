using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Engine.Checks;
using BDIT.TenantToolkit.Engine.Exchange;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ExchangeWorkspaceTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _logger;
    private readonly Workspace _workspace;
    private readonly string _file;

    public ExchangeWorkspaceTests()
    {
        _root.WriteStandard("2026.09.12.json",File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../standards/2026.09.12.json"))));
        _root.WriteManifest();
        _logger = new ToolkitLogger(_root.Paths.LogsDirectory,LogLevel.Debug);
        _workspace = new Workspace(_root.Paths,new ToolkitSettings(),_logger,diagnostics:true);
        _workspace.Initialise(); _workspace.ApplyProfileToSession(TestData.Profile(),save:false);
        _file = Path.Combine(_root.Root,"synthetic-exchange.json");
        File.WriteAllText(_file,ToolkitJson.Serialize(ExchangeTestData.Capture()));
    }

    [Fact]
    public void Import_preserves_Graph_evidence_plan_and_acknowledgement_but_never_becomes_before_evidence()
    {
        var graph = TestData.Snapshot(_workspace.RequireStandard(), capturedAt: DateTimeOffset.UtcNow);
        var plan = new DeploymentPlan();
        typeof(Workspace).GetProperty(nameof(Workspace.Snapshot))!.SetValue(_workspace, graph);
        typeof(Workspace).GetProperty(nameof(Workspace.SnapshotIsLive))!.SetValue(_workspace, true);
        typeof(Workspace).GetProperty(nameof(Workspace.Plan))!.SetValue(_workspace, plan);
        typeof(Workspace).GetProperty(nameof(Workspace.AcknowledgedSnapshotId))!.SetValue(_workspace, graph.Id);
        _workspace.ImportExchangeCapture(_file, ExchangeTestData.Domain);
        Assert.Same(graph, _workspace.Snapshot); Assert.True(_workspace.SnapshotIsLive);
        Assert.Same(plan, _workspace.Plan); Assert.Equal(graph.Id, _workspace.AcknowledgedSnapshotId);
        Assert.Null(graph.ExchangeCapture);
        var stored = _workspace.Evidence.LoadSnapshot(TestData.TenantA, _workspace.ExchangeSnapshot!.Id);
        Assert.NotNull(stored!.ExchangeCapture); Assert.False(stored.Complete); Assert.Empty(stored.Collections);
        Assert.Equal(graph.Id, _workspace.Assessment!.SnapshotId);
        Assert.Equal(FindingStatus.RequiresManualReview, _workspace.Assessment.Findings.Single(f => f.ControlId == "EX-004").Status);
    }

    [Fact]
    public void Exchange_only_evidence_reopened_from_history_is_judged_as_of_its_capture()
    {
        // Reopening stored Exchange-only evidence days later must reproduce its findings, as the headless runner does,
        // rather than marking every EX/PUR control stale.
        var capture = ExchangeTestData.Capture();
        capture.CapturedAt = Timestamps.Format(DateTimeOffset.UtcNow.AddDays(-3));
        var stored = ExchangeEvidenceImporter.Snapshot(capture, TestData.Profile(), _workspace.RequireStandard());
        _workspace.Evidence.SaveSnapshot(stored);

        _workspace.LoadStoredSnapshot(stored.Id);
        Assert.DoesNotContain(_workspace.Assessment!.Findings, f => f.Reason.Contains("over 24 hours old", StringComparison.Ordinal));

        // The same three-day-old observations brought in as current work are still stale.
        File.WriteAllText(_file, ToolkitJson.Serialize(capture));
        _workspace.ImportExchangeCapture(_file, ExchangeTestData.Domain);
        Assert.Contains(_workspace.Assessment!.Findings, f => f.ControlId == "EX-004" && f.Reason.Contains("over 24 hours old", StringComparison.Ordinal));
    }

    [Fact]
    public void A_modified_stored_Exchange_snapshot_cannot_be_reopened_as_evidence()
    {
        // Modified Exchange evidence is refused when reopened from history, as the headless runner refuses it.
        var stored = ExchangeEvidenceImporter.Snapshot(ExchangeTestData.Capture(), TestData.Profile(), _workspace.RequireStandard());
        var file = _workspace.Evidence.SaveSnapshot(stored);
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"3.9.2\"", "\"3.9.3\"", StringComparison.Ordinal));
        Assert.Throws<IntegrityException>(() => _workspace.LoadStoredSnapshot(stored.Id));
        Assert.Null(_workspace.ExchangeSnapshot);
    }

    [Fact]
    public void Exchange_only_import_cannot_enable_Graph_deployment()
    {
        _workspace.ImportExchangeCapture(_file, ExchangeTestData.Domain);
        Assert.Null(_workspace.Snapshot); Assert.False(_workspace.SnapshotIsLive);
        Assert.Null(_workspace.Plan); Assert.Null(_workspace.AcknowledgedSnapshotId);
        Assert.Throws<ToolkitException>(_workspace.AcknowledgeSnapshot);
    }

    [Fact]
    public void Wrong_tenant_or_wrong_entered_domain_cannot_replace_current_evidence()
    {
        _workspace.ImportExchangeCapture(_file,ExchangeTestData.Domain); var previous = _workspace.ExchangeSnapshot;
        Assert.Throws<ConfigurationException>(()=>_workspace.ImportExchangeCapture(_file,"different.invalid"));
        Assert.Same(previous,_workspace.ExchangeSnapshot);
        var capture = ExchangeTestData.Capture(); capture.ExchangeTenantId = TestData.TenantB;
        File.WriteAllText(_file,ToolkitJson.Serialize(capture));
        Assert.Throws<TenantMismatchException>(()=>_workspace.ImportExchangeCapture(_file,ExchangeTestData.Domain));
        Assert.Same(previous,_workspace.ExchangeSnapshot);
    }

    [Fact]
    public void Mandatory_domain_refuses_empty_saves_and_preserves_unrelated_inputs()
    {
        _workspace.Profile!.Parameters.PolicyInputs = new()
        {
            ["syntheticOther"] = "preserve"
        };
        Assert.Throws<ConfigurationException>(()=>_workspace.SaveExchangeDomain(""));
        _workspace.SaveExchangeDomain(" EXAMPLE.INVALID ");
        Assert.Equal("example.invalid",_workspace.Profile.Parameters.PolicyInputs["exchangeDomain"]!.GetValue<string>());
        Assert.Equal("preserve",_workspace.Profile.Parameters.PolicyInputs["syntheticOther"]!.GetValue<string>());
        var field = new PolicyInputField(_workspace.RequireStandard().Parameters.Single(p=>p.Key=="exchangeDomain"),null,DateTimeOffset.UtcNow);
        Assert.False(field.TryRead(out _,requireNow:true)); Assert.Contains("Client mail domain",field.Problem);
        field.Value = "https://example.invalid"; Assert.False(field.TryRead(out _)); Assert.True(field.HasProblem);
    }

    [Fact]
    public async Task DNS_refresh_uses_fake_answers_and_saves_a_new_inert_snapshot()
    {
        _workspace.ImportExchangeCapture(_file,ExchangeTestData.Domain); var originalId = _workspace.ExchangeSnapshot!.Id;
        var fake = new FakeDns(); await _workspace.CheckExchangeDnsAsync(fake);
        Assert.Equal(3,fake.Questions); Assert.NotEqual(originalId,_workspace.ExchangeSnapshot!.Id);
        Assert.False(_workspace.SnapshotIsLive); Assert.False(_workspace.ExchangeSnapshot.Complete);
        Assert.Equal(3,_workspace.Evidence.LoadSnapshot(TestData.TenantA,_workspace.ExchangeSnapshot.Id)!.ExchangeCapture!.Dns.Count);
        Assert.NotNull(_workspace.Evidence.LoadSnapshot(TestData.TenantA,originalId));
    }

    [Fact]
    public void Domain_selection_requires_complete_accepted_domains_and_discards_old_DNS_only()
    {
        _workspace.ImportExchangeCapture(_file, ExchangeTestData.Domain);
        var old = _workspace.ExchangeSnapshot!;
        Assert.Throws<ConfigurationException>(() => _workspace.SelectExchangeDomain("unobserved.invalid"));
        Assert.Same(old, _workspace.ExchangeSnapshot);
        _workspace.SelectExchangeDomain(ExchangeTestData.Domain);
        Assert.NotEqual(old.Id, _workspace.ExchangeSnapshot!.Id);
        Assert.Empty(_workspace.ExchangeSnapshot.ExchangeCapture!.Dns);
        Assert.Null(_workspace.Snapshot);
        _workspace.ExchangeSnapshot.ExchangeCapture.Collections["acceptedDomains"].Status = CaptureStatus.Error;
        Assert.Empty(_workspace.ExchangeDomains);
        Assert.Throws<ConfigurationException>(() => _workspace.SelectExchangeDomain(ExchangeTestData.Domain));
    }

    [Fact]
    public async Task Integrated_read_uses_verified_tenant_without_domain_input_and_preserves_Graph_capture()
    {
        var session = TestData.Session(mode: SessionMode.Assessment); session.PrimaryDomain = ExchangeTestData.Domain;
        var graph = new FakeGraphClient(_workspace.RequireStandard()) { Mode = SessionMode.Assessment };
        typeof(Workspace).GetProperty(nameof(Workspace.Connection))!.SetValue(_workspace, new ConnectedTenant(session, graph, null));
        var snapshot = TestData.Snapshot(_workspace.RequireStandard(), capturedAt: DateTimeOffset.UtcNow);
        typeof(Workspace).GetProperty(nameof(Workspace.Snapshot))!.SetValue(_workspace, snapshot);
        typeof(Workspace).GetProperty(nameof(Workspace.SnapshotIsLive))!.SetValue(_workspace, true);
        var runner = new FakeRunner();
        await _workspace.CaptureExchangeAsync(true, runner);
        Assert.Equal(TestData.TenantA, runner.Tenant); Assert.Equal(ExchangeTestData.Domain, runner.Domain);
        Assert.Equal(session.Account, runner.Account); Assert.True(runner.Purview);
        Assert.True(_workspace.ExchangeCapturedByTool); Assert.Same(snapshot, _workspace.Snapshot);
        Assert.True(_workspace.SnapshotIsLive); Assert.Empty(graph.Writes); Assert.Null(snapshot.ExchangeCapture);
        var previous = _workspace.ExchangeSnapshot;
        runner.WrongTenant = true;
        await Assert.ThrowsAsync<TenantMismatchException>(() => _workspace.CaptureExchangeAsync(true, runner));
        Assert.Same(previous, _workspace.ExchangeSnapshot); Assert.Same(snapshot, _workspace.Snapshot);
        session.OperatorVerified = false;
        var calls = runner.Calls;
        await Assert.ThrowsAsync<TenantMismatchException>(() => _workspace.CaptureExchangeAsync(true, runner));
        Assert.Equal(calls, runner.Calls);
    }

    [Fact]
    public void Editing_a_saved_profile_to_another_tenant_clears_separate_Exchange_context()
    {
        var saved = _workspace.SaveProfile(_workspace.Profile!);
        _workspace.ImportExchangeCapture(_file, ExchangeTestData.Domain);
        Assert.NotEmpty(_workspace.ExchangeDomains);
        saved = ToolkitJson.Deserialize<TenantProfile>(ToolkitJson.Serialize(saved));
        saved.TenantId = TestData.TenantB;
        _workspace.SaveProfile(saved);
        Assert.Null(_workspace.ExchangeSnapshot); Assert.Empty(_workspace.ExchangeDomains);
        Assert.False(_workspace.ExchangeCapturedByTool);
    }

    private sealed class FakeRunner : IExchangeCaptureRunner
    {
        public string Tenant = "", Domain = "", Account = "";
        public bool Purview, WrongTenant; public int Calls;
        public Task<string> CaptureAsync(string tenantId, string referenceDomain, string account, bool includePurview, IProgress<string>? progress, CancellationToken ct)
        {
            Calls++; Tenant = tenantId; Domain = referenceDomain; Account = account; Purview = includePurview;
            var capture = ExchangeTestData.Capture();
            if (WrongTenant) capture.ExchangeTenantId = TestData.TenantB;
            return Task.FromResult(ToolkitJson.Serialize(capture));
        }
    }

    private sealed class FakeDns : IDnsLookup
    {
        public int Questions { get; private set; }
        public Task<DnsObservation> QueryAsync(string name,DnsRecordKind kind,CancellationToken ct)
        { Questions++; return Task.FromResult(ExchangeTestData.Answer(name,kind)); }
    }

    [Fact]
    public void Proposal_export_refuses_missing_confirmation_then_writes_only_the_selected_inert_document()
    {
        _workspace.ImportExchangeCapture(_file, ExchangeTestData.Domain);
        Assert.Throws<PlanValidationException>(() => _workspace.ExportExchangeProposal("PUR-001", "", ExchangeTestData.Domain));
        var file = _workspace.ExportExchangeProposal("PUR-001", TestData.TenantA, ExchangeTestData.Domain);
        var content = File.ReadAllText(file);
        Assert.StartsWith(BDIT.TenantToolkit.Engine.Exchange.ExchangeProposal.Refusal, content);
        Assert.Contains("REVIEW PROPOSAL: PUR-001", content);
        Assert.DoesNotContain("REVIEW PROPOSAL: EX-001", content);
        Assert.False(_workspace.SnapshotIsLive); Assert.Null(_workspace.Plan); Assert.Null(_workspace.AcknowledgedSnapshotId);
    }
    [Fact]
    public async Task Stored_partial_check_uses_separately_imported_exchange_evidence_like_the_full_assessment()
    {
        // CLA-20261008-02: the Graph capture plus a separate Exchange import, as RunAssessment combines them.
        var graph = TestData.Snapshot(_workspace.RequireStandard(), capturedAt: DateTimeOffset.UtcNow);
        _workspace.Evidence.SaveSnapshot(graph);
        _workspace.LoadStoredSnapshot(graph.Id);
        _workspace.ImportExchangeCapture(_file, ExchangeTestData.Domain);
        var full = _workspace.Assessment!;
        var result = await _workspace.RunScopedCheckAsync(CheckSelection.ForArea(_workspace.RequireStandard(), _workspace.Profile!, "Exchange"), historical: true);
        Assert.Equal(graph.Id, result.Evidence.SourceCapture!.Id);
        Assert.Equal(_workspace.ExchangeSnapshot!.ExchangeCapture!.Id, result.Evidence.SeparateExchange!.Id);
        foreach (var finding in result.Evidence.Assessment.Findings)
            Assert.Equal(full.Findings.Single(f => f.ControlId == finding.ControlId).Status, finding.Status);
        Assert.Contains(result.Evidence.Assessment.Findings, f => f.ControlId == "EX-004" && f.Status == FindingStatus.RequiresManualReview);
        Assert.Null(result.NotSavedReason);
        Assert.Same(full, _workspace.Assessment);
    }

    [Fact]
    public async Task Exchange_only_evidence_can_be_checked_from_stored_evidence()
    {
        _workspace.ImportExchangeCapture(_file, ExchangeTestData.Domain);
        Assert.Null(_workspace.Snapshot);
        var page = new ShellViewModel(_workspace).Page<AssessmentViewModel>();
        page.CheckStored = true; page.CheckArea = "Exchange";
        Assert.True(page.CheckAreaCommand.CanExecute(null));
        var result = await _workspace.RunScopedCheckAsync(CheckSelection.ForArea(_workspace.RequireStandard(), _workspace.Profile!, "Exchange"), historical: true);
        Assert.Equal(_workspace.ExchangeSnapshot!.Id, result.Evidence.SourceCapture!.Id);
        Assert.Null(result.Evidence.SeparateExchange);
        Assert.Contains(result.Evidence.Assessment.Findings, f => f.ControlId == "EX-004" && f.Status == FindingStatus.RequiresManualReview);
    }
    public void Dispose() { _logger.Dispose(); _root.Dispose(); }
}
