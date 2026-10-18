using System.ComponentModel;
using System.Windows;
using BuildStandard.App.ViewModels;

namespace BuildStandard.App;

public partial class MainWindow : Window
{
    private bool _closeApproved;
    private bool _closing;
    private ShellViewModel? _navigationShell;
    private long _routeSelectionRevision;

    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => { if (DataContext is BuildStandard.App.ViewModels.ShellViewModel shell) shell.SetViewportWidth(ActualWidth); };
        DataContextChanged += (_, _) => { if (DataContext is BuildStandard.App.ViewModels.ShellViewModel shell) shell.SetViewportWidth(ActualWidth > 0 ? ActualWidth : Width); };
        DataContextChanged += (_, e) =>
        {
            if (_navigationShell is not null) _navigationShell.PropertyChanged -= NavigationChanged;
            _navigationShell = e.NewValue as ShellViewModel;
            if (_navigationShell is not null) _navigationShell.PropertyChanged += NavigationChanged;
            QueueRouteSelection();
        };
        Closed += (_, _) =>
        {
            _routeSelectionRevision++;
            if (_navigationShell is not null) _navigationShell.PropertyChanged -= NavigationChanged;
            _navigationShell = null;
        };
        FitToWorkArea(SystemParameters.WorkArea);
    }

    private void NavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.ActiveRouteKey)) QueueRouteSelection();
    }
    private void QueueRouteSelection()
    {
        if (DataContext is not ShellViewModel shell) return;
        var key = shell.ActiveRouteKey;
        var revision = ++_routeSelectionRevision;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (_closing || revision != _routeSelectionRevision || !ReferenceEquals(DataContext, shell) || shell.ActiveRouteKey != key) return;
            if (shell.ActiveNavItem is { } item && PageSelector.Items.Contains(item))
                PageSelector.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, item);
        }));
    }

    /// <summary>
    /// Opens no larger than the screen. The designed size is 1480x940, but a 1920x1080 laptop at the 150% scaling Windows
    /// recommends for it offers about 1280x672 above the taskbar; opened at the designed size, the bottom of every page
    /// and its buttons would start off-screen.
    /// </summary>
    private void FitToWorkArea(Rect workArea)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0) return;
        Width = Math.Max(MinWidth, Math.Min(Width, workArea.Width));
        Height = Math.Max(MinHeight, Math.Min(Height, workArea.Height));
    }

    private static bool ShowPendingReport(ShellViewModel shell)
    {
        if (!shell.Page<RunReportsViewModel>().HasPendingEvidence)
        {
            if (!shell.Page<DomainReadinessViewModel>().HasPending) return false;
            shell.Navigate("domain-readiness");
            shell.ShowError(new InvalidOperationException("An original domain report is still unsaved. Save the report pack, export the original, or explicitly discard it in Domain readiness before closing. No DNS read was repeated."));
            return true;
        }
        shell.Navigate("run-reports");
        shell.ShowError(new InvalidOperationException("An original report is still unsaved. Save it to its original client, export it, or explicitly discard it in Run reports before closing. No report was run again."));
        return true;
    }

    /// <summary>Never abandons an in-flight tenant write: closing waits for the executor to reach a safe boundary and finish evidence.</summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        ShellViewModel? shell = null;
        try
        {
            if (_closeApproved) return;
            shell = DataContext as ShellViewModel;
            if (shell is null) return;
            e.Cancel = true;
            if (_closing) return;
            if (ShowPendingReport(shell)) return;
            if (shell.Workspace.Busy)
            {
                var message = "An operation is in progress.\n\n" + shell.Workspace.StopGuidance + "\n\nStop and close?";
                if (MessageBox.Show(message, "Operation in progress", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }
            _closing = true;
            IsEnabled = false;
            Title = shell.WindowTitle + " - finishing evidence before closing";
            await shell.Workspace.StopAndWaitForEvidenceAsync();
            // A stopped runner may return truthful partial evidence whose local save failed.
            // Keep the workspace open for recovery before committing to shutdown/disconnect.
            if (ShowPendingReport(shell))
            {
                _closing = false; IsEnabled = true; Title = shell.WindowTitle;
                return;
            }
            await shell.Workspace.ShutdownAsync();
            if (shell.Workspace.Executor.CompletionError.Length > 0)
                MessageBox.Show(shell.Workspace.Executor.CompletionError, "Deployment needs review", MessageBoxButton.OK, MessageBoxImage.Warning);
            _closeApproved = true;
            // Shutdown can complete synchronously; let the current Closing event unwind first.
            await Dispatcher.InvokeAsync(Close);
        }
        catch (Exception ex)
        {
            e.Cancel = true;
            _closeApproved = false;
            IsEnabled = true;
            _closing = false;
            if (shell is not null) { Title = shell.WindowTitle; shell.ShowError(ex); }
            else MessageBox.Show("The toolkit could not close safely. " + ex.Message, "Shutdown needs review", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
