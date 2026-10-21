using System.Windows;
using BuildStandard.App.ViewModels;
namespace BuildStandard.App.Views;
public partial class LifecycleConfirmationDialog : Window
{
    private readonly LifecycleViewModel _viewModel;
    public LifecycleConfirmationDialog(LifecycleViewModel viewModel)
    {InitializeComponent();_viewModel=viewModel;DataContext=viewModel;Loaded+=(_,_)=>ProtectedReview.Focus();}
    private void Approve(object sender,RoutedEventArgs args){if(_viewModel.CanApply)DialogResult=true;}
    private void Cancel(object sender,RoutedEventArgs args)=>DialogResult=false;
}
