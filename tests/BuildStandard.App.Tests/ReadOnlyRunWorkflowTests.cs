using System.Text.Json.Nodes;
using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Scripts.Runner;
using BuildStandard.Graph;
using Xunit;

namespace BuildStandard.Tests;

/// <summary>Same coordination source executed portably and compiled into App.Tests. No PowerShell/auth/network transport.</summary>
internal sealed class ReadRunFixture : IDisposable
{
    public TempRoot Root { get; } = new();
    public FixedClock Clock { get; } = new() { UtcNow = new(2026, 10, 11, 1, 0, 0, TimeSpan.Zero) };
    public ScriptCatalogue Catalogue { get; }
    public ScriptEntry Entry => Catalogue.Entries.Single(e => e.Manifest.SchemaVersion == 2);
    public TenantProfile? Profile { get; set; } = TestData.Profile();
    public ConnectedTenant? Connection { get; set; }
    public ReadOnlyRunWorkflow Workflow { get; }
    public EvidenceStore Store { get; }
    public int Calls { get; private set; }
    public int Saves { get; private set; }
    public CancellationToken Token { get; private set; }
    public Func<ScriptEntry, ReadRunContext, IReadOnlyDictionary<string, string?>, CancellationToken, Task<ReadRunResult>>? Transport { get; set; }
    public Func<ExchangeReportEvidence, string>? Save { get; set; }
    public Func<(ConnectedTenant? Connection, TenantProfile? Profile)>? ContextProvider { get; set; }
    public IReadOnlyDictionary<string, string?> Inputs { get; } = new Dictionary<string, string?> { ["IncludeArchive"] = "false" };
    public ReadRunFixture()
    {
        // Independently load the real embedded, pinned library. Each fixture owns its lists for hostile-change cases.
        var assembly = typeof(ScriptCatalogue).Assembly;
        var files = new Dictionary<string, byte[]>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var path = name.Replace('\\', '/'); if (!path.StartsWith("Scripts/", StringComparison.Ordinal)) continue;
            using var stream = assembly.GetManifestResourceStream(name)!; using var bytes = new MemoryStream(); stream.CopyTo(bytes);
            files[path[8..]] = bytes.ToArray();
        }
        Catalogue = ScriptCatalogue.Load(files);
        var session = TestData.Session(mode: SessionMode.Assessment);
        session.Account = "operator@synthetic.example"; session.OperatorUpn = session.Account;
        session.TokenExpiresAt = "2026-10-11T02:00:00Z";
        Connection = new(session, new ForbiddenGraph(session.TenantId, session.Mode), null);
        Store = new(Root.Paths, NullLog.Instance);
        Workflow = new(() => ContextProvider?.Invoke() ?? (Connection, Profile), Catalogue, Clock, async (entry, context, inputs, runtime, progress, token) =>
        {
            Calls++; Token = token; Assert.Equal("7", runtime); Assert.NotSame(Entry.Manifest, entry.Manifest);
            return Transport is null ? Outcome(entry, context, inputs) : await Transport(entry, context, inputs, token);
        }, evidence => { Saves++; return Save?.Invoke(evidence) ?? Store.SaveExchangeReport(evidence); });
        Workflow.SetSelection(Entry.Manifest.Id, "7", Inputs);
    }
    public ReadOnlyRunReview Review()
    { Workflow.SetEnabled(true); return Workflow.PrepareReview(); }
    public ReadRunResult Outcome(ScriptEntry entry, ReadRunContext context, IReadOnlyDictionary<string, string?> inputs,
        string state = ReportReadState.Collected, ReadRunOutcome outcome = ReadRunOutcome.Completed)
    {
        var definition = ScriptCatalogue.ReportFor(entry.Manifest);
        var report = new ExchangeReportEvidence
        {
            Id = Guid.NewGuid().ToString("D"), RunId = Guid.NewGuid().ToString("D"), ReportId = definition.Id,
            TenantId = context.TenantId, InitiatingAccountObjectId = context.AccountObjectId, ObservedAccountUpn = context.AccountUpn,
            Resource = definition.Resource, SourceMode = "live", StartedAt = Timestamps.Format(Clock.UtcNow), EndedAt = Timestamps.Format(Clock.UtcNow.AddSeconds(1)),
            ToolkitVersion = "0.0.0-test", AdapterVersion = ExchangeReportRegistry.AdapterVersion, ModuleVersion = "3.9.0", RuntimeVersion = "7.4.6",
            ScriptSha256 = entry.Manifest.ScriptSha256, ManifestSha256 = entry.ManifestSha256, RunnerTemplateSha256 = entry.Manifest.Execution!.RunnerTemplateSha256,
            Parameters = ScriptReadRequest.Bind(entry, context, inputs, Clock.UtcNow).Select(p => new ExchangeReportParameter { Name = p.Name, Type = p.Type, Value = p.Value }).ToList(),
            SourceCommands = [.. definition.SourceCommands], Limitations = [definition.Limitations], Status = state,
            Sections = [new ReportSection { Id = "mailboxes", Status = state, Error = state == ReportReadState.Collected ? null : "Synthetic incomplete read.", Limitations = ["Synthetic section coverage limitation."], Rows = [] }]
        };
        ExchangeReportEvidenceSchema.Seal(report);
        return new(report.RunId, outcome, "Synthetic safe outcome.", report, false);
    }
    public void Dispose() => Root.Dispose();
    private sealed class ForbiddenGraph(string tenant, SessionMode mode) : IGraphClient
    {
        public string TenantId => tenant; public SessionMode Mode => mode;
        public Task<JsonObject> GetAsync(GraphApi api, string path, CancellationToken ct) => throw new InvalidOperationException("Graph transport is forbidden.");
        public Task<IReadOnlyList<JsonObject>> GetAllAsync(GraphApi api, string path, CancellationToken ct) => throw new InvalidOperationException("Graph transport is forbidden.");
        public Task<JsonObject> WriteReviewedAsync(GraphApi api, GraphWriteMethod method, string path, JsonObject payload,
            BuildStandard.Core.Safety.ReviewedWriteAdmission admission, CancellationToken ct) =>
            throw new BuildStandard.Core.WriteDeniedException("This synthetic read-only adapter does not support reviewed writes.");

        public Task<JsonObject> WriteAsync(GraphApi api, GraphWriteMethod method, string path, JsonObject payload, CancellationToken ct) => throw new InvalidOperationException("Tenant writes are forbidden.");
    }
}

