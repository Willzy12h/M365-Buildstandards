using System.Windows.Controls;
using System.Collections.Specialized;
using System.Windows.Threading;
using BDIT.TenantToolkit.App.ViewModels;
namespace BDIT.TenantToolkit.App.Views;
public partial class ApplicationSetupView : UserControl
{
    private ApplicationSetupViewModel? _viewModel;
    public ApplicationSetupView()
    {
        InitializeComponent();
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
        DataContextChanged += (_, _) => { if (IsLoaded) Subscribe(); };
    }
    private void Subscribe()
    {
        Unsubscribe();
        _viewModel = DataContext as ApplicationSetupViewModel;
        if (_viewModel is not null) _viewModel.PlanRows.CollectionChanged += OnPreview;
    }
    private void Unsubscribe()
    {
        if (_viewModel is not null) _viewModel.PlanRows.CollectionChanged -= OnPreview;
        _viewModel = null;
    }
    private void OnPreview(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (IsLoaded && _viewModel?.PlanRows.Count > 0) CreateApplicationsButton.BringIntoView();
        });
    }
}
