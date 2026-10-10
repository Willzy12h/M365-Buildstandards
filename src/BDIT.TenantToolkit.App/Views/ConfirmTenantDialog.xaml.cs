using System.Windows;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;

namespace BDIT.TenantToolkit.App.Views;

/// <summary>Final gate before a write: the engineer explicitly approves the verified tenant and every planned write.</summary>
public partial class ConfirmTenantDialog : Window
{
    private readonly TenantProfile _profile;
    private readonly DeploymentPlan _plan;
    private readonly TenantSession? _session;
    public string ConfirmedTenantId => ApprovalBox.IsChecked == true && CanApprove(_profile, _plan, _session, true) ? _profile.TenantId : "";
    public static bool CanApprove(TenantProfile profile, DeploymentPlan plan, TenantSession? session, bool approved) => approved
        && session is { TenantVerified: true, OperatorVerified: true, Mode: SessionMode.Deployment }
        && ProfileValidator.IsGuid(plan.OperatorObjectId)
        && TenantConfirmation.Matches(profile.TenantId, plan.TenantId)
        && TenantConfirmation.Matches(session.TenantId, plan.TenantId)
        && string.Equals(session.OperatorObjectId, plan.OperatorObjectId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(session.ClientId, plan.ClientId, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrEmpty(plan.PlanDigest) && plan.WriteRows.Any();

    public ConfirmTenantDialog(TenantProfile profile, DeploymentPlan plan, TenantSession? session = null)
    {
        InitializeComponent();
        _profile = profile; _plan = plan; _session = session;
        var writes = plan.WriteRows.ToList();
        SummaryText.Text = $"{profile.Company} · {plan.TenantName}\nDomain: {session?.PrimaryDomain ?? profile.Domain}\nSigned in: {session?.Account ?? "Not verified"}\n{writes.Count(r => r.Action == PlanAction.Create)} object(s) to create, {writes.Count(r => r.Action == PlanAction.Update)} to update · plan {plan.Id} (digest {plan.PlanDigest[..Math.Min(12, plan.PlanDigest.Length)]}…)";
        TenantIdText.Text = profile.TenantId;
        foreach (var row in writes)
            ChangeList.Items.Add($"{row.Action,-7} {row.ControlId,-12} {row.Name}  [{row.SafeState}]" + (row.OperatorExclusion is null ? "" : $"  operator excluded: {row.OperatorExclusion.UserPrincipalName}"));
        ApprovalBox.Focus();
    }
    private void OnApprovalChanged(object sender, RoutedEventArgs e)
    { if (DeployButton is not null) DeployButton.IsEnabled = _profile is not null && CanApprove(_profile, _plan, _session, ApprovalBox.IsChecked == true); }
    private void OnConfirm(object sender, RoutedEventArgs e)
    { if (ConfirmedTenantId.Length > 0) DialogResult = true; }
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
