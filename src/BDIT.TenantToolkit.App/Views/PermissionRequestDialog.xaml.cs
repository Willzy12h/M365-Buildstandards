using System.Windows;

namespace BDIT.TenantToolkit.App.Views;

public partial class PermissionRequestDialog : Window
{
    public PermissionRequestDialog(string details)
    { InitializeComponent(); DetailsText.Text = details; ApprovalBox.Focus(); }
    public bool Confirmed { get; private set; }
    public bool ShowForReview(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var cancellation = ct.Register(() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsVisible) Close();
        })));
        ct.ThrowIfCancellationRequested();
        ShowDialog();
        ct.ThrowIfCancellationRequested();
        return Confirmed;
    }
    private void OnApprovalChanged(object sender, RoutedEventArgs e)
    { if (ContinueButton is not null) ContinueButton.IsEnabled = ApprovalBox.IsChecked == true; }
    private void OnContinue(object sender, RoutedEventArgs e)
    {
        if (ApprovalBox.IsChecked != true) return;
        Confirmed = true; DialogResult = true;
    }
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
