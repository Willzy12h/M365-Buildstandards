using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Workflow;

namespace BDIT.TenantToolkit.App.ViewModels;

public sealed record JobRow(string Id, string Opened, string Intention, string Owner, string Claim);

/// <summary>
/// INT-049/050 jobs: open a job for the selected client, see every requirement's projected state, and record an
/// outcome, a decision or a cutover revision through the engine workflow. Nothing on this page reads or writes the tenant, and a complete
/// job confers no authority: planning and deployment keep every one of their own checks.
/// </summary>
public sealed class JobsViewModel : PageViewModel
{
    public const string OutcomeKind = "Outcome";
    public const string DecisionKind = "Decision";
    public const string CutoverKind = "Cutover";
    /// <summary>The case key that starts a new cutover case at review.</summary>
    public const string NewCase = "";

    private JobRow? _selectedJob;
    private RequirementCompletion? _selectedRequirement;
    private JobProjection? _projection;
    private JobCompletion? _completion;
    private string _newIntention = JobIntention.NewBuild, _newOwner = "", _newNotes = "";
    private string _recordKind = OutcomeKind, _status = ObservationStatus.Pass, _decision = DispositionDecision.Investigate;
    private string _decisionOwner = "", _reason = "", _reviewDue = "", _objectIds = "", _deviationId = "";
    private string _problems = "";
    private string _selectedCase = NewCase, _stage = CutoverStage.Review, _newObjectIds = "", _runId = "", _overlaps = "", _recoveryLimits = "";
    private string _pilotGroupIds = "", _pilotApprovedBy = "", _pilotReference = "", _prerequisites = "", _criteria = "";
    private string _retirement = "", _retirementApprovedBy = "", _retirementReference = "", _residualDeviationIds = "";
    private string _escalationReason = "", _escalationOwner = "", _escalationPath = "";
    private string _assessmentId = "";

    public JobsViewModel(ShellViewModel shell) : base(shell, "Jobs")
    {
        OpenJobCommand = Sync(OpenJob, () => Workspace.Profile is not null && Workspace.Standard is not null && Workspace.Idle);
        RecordCommand = Sync(Record, () => SelectedJob is not null && SelectedRequirement is not null && Workspace.Idle);
        ReviewDue = DefaultReviewDue();
        Refresh();
    }

    public ICommand OpenJobCommand { get; }
    public ICommand RecordCommand { get; }

    public ObservableCollection<JobRow> Jobs { get; } = new();
    public ObservableCollection<RequirementCompletion> Requirements { get; } = new();
    public ObservableCollection<string> DeviationIds { get; } = new();
    /// <summary>The selected requirement's cutover cases, and the choice to start a new one.</summary>
    public ObservableCollection<FilterOption> CutoverCases { get; } = new();
    /// <summary>Saved deployment runs of this client, newest first: the evidence a candidate stage pins.</summary>
    public ObservableCollection<FilterOption> Runs { get; } = new();
    /// <summary>Stored assessments of the capture in view under this standard, each with its finding for the selected requirement.</summary>
    public ObservableCollection<FilterOption> Assessments { get; } = new();

    public IReadOnlyList<FilterOption> Intentions { get; } = JobIntention.All.Select(i => new FilterOption(i, WordsConverter.Words(i))).ToList();
    public IReadOnlyList<FilterOption> RecordKinds { get; } = new[] { new FilterOption(OutcomeKind, "Outcome (observation)"), new FilterOption(DecisionKind, "Decision (legacy disposition)"), new FilterOption(CutoverKind, "Cutover revision") };
    public IReadOnlyList<FilterOption> Stages { get; } = CutoverStage.Order.Select(s => new FilterOption(s, WordsConverter.Words(s))).ToList();
    public IReadOnlyList<FilterOption> Retirements { get; } = new[] { new FilterOption("", "Not yet decided") }
        .Concat(RetirementDecision.All.Select(r => new FilterOption(r, WordsConverter.Words(r)))).ToList();
    public IReadOnlyList<string> Statuses { get; } = ObservationStatus.All;
    public IReadOnlyList<FilterOption> Decisions { get; } = DispositionDecision.All.Select(d => new FilterOption(d, WordsConverter.Words(d))).ToList();

