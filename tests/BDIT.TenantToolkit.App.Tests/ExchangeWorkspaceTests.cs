using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
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
    public void Import_keeps_durable_offline_evidence_and_clears_every_deployment_approval()
    {
        typeof(Workspace).GetProperty(nameof(Workspace.SnapshotIsLive))!.SetValue(_workspace,true);
        typeof(Workspace).GetProperty(nameof(Workspace.Plan))!.SetValue(_workspace,new DeploymentPlan());
        typeof(Workspace).GetProperty(nameof(Workspace.AcknowledgedSnapshotId))!.SetValue(_workspace,"synthetic obsolete approval");
        _workspace.ImportExchangeCapture(_file,ExchangeTestData.Domain);
        Assert.False(_workspace.SnapshotIsLive); Assert.Null(_workspace.Plan); Assert.Null(_workspace.AcknowledgedSnapshotId);
        Assert.NotNull(_workspace.Assessment);
        var stored = _workspace.Evidence.LoadSnapshot(TestData.TenantA,_workspace.Snapshot!.Id);
        Assert.NotNull(stored!.ExchangeCapture); Assert.False(stored.Complete); Assert.Empty(stored.Collections);
        Assert.Throws<ToolkitException>(_workspace.AcknowledgeSnapshot);
        var f = _workspace.Assessment!.Findings.Single(f=>f.ControlId=="EX-004");
        Assert.Equal(FindingStatus.RequiresManualReview,f.Status);
    }

    [Fact]
    public void Wrong_tenant_or_wrong_entered_domain_cannot_replace_current_evidence()
    {
        _workspace.ImportExchangeCapture(_file,ExchangeTestData.Domain); var previous = _workspace.Snapshot;
        Assert.Throws<ConfigurationException>(()=>_workspace.ImportExchangeCapture(_file,"different.invalid"));
        Assert.Same(previous,_workspace.Snapshot);
        var capture = ExchangeTestData.Capture(); capture.ExchangeTenantId = TestData.TenantB;
        File.WriteAllText(_file,ToolkitJson.Serialize(capture));
        Assert.Throws<TenantMismatchException>(()=>_workspace.ImportExchangeCapture(_file,ExchangeTestData.Domain));
        Assert.Same(previous,_workspace.Snapshot);
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
        _workspace.ImportExchangeCapture(_file,ExchangeTestData.Domain); var originalId = _workspace.Snapshot!.Id;
        var fake = new FakeDns(); await _workspace.CheckExchangeDnsAsync(fake);
        Assert.Equal(3,fake.Questions); Assert.NotEqual(originalId,_workspace.Snapshot!.Id);
        Assert.False(_workspace.SnapshotIsLive); Assert.False(_workspace.Snapshot.Complete);
        Assert.Equal(3,_workspace.Evidence.LoadSnapshot(TestData.TenantA,_workspace.Snapshot.Id)!.ExchangeCapture!.Dns.Count);
        Assert.NotNull(_workspace.Evidence.LoadSnapshot(TestData.TenantA,originalId));
    }

    private sealed class FakeDns : IDnsLookup
    {
        public int Questions { get; private set; }
        public Task<DnsObservation> QueryAsync(string name,DnsRecordKind kind,CancellationToken ct)
        { Questions++; return Task.FromResult(ExchangeTestData.Answer(name,kind)); }
    }
    public void Dispose() { _logger.Dispose(); _root.Dispose(); }
}