public sealed class ReadOnlyRunWorkflowTests
{
    [Fact]
    public async Task Verified_connection_is_default_off_and_does_not_grant_exact_run_approval()
    {
        using var f = new ReadRunFixture();
        Assert.Single(f.Workflow.Items); Assert.False(f.Workflow.Enabled);
        Assert.Throws<SafetyViolationException>(f.Workflow.PrepareReview);
        var review = f.Review();
        await Assert.ThrowsAsync<SafetyViolationException>(() => f.Workflow.RunAsync(review, false, null, default));
        await Assert.ThrowsAsync<SafetyViolationException>(() => f.Workflow.RunAsync(new(review.Text), true, null, default));
        Assert.Equal(0, f.Calls); Assert.Equal(0, f.Saves);
    }

    [Theory]
    [InlineData("profile-only")][InlineData("no-profile")][InlineData("tenant-unverified")][InlineData("account-unverified")]
    [InlineData("missing-object")][InlineData("operator-mismatch")][InlineData("missing-upn")][InlineData("bad-upn")]
    [InlineData("profile-tenant")][InlineData("graph-tenant")][InlineData("graph-mode")][InlineData("bad-expiry")]
    public void Historical_profile_and_unverified_or_mismatched_connection_never_enable_run(string change)
    {
        using var f = new ReadRunFixture(); var s = f.Connection!.Session;
        switch (change)
        {
            case "profile-only": f.Connection = null; break;
            case "no-profile": f.Profile = null; break;
            case "tenant-unverified": s.TenantVerified = false; break;
            case "account-unverified": s.OperatorVerified = false; break;
            case "missing-object": s.AccountObjectId = ""; break;
            case "operator-mismatch": s.OperatorObjectId = TestData.Emergency; break;
            case "missing-upn": s.OperatorUpn = ""; break;
            case "bad-upn": s.OperatorUpn = "private\ncommand"; break;
            case "profile-tenant": f.Profile!.TenantId = TestData.TenantB; break;
            case "graph-tenant": s.TenantId = TestData.TenantB; f.Profile!.TenantId = s.TenantId; break;
            case "graph-mode": s.Mode = SessionMode.Deployment; break;
            case "bad-expiry": s.TokenExpiresAt = "unknown"; break;
        }
        Assert.False(f.Workflow.CanEnable); Assert.ThrowsAny<ToolkitException>(() => f.Workflow.SetEnabled(true));
        Assert.False(f.Workflow.Enabled); Assert.Equal(0, f.Calls);
    }

