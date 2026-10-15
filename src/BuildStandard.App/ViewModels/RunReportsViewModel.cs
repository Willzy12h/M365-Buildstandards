using System.Collections.ObjectModel;
using System.Data;
using System.Windows.Input;
using BuildStandard.App.Infrastructure;
using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;
using BuildStandard.Engine.Scripts;

namespace BuildStandard.App.ViewModels;

/// <summary>INT-088 slice 4. A separate explicit read-only Run surface; the schema 1 Copy page is unchanged.</summary>
public sealed class RunReportsViewModel : PageViewModel
{
    private ScriptLibraryItem? _selected;
    private string _runtime = "7", _localMessage = "";
    private bool _building, _approved;
    private int _selectedTab;
    private ReadOnlyRunReview? _shownReview;
    private ExchangeReportEvidence? _displayedReport;
    public DataView? OutcomeRows { get; private set; }
    private string _exportPath = "";
    private ExportFormat _exportFormat = ExportFormat.Html;
    public ExportFormat[] ExportFormats { get; } = [ExportFormat.Html,ExportFormat.Json,ExportFormat.Csv,ExportFormat.Xlsx];
    public ExportFormat SelectedExportFormat { get => _exportFormat; set { if (SetProperty(ref _exportFormat,value)) CommandManager.InvalidateRequerySuggested(); } }
    public string LastExport => _exportPath;
    public bool HasReportExport => _exportPath.Length > 0;

