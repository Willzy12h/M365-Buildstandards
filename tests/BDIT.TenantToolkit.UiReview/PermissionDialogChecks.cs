using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BDIT.TenantToolkit.App.Views;

internal static partial class Program
{
    private static void CheckPermissionRequestDialog(string output)
    {
        const string details = "Purpose: synthetic read-only access\nTarget tenant: Example client (not yet verified)\nApplication (client) ID: cccccccc-cccc-cccc-cccc-cccccccccccc\nResource: Microsoft Graph\nAccount: Microsoft will ask you to choose\n\nExact permissions:\nUser.Read\nOrganization.Read.All";
        var dialog = new PermissionRequestDialog(details)
        { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        try
        {
            if (dialog.Content is not FrameworkElement root) throw new InvalidOperationException("The access request has no content.");
            var approval = Part<CheckBox>(dialog, "ApprovalBox");
            var confirm = Part<Button>(dialog, "ContinueButton");
            if (dialog.Confirmed || approval.IsChecked == true || confirm.IsEnabled)
                throw new InvalidOperationException("Access request approval is not off by default.");
            if (Part<TextBox>(dialog, "DetailsText").Text != details)
                throw new InvalidOperationException("Access request does not display its exact reviewed context.");
            foreach (var size in new[] { new Size(960, 760), new Size(680, 620), new Size(540, 460) })
            {
                var where = $"permission-request {(int)size.Width}x{(int)size.Height}";
                dialog.Width = size.Width; dialog.Height = size.Height;
                dialog.Show(); Pump(); root.UpdateLayout(); Pump();
                RecordUnnamedControls(root, where); RecordLowContrast(root, where); RecordClipping(root, where);
                var bounds = VisibleBounds(confirm, root);
                if (bounds.Width < confirm.ActualWidth - 1 || bounds.Height < confirm.ActualHeight - 1)
                    throw new InvalidOperationException("Access request continue button is clipped at " + where);
                SaveWindowImage(root, Path.Combine(output, $"permission-request-{(int)size.Width}x{(int)size.Height}.png"));
                DialogChecks++;
            }
            dialog.Activate(); approval.Focus(); Pump();
            if (!ReferenceEquals(Keyboard.FocusedElement, approval))
                KeyboardProblems.Add("  permission-request · deliberate approval cannot receive keyboard focus");
            foreach (var value in new[] { true, false })
            {
                approval.IsChecked = value; Pump();
                if (confirm.IsEnabled != value) throw new InvalidOperationException("Access request button ignores unticked approval.");
            }
            if (dialog.Confirmed) throw new InvalidOperationException("Merely ticking approval authorised the request.");
        }
        finally { dialog.Close(); Pump(); }

        CheckPermissionReviewCancellation(details, alreadyCancelled: false);
        CheckPermissionReviewCancellation(details, alreadyCancelled: true);
    }

    private static void CheckPermissionReviewCancellation(string details, bool alreadyCancelled)
    {
        using var stop = new CancellationTokenSource();
        var dialog = new PermissionRequestDialog(details)
        { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        var opened = false; var expired = false;
        // A broken cancellation registration must fail rather than leave Windows CI in a modal dialog forever.
        var deadline = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        deadline.Tick += (_, _) => { expired = true; dialog.Close(); };
        dialog.Loaded += (_, _) =>
        {
            opened = true;
            Part<CheckBox>(dialog, "ApprovalBox").IsChecked = true;
            ThreadPool.QueueUserWorkItem(_ => stop.Cancel());
        };
        if (alreadyCancelled) stop.Cancel();
        try
        {
            deadline.Start();
            try
            {
                dialog.ShowForReview(stop.Token);
                throw new InvalidOperationException("Cancelled access review returned approval instead of cancellation.");
            }
            catch (OperationCanceledException)
            {
                if (expired || dialog.IsVisible || dialog.Confirmed || opened == alreadyCancelled)
                    throw new InvalidOperationException("Cancelled access review did not promptly close without approval.");
            }
            DialogChecks++;
        }
        finally { deadline.Stop(); dialog.Close(); Pump(); }
    }
}