    [Fact]
    public async Task Exact_review_runs_once_and_saves_through_the_existing_immutable_store_without_Graph_calls()
    {
        using var f = new ReadRunFixture(); var review = f.Review();
        Assert.Contains(TestData.TenantA, review.Text); Assert.Contains(TestData.Operator, review.Text);
        Assert.Contains("operator@synthetic.example", review.Text); Assert.Contains("Separate Microsoft Exchange sign-in", review.Text);
        Assert.Contains(f.Entry.ManifestSha256, review.Text); Assert.Contains(f.Entry.Manifest.ScriptSha256, review.Text);
        Assert.Contains(f.Entry.Manifest.Execution!.RunnerTemplateSha256, review.Text); Assert.Contains("IncludeArchive (Boolean): false", review.Text);
        Assert.Contains("No module installation, policy change, alternate host, automatic retry", review.Text);
        await f.Workflow.RunAsync(review, true, null, default);
        Assert.Equal(1, f.Calls); Assert.Equal(1, f.Saves); Assert.Null(f.Workflow.Review);
        Assert.Equal(ReadRunOutcome.Completed, f.Workflow.Result!.Outcome); Assert.Contains("Report read status: Collected", f.Workflow.Output);
        var saved = f.Store.LoadExchangeReport(TestData.TenantA, f.Workflow.Result.Evidence!.Id)!;
        Assert.Equal(f.Workflow.EvidenceJson, ExchangeReportEvidenceSchema.Serialize(saved));
        var bytes = File.ReadAllBytes(f.Workflow.SavedFile);
        await Assert.ThrowsAsync<SafetyViolationException>(() => f.Workflow.RunAsync(review, true, null, default));
        Assert.Equal(bytes, File.ReadAllBytes(f.Workflow.SavedFile)); Assert.Equal(1, f.Calls);
        Assert.False(f.Workflow.CanSave); Assert.Throws<SafetyViolationException>(f.Workflow.SaveEvidence);
    }

