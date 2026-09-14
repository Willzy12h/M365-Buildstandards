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
        if (_closeApproved) return;
        if (DataContext is not ShellViewModel shell) return;
        e.Cancel = true;
        if (_closing) return;
        if (shell.Workspace.Busy)
        {
            var deploying = shell.Workspace.Executor.IsRunning;
            var message = deploying
                ? "A deployment is in progress.\n\nThe toolkit will stop at the next safe boundary, finish the current write, capture the after-change snapshot and then close.\n\nStop and close?"
                : "An operation is in progress.\n\nThe toolkit will request cancellation, wait for the operation to finish and disconnect before closing.\n\nStop and close?";
            var result = MessageBox.Show(message, deploying ? "Deployment in progress" : "Operation in progress", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }
        _closing = true;
        IsEnabled = false;
        Title = shell.WindowTitle + " - finishing evidence before closing";
        try
        {
            await shell.Workspace.ShutdownAsync();
            _closeApproved = true;
            // Shutdown can complete synchronously; let the current Closing event unwind first.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
        catch (Exception ex)
        {
            shell.ShowError(ex);
            IsEnabled = true;
            _closing = false;
        }
    }
}