    public RunReportsViewModel(ShellViewModel shell) : this(shell, null) { }
    internal RunReportsViewModel(ShellViewModel shell, ReadOnlyRunWorkflow? workflow) : base(shell, "Run reports")
    {
        try
        {
            Workflow = workflow ?? new ReadOnlyRunWorkflow(() => (Workspace.Connection, Workspace.Profile), Workspace.Evidence, () => Workspace.CapturePresentationContext().Generation);
            foreach (var entry in Workflow.Items) Items.Add(new ScriptLibraryItem(entry));
        }
        catch (Exception) { _localMessage = "The pinned script library could not be verified. Run is unavailable; use a complete verified toolkit package."; }
        ExportReportCommand = new AsyncCommand(ExportReportAsync,_ => { _localMessage = "Local export could not be confirmed. An output may exist for the original report; original evidence is unchanged. No run is repeated.";Refresh(); },()=>CanExportReport);
        ReviewCommand = Safe(PrepareReview, () => CanReview);
        RunCommand = new AsyncCommand(RunAsync, _ => { _localMessage = ReadOnlyRunWorkflow.FailedReason; Refresh(); }, () => CanRun);
        CancelCommand = Safe(() => Workflow?.Cancel(), () => Workflow is { IsRunning: true, CancellationRequested: false });
        SaveEvidenceCommand = Safe(() => Workflow?.SaveEvidence(), () => Workspace.Idle && Workflow?.CanSave == true);
        ExportEvidenceCommand = Safe(ExportOriginalEvidence, () => Workspace.Idle && Workflow?.CanSave == true);
        DiscardEvidenceCommand = Safe(DiscardOriginalEvidence, () => Workspace.Idle && Workflow?.CanSave == true);
        CopyOutcomeCommand = Safe(() => PutOnClipboard(Workflow!.Output), () => Workspace.Idle && Workflow is { IsRunning: false, Output.Length: > 0 });
        CopyEvidenceCommand = Safe(() => PutOnClipboard(Workflow!.EvidenceJson), () => Workspace.Idle && Workflow is { IsRunning: false, EvidenceJson.Length: > 0 });
        OpenMailboxCapacityCommand = Safe(() => Shell.Navigate("mailbox-capacity"), () => Workspace.Idle && Workspace.Profile is not null);
        Workspace.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Workspace.Busy)) Refresh(); };
    }

    public ReadOnlyRunWorkflow? Workflow { get; }
    public ObservableCollection<ScriptLibraryItem> Items { get; } = new();
    public ObservableCollection<ScriptFormField> Fields { get; } = new();
    public ObservableCollection<string> Problems { get; } = new();
    public ObservableCollection<string> Runtimes { get; } = new();
    public ICommand ExportReportCommand { get; }
    public ICommand ReviewCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SaveEvidenceCommand { get; }
    public ICommand ExportEvidenceCommand { get; }
    public ICommand DiscardEvidenceCommand { get; }
    public ICommand CopyOutcomeCommand { get; }
    public ICommand CopyEvidenceCommand { get; }
    public ICommand OpenMailboxCapacityCommand { get; }
    internal Action<string> PutOnClipboard { get; set; } = System.Windows.Clipboard.SetText;

    internal Func<string?> ChooseRecoveryPath { get; set; } = () =>
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Export original unsaved report", Filter = "Report evidence (*.json)|*.json", DefaultExt = ".json", AddExtension = true, FileName = "unsaved-report.json" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    };
    internal Func<string, bool> ConfirmDiscard { get; set; } = summary => System.Windows.MessageBox.Show(
        summary + "\n\nDiscard this in-memory report? Any existing saved files remain untouched. This cannot be undone and does not run the report again.",
        "Discard original unsaved report", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
        System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;
    private void ExportOriginalEvidence()
    {
        var id = Workflow!.PendingEvidenceId;
        var path = ChooseRecoveryPath();
        if (path is not null) Workflow.ExportEvidence(path, id);
    }
    private void DiscardOriginalEvidence()
    {
        var id = Workflow!.PendingEvidenceId;
        if (ConfirmDiscard(Workflow.PendingSummary)) Workflow.DiscardEvidence(id, confirmed: true);
    }
    public bool HasPendingEvidence => Workflow?.HasPendingEvidence == true;
    public string PendingSummary => Workflow?.PendingSummary ?? "";

    private RelayCommand Safe(Action work, Func<bool> canExecute) => new(() =>
    {
        if (!canExecute()) return; // RelayCommand itself permits direct Execute when unavailable.
        try { work(); _localMessage = ""; }
        catch (Exception) { _localMessage = "The local action could not complete. Review the current connection and values, or check clipboard/local file access. No run is repeated."; }
        Refresh();
    }, canExecute);

    public ScriptLibraryItem? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            _building = true; Fields.Clear(); Runtimes.Clear();
            if (value is not null)
            {
                foreach (var p in value.Entry.Manifest.Parameters) Fields.Add(new ScriptFormField(p, Revalidate));
                foreach (var runtime in value.Entry.Manifest.SupportedRuntimes) Runtimes.Add(runtime);
                _runtime = Runtimes.Contains("7") ? "7" : Runtimes[0];
            }
            _building = false; OnPropertyChanged(nameof(Runtime)); Revalidate();
        }
    }
    public string Runtime { get => _runtime; set { if (SetProperty(ref _runtime, value ?? "")) Revalidate(); } }
    public bool ExperimentalReadEnabled
    {
        get => Workflow?.Enabled == true;
        set
        {
            try
            {
                if (!Workspace.Idle) throw new SafetyViolationException(ReadOnlyRunWorkflow.ChangedReason);
                Workflow?.SetEnabled(value); _localMessage = "";
            }
            catch (Exception) { _localMessage = "Experimental read-only Run needs an idle current verified connection. Reconnect or wait for the active task."; }
            Refresh();
        }
    }
    public bool CanOptIn => Workspace.Idle && Workflow?.CanEnable == true;
    public bool Approved
    {
        get => _approved;
        set { if (SetProperty(ref _approved, value && _shownReview is not null)) { OnPropertyChanged(nameof(CanRun)); CommandManager.InvalidateRequerySuggested(); } }
    }
    public bool HasReview => _shownReview is not null;
    public int SelectedTab { get => _selectedTab; set => SetProperty(ref _selectedTab, value); }
    public string ReviewText => _shownReview?.Text ?? "Prepare a review to see the exact verified connection, purpose, requirements, typed values and pins. Nothing runs while reviewing.";
    public bool HasSelection => Selected is not null;
    public bool IsValid { get; private set; }
    public bool HasProblems => Problems.Count > 0;
    public string Requirements => Selected is null ? "Choose a registered report. Only registered schema 2 read-only reports support Run."
        : Selected.Description + "\n" + Selected.Needs + "\n" + Selected.Entry.Manifest.Prerequisites + "\n" + Selected.LiveStatus;
    public string ContextText => Workflow?.ContextText ?? "Run unavailable: the pinned library could not be loaded.";
    public string Availability => !Workspace.Idle && Workflow?.IsRunning != true ? "Wait for the active workspace task. This page's Cancel does not stop another task."
        : (Workflow?.Blocker ?? ContextText) + (Workflow?.IsRunning == true ? "" : " Cancel is available only while this page's run is active.");
    public string LocalOutputGuidance => Workflow?.IsRunning == true ? "Copy and Save are unavailable until the owned run ends. Cancel only requests a stop; closing waits for it."
        : HasPendingEvidence ? "Resolve the original unsaved report in the recovery panel: save it to its original client, export to a new local file, or explicitly discard it. Another run is blocked."
        : Workflow?.Result is null ? "No accepted outcome yet. Copy and Save become available after a verified outcome/report; review refusal and unavailable guidance above."
        : Workflow.EvidenceJson.Length == 0 ? "Copy outcome is available. There is no accepted report evidence to copy or save."
        : Workflow.SavedFile.Length == 0 ? "The original report was exported separately; it has not been admitted to the client evidence store. Copy outcome or evidence deliberately."
        : "Evidence was saved once locally. Save is disabled to preserve that immutable record. Copy outcome or sensitive evidence deliberately; no upload is performed.";
    public string Message => _localMessage.Length > 0 ? _localMessage : Workflow?.Message ?? ContextText;
    public string MailboxReviewGuidance => !Workspace.Idle ? "Wait for the active task to finish before opening mailbox capacity."
        : Workspace.Profile is null ? "Select a client in Connect before reviewing its mailbox evidence."
        : "Open Mailbox capacity to review a saved or supplied mailbox inventory alongside a users/licences report. Collect licences separately in Tenant reports if needed. Refresh the saved lists and explicitly select both source records; opening the page does not collect or choose evidence.";
    public string Output => Workflow?.Output ?? "";
    public bool CanReview => Workspace.Idle && HasSelection && IsValid && Workflow is { Enabled: true, IsRunning: false, HasPendingEvidence: false };
    public bool CanRun => CanReview && Approved && _shownReview is not null && ReferenceEquals(_shownReview, Workflow?.Review);

    public void Revalidate()
    {
        if (_building) return;
        Problems.Clear();
        var values = Fields.ToDictionary(f => f.Name, f => f.Raw, StringComparer.OrdinalIgnoreCase);
        if (Selected is null) IsValid = false;
        else
        {
            var binding = ScriptInputs.Bind(Selected.Entry.Manifest, values, SystemClock.Instance.UtcNow);
            IsValid = binding.IsValid;
            foreach (var problem in binding.Problems) Problems.Add(problem);
            foreach (var field in Fields) field.Problem = string.Join(" ", binding.Problems.Where(field.Owns));
        }
        Workflow?.SetSelection(Selected?.Id, Runtime, values);
        _localMessage = ""; Refresh();
    }
    public void PrepareReview()
    {
        if (!CanReview) throw new SafetyViolationException(ReadOnlyRunWorkflow.ClosedReason);
        _shownReview = Workflow!.PrepareReview(); _approved = false; _localMessage = ""; SelectedTab = 1; Refresh();
    }
    public async Task RunAsync()
    {
        try
        {
            if (!CanRun) throw new SafetyViolationException(ReadOnlyRunWorkflow.ChangedReason);
            var review = _shownReview!; var approved = Approved;
            SelectedTab = 2;
            await Workspace.RunExclusiveAsync("Running reviewed read-only Exchange report", async progress =>
            {
                var task = Workflow!.RunAsync(review, approved, progress, Workspace.OperationToken);
                Refresh(); await task;
            });
            _localMessage = "";
        }
        catch (Exception) { _localMessage = "Run was refused or could not be confirmed. Review the current verified connection, opt-in and exact values again. No automatic retry was performed."; }
        Refresh();
    }
    private bool CanExportReport => Workspace.Idle && ExportFormats.Contains(SelectedExportFormat)
        && Workflow is { IsRunning:false, HasPendingEvidence:false, Result.Evidence:not null, EvidenceJson.Length: > 0 };
    public async Task ExportReportAsync()
    {
        Workflow?.RefreshContext();
        if (!CanExportReport) throw new SafetyViolationException("Save or export the original evidence first and use its current client context before exporting a formatted report.");
        var context=Workspace.CapturePresentationContext();var json=Workflow!.EvidenceJson;
        var tenant=Workspace.Profile?.TenantId ?? throw new SafetyViolationException("Select the original client.");
        var format=SelectedExportFormat;var file="";
        await Workspace.RunExclusiveAsync("Exporting original read-only report",async _ =>
        {
            file=await Task.Run(()=>Workspace.Exporter.ExportExchangeReport(ExchangeReportEvidenceSchema.Read(json,tenant),format),Workspace.OperationToken);
        });
        if (!Workspace.IsCurrent(context) || Workflow.EvidenceJson != json)
            throw new SafetyViolationException("The context changed during local export. An output may exist for the original report; it is not attributed to the current client.");
        _exportPath=file;_localMessage="Original report exported locally; no new read or tenant change was made.";
        OnPropertyChanged(nameof(LastExport));OnPropertyChanged(nameof(HasReportExport));Refresh();
    }
    private void RefreshRows()
    {
        var report = Workflow?.Result?.Evidence;
        if (ReferenceEquals(report, _displayedReport)) return;
        _displayedReport = report; OutcomeRows = null; _exportPath = "";
        if (report is not null)
        {
            var sheet = RegisteredReportDocuments.Sheets(report).Single(s => s.Name != "Report provenance");
            var table = new DataTable();
            foreach (var column in sheet.Rows[0]) table.Columns.Add(column, typeof(string));
            table.BeginLoadData();
            foreach (var row in sheet.Rows.Skip(1)) table.Rows.Add(row.Cast<object>().ToArray());
            table.EndLoadData(); OutcomeRows = table.DefaultView;
        }
        OnPropertyChanged(nameof(OutcomeRows));OnPropertyChanged(nameof(LastExport));OnPropertyChanged(nameof(HasReportExport));
    }
    public override void Refresh()
    {
        Workflow?.RefreshContext();
        RefreshRows();
        if (!ReferenceEquals(_shownReview, Workflow?.Review)) { _shownReview = null; _approved = false; }
        foreach (var name in new[] { nameof(ExperimentalReadEnabled), nameof(CanOptIn), nameof(Approved), nameof(HasReview), nameof(ReviewText),
            nameof(HasSelection), nameof(IsValid), nameof(HasProblems), nameof(Requirements), nameof(ContextText), nameof(Availability),
            nameof(HasPendingEvidence), nameof(PendingSummary), nameof(LocalOutputGuidance), nameof(MailboxReviewGuidance), nameof(Message), nameof(Output), nameof(CanReview), nameof(CanRun) }) OnPropertyChanged(name);
        CommandManager.InvalidateRequerySuggested();
    }
}