    public JobRow? SelectedJob
    {
        get => _selectedJob;
        set { if (SetProperty(ref _selectedJob, value)) Project(); }
    }

    public RequirementCompletion? SelectedRequirement
    {
        get => _selectedRequirement;
        set
        {
            if (!SetProperty(ref _selectedRequirement, value)) return;
            OnPropertyChanged(nameof(RequirementText));
            LoadCases();
            LoadAssessments();
        }
    }

    public string NewIntention { get => _newIntention; set => SetProperty(ref _newIntention, value); }
    public string NewOwner { get => _newOwner; set => SetProperty(ref _newOwner, value); }
    public string NewNotes { get => _newNotes; set => SetProperty(ref _newNotes, value); }

    public string RecordKind
    {
        get => _recordKind;
        set
        {
            if (!SetProperty(ref _recordKind, value)) return;
            OnPropertyChanged(nameof(IsOutcome)); OnPropertyChanged(nameof(IsDecision)); OnPropertyChanged(nameof(IsCutover));
            OnPropertyChanged(nameof(HasOwner)); OnPropertyChanged(nameof(ObjectsLabel));
            FillFromCase();
        }
    }
    public bool IsOutcome => RecordKind == OutcomeKind;
    public bool IsDecision => RecordKind == DecisionKind;
    public bool IsCutover => RecordKind == CutoverKind;
    public bool HasOwner => !IsOutcome;
    public string ObjectsLabel => IsCutover ? "Old object IDs, as reviewed (one per line)" : "Object IDs (one per line)";

