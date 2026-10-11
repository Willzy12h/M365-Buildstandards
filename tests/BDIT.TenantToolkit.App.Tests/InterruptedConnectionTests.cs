using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class InterruptedConnectionTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _log;
    private readonly Workspace _workspace;
    private readonly ShellViewModel _shell;
    private readonly ReadHttp _handler = new();
    private readonly Tokens _tokens = new();
    private readonly HttpClient _http;
    public InterruptedConnectionTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson); _root.WriteManifest();
        _log = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        _workspace = new Workspace(_root.Paths, new ToolkitSettings(), _log, true);
        _workspace.Initialise(); _shell = new ShellViewModel(_workspace);
        _http = new HttpClient(_handler);
        Set(nameof(Workspace.Profile), TestData.Profile(TestData.TenantB));
        Set(nameof(Workspace.Connection), new ConnectedTenant(TestData.Session(TestData.TenantB), new FakeGraphClient(_workspace.RequireStandard()), null));
        Set(nameof(Workspace.InterruptedNotice), "Old tenant B interrupted notice");
        Set(nameof(Workspace.Access), new AccessReport { TenantId = TestData.TenantB, Account = "old@b.invalid" });
        Set(nameof(Workspace.Licences), new LicenceInventory { TenantId = TestData.TenantB });
        Set(nameof(Workspace.Snapshot), TestData.Snapshot(_workspace.RequireStandard(), TestData.TenantB));
        Set(nameof(Workspace.SnapshotIsLive), true); Set(nameof(Workspace.AcknowledgedSnapshotId), "old-ack");
    }
    public void Dispose() { _http.Dispose(); _log.Dispose(); _root.Dispose(); }
    private void Set(string property, object? value) => typeof(Workspace).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.SetValue(_workspace, value);
    private DeploymentRun SaveRunning()
    {
        var run = new DeploymentRun { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA,
            PlanId = Guid.NewGuid().ToString(), StartedAt = "2026-10-10T09:00:00.000Z", Status = RunStatus.Running,
            Results = [new() { ControlId = "CA-001", PlannedAction = "Create", Status = ResultStatus.InProgress, WriteAcceptance = WriteAcceptance.Unknown }] };
        _workspace.Evidence.SaveRun(run); return run;
    }
    private ConnectedTenant Connection(SessionMode mode)
    {
        var session = TestData.Session(mode: mode); session.Account = "new@a.invalid";
        session.Scopes = _workspace.Connections.ScopesFor(mode, _workspace.RequireStandard()).ToList();
        session.TokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToString("O");
        var graph = new GraphClient(_http, _tokens, session.TenantId, mode, GraphRouteAllowList.FromStandard(_workspace.RequireStandard()),
            new GraphClientOptions { Sleep = false }, NullLog.Instance);
        return new ConnectedTenant(session, graph, null);
    }
    private Task Adopt(ConnectedTenant next, TenantProfile profile) => _workspace.RunExclusiveAsync("Synthetic post-authentication adoption",
        progress => _workspace.CompleteConnectionAsync(profile, next, _workspace.RequireStandard(), progress));

    [Theory]
    [InlineData(SessionMode.Assessment, "bad-digest")]
    [InlineData(SessionMode.Deployment, "bad-digest")]
    [InlineData(SessionMode.Assessment, "invalid-json")]
    public async Task Refused_history_keeps_verified_connection_and_runs_read_checks_with_a_blocking_notice(SessionMode mode, string fault)
    {
        var valid = SaveRunning(); var damaged = SaveRunning(); var file = _workspace.Evidence.RunFile(damaged);
        var node = JsonNode.Parse(File.ReadAllText(file))!.AsObject(); node["results"]![0]!["status"] = ResultStatus.Pending;
        File.WriteAllText(file, fault == "invalid-json" ? "{" : node.ToJsonString());
        var bytes = File.ReadAllBytes(file); var validFile = _workspace.Evidence.RunFile(valid); var validBytes = File.ReadAllBytes(validFile);
        var next = Connection(mode); var profile = TestData.Profile();
        var workspaceEvents = new List<string?>(); var shellEvents = new List<string?>(); var overviewEvents = new List<string?>();
        _workspace.PropertyChanged += (_, e) => workspaceEvents.Add(e.PropertyName);
        _shell.PropertyChanged += (_, e) => shellEvents.Add(e.PropertyName);
        _shell.Page<OverviewViewModel>().PropertyChanged += (_, e) => overviewEvents.Add(e.PropertyName);
        var changed = 0; _workspace.StateChanged += () => changed++;

        await Adopt(next, profile);

        Assert.Same(next, _workspace.Connection); Assert.Same(profile, _workspace.Profile);
        Assert.Same(next.Session, _workspace.Session); Assert.Equal("new@a.invalid", _shell.HeaderAccount);
        Assert.Contains("untrusted", _workspace.InterruptedNotice); Assert.Contains("block writes", _workspace.InterruptedNotice);
        Assert.DoesNotContain("Old tenant", _workspace.InterruptedNotice);
        Assert.Contains(_workspace.InterruptedNotice, _shell.Page<OverviewViewModel>().Summary);
        Assert.Equal(TestData.TenantA, _workspace.Access!.TenantId); Assert.Equal(next.Session.Account, _workspace.Access.Account);
        Assert.Equal(_workspace.RequireStandard().Collections.Count, _workspace.Access.Reads.Count);
        Assert.All(_workspace.Access.Reads, read => Assert.Equal("Pass", read.Status));
        Assert.Contains("new@a.invalid", _shell.Page<ConnectViewModel>().AccessSummary);
        Assert.Equal(TestData.TenantA, _workspace.Licences!.TenantId); Assert.True(_workspace.Licences.SubscriptionsComplete);
        Assert.Single(_workspace.Licences.Subscriptions); Assert.Contains("subscriptions: collected", _shell.Page<OverviewViewModel>().ReportStatus);
        Assert.Null(_workspace.Snapshot); Assert.False(_workspace.SnapshotIsLive); Assert.Null(_workspace.AcknowledgedSnapshotId);
        Assert.False(_workspace.Busy); Assert.Empty(_workspace.BusyMessage); Assert.Empty(_workspace.ProgressDetail);
        Assert.True(changed > 0); Assert.Contains(nameof(Workspace.Session), workspaceEvents);
        Assert.Contains(nameof(ShellViewModel.HeaderAccount), shellEvents); Assert.Contains(nameof(OverviewViewModel.Summary), overviewEvents);
        Assert.Equal(bytes, File.ReadAllBytes(file)); Assert.Equal(validBytes, File.ReadAllBytes(validFile));
        Assert.ThrowsAny<ToolkitException>(() => _workspace.Evidence.AssertCompletionWritesResolved(TestData.TenantA, ["CA-001"]));
        Assert.All(_handler.Methods, method => Assert.Equal(HttpMethod.Get, method)); Assert.True(_tokens.Calls > 0);
        Assert.Single(Directory.GetFiles(Path.Combine(_root.Paths.TenantDirectory(TestData.TenantA), "licensing"), "*.json"));
        Assert.False(Directory.Exists(Path.Combine(_root.Paths.TenantDirectory(TestData.TenantB), "licensing")));
    }

    [Theory]
    [InlineData(SessionMode.Assessment)]
    [InlineData(SessionMode.Deployment)]
    public async Task Held_tenant_lease_reports_unavailable_access_without_claiming_untrusted_history_and_keeps_read_checks(SessionMode mode)
    {
        var run = SaveRunning(); var file = _workspace.Evidence.RunFile(run); var bytes = File.ReadAllBytes(file);
        var active = new EvidenceStore(_root.Paths, NullLog.Instance);
        var next = Connection(mode); var profile = TestData.Profile();
        using (active.AcquireTenantWriteLease(TestData.TenantA))
        {
            await Adopt(next, profile);

            Assert.Same(next, _workspace.Connection); Assert.Same(profile, _workspace.Profile);
            Assert.Contains("exclusive access", _workspace.InterruptedNotice);
            Assert.Contains("unavailable", _workspace.InterruptedNotice);
            Assert.Contains("another operation may be active", _workspace.InterruptedNotice);
            Assert.Contains("Wait for active work to finish, then retry", _workspace.InterruptedNotice);
            Assert.Contains("local file access may have failed", _workspace.InterruptedNotice);
            Assert.Contains("check local storage access", _workspace.InterruptedNotice);
            Assert.Contains("write guards remain unchanged", _workspace.InterruptedNotice);
            Assert.DoesNotContain("untrusted", _workspace.InterruptedNotice);
            Assert.DoesNotContain("reconcile", _workspace.InterruptedNotice);
            Assert.Contains(_workspace.InterruptedNotice, _shell.Page<OverviewViewModel>().Summary);
            Assert.Equal(bytes, File.ReadAllBytes(file));
            Assert.Equal(RunStatus.Running, _workspace.Evidence.LoadRun(TestData.TenantA, run.Id)!.Status);
            Assert.Throws<PlanValidationException>(() => _workspace.Evidence.AssertCompletionWritesResolved(TestData.TenantA, ["CA-001"]));
            Assert.All(_workspace.Access!.Reads, read => Assert.Equal("Pass", read.Status));
            Assert.Equal(TestData.TenantA, _workspace.Access.TenantId);
            Assert.True(_workspace.Licences!.SubscriptionsComplete); Assert.Single(_workspace.Licences.Subscriptions);
            Assert.False(_workspace.Busy); Assert.Null(_workspace.Snapshot); Assert.Null(_workspace.AcknowledgedSnapshotId);
            Assert.NotEmpty(_handler.Methods); Assert.All(_handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
            Assert.True(_tokens.Calls > 0);
        }
        Assert.Equal(bytes, File.ReadAllBytes(file));
    }

    [Fact]
    public async Task Verified_history_keeps_the_existing_interrupted_notice_and_read_checks()
    {
        var run = SaveRunning(); var next = Connection(SessionMode.Assessment);
        await Adopt(next, TestData.Profile());
        Assert.Contains("1 interrupted deployment run(s)", _workspace.InterruptedNotice);
        Assert.DoesNotContain("untrusted", _workspace.InterruptedNotice);
        var marked = _workspace.Evidence.LoadRun(TestData.TenantA, run.Id)!;
        Assert.Equal(RunStatus.Interrupted, marked.Status); Assert.Equal(WriteAcceptance.Unknown, marked.Results[0].WriteAcceptance);
        Assert.True(EvidenceIntegrity.Verify(marked, marked.IntegrityDigest));
        Assert.NotNull(_workspace.Access); Assert.True(_workspace.Licences!.SubscriptionsComplete);
        Assert.False(_workspace.Busy); Assert.All(_handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task Empty_verified_history_clears_the_old_tenant_notice_after_adoption()
    {
        var next = Connection(SessionMode.Assessment);
        await Adopt(next, TestData.Profile());
        Assert.Same(next, _workspace.Connection); Assert.Empty(_workspace.InterruptedNotice);
        Assert.NotNull(_workspace.Access); Assert.True(_workspace.Licences!.SubscriptionsComplete);
        Assert.False(_workspace.Busy);
    }

    [Fact]
    public async Task Cancellation_of_the_following_read_preserves_the_untrusted_history_notice()
    {
        var run = SaveRunning(); var file = _workspace.Evidence.RunFile(run);
        File.WriteAllText(file, "{"); var bytes = File.ReadAllBytes(file);
        _handler.BeforeRead = _workspace.CancelOperation;
        var next = Connection(SessionMode.Assessment);
        await Adopt(next, TestData.Profile());
        Assert.Same(next, _workspace.Connection); Assert.Contains("untrusted", _workspace.InterruptedNotice);
        Assert.Null(_workspace.Access); Assert.Null(_workspace.Licences); Assert.False(_workspace.Busy);
        Assert.Equal(bytes, File.ReadAllBytes(file)); Assert.Single(_handler.Methods);
    }

    private sealed class Tokens : IAccessTokenProvider
    {
        public int Calls { get; private set; }
        public Task<string> GetAccessTokenAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); Calls++; return Task.FromResult("synthetic-token"); }
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) => GetAccessTokenAsync(ct);
    }
    private sealed class ReadHttp : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = new();
        public Action? BeforeRead { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Methods.Add(request.Method); Assert.Equal(HttpMethod.Get, request.Method); BeforeRead?.Invoke(); ct.ThrowIfCancellationRequested();
            var body = request.RequestUri!.AbsolutePath.EndsWith("/subscribedSkus", StringComparison.Ordinal)
                ? "{\"value\":[{\"skuId\":\"77777777-7777-4777-8777-777777777777\",\"skuPartNumber\":\"SYNTHETIC\",\"prepaidUnits\":{\"enabled\":5},\"consumedUnits\":1}]}"
                : "{\"value\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
