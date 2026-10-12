using System.Windows.Input;
using BuildStandard.App.Infrastructure;

namespace BuildStandard.App.ViewModels;

public sealed record HomeEvidenceRow(string Title, string CapturedAt, string Status, string PageKey, string Provenance);

public sealed record HomeTaskCard(string Title, string Description, string ActionLabel, string PageKey, string AccessibleName);

public sealed class HomeViewModel : PageViewModel
{
    public HomeViewModel(ShellViewModel shell) : base(shell, "Home")
    {
        NextStepCommand = new RelayCommand(() => Shell.Navigate(Projection.NextPageKey), () => Projection.CanTakeNextAction);
        OpenTaskCommand = new RelayCommand(value =>
        {
            if (value is HomeTaskCard task && IsHomeTask(task)) Shell.Navigate(task.PageKey);
        }, value => value is HomeTaskCard task && IsHomeTask(task));
        Workspace.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName is nameof(Workspace.Busy) or nameof(Workspace.BusyMessage)) Refresh();
        };
        Refresh();
    }

    public ICommand NextStepCommand { get; }
    public ICommand OpenTaskCommand { get; }
    public HomeTaskProjection Projection { get; private set; } = HomeTaskProjection.Create(new());
    public string BoundaryText => HomeTaskProjection.BoundaryText;

    public IReadOnlyList<HomeTaskCard> PrimaryTasks { get; } = Array.AsReadOnly(new[]
    {
        new HomeTaskCard("Discover the tenant", "Choose and verify a client before collecting read-only configuration.", "Open client connection", "connect", "Open client connection · discover the tenant"),
        new HomeTaskCard("Assess saved evidence", "Review captured settings, gaps and unknowns against the selected standard.", "Open assessment", "assessment", "Open assessment · assess saved evidence"),
        new HomeTaskCard("Backfill a legacy build", "Record the existing build and its evidence before proposing changes.", "Open jobs and backfill", "jobs", "Open jobs and backfill · legacy build"),
        new HomeTaskCard("Review a change plan", "Inspect the exact proposal and its prerequisites before any approval.", "Open plan changes", "plan", "Open plan changes · review a change plan")
    });
    public IReadOnlyList<HomeEvidenceRow> RecentEvidence { get; private set; } = Array.Empty<HomeEvidenceRow>();
    public bool HasRecentEvidence => RecentEvidence.Count > 0;
    public IReadOnlyList<HomeTaskCard> Tasks { get; } = Array.AsReadOnly(new[]
    {
        new HomeTaskCard("Start a new tenant", "Choose the client and verify its identity. Begin with a read-only capture, then review the build requirements.", "Open Connect", "connect", "Open Connect · Home new tenant workflow"),
        new HomeTaskCard("Review an existing tenant", "Inspect existing settings, assess gaps and review backfill options. Plan only the selected, supported changes.", "Open Configuration", "configuration", "Open Configuration · Home legacy and backfill workflow"),
        new HomeTaskCard("Return for a repeat review", "Compare original captures, standard versions and drift. Use a fresh read for current claims and retain unresolved outcomes.", "Open Evidence and drift", "history", "Open Evidence and drift · Home repeat review workflow"),
        new HomeTaskCard("Hand work to another engineer", "Receive or export a workspace. Review the client identity, original evidence and remaining engineer decisions together.", "Open workspace handoff", "settings", "Open workspace handoff · Home engineer handoff workflow"),
        new HomeTaskCard("Inspect tenant reports", "Choose read-only inventory reports and naming review. Failed or unknown reads remain visible for follow-up.", "Open Tenant reports", "reports", "Open Tenant reports · Home tenant reports workflow"),
        new HomeTaskCard("Browse Scripts and Reports", "Review the existing library, permission requirements and manual instructions on its dedicated page.", "Open Scripts and Reports", "scripts", "Open Scripts and Reports · Home scripts and reports workflow")
    });

    public IReadOnlyList<HomeTaskCard> Shortcuts { get; } = Array.AsReadOnly(new[]
    {
        new HomeTaskCard("Assessment", "Review findings and their evidence.", "", "assessment", "Assessment · Home assessment shortcut"),
        new HomeTaskCard("Manual checks", "Record engineer checks with evidence.", "", "checks", "Manual checks · Home manual checks shortcut"),
        new HomeTaskCard("Deviations", "Review client exceptions and their approvals.", "", "deviations", "Deviations · Home deviations shortcut"),
        new HomeTaskCard("Plan changes", "Inspect selected proposals and their blockers.", "", "plan", "Plan changes · Home plan changes shortcut"),
        new HomeTaskCard("Jobs and completion", "Review job evidence, decisions and completion blockers.", "", "jobs", "Jobs and completion · Home jobs and completion shortcut"),
        new HomeTaskCard("Undo and recovery", "Inspect recorded changes and recovery prerequisites.", "", "recovery", "Undo and recovery · Home recovery shortcut"),
        new HomeTaskCard("Overview and licences", "Inspect subscriptions and licence assignments.", "", "overview", "Overview and licences · Home overview and licences shortcut"),
        new HomeTaskCard("Build Standard", "Review the loaded catalogue and requirements.", "", "standard", "Build Standard · Home build standard shortcut")
    });

    private bool IsHomeTask(HomeTaskCard task) => PrimaryTasks.Concat(Tasks).Concat(Shortcuts).Any(item => ReferenceEquals(item, task));

    public override void Refresh()
    {
        Projection = HomeTaskProjection.Create(Workspace.CaptureHomeTaskContext());
        var rows = new List<HomeEvidenceRow>();
        var tenant = Workspace.Profile?.TenantId;
        bool Current(string id) => !string.IsNullOrWhiteSpace(tenant) && string.Equals(id, tenant, StringComparison.OrdinalIgnoreCase);
        if (Workspace.Snapshot is { } capture && Current(capture.TenantId))
            rows.Add(new("Configuration capture", capture.CapturedAt, capture.Complete ? "Recorded complete · review sources" : "Incomplete · review sources",
                "configuration", $"Original capture {capture.Id} · standard {capture.StandardRelease} · captured by {capture.CapturedBy}"));
        if (Workspace.Assessment is { } assessment && Current(assessment.TenantId))
            rows.Add(new("Build standard assessment", assessment.AssessedAt, "Review findings and limitations", "assessment",
                $"Original assessment {assessment.Id} · snapshot {assessment.SnapshotId} · standard {assessment.Release}"));
        if (Workspace.ExchangeSnapshot is { } exchange && Current(exchange.TenantId))
            rows.Add(new("Exchange/Purview evidence", exchange.CapturedAt, "Supplemental · separate access", "configuration",
                $"Original Exchange capture {exchange.Id} · captured by {exchange.CapturedBy}"));
        if (Workspace.LastRun is { } run && Current(run.TenantId))
            rows.Add(new("Recorded deployment outcomes", run.StartedAt, run.Status + " · functional checks remain separate", "deploy",
                $"Original run {run.Id} · before snapshot {run.BeforeSnapshotId}"));
        RecentEvidence = rows.AsReadOnly();
        OnPropertyChanged(nameof(RecentEvidence)); OnPropertyChanged(nameof(HasRecentEvidence));
        OnPropertyChanged(nameof(Projection));
    }
}
