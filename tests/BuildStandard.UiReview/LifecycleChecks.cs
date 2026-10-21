using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using BuildStandard.App.ViewModels;
using BuildStandard.App.Views;

internal static partial class Program
{
    private static void CheckLifecycleNativeFlows(ShellViewModel shell, Window window, FrameworkElement content, string output)
    {
        shell.Navigate("lifecycle");
        var page = shell.Page<LifecycleViewModel>();
        foreach (var size in PageSizes)
        {
            FitNativeReportClientSize(window, content, size);
            var tabs = Descendants(content).OfType<TabControl>().Single(t => t.Items.OfType<TabItem>().Any(i => Equals(i.Header, "Select and check")));
            tabs.SelectedIndex = 0; content.UpdateLayout(); Pump();
            var user = Descendants(content).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Exact lifecycle target user ID");
            // A GUID absent from cached choices must survive the actual WPF ComboBox/TextBox binding cycle.
            user.Text = Id(999); content.UpdateLayout(); Pump();
            if (page.SelectedUserId != Id(999)) throw new InvalidOperationException("Lifecycle manual user ID was lost to an empty captured picker.");
            user.BringIntoView(); content.UpdateLayout(); Pump();
            if (!user.Focus() || !user.IsTabStop) throw new InvalidOperationException("Lifecycle manual target is not keyboard reachable.");
            if (page.CanApply || page.CanReviewAction || string.IsNullOrWhiteSpace(page.ApplyGuidance))
                throw new InvalidOperationException("Lifecycle idle navigation granted action authority or omitted the next step.");
            CaptureReportState(content, output, "lifecycle-select-manual-target", size, checkPageScrolling: false);
            tabs.SelectedIndex = 1; content.UpdateLayout(); Pump();
            CaptureReportState(content, output, "lifecycle-exact-review-closed", size, checkPageScrolling: false);
            tabs.SelectedIndex = 2; AwaitReader(page.LoadHistoryAsync()); content.UpdateLayout(); Pump();
            if (page.CanApply || !page.HistoryStatus.Contains("attempts", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Lifecycle local history lost its non-authorising meaning.");
            CaptureReportState(content, output, "lifecycle-local-handover", size, checkPageScrolling: false);
        }
        var dialog = new LifecycleConfirmationDialog(page) { Owner = window, ShowActivated = false };
        dialog.Show(); Pump(); dialog.UpdateLayout();
        try
        {
            var buttons = Descendants(dialog).OfType<Button>().ToArray();
            if (!buttons.Single(b => Equals(b.Content, "Cancel")).IsDefault
                || buttons.Single(b => Equals(b.Content, "Approve this one action")).IsEnabled)
                throw new InvalidOperationException("Lifecycle confirmation default or absent-preview refusal is unsafe.");
            SaveImage(dialog, new Size(dialog.ActualWidth, dialog.ActualHeight), Path.Combine(output, "lifecycle-confirmation-no-authority.png"));
        }
        finally { dialog.Close(); }
        page.SelectedUserId = "";
    }
}
