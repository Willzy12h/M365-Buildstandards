using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text;
using System.Windows.Input;
using Microsoft.Win32;
using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Standards.Authoring;
namespace BuildStandard.App.ViewModels;

/// <summary>Native local authoring controls. All catalogue rules and output validation remain in the engine.</summary>
public sealed class CandidateAuthoringViewModel : PageViewModel
{
    private readonly CandidateAuthoringWorkflow _workflow;
    private readonly CandidateBaselineLoader _baselines;
    private readonly CandidateAuthoringService _authoring;
    private readonly CandidateEditingService _editing;
    private readonly CandidateLocalOutputs _outputs;
    private bool _expanded;
    private string _reviewDigest = "", _reviewText = "";
    private string _importName = "", _mappingFile = "";
    private ControlDefinition? _control;
    private CandidateEditableField? _field;
    private string _value = "", _initialValue = "", _title = "Proposed build standard", _message = "No local draft opened.";
    public CandidateAuthoringViewModel(ShellViewModel shell) : base(shell, "Local candidate authoring")
    {
        _workflow = new(Workspace.CapturePresentationContext);
        Workspace.RegisterUnsavedStandardGuard(() => HasUnsavedChanges);
        PropertyChanged += (_, _) => Workspace.NotifyStandardReplacementChanged();
        _baselines = new(Workspace.Paths, Workspace.Logger); _authoring = new(_baselines);
        _editing = new(_baselines, _authoring); _outputs = new(Workspace.Paths, _authoring);
        NewDraftCommand = Command(CreateAsync, () => Workspace.Idle && Workspace.Standard is not null && !HasUnsavedChanges);
        OpenDraftCommand = Command(async () =>
        {
            RequireReplaceable();
            var dialog = new OpenFileDialog { Filter = "Candidate draft (*.json)|*.json", Title = "Open a local candidate draft" };
            if (dialog.ShowDialog() == true) await OpenAsync(dialog.FileName);
        }, () => Workspace.Idle && !HasUnsavedChanges);
        ImportPolicyCommand = Command(async () =>
        {
            RequireEditable();
            var dialog = new OpenFileDialog { Filter = "Policy export (*.json)|*.json", Title = "Inspect a supported policy for this candidate control" };
            if (dialog.ShowDialog() == true) await ImportPolicyAsync(dialog.FileName, ImportName, MappingFile);
        }, () => CanEdit && SelectedControl is not null && !HasPendingInput && !string.IsNullOrWhiteSpace(ImportName));
        ChooseMappingCommand = Sync(() =>
        {
            var dialog = new OpenFileDialog { Filter = "Parameter mapping (*.json)|*.json", Title = "Choose explicit mappings to existing typed parameters" };
            if (dialog.ShowDialog() == true) MappingFile = dialog.FileName;
        }, () => Workspace.Idle);
        LoadFieldsCommand = Command(LoadFieldsAsync, () => CanEdit && SelectedControl is not null && !HasPendingInput);
        ApplyCommand = Command(ApplyAsync, () => CanEdit && SelectedField is not null && HasPendingInput);
        ResetInputCommand = Sync(() => { ValueText = _initialValue; }, () => Workspace.Idle && HasPendingInput);
        ValidateCommand = Command(ValidateAsync, () => CanEdit && !HasPendingInput);
        SaveDraftCommand = Command(SaveAsync, () => Workspace.Idle && _workflow.HasDraft && !HasPendingInput);
        ExportCandidateCommand = Command(ExportAsync, () => CanEdit && !HasPendingInput && _workflow.Validation is not null);
        DiscardDraftCommand = Sync(Discard, () => Workspace.Idle && (_workflow.HasDraft || HasPendingInput));
    }
    public ICommand ImportPolicyCommand { get; }
    public ICommand ChooseMappingCommand { get; }
    public string ImportName { get => _importName; set => SetProperty(ref _importName, value); }
    public string MappingFile { get => _mappingFile; set => SetProperty(ref _mappingFile, value); }
    public ICommand NewDraftCommand { get; }
    public ICommand OpenDraftCommand { get; }
    public ICommand LoadFieldsCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand ResetInputCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand SaveDraftCommand { get; }
    public ICommand ExportCandidateCommand { get; }
    public ICommand DiscardDraftCommand { get; }
    public ObservableCollection<ControlDefinition> Controls { get; } = new();
    public ObservableCollection<CandidateEditableField> Fields { get; } = new();
    public bool IsExpanded { get => _expanded; set => SetProperty(ref _expanded, value); }
    public string DraftTitle { get => _title; set => SetProperty(ref _title, value); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string ReviewText => !_workflow.HasDraft ? "No local draft to review."
        : !_workflow.ContextCurrent ? "Working context changed. Save and reopen this draft before reviewing it against the current context."
        : _reviewText;
    public bool HasPendingInput => _value != _initialValue;
    public bool HasUnsavedChanges => HasPendingInput || _workflow.HasUnsavedChanges;
    public bool CanEdit => Workspace.Idle && _workflow.ContextCurrent;
    public string Guidance => Workspace.Busy ? "Wait for the current operation, or use Stop."
        : HasPendingInput ? "Apply the proposed value, or reset it, before selecting another setting, saving or exporting."
        : !_workflow.HasDraft ? "Create a draft from the loaded verified catalogue, or open a saved draft. This is entirely offline."
        : !_workflow.ContextCurrent ? "The working context changed. Save this draft, then reopen it deliberately; comparison and validation have been cleared."
        : _workflow.UnresolvedCount > 0 ? "This import has unresolved foreign references. Save it for review; validation and export cannot succeed. Prepare explicit mappings and import from an unedited draft. Published standards and assignments remain unchanged."
        : _workflow.Validation is null ? "Edit existing settings, review the comparison, then Validate. Export stays unavailable until validation succeeds. Save preserves a draft; it does not publish or deploy it."
        : "Validated local candidate. Export writes separate catalogue, draft, comparison and checksums. Publication and deployment remain separate reviewed workflows.";
    public string Identity => _workflow.Baseline is { } baseline
        ? $"{_workflow.DraftTitle}\nBaseline {baseline.Release} · SHA-256 {baseline.FileSha256}\nDraft SHA-256 {_workflow.DraftDigest}\n{(HasUnsavedChanges ? "Unsaved local edits" : "Draft saved locally")} · not a published standard"
        : "No draft. Published standards are never edited by these controls.";
    public ControlDefinition? SelectedControl
    {
        get => _control;
        set
        {
            if (HasPendingInput) { Message = "Apply or reset the current input before changing controls."; OnPropertyChanged(); return; }
            if (SetProperty(ref _control, value)) { Fields.Clear(); SetField(null); ImportName = value?.Payload?["displayName"]?.ToString() ?? value?.Name ?? ""; MappingFile = ""; }
        }
    }
    public CandidateEditableField? SelectedField
    {
        get => _field;
        set
        {
            if (HasPendingInput) { Message = "Apply or reset the current input before changing settings."; OnPropertyChanged(); return; }
            SetField(value);
        }
    }
    public string ValueText { get => _value; set { if (SetProperty(ref _value, value)) RaiseAll(); } }
    public string OriginalValue => _field?.OriginalJson ?? "Select a setting to compare its baseline and proposed values.";
    public string ValueHint => _field?.ValueType == "String" ? "Enter text without JSON quotes."
        : "Enter one JSON value of the existing type (for example true, false, 30 or the complete existing array). Unsupported shapes are refused.";
    public async Task CreateAsync()
    {
        RequireReplaceable(); var file = Workspace.RequireStandard().SourceFileName; var title = DraftTitle;
        await LocalAsync("Opening local candidate baseline", async ct =>
        { await _workflow.OpenAsync(_ => _baselines.Create(file, title), ct); await LoadControlsAsync(ct); });
        Message = "Local draft created. Select a control, then Show settings. Nothing has been changed in the published catalogue.";
    }
    public async Task OpenAsync(string file)
    {
        RequireReplaceable();
        await LocalAsync("Opening local candidate draft", async ct =>
        {
            await _workflow.OpenAsync(_ => { var draft = CandidateDraftCodec.ReadFile(file); _baselines.Load(draft); return draft; }, ct);
            await LoadControlsAsync(ct);
        });
        _workflow.MarkSaved(_workflow.DraftDigest);
        Message = "Draft opened against its exact verified baseline. Validate again before export."; RaiseAll();
    }
    public async Task LoadFieldsAsync()
    {
        RequireEditable(); var control = SelectedControl?.Id ?? throw new ConfigurationException("Select a control first.");
        var draft = _workflow.Draft!; var selectedPath = SelectedField?.Path;
        await LocalAsync("Reading local candidate settings", async ct =>
        {
            var fields = await Task.Run(() => _editing.Fields(draft, control), ct);
            ct.ThrowIfCancellationRequested();
            if (!_workflow.ContextCurrent || SelectedControl?.Id != control) throw new ConfigurationException("The selection changed; show its settings again.");
            Fields.Clear(); foreach (var field in fields) Fields.Add(field); SetField(Fields.FirstOrDefault(f => f.Path == selectedPath) ?? Fields.FirstOrDefault());
        });
    }
    public async Task ApplyAsync()
    {
        RequireEditable(allowPending: true); var control = SelectedControl?.Id ?? throw new ConfigurationException("Select a control.");
        var field = SelectedField ?? throw new ConfigurationException("Select a field.");
        var json = field.ValueType == "String" ? JsonSerializer.Serialize(ValueText) : ValueText;
        await LocalAsync("Validating proposed local setting", ct => _workflow.EditAsync((draft, _) => _editing.Set(draft, control, field.Path, json), ct));
        _initialValue = _value; await LoadFieldsAsync();
        Message = "Proposed setting applied to this draft only. Validate the complete draft before export.";
    }
    public async Task ImportPolicyAsync(string file, string name, string? mappingFile = null)
    {
        RequireEditable();
        var control = SelectedControl?.Id ?? throw new ConfigurationException("Select the existing control to import into.");
        await LocalAsync("Inspecting local policy import", ct => _workflow.EditAsync((draft, token) =>
        {
            token.ThrowIfCancellationRequested();
            var mappings = string.IsNullOrWhiteSpace(mappingFile) ? null : CandidatePolicyImportService.ReadParameterMappings(mappingFile);
            return new CandidatePolicyImportService(_baselines, _authoring).ImportFile(draft, control, file, name, mappings);
        }, ct));
        Fields.Clear(); SetField(null);
        var result = _workflow.Draft!;
        Message = $"Policy imported into local draft for {control}. {result.Unresolved.Count} unresolved reference(s). Inspect the retained changes, source and assignment review below, then validate. Nothing was assigned or published.";
        RaiseAll();
    }
    public async Task ValidateAsync()
    {
        RequireEditable();
        await LocalAsync("Validating local candidate", ct => _workflow.ValidateAsync((draft, _) => CandidateValidationTicket.Create(_authoring, draft), ct));
        Message = "Candidate validated locally. No publication, tenant action or live behaviour is implied.";
    }
    public async Task SaveAsync()
    {
        if (HasPendingInput) throw new ConfigurationException("Apply or reset the pending input before saving.");
        var draft = _workflow.Draft ?? throw new ConfigurationException("Open a draft first.");
        var path = await Workspace.ExportAsync(() => _outputs.ExportDraft(draft, Workspace.OperationToken), "Saving local candidate draft", "Writing a fresh draft without changing standards or tenant evidence.");
        if (string.IsNullOrEmpty(path)) throw new ConfigurationException("Draft save did not complete.");
        _workflow.MarkSaved(draft.ContentDigest); Message = "Draft saved: " + path; RaiseAll();
    }
    public async Task ExportAsync()
    {
        RequireEditable(); var draft = _workflow.Draft!;
        var ticket = _workflow.Validation ?? throw new ConfigurationException("Validate this exact draft before export.");
        var path = await Workspace.ExportAsync(() => _outputs.Export(draft, ticket, Workspace.OperationToken).Checksums,
            "Exporting local candidate", "Writing a candidate, draft, comparison and checksums. This does not publish a standard.");
        if (string.IsNullOrEmpty(path)) throw new ConfigurationException("Candidate export did not complete.");
        _workflow.MarkSaved(draft.ContentDigest); Message = "Candidate set exported. Checksums and filenames: " + path; RaiseAll();
    }
    public void Discard()
    {
        if (!Workspace.Idle) throw new ConfigurationException("Wait for the current operation before discarding local edits.");
        _workflow.Discard(); _reviewDigest = ""; _reviewText = ""; Controls.Clear(); Fields.Clear(); _control = null; SetField(null);
        Message = "Unsaved local draft discarded explicitly. Existing files and published standards were not deleted."; RaiseAll();
    }
    public override void Refresh()
    {
        if (_workflow is null) return;
        _workflow.Refresh();
        if (!_workflow.ContextCurrent) { Fields.Clear(); if (!HasPendingInput) SetField(null); }
        RaiseAll();
    }
    private void RequireReplaceable()
    {
        if (!Workspace.Idle || HasUnsavedChanges) throw new ConfigurationException("Wait for current work. Keep, save or explicitly discard the current draft before replacing it.");
    }
    private void RequireEditable(bool allowPending = false)
    {
        if (!CanEdit || !allowPending && HasPendingInput) throw new ConfigurationException(Guidance);
    }
    private async Task LoadControlsAsync(CancellationToken ct)
    {
        var draft = _workflow.Draft!; var baseline = await Task.Run(() => _baselines.Load(draft), ct);
        ct.ThrowIfCancellationRequested(); if (!_workflow.ContextCurrent) throw new ConfigurationException("Context changed while loading draft controls.");
        Controls.Clear(); foreach (var control in baseline.Controls.Where(c => c.HasRecipe)) Controls.Add(control);
        Fields.Clear(); _control = Controls.FirstOrDefault(); ImportName = _control?.Payload?["displayName"]?.ToString() ?? _control?.Name ?? ""; MappingFile = ""; SetField(null); RaiseAll();
    }
    private async Task LocalAsync(string title, Func<CancellationToken, Task> work)
    {
        var completed = false;
        await Workspace.RunExclusiveAsync(title, async _ => { await work(Workspace.OperationToken); await RefreshReviewAsync(Workspace.OperationToken); Workspace.OperationToken.ThrowIfCancellationRequested(); completed = true; });
        if (!completed) throw new OperationCanceledException("Local authoring stopped; no new result was accepted.");
        RaiseAll();
    }
    private async Task RefreshReviewAsync(CancellationToken ct)
    {
        if (!_workflow.ContextCurrent || _reviewDigest == _workflow.DraftDigest) return;
        var draft = _workflow.Draft!;
        var text = await Task.Run(() => DescribeReview(draft), ct);
        ct.ThrowIfCancellationRequested();
        if (!_workflow.ContextCurrent || _workflow.DraftDigest != draft.ContentDigest)
            throw new ConfigurationException("The draft or working context changed; no review text was admitted.");
        _reviewDigest = draft.ContentDigest; _reviewText = text;
    }
    private static string DescribeReview(StandardCandidateDraft draft)
    {
        var text = new StringBuilder("CONFIGURATION CHANGES — local proposal only\n");
        if (draft.Changes.Count == 0) text.AppendLine("No proposed setting changes.");
        foreach (var change in draft.Changes)
            text.AppendLine(change.ControlId + " · " + change.Path)
                .AppendLine("Baseline: " + (change.ExpectedOriginal?.ToJsonString() ?? "null"))
                .AppendLine("Proposed: " + (change.Proposed?.ToJsonString() ?? "null"));
        text.AppendLine("\nIMPORT SOURCES — integrity is not authenticity");
        if (draft.Imports.Count == 0) text.AppendLine("No policy imported.");
        foreach (var source in draft.Imports)
            text.AppendLine(source.ControlId + " · " + source.PolicyType).AppendLine("Original SHA-256: " + source.Sha256);
        text.AppendLine("\nUNRESOLVED INPUTS — validation and export remain blocked");
        if (draft.Unresolved.Count == 0) text.AppendLine("No unresolved imported references recorded.");
        foreach (var input in draft.Unresolved)
            text.AppendLine(input.ControlId + " · " + input.Path + ": " + input.Reason).AppendLine("Source SHA-256: " + input.SourceSha256);
        text.AppendLine("\nASSIGNMENT OBSERVATIONS — never applied by authoring");
        if (draft.AssignmentObservations.Count == 0) text.AppendLine("No imported assignment observations. No assignment coverage is inferred.");
        foreach (var observation in draft.AssignmentObservations)
            text.AppendLine(observation.ControlId + " · " + observation.Status + ": " + observation.Detail)
                .AppendLine("Source SHA-256: " + observation.SourceSha256);
        return text.ToString();
    }
    private void SetField(CandidateEditableField? field)
    {
        _field = field; _value = field is null ? "" : field.ValueType == "String" ? JsonSerializer.Deserialize<string>(field.ProposedJson) ?? "" : field.ProposedJson;
        _initialValue = _value; RaiseAll();
    }
}
