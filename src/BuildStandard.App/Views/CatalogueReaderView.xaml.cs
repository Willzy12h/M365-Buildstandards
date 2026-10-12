using System.IO;
using System.Windows;
using System.Windows.Controls;
using BuildStandard.App.Services.Reader;
using BuildStandard.App.ViewModels;
using Microsoft.Web.WebView2.Core;
namespace BuildStandard.App.Views;
public partial class CatalogueReaderView : UserControl
{
    private CatalogueReaderHost? _host;
    private CatalogueReaderViewModel? _vm;
    private double _zoom = 1;
    public CatalogueReaderView() { InitializeComponent(); Loaded += Open; Unloaded += Close; }
    private async void Open(object sender, RoutedEventArgs e)
    {
        if (_host is not null || DataContext is not CatalogueReaderViewModel vm || vm.Document is not { } document) return;
        _vm = vm; _host = new CatalogueReaderHost(vm.RecordCleanup);
        vm.Closing += Retire; vm.FindRequested += Find; vm.ZoomRequested += Zoom;
        vm.Loading("Checking the managed WebView2 Runtime and owned local cache.");
        try
        {
            await _host.OpenAsync(document, vm.ControlId, () => vm.IsCurrent,
                control => BrowserSlot.Children.Add(control),
                notice => vm.Failed("Failed", notice), vm.SetPendingReference, vm.ObserveAnchor).WaitAsync(TimeSpan.FromSeconds(45));
            if (vm.IsCurrent) vm.Ready(_host.RuntimeVersion);
        }
        catch (WebView2RuntimeNotFoundException) { vm.Failed("Unavailable", "Managed WebView2 Runtime was not found. Native catalogue and HTML export remain available; no Runtime is installed by this app."); Retire(); }
        catch (OperationCanceledException) { Retire(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException or NotImplementedException or System.Security.SecurityException)
        {
            var stage = _host?.Stage;
            vm.Failed(stage == "Cache" ? "CacheUnavailable" : stage == "Document" ? "Invalid" : "Failed",
                stage == "Cache" ? "Reader cache could not be created or verified. Native catalogue and the separate HTML export destination remain available."
                : stage == "Configuration" ? "Reader Runtime/profile overrides could not be admitted. Ask the maintainer/security owner to review the configuration; native catalogue and exports remain available."
                : "Reader could not initialise or display this document. Return to the native catalogue; a detected Runtime does not prove policy or cache access.");
            Retire();
        }
    }
    private async void Find(string text)
    {
        try { if (_host is not null) await _host.FindAsync(text); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        { _vm?.Failed("Failed", "Native reader find is unavailable on this Runtime."); Retire(); }
    }
    private void Zoom(double delta) { _zoom = Math.Clamp(_zoom + delta, .5, 2); _host?.SetZoom(_zoom); }
    private void Retire() { _host?.Dispose(); BrowserSlot.Children.Clear(); }
    private void Close(object sender, RoutedEventArgs e)
    {
        _vm?.Close(); Retire();
        if (_vm is not null) { _vm.Closing -= Retire; _vm.FindRequested -= Find; _vm.ZoomRequested -= Zoom; }
    }
}
