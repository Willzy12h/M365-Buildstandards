using System.Collections.ObjectModel;
using System.Windows.Input;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;

namespace BDIT.TenantToolkit.App.ViewModels;

public sealed class OverviewViewModel : PageViewModel
{
    private LicenceSummary? _selected;
    private string _search = "";
    public OverviewViewModel(ShellViewModel shell) : base(shell, "Tenant overview")
    {
        ConnectCommand = Sync(() => Shell.Navigate("connect"));
        RefreshLicencesCommand = Command(() => Workspace.LoadLicencesAsync(false), () => Workspace.IsConnected && Workspace.Idle);
        LoadAssignmentsCommand = Command(() => Workspace.LoadLicencesAsync(true), () => Workspace.IsConnected && Workspace.Idle);
    }
    public ICommand ConnectCommand { get; }
    public ICommand RefreshLicencesCommand { get; }
    public ICommand LoadAssignmentsCommand { get; }
    public ObservableCollection<LicenceSummary> Licences { get; } = new();
    public ObservableCollection<LicensedUser> Users { get; } = new();
    public ObservableCollection<LicenceScopeResult> Scope { get; } = new();
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) RefreshUsers(); } }
    public LicenceSummary? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) { RefreshUsers(); OnPropertyChanged(nameof(SelectedDetail)); } } }
    public string SelectedDetail => Selected is null ? "Select a licence, then load user assignments. Search by name, sign-in name or object ID." : Selected.Product + " — " + Selected.Detail;
    public string Summary => Workspace.Session is null ? "Connect to a tenant to see its actual subscriptions and licence assignments."
        : $"{Workspace.Session.TenantName} · {Workspace.Session.PrimaryDomain}\n{Workspace.Session.TenantId}\n{Workspace.InterruptedNotice}";
    public string ReportStatus => Workspace.Licences is not { } report ? "Licence inventory has not been loaded."
        : $"Captured {report.CapturedAt} · subscriptions: {(report.SubscriptionsComplete ? "collected" : "incomplete")} · user assignments: {(report.UsersComplete ? "collected" : "not complete")}\n{report.SubscriptionError} {report.UserError}";
    public string UserStatus => Workspace.Licences?.UsersComplete == true ? $"{Users.Count} matching assigned users displayed." : "User counts and scope are unknown until a complete assignment read succeeds.";
    /// <summary>The first read-only assessment, step by step, shown until a tenant is connected (docs/OPERATOR-START.md).</summary>
    public bool ShowGettingStarted => Workspace.Session is null;
    public string GettingStarted =>
        "1.  Connect → Quick Connect with Microsoft. Sign in with the client's account, check the organisation, then connect read-only.\n" +
        "2.  Configuration → Capture configuration (read-only). A collection that could not be read is unknown, not absent.\n" +
        "3.  Assessment → review the findings and their evidence.\n" +
        "4.  Assessment → Export assessment reports → Engineer report (HTML).\n\n" +
        "None of these steps changes the client's tenant. Receiving evidence from another engineer? Use Settings → Receive a workspace.";

    public override void Refresh()
    {
        OnPropertyChanged(nameof(ShowGettingStarted));
        var sku = Selected?.SkuId;
        Licences.Clear(); Scope.Clear();
        if (Workspace.Licences is { } inventory && inventory.TenantId == Workspace.Session?.TenantId)
        {
            foreach (var licence in LicenceInventoryService.Summaries(inventory)) Licences.Add(licence);
            if (Workspace.Standard is { } standard && Workspace.Profile is { } profile)
                foreach (var row in LicenceInventoryService.CheckScope(inventory, standard, profile, Workspace.Plan)) Scope.Add(row);
        }
        Selected = Licences.FirstOrDefault(l => l.SkuId == sku) ?? Licences.FirstOrDefault();
        RefreshUsers();
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(ReportStatus));
    }
    private void RefreshUsers()
    {
        Users.Clear();
        if (Workspace.Licences is { } inventory && inventory.TenantId == Workspace.Session?.TenantId && Selected is not null)
            foreach (var user in LicenceInventoryService.AssignedUsers(inventory, Selected.SkuId, Search)) Users.Add(user);
        OnPropertyChanged(nameof(UserStatus));
    }
}
