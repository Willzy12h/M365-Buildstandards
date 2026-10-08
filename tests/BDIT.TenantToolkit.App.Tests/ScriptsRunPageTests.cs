using System.Text;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Scripts;
using BDIT.TenantToolkit.Graph;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// Run on the Scripts &amp; Reports page (INT-081, proposed): offered only for read-only items, off without a selected
/// client and a connected session for it, never started without the same confirmation Copy uses, pinned to the tenant
/// and account that were confirmed, and kept in the client's run history. The runner is a fake: no process is started.
/// </summary>
public sealed class ScriptsRunPageTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _logger;
    private readonly ShellViewModel _shell;
    private readonly ScriptsViewModel _page;
    private readonly FakePrompts _prompts = new();
    private readonly FakeRunner _runner = new();

    public ScriptsRunPageTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson);
        _root.WriteManifest();
        _logger = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(_root.Paths, new ToolkitSettings(), _logger, diagnostics: true);
        workspace.Initialise();
        if (workspace.Standard is null) throw new InvalidOperationException("The synthetic standard did not load: " + workspace.StandardError);
        _shell = new ShellViewModel(workspace);
        _page = _shell.Page<ScriptsViewModel>();
        _page.Now = () => Now;
        _page.Prompts = _prompts;
        _page.Runner = _runner;
    }

    public void Dispose()
    {
        _logger.Dispose();
        _root.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SelectClient() => _shell.Workspace.ApplyProfileToSession(TestData.Profile(), save: false);

    private void Connect(string tenant = TestData.TenantA, string? account = null)
    {
        var session = TestData.Session(tenant, SessionMode.Assessment);
        if (account is not null) session.Account = account;
        typeof(Workspace).GetProperty(nameof(Workspace.Connection))!.SetValue(_shell.Workspace,
            new ConnectedTenant(session, new FakeGraphClient(TestData.Standard()) { Mode = SessionMode.Assessment }, null));
        _page.Refresh();
    }

    private void OpenInventory() => _page.Selected = _page.Items.Single(i => i.Id == "exo.mailbox-inventory");

    private static ScriptEntry ChangeItem()
    {
        var shipped = ScriptCatalogue.Shipped.Find("exo.mailbox-inventory");
        return shipped with { Manifest = shipped.Manifest with { Id = "exo.synthetic-change", Name = "Synthetic change", Mode = ScriptMode.CopyOnlyChange } };
    }

    [Fact]
    public async Task Run_is_not_offered_for_a_change_item_even_with_a_client_and_a_session()
    {
        SelectClient();
        Connect();
        var change = new ScriptLibraryItem(ChangeItem());
        Assert.Equal("Copy only", change.Actions);
        _page.Selected = change;
        Assert.False(_page.ShowsRun);
        Assert.False(_page.CanRun);
        Assert.False(_page.RunScriptCommand.CanExecute(null));
        Assert.Equal("", _page.RunBlockedText);
        Assert.Throws<ToolkitException>(() => _page.Review(ScriptCopyAction.Run));
        await Assert.ThrowsAsync<ToolkitException>(_page.RunSelectedAsync);
        Assert.Equal(0, _prompts.Confirmations);
        Assert.Equal(0, _runner.Calls);

        OpenInventory();
        Assert.Equal("Run or copy", _page.Selected!.Actions);
        Assert.True(_page.ShowsRun);
        Assert.True(_page.CanRun);
    }

    [Fact]
    public void Run_stays_off_without_a_client_and_without_a_session_for_that_client()
    {
        OpenInventory();
        Assert.True(_page.ShowsRun);
        Assert.False(_page.CanRun);
        Assert.False(_page.RunScriptCommand.CanExecute(null));
        Assert.Contains("Select a client", _page.RunBlockedText);

        SelectClient();
        Assert.False(_page.HasRunSession);
        Assert.False(_page.CanRun);
        Assert.Contains("Connect to this client first", _page.RunBlockedText);
        Assert.Throws<ToolkitException>(() => _page.Review(ScriptCopyAction.Run));

        // A session for another tenant is not a session for this client: Copy still works, Run does not.
        Connect(TestData.TenantB);
        Assert.False(_page.HasRunSession);
        Assert.False(_page.CanRun);
        Assert.True(_page.CanCopy);

        Connect();
        Assert.True(_page.HasRunSession);
        Assert.True(_page.CanRun);
        Assert.True(_page.RunScriptCommand.CanExecute(null));
    }

    [Fact]
    public async Task Nothing_runs_until_the_tenant_account_and_values_are_confirmed()
    {
        SelectClient();
        Connect();
        OpenInventory();
        _prompts.Answer = false;
        await _page.RunSelectedAsync();
        Assert.Equal(1, _prompts.Confirmations);
        Assert.Equal(ScriptCopyAction.Run, _prompts.LastReview!.Action);
        Assert.Equal(0, _runner.Calls);
        Assert.Empty(_page.RunHistory);
        Assert.Contains("Not run", _page.Status);

        var review = _page.Review(ScriptCopyAction.Run);
        Assert.True(review.IsRun);
        Assert.Equal("engineer@test.example", review.Account);
        Assert.Contains("Run this read-only script against this tenant only", review.ApprovalText);
        Assert.Throws<ToolkitException>(() => _page.PrepareRun(review, confirmed: false));
        // A Copy confirmation cannot start a run, and a Run confirmation cannot produce a copied script.
        Assert.Throws<ToolkitException>(() => _page.PrepareRun(_page.Review(ScriptCopyAction.Clipboard), confirmed: true));
        Assert.Throws<ToolkitException>(() => _page.Generate(review, confirmed: true));

        // The review goes stale when a value, the connected account or the session's tenant changes after it was shown.
        _page.Field("MaxRows").Value = "25";
        Assert.Throws<ToolkitException>(() => _page.PrepareRun(review, confirmed: true));
        var fresh = _page.Review(ScriptCopyAction.Run);
        Connect(account: "someone.else@test.example");
        Assert.Throws<ToolkitException>(() => _page.PrepareRun(fresh, confirmed: true));
        var again = _page.Review(ScriptCopyAction.Run);
        Connect(TestData.TenantB);
        Assert.Throws<ToolkitException>(() => _page.PrepareRun(again, confirmed: true));
        Assert.Equal(0, _runner.Calls);
    }

    [Fact]
    public async Task A_confirmed_run_is_pinned_to_the_reviewed_tenant_and_account_and_kept_in_run_history()
    {
        SelectClient();
        Connect();
        OpenInventory();
        _page.Field("MaxRows").Value = "25";
        await _page.RunSelectedAsync();
        Assert.Equal("", _shell.ErrorMessage);
        Assert.Equal(1, _prompts.Confirmations);
        Assert.Equal(1, _runner.Calls);
        var request = _runner.LastRequest!;
        Assert.Equal(TestData.TenantA, request.Target.TenantId);
        Assert.Equal("engineer@test.example", request.Target.Account);
        Assert.Equal("exo.mailbox-inventory", request.Entry.Manifest.Id);
        Assert.Contains(request.Binding.Arguments, a => a.Name == "MaxRows" && a.Value == new ScriptNumber(25));

        var run = Assert.Single(_page.RunHistory);
        Assert.Same(run, _page.SelectedRun);
        Assert.Equal(ReportReadState.Partial, run.Record.Status);
        Assert.StartsWith("Partial · 2 rows", run.StatusText);
        Assert.Contains("BDIT:PARTIAL", run.Detail);
        Assert.Equal(2, run.Preview.Count);
        Assert.Equal(request.Entry.Manifest.OutputSchema.Columns, run.Preview.Table!.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName));
        Assert.StartsWith(_shell.Workspace.Evidence.ScriptRunsDirectory(TestData.TenantA), run.File);
        var stored = _shell.Workspace.Evidence.LoadScriptRun(TestData.TenantA, run.Record.Id)!;
        Assert.Equal(run.Record.IntegrityDigest, stored.IntegrityDigest);
        Assert.Equal("engineer@test.example", stored.Account);
        Assert.Contains(stored.Inputs, i => i.Name == "MaxRows" && i.Value == "25");
        Assert.False(_page.IsRunning);
        Assert.False(_shell.Workspace.Busy);

        Assert.True(_page.ExportRunCsvCommand.CanExecute(null));
        var path = _page.ExportSelectedRunCsv();
        Assert.StartsWith(_root.Paths.ReportsDirectory, path);
        var csv = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("\"DisplayName\",\"PrimarySmtpAddress\"", csv);
        Assert.Contains("\"'=HYPERLINK(1)\"", csv);
        var before = File.ReadAllBytes(path);
        Assert.Throws<ToolkitException>(() => _page.ExportSelectedRunCsv());
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task A_stopped_run_says_so_and_keeps_nothing_to_export()
    {
        SelectClient();
        Connect();
        OpenInventory();
        _runner.End = ScriptRunEnd.Cancelled;
        await _page.RunSelectedAsync();
        var run = Assert.Single(_page.RunHistory);
        Assert.Equal(ReportReadState.Cancelled, run.Record.Status);
        Assert.Equal("Cancelled · no rows kept", run.StatusText);
        Assert.False(run.HasRows);
        Assert.False(_page.ExportRunCsvCommand.CanExecute(null));
        Assert.Throws<ToolkitException>(() => _page.ExportSelectedRunCsv());
        Assert.Contains("Cancelled", _page.RunStatus);
    }

    private sealed class FakePrompts : IScriptCopyPrompts
    {
        public bool Answer { get; set; } = true;
        public int Confirmations { get; private set; }
        public ScriptCopyReview? LastReview { get; private set; }
        public bool Confirm(ScriptCopyReview review) { Confirmations++; LastReview = review; return Answer; }
        public string? ChooseSavePath(string suggestedName) => null;
        public void PutOnClipboard(string text) { }
    }

    /// <summary>Returns a synthetic result for the request it is given. It checks the request the way the real runner does first.</summary>
    private sealed class FakeRunner : IScriptRunner
    {
        public int Calls { get; private set; }
        public ScriptRunRequest? LastRequest { get; private set; }
        public ScriptRunEnd End { get; set; } = ScriptRunEnd.Completed;

        public Task<ScriptRunResult> RunAsync(ScriptRunRequest request, IProgress<string>? progress, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            var wrapper = ScriptRunner.Prepare(request);
            progress?.Report("Synthetic run.");
            var columns = request.Entry.Manifest.OutputSchema.Columns;
            IReadOnlyList<IReadOnlyList<string>> rows = End == ScriptRunEnd.Completed
                ? new[] { columns.Select((_, i) => i == 0 ? "=HYPERLINK(1)" : "v").ToArray(), columns.Select(_ => "w").ToArray() }
                : Array.Empty<IReadOnlyList<string>>();
            var completed = End == ScriptRunEnd.Completed;
            return Task.FromResult(new ScriptRunResult("5.1", ScriptCatalogue.Sha256(Encoding.UTF8.GetBytes(wrapper)), Now, Now.AddSeconds(3), End,
                completed ? 0 : null, columns, rows, false, completed, false,
                completed ? new[] { "BDIT:PARTIAL Synthetic: more mailboxes exist." } : Array.Empty<string>(), Array.Empty<string>(),
                completed ? null : "Cancelled by the engineer. The PowerShell process was stopped; no rows were kept."));
        }
    }
}