    /// <summary>Choosing a case fills the form with its current revision, so a new revision starts from the reviewed state.</summary>
    public string SelectedCase
    {
        get => _selectedCase;
        set { if (SetProperty(ref _selectedCase, value ?? NewCase)) FillFromCase(); }
    }
    public string Stage { get => _stage; set => SetProperty(ref _stage, value); }
    public string NewObjectIds { get => _newObjectIds; set => SetProperty(ref _newObjectIds, value); }
    public string RunId { get => _runId; set => SetProperty(ref _runId, value ?? ""); }
    public string Overlaps { get => _overlaps; set => SetProperty(ref _overlaps, value); }
    public string RecoveryLimits { get => _recoveryLimits; set => SetProperty(ref _recoveryLimits, value); }
    public string PilotGroupIds { get => _pilotGroupIds; set => SetProperty(ref _pilotGroupIds, value); }
    public string PilotApprovedBy { get => _pilotApprovedBy; set => SetProperty(ref _pilotApprovedBy, value); }
    public string PilotReference { get => _pilotReference; set => SetProperty(ref _pilotReference, value); }
    /// <summary>One per line; "[x]" marks a met prerequisite.</summary>
    public string Prerequisites { get => _prerequisites; set => SetProperty(ref _prerequisites, value); }
    /// <summary>One per line: "result | description | what was observed".</summary>
    public string Criteria { get => _criteria; set => SetProperty(ref _criteria, value); }
    public string Retirement { get => _retirement; set => SetProperty(ref _retirement, value ?? ""); }
    public string RetirementApprovedBy { get => _retirementApprovedBy; set => SetProperty(ref _retirementApprovedBy, value); }
    public string RetirementReference { get => _retirementReference; set => SetProperty(ref _retirementReference, value); }
    public string ResidualDeviationIds { get => _residualDeviationIds; set => SetProperty(ref _residualDeviationIds, value); }
    public string EscalationReason { get => _escalationReason; set => SetProperty(ref _escalationReason, value); }
    public string EscalationOwner { get => _escalationOwner; set => SetProperty(ref _escalationOwner, value); }
    public string EscalationPath { get => _escalationPath; set => SetProperty(ref _escalationPath, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    /// <summary>The stored assessment an outcome cites, or "" for none. Citing one pins the capture it assessed.</summary>
    public string AssessmentId { get => _assessmentId; set => SetProperty(ref _assessmentId, value ?? ""); }
    public string Decision { get => _decision; set { if (SetProperty(ref _decision, value)) OnPropertyChanged(nameof(IsDeparture)); } }
    public bool IsDeparture => Decision == DispositionDecision.ApprovedDeparture;
    public string DecisionOwner { get => _decisionOwner; set => SetProperty(ref _decisionOwner, value); }
    public string Reason { get => _reason; set => SetProperty(ref _reason, value); }
    public string ReviewDue { get => _reviewDue; set => SetProperty(ref _reviewDue, value); }
    /// <summary>Exact object IDs, one per line or separated by commas. Each must be in the capture in view.</summary>
    public string ObjectIds { get => _objectIds; set => SetProperty(ref _objectIds, value); }
    public string DeviationId { get => _deviationId; set => SetProperty(ref _deviationId, value); }

    public string ContextText => Workspace.Profile is null
        ? "Select a client on the Connect page to see its jobs."
        : $"Jobs for {Workspace.Profile.Company}. A job records outcomes and decisions against the standard and client inputs; it never changes the tenant and grants no permission to.";

    public string CaptureText => Workspace.SavedCapture() is { } capture
        ? $"Records pin the capture in view: {capture.CapturedAt}{(capture.Complete ? "" : " (INCOMPLETE)")}. Named objects must be in it."
        : "No saved capture of this client is in view. Outcomes can still be recorded, but a pass without stored evidence never verifies a requirement. Capture or reopen one in Configuration.";

    public string ClaimText => _completion?.Claim ?? (Workspace.Profile is null ? "" : Jobs.Count == 0 ? "No jobs yet. Open one to start recording outcomes." : "Select a job to see where each requirement stands.");

    public bool HasProblems => _problems.Length > 0;
    public string ProblemsText => _problems;

    public string RequirementText
    {
        get
        {
            var r = SelectedRequirement;
            if (r is null) return "Select a requirement to see why it stands or not, and to record an outcome or decision for it.";
            var text = new StringBuilder($"{r.InstanceKey} · {r.Name}\n{WordsConverter.Words(r.State)}");
            foreach (var reason in r.Reasons) text.Append("\n• ").Append(reason);
            var history = _projection?.Subjects.FirstOrDefault(s => Same(s.InstanceKey, r.InstanceKey))?.History;
            if (history is { Count: > 0 })
            {
                text.Append("\n\nOutcome history");
                foreach (var o in history)
                    text.Append($"\n{o.RecordedAt}  {o.Status}  {o.Actor}: {o.Reason}{(o.Evidence.Any(e => e.Kind == EvidenceKind.Assessment) ? " (cites a stored assessment)" : "")}");
            }
            var decisions = _projection?.Dispositions.FirstOrDefault(d => Same(d.InstanceKey, r.InstanceKey))?.History;
            if (decisions is { Count: > 0 })
            {
                text.Append("\n\nDecision history");
                foreach (var d in decisions) text.Append($"\n{d.RecordedAt}  {WordsConverter.Words(d.Decision)}  owner {d.Owner}: {d.Reason}");
            }
            foreach (var c in _projection?.Cutovers.Where(c => Same(c.InstanceKey, r.InstanceKey)) ?? Enumerable.Empty<CutoverProjection>())
                text.Append($"\n\nCutover case {c.CaseId[..8]}: {c.History.Count} revision(s), now {(c.Current is null ? "needs review" : WordsConverter.Words(c.Current.Stage))}{(c.Escalated ? ", escalated" : "")}.");
            return text.ToString();
        }
    }

    private void OpenJob()
    {
        if (string.IsNullOrWhiteSpace(NewOwner)) throw new ToolkitException("Name the job's owner.");
        var job = Workspace.OpenJob(NewIntention, NewOwner, NewNotes);
        NewNotes = "";
        SelectedJob = Jobs.FirstOrDefault(j => j.Id == job.Id);
    }

    private void Record()
    {
        var job = SelectedJob ?? throw new ToolkitException("Select a job first.");
        var requirement = SelectedRequirement ?? throw new ToolkitException("Select a requirement first.");
        if (!DateOnly.TryParseExact(ReviewDue.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due))
            throw new ToolkitException("Review by must be a date in yyyy-MM-dd format.");
        var reviewDue = new DateTimeOffset(due.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (IsCutover)
        {
            RecordCutover(job, requirement, reviewDue, Workspace.SavedCapture());
            return;
        }
        var objects = ObjectIds.Split(new[] { '\n', '\r', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var capture = Workspace.SavedCapture();
        if (objects.Count > 0 && capture is null) throw new ToolkitException("Named objects need the capture they were observed in. Capture or reopen one in Configuration first.");

        if (IsOutcome)
        {
            var subject = _projection?.Subjects.FirstOrDefault(s => Same(s.InstanceKey, requirement.InstanceKey));
            Workspace.RecordObservation(job.Id, new ObservationRequest
            {
                SemanticId = Workspace.SemanticIdFor(requirement.ControlId, subject?.History.FirstOrDefault()?.SemanticId),
                ControlId = requirement.ControlId, InstanceKey = requirement.InstanceKey, Status = Status, Reason = Reason,
                ReviewDueAt = reviewDue, SnapshotId = capture?.Id, ObservedObjectIds = objects, SupersedesId = Head(subject?.Current?.Id, subject?.History.Count ?? 0),
                AssessmentId = AssessmentId.Length > 0 ? AssessmentId : null
            });
            AssessmentId = "";
        }
        else
        {
            var decided = _projection?.Dispositions.FirstOrDefault(d => Same(d.InstanceKey, requirement.InstanceKey));
            Workspace.RecordDecision(job.Id, new DispositionRequest
            {
                SemanticId = Workspace.SemanticIdFor(requirement.ControlId, decided?.History.FirstOrDefault()?.SemanticId),
                ControlId = requirement.ControlId, InstanceKey = requirement.InstanceKey, Decision = Decision, Owner = DecisionOwner, Reason = Reason,
                ReviewDueAt = reviewDue, SnapshotId = capture?.Id, ObservedObjectIds = objects,
                DeviationId = IsDeparture && DeviationId.Trim().Length > 0 ? DeviationId.Trim() : null,
                SupersedesId = Head(decided?.Current?.Id, decided?.History.Count ?? 0)
            });
        }
        Reason = ""; ObjectIds = "";
        SelectedRequirement = Requirements.FirstOrDefault(r => r.InstanceKey == requirement.InstanceKey);
    }

    private void RecordCutover(JobRow job, RequirementCompletion requirement, DateTimeOffset reviewDue, TenantSnapshot? capture)
    {
        var head = CaseHead(SelectedCase);
        if (SelectedCase != NewCase && head is null)
            throw new ToolkitException("This cutover case has no single current revision. It needs a deliberate review before anything else is recorded.");
        Workspace.RecordCutover(job.Id, new CutoverRequest
        {
            CaseId = head?.CaseId, SupersedesId = head?.Id,
            SemanticId = head?.SemanticId ?? Workspace.SemanticIdFor(requirement.ControlId, null),
            ControlId = requirement.ControlId, InstanceKey = requirement.InstanceKey, Stage = Stage, Owner = DecisionOwner, Reason = Reason,
            ReviewDueAt = reviewDue, SnapshotId = capture?.Id, RunId = RunId.Length > 0 ? RunId : null,
            OldObjectIds = CutoverText.Ids(ObjectIds), NewObjectIds = CutoverText.Ids(NewObjectIds), Overlaps = Overlaps, RecoveryLimits = RecoveryLimits,
            PilotGroupIds = CutoverText.Ids(PilotGroupIds), PilotApproval = CutoverText.Approval(PilotApprovedBy, PilotReference),
            Prerequisites = CutoverText.Prerequisites(Prerequisites), Criteria = CutoverText.Criteria(Criteria),
            Retirement = Retirement.Length > 0 ? Retirement : null, RetirementApproval = CutoverText.Approval(RetirementApprovedBy, RetirementReference),
            ResidualDeviationIds = CutoverText.Ids(ResidualDeviationIds), Unsupported = CutoverText.Escalation(EscalationReason, EscalationOwner, EscalationPath)
        });
        var caseId = head?.CaseId;
        Reason = "";
        SelectedRequirement = Requirements.FirstOrDefault(r => r.InstanceKey == requirement.InstanceKey);
        // Stay on the case just written: a new case is the selected requirement's newest one.
        SelectedCase = caseId ?? _projection?.Cutovers.Where(c => Same(c.InstanceKey, requirement.InstanceKey))
            .OrderByDescending(c => c.History.FirstOrDefault()?.RecordedAt, StringComparer.Ordinal).FirstOrDefault()?.CaseId ?? NewCase;
    }

    private CutoverRevision? CaseHead(string caseId) =>
        caseId == NewCase ? null : _projection?.Cutovers.FirstOrDefault(c => string.Equals(c.CaseId, caseId, StringComparison.OrdinalIgnoreCase))?.Current;

    private void LoadCases()
    {
        var keep = SelectedCase;
        CutoverCases.Clear();
        CutoverCases.Add(new FilterOption(NewCase, "New case (starts at review)"));
        if (SelectedRequirement is { } requirement)
            foreach (var c in _projection?.Cutovers.Where(c => Same(c.InstanceKey, requirement.InstanceKey)) ?? Enumerable.Empty<CutoverProjection>())
                CutoverCases.Add(new FilterOption(c.CaseId, $"Case {c.CaseId[..8]} · {(c.Current is null ? "needs review" : WordsConverter.Words(c.Current.Stage))}"));
        _selectedCase = CutoverCases.Any(c => c.Key == keep) ? keep : NewCase;
        OnPropertyChanged(nameof(SelectedCase));
        FillFromCase();
    }

    /// <summary>The next stage, and every reviewed field, from the case's current revision; a new case starts empty at review.</summary>
    private void FillFromCase()
    {
        if (!IsCutover) return;
        var head = CaseHead(SelectedCase);
        if (head is null)
        {
            Stage = CutoverStage.Review;
            NewObjectIds = ""; Overlaps = ""; RecoveryLimits = ""; PilotGroupIds = ""; PilotApprovedBy = ""; PilotReference = "";
            Prerequisites = ""; Criteria = ""; Retirement = ""; RetirementApprovedBy = ""; RetirementReference = ""; ResidualDeviationIds = "";
            EscalationReason = ""; EscalationOwner = ""; EscalationPath = ""; RunId = "";
            ObjectIds = "";
            return;
        }
        var rank = CutoverStage.Rank(head.Stage);
        Stage = head.Unsupported is not null || head.Stage == CutoverStage.Closed ? head.Stage : CutoverStage.Order[Math.Min(rank + 1, CutoverStage.Order.Length - 1)];
        ObjectIds = CutoverText.Ids(head.OldObjectIds);
        NewObjectIds = CutoverText.Ids(head.NewObjectIds);
        Overlaps = head.Overlaps; RecoveryLimits = head.RecoveryLimits;
        PilotGroupIds = CutoverText.Ids(head.PilotGroupIds);
        PilotApprovedBy = head.PilotApproval?.ApprovedBy ?? ""; PilotReference = head.PilotApproval?.Reference ?? "";
        Prerequisites = CutoverText.Prerequisites(head.Prerequisites);
        Criteria = CutoverText.Criteria(head.Criteria);
        Retirement = head.Retirement ?? "";
        RetirementApprovedBy = head.RetirementApproval?.ApprovedBy ?? ""; RetirementReference = head.RetirementApproval?.Reference ?? "";
        ResidualDeviationIds = CutoverText.Ids(head.ResidualDeviationIds);
        EscalationReason = head.Unsupported?.Reason ?? ""; EscalationOwner = head.Unsupported?.Owner ?? ""; EscalationPath = head.Unsupported?.Path ?? "";
        DecisionOwner = head.Owner;
        RunId = "";
    }

    /// <summary>A revision supersedes the current record; a history with no current record needs a deliberate review first.</summary>
    private static string? Head(string? currentId, int historyCount) =>
        historyCount > 0 && currentId is null
            ? throw new ToolkitException("This requirement's history has no single current record (a fork or a missing link). It needs a deliberate review before anything else is recorded.")
            : currentId;

    private void Project()
    {
        var instance = SelectedRequirement?.InstanceKey;
        Requirements.Clear();
        _projection = null; _completion = null;
        var problems = new List<string>();
        if (SelectedJob is { } job && Workspace.Profile is not null && Workspace.Standard is not null)
        {
            try
            {
                (_projection, _completion) = Workspace.ProjectJob(job.Id);
                foreach (var r in _completion.Requirements) Requirements.Add(r);
                problems.AddRange(_completion.Blockers);
                problems.AddRange(_projection.Unattached.Select(o => $"An outcome for {o.InstanceKey} was written but never attached (not counted)."));
                problems.AddRange(_projection.UnattachedDispositions.Select(d => $"A decision for {d.InstanceKey} was written but never attached (not counted)."));
                problems.AddRange(_projection.UnattachedCutovers.Select(c => $"A cutover revision for {c.InstanceKey} was written but never attached (not counted)."));
                if (_projection.LegacyAttestations.Count > 0)
                    problems.Add($"{_projection.LegacyAttestations.Count} manual check(s) from the Manual checks page are legacy attestations and count for nothing in a job.");
            }
            catch (ToolkitException ex) { problems.Add("This job cannot be projected: " + ex.Message); }
        }
        _problems = string.Join("\n", problems);
        _selectedRequirement = instance is null ? null : Requirements.FirstOrDefault(r => Same(r.InstanceKey, instance));
        OnPropertyChanged(nameof(SelectedRequirement));
        LoadCases();
        LoadAssessments();
        OnPropertyChanged(nameof(RequirementText));
        OnPropertyChanged(nameof(ClaimText));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(ProblemsText));
    }

    public override void Refresh()
    {
        var selectedId = SelectedJob?.Id;
        Jobs.Clear();
        DeviationIds.Clear();
        var problems = new List<string>();
        if (Workspace.Profile is not null && Workspace.Standard is not null)
        {
            try
            {
                var (jobs, unreadable) = Workspace.LoadJobs();
                foreach (var job in jobs.OrderByDescending(j => j.CreatedAt, StringComparer.Ordinal))
                {
                    string claim;
                    try { claim = Workspace.ProjectJob(job.Id).Completion.Claim; }
                    catch (ToolkitException ex) { claim = "Cannot be projected: " + ex.Message; }
                    Jobs.Add(new JobRow(job.Id, job.CreatedAt, WordsConverter.Words(job.Intention), job.Owner, claim));
                }
                problems.AddRange(unreadable.Select(u => $"A job file cannot be read ({System.IO.Path.GetFileName(u.File)}): {u.Problem}"));
                foreach (var d in Workspace.LoadDeviations().Where(d => d.Kind == DeviationKind.ApprovedDeviation)) DeviationIds.Add(d.Id);
                Runs.Clear();
                Runs.Add(new FilterOption("", "No run (not the candidate stage)"));
                foreach (var run in Workspace.Evidence.LoadRuns(Workspace.Profile.TenantId).OrderByDescending(r => r.StartedAt, StringComparer.Ordinal))
                    Runs.Add(new FilterOption(run.Id, $"{run.StartedAt} · {WordsConverter.Words(run.Status.ToString())} · {run.Results.Count} result(s)"));
            }
            catch (ToolkitException ex) { Shell.ShowError(ex); }
        }
        if (NewOwner.Length == 0 && Workspace.Session is not null) NewOwner = Workspace.Session.Account;
        if (DecisionOwner.Length == 0 && Workspace.Session is not null) DecisionOwner = Workspace.Session.Account;
        _selectedJob = selectedId is null ? null : Jobs.FirstOrDefault(j => j.Id == selectedId);
        OnPropertyChanged(nameof(SelectedJob));
        Project();
        if (problems.Count > 0) { _problems = string.Join("\n", problems.Concat(_problems.Length > 0 ? new[] { _problems } : Array.Empty<string>())); OnPropertyChanged(nameof(HasProblems)); OnPropertyChanged(nameof(ProblemsText)); }
        OnPropertyChanged(nameof(ContextText));
        OnPropertyChanged(nameof(CaptureText));
    }

    private void LoadAssessments()
    {
        var keep = AssessmentId;
        Assessments.Clear();
        Assessments.Add(new FilterOption("", "None"));
        try
        {
            foreach (var stored in Workspace.CitableAssessments())
            {
                var finding = SelectedRequirement is { } r ? stored.Result.Findings.FirstOrDefault(f => Same(f.ControlId, r.InstanceKey)) : null;
                Assessments.Add(new FilterOption(stored.Result.Id,
                    $"{stored.Result.AssessedAt} · {stored.Result.AssessedBy}{(finding is null ? "" : " · " + WordsConverter.Words(finding.Status.ToString()))}"));
            }
        }
        catch (ToolkitException ex) { Shell.ShowError(ex); }
        _assessmentId = Assessments.Any(a => a.Key == keep) ? keep : "";
        OnPropertyChanged(nameof(AssessmentId));
    }

    private static string DefaultReviewDue() => DateOnly.FromDateTime(DateTime.Today).AddMonths(6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
