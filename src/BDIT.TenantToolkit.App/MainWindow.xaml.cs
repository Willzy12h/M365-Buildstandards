using System.ComponentModel;
using System.Windows;
using BDIT.TenantToolkit.App.ViewModels;

namespace BDIT.TenantToolkit.App;

public partial class MainWindow : Window
{
    private bool _closeApproved;
    private bool _closing;

    public MainWindow() => InitializeComponent();

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
            if (shell.Workspace.Busy)
            {
                var message = "An operation is in progress.\n\n" + shell.Workspace.StopGuidance + "\n\nStop and close?";
                if (MessageBox.Show(message, "Operation in progress", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            }
            _closing = true;
            IsEnabled = false;
            Title = shell.WindowTitle + " - finishing evidence before closing";
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
