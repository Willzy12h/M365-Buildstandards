using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Threading;
using BDIT.TenantToolkit.App.ViewModels;

namespace BDIT.TenantToolkit.App.Views;

public partial class ConnectView : UserControl
{
    private ConnectViewModel? _viewModel;

    public ConnectView()
    {
        InitializeComponent();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
        DataContextChanged += (_, _) => { if (IsLoaded) Subscribe(); };
    }

    private void Subscribe()
    {
        Unsubscribe();
        _viewModel = DataContext as ConnectViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnDiscoveryChanged;
    }

    private void Unsubscribe()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnDiscoveryChanged;
        _viewModel = null;
    }

    private void OnDiscoveryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConnectViewModel.HasDiscoveredTenant)) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (IsLoaded && _viewModel?.HasDiscoveredTenant == true) ConfirmQuickConnectButton.BringIntoView();
        });
    }
}