    [Theory]
    [InlineData("reconnect")][InlineData("disconnect")][InlineData("account")][InlineData("object")][InlineData("upn")]
    [InlineData("scopes")][InlineData("mode")][InlineData("client")][InlineData("profile")][InlineData("unverified")]
    [InlineData("input")][InlineData("runtime")][InlineData("selection")][InlineData("manifest")][InlineData("body")][InlineData("off-on")]
    public async Task Any_changed_reviewed_context_or_selection_refuses_before_transport(string change)
    {
        using var f = new ReadRunFixture(); var review = f.Review(); var s = f.Connection!.Session;
        switch (change)
        {
            case "reconnect": f.Connection = new(s, f.Connection.Graph, null); break;
            case "disconnect": f.Connection = null; break;
            case "account": s.Account = "other@synthetic.example"; break;
            case "object": s.AccountObjectId = TestData.Emergency; s.OperatorObjectId = s.AccountObjectId; break;
            case "upn": s.OperatorUpn = "other@synthetic.example"; break;
            case "scopes": s.Scopes.Add("Group.Read.All"); break;
            case "mode": s.Mode = SessionMode.Deployment; break;
            case "client": s.ClientId = TestData.Emergency; break;
            case "profile": f.Profile!.Company = "Changed synthetic client"; break;
            case "unverified": s.OperatorVerified = false; break;
            case "input": f.Workflow.SetSelection(f.Entry.Manifest.Id, "7", new Dictionary<string, string?> { ["IncludeArchive"] = "true" }); break;
            case "runtime": f.Workflow.SetSelection(f.Entry.Manifest.Id, "5.1", f.Inputs); break;
            case "selection": f.Workflow.SetSelection(f.Catalogue.Entries.First(e => e.Manifest.SchemaVersion == 1).Manifest.Id, "7", f.Inputs); break;
            case "manifest": ((IList<ScriptModule>)f.Entry.Manifest.Modules)[0] = new("ExchangeOnlineManagement", "99.0.0"); break;
            case "body": var entries = (IList<ScriptEntry>)f.Catalogue.Entries; var at = entries.IndexOf(f.Entry); entries[at] = f.Entry with { Script = f.Entry.Script + "\n#changed" }; break;
            case "off-on": f.Workflow.SetEnabled(false); f.Workflow.SetEnabled(true); break;
        }
        await Assert.ThrowsAnyAsync<ToolkitException>(() => f.Workflow.RunAsync(review, true, null, default));
        Assert.Equal(0, f.Calls); Assert.Equal(0, f.Saves); Assert.Null(f.Workflow.Result);
        if (change is "reconnect" or "disconnect" or "account" or "object" or "upn" or "scopes" or "mode" or "client" or "profile" or "unverified") Assert.False(f.Workflow.Enabled);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Direct_or_caller_cancellation_targets_the_owned_run_and_refuses_late_Collected(bool caller)
    {
        using var f = new ReadRunFixture(); using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport = async (entry, context, inputs, _) => { entered.SetResult(); await release.Task; return f.Outcome(entry, context, inputs); };
        var task = f.Workflow.RunAsync(f.Review(), true, null, stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.True(f.Workflow.IsRunning);
            if (caller) stop.Cancel(); else f.Workflow.Cancel();
            Assert.True(f.Token.IsCancellationRequested); Assert.True(f.Workflow.CancellationRequested);
            Assert.False(task.IsCompleted); Assert.False(f.Workflow.CanSave);
        }
        finally { release.TrySetResult(); await task.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.False(f.Workflow.IsRunning); Assert.Equal(1, f.Calls); Assert.Equal(0, f.Saves);
        Assert.Equal(ReadRunOutcome.Cancelled, f.Workflow.Result!.Outcome); Assert.Null(f.Workflow.Result.Evidence);
        Assert.Empty(f.Workflow.EvidenceJson); Assert.Contains("No rows were accepted", f.Workflow.Output);
    }

    [Fact]
    public async Task External_cancellation_callbacks_cannot_leak_private_errors_or_cancel_a_later_run()
    {
        using var f = new ReadRunFixture(); using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport = async (entry, context, inputs, token) =>
        {
            using var registration = token.Register(() => throw new InvalidOperationException("synthetic-private-path credential-secret"));
            entered.SetResult(); await release.Task; return f.Outcome(entry, context, inputs);
        };
        var task = f.Workflow.RunAsync(f.Review(), true, null, stop.Token);
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); stop.Cancel(); }
        finally { release.TrySetResult(); await task.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.Null(f.Workflow.Result); Assert.Equal(ReadOnlyRunWorkflow.FailedReason, f.Workflow.Message); Assert.Equal(0, f.Saves);
        f.Transport = null;
        await f.Workflow.RunAsync(f.Workflow.PrepareReview(), true, null, default);
        f.Workflow.Cancel(); Assert.Equal(ReadRunOutcome.Completed, f.Workflow.Result!.Outcome); Assert.Equal(2, f.Calls);
    }

    [Theory]
    [InlineData("identity")][InlineData("selection")][InlineData("reconnect")]
    public async Task Context_drift_during_the_run_refuses_acceptance_and_save_without_retry(string change)
    {
        using var f = new ReadRunFixture(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport = async (entry, context, inputs, _) => { entered.SetResult(); await release.Task; return f.Outcome(entry, context, inputs); };
        var task = f.Workflow.RunAsync(f.Review(), true, null, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (change == "identity") f.Connection!.Session.OperatorUpn = "other@synthetic.example";
            else if (change == "reconnect") f.Connection = new(f.Connection!.Session, f.Connection.Graph, null);
            else f.Workflow.SetSelection(f.Entry.Manifest.Id, "7", new Dictionary<string, string?> { ["IncludeArchive"] = "true" });
            f.Workflow.RefreshContext();
        }
        finally { release.TrySetResult(); await task.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.Null(f.Workflow.Result); Assert.Empty(f.Workflow.EvidenceJson); Assert.Equal(1, f.Calls); Assert.Equal(0, f.Saves);
    }

    [Theory]
    [InlineData("tenant")][InlineData("object")][InlineData("upn")][InlineData("run")][InlineData("manifest")][InlineData("body")][InlineData("wrapper")][InlineData("parameter")][InlineData("historical")]
    public async Task A_well_sealed_result_for_other_reviewed_values_is_never_accepted(string change)
    {
        using var f = new ReadRunFixture();
        f.Transport = (entry, context, inputs, _) =>
        {
            var outcome = f.Outcome(entry, context, inputs); var r = outcome.Evidence!;
            switch (change)
            {
                case "tenant": r.TenantId = TestData.TenantB; break;
                case "object": r.InitiatingAccountObjectId = TestData.Emergency; break;
                case "upn": r.ObservedAccountUpn = "other@synthetic.example"; break;
                case "run": r.RunId = Guid.NewGuid().ToString("D"); break;
                case "manifest": r.ManifestSha256 = new string('a', 64); break;
                case "body": r.ScriptSha256 = new string('a', 64); break;
                case "wrapper": r.RunnerTemplateSha256 = new string('a', 64); break;
                case "parameter": r.Parameters[0].Value = "true"; break;
                case "historical": r.SourceMode = "historical"; break;
            }
            ExchangeReportEvidenceSchema.Seal(r); return Task.FromResult(outcome);
        };
        await f.Workflow.RunAsync(f.Review(), true, null, default);
        Assert.Null(f.Workflow.Result); Assert.Empty(f.Workflow.EvidenceJson); Assert.Equal(ReadOnlyRunWorkflow.FailedReason, f.Workflow.Message);
        Assert.Equal(1, f.Calls); Assert.Equal(0, f.Saves);
    }

    [Theory]
    [InlineData(ReadRunOutcome.Completed, ReportReadState.Partial)]
    [InlineData(ReadRunOutcome.Cancelled, ReportReadState.Cancelled)]
    [InlineData(ReadRunOutcome.TimedOut, ReportReadState.Partial)]
    public async Task Completed_process_partial_and_cancelled_evidence_keep_exact_read_states_and_limitations(ReadRunOutcome outcome, string state)
    {
        using var f = new ReadRunFixture(); f.Transport = (entry, context, inputs, _) => Task.FromResult(f.Outcome(entry, context, inputs, state, outcome));
        await f.Workflow.RunAsync(f.Review(), true, null, default);
        Assert.Equal(outcome, f.Workflow.Result!.Outcome); Assert.Equal(state, f.Workflow.Result.Evidence!.Status);
        Assert.Equal(1, f.Saves); Assert.Contains("Report read status: " + state, f.Workflow.Output);
        Assert.Contains("Synthetic incomplete read.", f.Workflow.Output); Assert.Contains("Synthetic section coverage limitation.", f.Workflow.Output);
        Assert.Contains("never deployment before-evidence", f.Workflow.Output);
    }

    [Theory]
    [InlineData(ReadRunOutcome.RuntimeUnavailable)][InlineData(ReadRunOutcome.HostRefused)][InlineData(ReadRunOutcome.ModuleMissing)]
    [InlineData(ReadRunOutcome.Refused)][InlineData(ReadRunOutcome.Failed)][InlineData(ReadRunOutcome.Cancelled)][InlineData(ReadRunOutcome.TimedOut)]
    public async Task Unavailable_failed_and_cancelled_outcomes_without_evidence_never_look_like_empty_success(ReadRunOutcome outcome)
    {
        using var f = new ReadRunFixture(); f.Transport = (_, _, _, _) => Task.FromResult(new ReadRunResult(Guid.NewGuid().ToString("D"), outcome, "Synthetic fixed unavailable guidance.", null, true));
        await f.Workflow.RunAsync(f.Review(), true, null, default);
        Assert.Equal(outcome, f.Workflow.Result!.Outcome); Assert.Contains("No verified report evidence or rows were accepted", f.Workflow.Output);
        Assert.Contains("Diagnostics truncated: yes", f.Workflow.Output); Assert.Equal(0, f.Saves); Assert.False(f.Workflow.CanSave);
    }

    [Fact]
    public async Task Private_transport_and_local_save_errors_are_fixed_and_save_retry_never_reruns_or_overwrites_evidence()
    {
        using var f = new ReadRunFixture(); const string privateError = "synthetic-private-path password=do-not-log";
        f.Transport = (_, _, _, _) => throw new InvalidOperationException(privateError);
        await f.Workflow.RunAsync(f.Review(), true, null, default);
        Assert.Equal(ReadOnlyRunWorkflow.FailedReason, f.Workflow.Message); Assert.Empty(f.Workflow.Output); Assert.Null(f.Workflow.Result);
        f.Transport = null; f.Save = _ => throw new IOException(privateError);
        await f.Workflow.RunAsync(f.Workflow.PrepareReview(), true, null, default);
        Assert.Equal(ReadOnlyRunWorkflow.SaveFailedReason, f.Workflow.Message); Assert.True(f.Workflow.CanSave);
        Assert.DoesNotContain(privateError, f.Workflow.Output + f.Workflow.EvidenceJson);
        f.Save = null; f.Workflow.SaveEvidence(); Assert.Equal(2, f.Calls); Assert.Equal(2, f.Saves);
        var json = File.ReadAllText(f.Workflow.SavedFile); Assert.False(f.Workflow.CanSave);
        Assert.Throws<SafetyViolationException>(f.Workflow.SaveEvidence); Assert.Equal(json, File.ReadAllText(f.Workflow.SavedFile));
    }

    [Fact]
    public async Task Existing_immutable_record_save_refusal_preserves_original_bytes_and_never_repeats_the_run()
    {
        using var f = new ReadRunFixture();
        var original = ""; byte[] bytes = [];
        f.Transport = (entry, context, inputs, _) =>
        {
            var result = f.Outcome(entry, context, inputs);
            // Use the actual store to create this exact ID before the workflow's attempted save.
            original = f.Store.SaveExchangeReport(result.Evidence!); bytes = File.ReadAllBytes(original);
            return Task.FromResult(result);
        };
        await f.Workflow.RunAsync(f.Review(), true, null, default);
        Assert.Equal(1, f.Calls); Assert.Equal(1, f.Saves); Assert.Empty(f.Workflow.SavedFile);
        Assert.Equal(ReadOnlyRunWorkflow.SaveFailedReason, f.Workflow.Message); Assert.True(f.Workflow.CanSave);
        Assert.Equal(bytes, File.ReadAllBytes(original)); Assert.NotNull(f.Workflow.Result!.Evidence);
        f.Workflow.SaveEvidence();
        Assert.Equal(1, f.Calls); Assert.Equal(2, f.Saves); Assert.Empty(f.Workflow.SavedFile);
        Assert.Equal(bytes, File.ReadAllBytes(original)); Assert.Equal(ReadOnlyRunWorkflow.SaveFailedReason, f.Workflow.Message);
        Assert.Equal(f.Workflow.Result.Evidence!.IntegrityDigest, f.Store.LoadExchangeReport(TestData.TenantA, f.Workflow.Result.Evidence.Id)!.IntegrityDigest);
    }

    [Fact]
    public async Task Completed_old_caller_registration_is_disposed_before_another_run_and_idle_cancel_is_inert()
    {
        using var f = new ReadRunFixture(); using var oldStop = new CancellationTokenSource();
        await f.Workflow.RunAsync(f.Review(), true, null, oldStop.Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport = async (entry, context, inputs, _) => { entered.SetResult(); await release.Task; return f.Outcome(entry, context, inputs); };
        var next = f.Workflow.RunAsync(f.Workflow.PrepareReview(), true, null, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); oldStop.Cancel(); Assert.False(f.Token.IsCancellationRequested);
            Assert.Null(f.Workflow.Result); Assert.Empty(f.Workflow.Output); Assert.Empty(f.Workflow.EvidenceJson);
        }
        finally { release.TrySetResult(); await next.WaitAsync(TimeSpan.FromSeconds(10)); }
        f.Workflow.Cancel(); Assert.Equal(ReadRunOutcome.Completed, f.Workflow.Result!.Outcome); Assert.Equal(2, f.Calls); Assert.Equal(2, f.Saves);
    }

    [Fact]
    public async Task Pre_cancelled_run_never_calls_the_transport_and_cannot_be_reentered_while_active()
    {
        using var f = new ReadRunFixture(); using var stop = new CancellationTokenSource(); stop.Cancel();
        await f.Workflow.RunAsync(f.Review(), true, null, stop.Token);
        Assert.Equal(0, f.Calls); Assert.Null(f.Workflow.Result); Assert.Empty(f.Workflow.EvidenceJson);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport = async (entry, context, inputs, _) => { entered.SetResult(); await release.Task; return f.Outcome(entry, context, inputs); };
        var review = f.Workflow.PrepareReview(); var active = f.Workflow.RunAsync(review, true, null, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<SafetyViolationException>(() => f.Workflow.RunAsync(review, true, null, default));
            Assert.Equal(1, f.Calls);
        }
        finally { release.TrySetResult(); await active.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Theory]
    [InlineData("copy-only")][InlineData("invalid-bool")][InlineData("unsupported-runtime")]
    public void Copy_items_and_invalid_inputs_cannot_prepare_an_executable_review(string selection)
    {
        using var f = new ReadRunFixture(); f.Workflow.SetEnabled(true);
        if (selection == "copy-only") f.Workflow.SetSelection(f.Catalogue.Entries.First(e => e.Manifest.SchemaVersion == 1).Manifest.Id, "7", new Dictionary<string, string?>());
        if (selection == "invalid-bool") f.Workflow.SetSelection(f.Entry.Manifest.Id, "7", new Dictionary<string, string?> { ["IncludeArchive"] = "Start-Process private" });
        if (selection == "unsupported-runtime") f.Workflow.SetSelection(f.Entry.Manifest.Id, "custom/private/pwsh", f.Inputs);
        Assert.ThrowsAny<ToolkitException>(f.Workflow.PrepareReview); Assert.Equal(0, f.Calls);
    }
}
