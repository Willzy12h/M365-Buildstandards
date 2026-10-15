using System.Windows;
using BuildStandard.App.ViewModels;
namespace BuildStandard.App.Views;
public partial class StandardView
{
    private void InitialiseReferenceReturn()
    {
        Unloaded += (_, _) => { if (DataContext is StandardViewModel vm) vm.ReferenceScrollOffset = CataloguePageScroll.VerticalOffset; };
        Loaded += (_, _) =>
        {
            if (DataContext is not StandardViewModel vm || vm.ReferenceReturnTicket is not { } ticket || !vm.CanRestoreReferenceReturn) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(vm.Shell.CurrentPage, vm) && vm.ReferenceReturnRequested && vm.IsCurrentReferenceReturn(ticket))
                { CataloguePageScroll.ScrollToVerticalOffset(vm.ReferenceScrollOffset); ViewReferenceButton.Focus(); }
                vm.FinishReferenceReturn(ticket);
            });
        };
    }
}
