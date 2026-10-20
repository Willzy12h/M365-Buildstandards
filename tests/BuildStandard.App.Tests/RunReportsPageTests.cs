using BuildStandard.Core;
using System.IO;
using System.Reflection;
using BuildStandard.App.Infrastructure;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;

namespace BuildStandard.Tests;

/// <summary>Actual WPF assembly commands and Workspace shutdown. Authored/crosscompiled only until Windows executes them.</summary>
public sealed class RunReportsPageTests : IDisposable
{
    private readonly ReadRunFixture _f = new();
    private readonly ToolkitLogger _logger;
    private readonly Workspace _workspace;
    private readonly ShellViewModel _shell;
    private readonly RunReportsViewModel _page;
    public RunReportsPageTests()
    {
        _f.Root.WriteStandard("test.json", TestData.StandardJson); _f.Root.WriteManifest();
        _logger = new(_f.Root.Paths.LogsDirectory, LogLevel.Debug);
        _workspace = new(_f.Root.Paths, new ToolkitSettings(), _logger, diagnostics: true); _workspace.Initialise();
        _workspace.ApplyProfileToSession(_f.Profile!, save: false);
        typeof(Workspace).GetProperty(nameof(Workspace.Connection), BindingFlags.Instance | BindingFlags.Public)!.SetValue(_workspace, _f.Connection);
        _f.ContextProvider = () => (_workspace.Connection, _workspace.Profile);
        _shell = new(_workspace); _page = new(_shell, _f.Workflow);
        _page.Selected = _page.Items.Single(i => i.Id == "exo.mailbox-size-quota");
    }
    private void Approve()
    { _page.ExperimentalReadEnabled = true; _page.ReviewCommand.Execute(null); Assert.True(_page.HasReview); Assert.False(_page.Approved); _page.Approved = true; }
    private static async Task Finish(AsyncCommand command)
    { using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10)); while (command.IsRunning) await Task.Delay(10, stop.Token); }

    [Fact]
    public async Task Multiple_registered_sections_do_not_drop_target_diagnostics_or_crash_the_outcome()
    {
        _page.Selected = _page.Items.Single(item => item.Id == "exo.inbox-rule-observations");
        _page.Fields.Single(field => field.Name == "MailboxGuids").Value = TestData.Office;
        Approve(); await _page.RunAsync();
        Assert.NotNull(_page.OutcomeRows);
        Assert.Single(_page.OutcomeRows.Cast<System.Data.DataRowView>());
        Assert.Equal(1, _f.Calls); Assert.NotNull(_f.Workflow.Result!.Evidence);
    }

    [Fact]
    public async Task Formatted_report_export_reuses_original_evidence_and_clears_on_disconnect()
    {
        Approve();await _page.RunAsync();var original=_f.Workflow.EvidenceJson;
        _page.SelectedExportFormat=BuildStandard.Engine.Reports.ExportFormat.Json;await _page.ExportReportAsync();
        Assert.True(File.Exists(_page.LastExport));Assert.Equal(original,File.ReadAllText(_page.LastExport));Assert.Equal(1,_f.Calls);
        typeof(Workspace).GetProperty(nameof(Workspace.Connection))!.SetValue(_workspace,null);_page.Refresh();
        Assert.Empty(_page.LastExport);Assert.False(_page.ExportReportCommand.CanExecute(null));
        await Assert.ThrowsAsync<SafetyViolationException>(_page.ExportReportAsync);Assert.Equal(1,_f.Calls);
    }

    [Fact]
    public async Task Stop_at_local_export_start_never_claims_success_without_a_file()
    {
        Approve();await _page.RunAsync();
        _workspace.PropertyChanged += (_,e) =>
        {
            if(e.PropertyName==nameof(Workspace.BusyMessage) && _workspace.BusyMessage=="Exporting original read-only report")
                _workspace.CancelOperation();
        };
        await _page.ExportReportAsync();
        Assert.True(File.Exists(_page.LastExport));
        Assert.Equal(1,_f.Calls);
    }

    [Fact]
    public async Task Unsaved_report_blocks_another_review_and_keeps_offline_save_available_after_disconnect()
    {
        _f.Save = _ => throw new IOException("Synthetic save failure");
        Approve(); await _page.RunAsync();
        Assert.True(_page.SaveEvidenceCommand.CanExecute(null));
        Assert.NotNull(_page.OutcomeRows); var rows = _page.OutcomeRows; _page.Refresh(); Assert.Same(rows,_page.OutcomeRows);
        Assert.False(_page.CanReview);
        typeof(Workspace).GetProperty(nameof(Workspace.Connection), BindingFlags.Instance | BindingFlags.Public)!.SetValue(_workspace, null);
        _page.Refresh();
        Assert.True(_page.SaveEvidenceCommand.CanExecute(null)); Assert.Empty(_page.Output); Assert.Null(_page.OutcomeRows);
        Assert.False(_page.CanRun); Assert.False(_page.CanReview); Assert.Equal(1, _f.Calls);
    }

    [Fact]
    public async Task Recovery_commands_require_a_deliberate_choice_and_export_original_bytes_offline()
    {
        _f.Save = _ => throw new IOException("Synthetic save failure");
        Approve(); await _page.RunAsync(); var original = _f.Workflow.EvidenceJson;
        typeof(Workspace).GetProperty(nameof(Workspace.Connection))!.SetValue(_workspace, null); _page.Refresh();
        Assert.True(_page.HasPendingEvidence); Assert.Contains(TestData.TenantA, _page.PendingSummary);
        _page.ChooseRecoveryPath = () => null; _page.ExportEvidenceCommand.Execute(null);
        _page.ConfirmDiscard = _ => false; _page.DiscardEvidenceCommand.Execute(null);
        Assert.True(_page.HasPendingEvidence); Assert.Equal(1, _f.Calls);
        var file = Path.Combine(_f.Root.Paths.LogsDirectory, "original-report.json");
        _page.ChooseRecoveryPath = () => file; _page.ExportEvidenceCommand.Execute(null);
        Assert.Equal(original, File.ReadAllText(file)); Assert.False(_page.HasPendingEvidence);
        Assert.False(_page.ExportEvidenceCommand.CanExecute(null)); Assert.False(_page.DiscardEvidenceCommand.CanExecute(null));
        Assert.Empty(_page.Output); Assert.Equal(1, _f.Calls);
    }

    [Fact]
    public async Task Pending_report_close_guard_routes_to_recovery_until_explicit_discard()
    {
        _f.Save = _ => throw new IOException("Synthetic save failure"); Approve(); await _page.RunAsync();
        var pages = (IDictionary<string, PageViewModel>)typeof(ShellViewModel).GetField("_pages", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_shell)!;
        pages["run-reports"] = _page; _shell.Navigate("home");
        var guard = typeof(BuildStandard.App.MainWindow).GetMethod("ShowPendingReport", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True((bool)guard.Invoke(null, [_shell])!); Assert.Same(_page, _shell.CurrentPage);
        Assert.False(_workspace.ShutdownComplete); Assert.Contains("unsaved", _shell.ErrorMessage);
        _page.ConfirmDiscard = _ => true; _page.DiscardEvidenceCommand.Execute(null);
        Assert.False((bool)guard.Invoke(null, [_shell])!); Assert.Equal(1, _f.Calls);
    }

    [Fact]
    public async Task Mailbox_review_shortcut_does_not_run_select_sources_or_invalidate_exact_review()
    {
        Approve(); var review = _f.Workflow.Review; var connection = _workspace.Connection;
        _page.OpenMailboxCapacityCommand.Execute(null);
        var capacity = Assert.IsType<MailboxCapacityViewModel>(_shell.CurrentPage);
        Assert.Null(capacity.SelectedMailbox); Assert.Null(capacity.SelectedLicence); Assert.Empty(capacity.Rows);
        Assert.Same(review, _f.Workflow.Review); Assert.True(_page.Approved); Assert.True(_page.HasReview);
        Assert.Equal(0, _f.Calls); Assert.Null(_f.Workflow.Result);
        await _workspace.RunExclusiveAsync("synthetic active task", _ =>
        {
            _shell.Navigate("home"); var current = _shell.CurrentPage;
            Assert.False(_page.OpenMailboxCapacityCommand.CanExecute(null));
            _page.OpenMailboxCapacityCommand.Execute(null); Assert.Same(current, _shell.CurrentPage);
            return Task.CompletedTask;
        });
        Assert.Same(connection, _workspace.Connection); Assert.Null(_workspace.Snapshot); Assert.Null(_workspace.Plan);
        Assert.False(_workspace.ExperimentalChangesEnabled); Assert.Empty(_shell.ErrorMessage);
    }

    [Fact]
    public async Task Actual_commands_are_default_off_then_run_once_exclusively_without_write_opt_in_or_capture_plan_mutation()
    {
        var standard = _workspace.Standard; var profile = _workspace.Profile; var connection = _workspace.Connection;
        Assert.False(_page.ExperimentalReadEnabled); Assert.False(_page.RunCommand.CanExecute(null));
        Assert.False(_page.CancelCommand.CanExecute(null)); Assert.False(_page.CopyOutcomeCommand.CanExecute(null));
        _page.RunCommand.Execute(null); await Finish(Assert.IsType<AsyncCommand>(_page.RunCommand)); Assert.Equal(0, _f.Calls);
        Approve(); Assert.True(_page.RunCommand.CanExecute(null)); Assert.False(_workspace.ExperimentalChangesEnabled);
        _f.Transport = (entry, context, inputs, token) =>
        {
            Assert.True(_workspace.Busy); Assert.True(token.CanBeCanceled); Assert.False(_workspace.ExperimentalChangesEnabled);
            return Task.FromResult(_f.Outcome(entry, context, inputs));
        };
        _page.RunCommand.Execute(null); await Finish(Assert.IsType<AsyncCommand>(_page.RunCommand));
        Assert.Equal(1, _f.Calls); Assert.True(_workspace.Idle); Assert.False(_page.HasReview); Assert.False(_page.Approved);
        Assert.True(_page.CopyOutcomeCommand.CanExecute(null)); Assert.True(_page.CopyEvidenceCommand.CanExecute(null)); Assert.False(_page.SaveEvidenceCommand.CanExecute(null));
        Assert.Contains("immutable record", _page.LocalOutputGuidance); Assert.Same(standard, _workspace.Standard); Assert.Same(profile, _workspace.Profile); Assert.Same(connection, _workspace.Connection);
        Assert.Null(_workspace.Snapshot); Assert.Null(_workspace.Plan); Assert.Null(_workspace.AcknowledgedSnapshotId);
        var copied = ""; _page.PutOnClipboard = text => copied = text;
        _page.CopyOutcomeCommand.Execute(null); Assert.Equal(_f.Workflow.Output, copied);
        _page.CopyEvidenceCommand.Execute(null); Assert.Equal(_f.Workflow.EvidenceJson, copied);
        Assert.Empty(_shell.ErrorMessage);
    }

    [Fact]
    public void Real_shared_typed_field_changes_invalidate_exact_approval_without_enabling_tenant_writes()
    {
        Approve(); Assert.True(_page.CanRun);
        var field = Assert.Single(_page.Fields); Assert.IsType<ScriptFormField>(field); Assert.True(field.IsFlag);
        field.IsChecked = !field.IsChecked;
        Assert.False(_page.HasReview); Assert.False(_page.Approved); Assert.False(_page.CanRun); Assert.True(_page.ExperimentalReadEnabled);
        _page.ReviewCommand.Execute(null); Assert.True(_page.HasReview); Assert.Contains("IncludeArchive (Boolean): true", _page.ReviewText);
        _page.Approved = true; _workspace.Session!.OperatorVerified = false; _page.Refresh();
        Assert.False(_page.ExperimentalReadEnabled); Assert.False(_page.Approved); Assert.False(_page.RunCommand.CanExecute(null)); Assert.Equal(0, _f.Calls);
    }

    [Fact]
    public async Task Disabled_direct_run_and_idle_cancel_do_not_affect_an_unrelated_workspace_operation()
    {
        Approve(); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var otherToken = CancellationToken.None;
        var other = _workspace.RunExclusiveAsync("Synthetic other task", _ => { otherToken = _workspace.OperationToken; return release.Task; });
        try
        {
            Assert.False(_page.RunCommand.CanExecute(null)); Assert.False(_page.CancelCommand.CanExecute(null)); Assert.Contains("Wait for the active workspace task", _page.Availability);
            _page.CancelCommand.Execute(null); _page.RunCommand.Execute(null); await Finish(Assert.IsType<AsyncCommand>(_page.RunCommand));
            Assert.False(otherToken.IsCancellationRequested); Assert.Equal(0, _f.Calls);
        }
        finally { release.TrySetResult(); await other.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Own_cancel_and_actual_shutdown_wait_for_the_owned_runner_without_accepting_late_rows(bool shutdown)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _f.Transport = async (entry, context, inputs, _) => { entered.SetResult(); await release.Task; return _f.Outcome(entry, context, inputs); };
        Approve(); var command = Assert.IsType<AsyncCommand>(_page.RunCommand); command.Execute(null); Task? closing = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.True(_workspace.Busy); Assert.True(_page.CancelCommand.CanExecute(null));
            if (shutdown) closing = _workspace.ShutdownAsync(); else _page.CancelCommand.Execute(null);
            Assert.True(_f.Token.IsCancellationRequested); Assert.False(command.CanExecute(null)); Assert.False(_page.CopyEvidenceCommand.CanExecute(null));
            if (closing is not null) Assert.False(closing.IsCompleted);
        }
        finally { release.TrySetResult(); await Finish(command); if (closing is not null) await closing.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.Equal(1, _f.Calls); Assert.Equal(0, _f.Saves); Assert.True(_workspace.Idle);
        Assert.True(_f.Workflow.Result is null || _f.Workflow.Result.Outcome == ReadRunOutcome.Cancelled);
        if (shutdown) { Assert.True(_workspace.ShutdownComplete); Assert.Null(_workspace.Connection); }
    }

    [Fact]
    public async Task Private_runner_storage_and_clipboard_errors_never_reach_the_general_shell_or_logger()
    {
        const string secret = "synthetic-private-host private-user password=private";
        _f.Transport = (_, _, _, _) => throw new InvalidOperationException(secret);
        Approve(); _page.RunCommand.Execute(null); await Finish(Assert.IsType<AsyncCommand>(_page.RunCommand));
        Assert.Contains(ReadOnlyRunWorkflow.FailedReason, _page.Message); Assert.Empty(_shell.ErrorMessage);
        _f.Transport = null; _f.Save = _ => throw new IOException(secret);
        _page.ReviewCommand.Execute(null); _page.Approved = true; _page.RunCommand.Execute(null); await Finish(Assert.IsType<AsyncCommand>(_page.RunCommand));
        Assert.True(_page.SaveEvidenceCommand.CanExecute(null)); _page.SaveEvidenceCommand.Execute(null);
        _page.PutOnClipboard = _ => throw new InvalidOperationException(secret); _page.CopyOutcomeCommand.Execute(null);
        Assert.DoesNotContain(secret, _page.Message + _page.Output + _shell.ErrorMessage);
        _logger.Flush();
        // The synchronous logger keeps its write handle open. A Windows reader must permit that access too.
        using var stream = new FileStream(Assert.IsType<string>(_logger.FilePath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        Assert.DoesNotContain(secret, reader.ReadToEnd());
    }
    public void Dispose() { _logger.Dispose(); _f.Dispose(); }
}
