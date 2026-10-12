using System.Windows.Input;
using BuildStandard.App.Infrastructure;

namespace BuildStandard.App.ViewModels;

public sealed record HomeTaskCard(string Title, string Description, string ActionLabel, string PageKey, string AccessibleName);

public sealed class HomeViewModel : PageViewModel
{
    public HomeViewModel(ShellViewModel shell) : base(shell, "Home")
    {
        NextStepCommand = new RelayCommand(() => Shell.Navigate(Projection.NextPageKey));
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

    private bool IsHomeTask(HomeTaskCard task) => Tasks.Concat(Shortcuts).Any(item => ReferenceEquals(item, task));

    public override void Refresh()
    {
        Projection = HomeTaskProjection.Create(new HomeTaskContext
        {
            Profile = Workspace.Profile, Session = Workspace.Session, Standard = Workspace.Standard,
            Snapshot = Workspace.Snapshot, SnapshotIsLive = Workspace.SnapshotIsLive,
            ExchangeSnapshot = Workspace.ExchangeSnapshot, ExchangeCapturedByTool = Workspace.ExchangeCapturedByTool,
            DisplayedTenantId = Shell.HeaderTenantId,
            ApplicationSetupActive = Workspace.ApplicationSetup is not null, Busy = Workspace.Busy,
            BusyMessage = Workspace.BusyMessage, InterruptedNotice = Workspace.InterruptedNotice
        });
        OnPropertyChanged(nameof(Projection));
    }
}
