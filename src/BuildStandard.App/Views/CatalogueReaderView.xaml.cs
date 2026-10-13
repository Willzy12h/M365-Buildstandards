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
    public CatalogueReaderView()
    {
        InitializeComponent(); Loaded += Open; Unloaded += Close;
        DataContextChanged += (_, _) => { if (IsLoaded) { Detach(); Open(this, new RoutedEventArgs()); } };
    }
    private async void Open(object sender, RoutedEventArgs e)
    {
        if (_host is not null || DataContext is not CatalogueReaderViewModel vm || vm.Document is not { } document) return;
        _vm = vm; var host = new CatalogueReaderHost(vm.RecordCleanup); _host = host; _zoom = 1;
        bool Current() => IsLoaded && ReferenceEquals(_vm, vm) && ReferenceEquals(_host, host) && vm.IsCurrent;
        vm.Closing += Retire; vm.FindRequested += Find; vm.ZoomRequested += Zoom;
        vm.Loading("Checking the managed WebView2 Runtime and owned local cache.");
        try
        {
            await host.OpenAsync(document, vm.ControlId, Current,
                control => { if (Current()) BrowserSlot.Children.Add(control); else host.Dispose(); },
                notice => { if (Current()) vm.Failed("Failed", notice); },
                url => { if (Current()) vm.SetPendingReference(url); },
                anchor => { if (Current()) vm.ObserveAnchor(anchor); }).WaitAsync(TimeSpan.FromSeconds(45));
            if (Current()) vm.Ready(host.RuntimeVersion); else host.Dispose();
        }
        catch (Exception ex) when (ex is InvalidDataException or BuildStandard.Core.ConfigurationException)
        {
            if (Current()) vm.Failed("Invalid", "Catalogue document identity could not be verified. Return to the native catalogue and regenerate its reference; this document cannot be saved or displayed.");
            Retire(host);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            if (Current()) vm.Failed("Unavailable", "Managed WebView2 Runtime was not found. Native catalogue and HTML export remain available; no Runtime is installed by this app.");
            Retire(host);
        }
        catch (OperationCanceledException) { Retire(host); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException or TimeoutException or NotImplementedException or System.Security.SecurityException)
        {
            if (Current())
            {
                var stage = host.Stage;
                vm.Failed(stage == "Cache" ? "CacheUnavailable" : stage == "Document" ? "Invalid" : "Failed",
                    stage == "Cache" ? "Reader cache could not be created or verified. Native catalogue and the separate HTML export destination remain available."
                    : stage == "Configuration" ? "Reader Runtime/profile overrides could not be admitted. Ask the maintainer/security owner to review the configuration; native catalogue and exports remain available."
                    : "Reader could not initialise or display this document. Return to the native catalogue; a detected Runtime does not prove policy or cache access.");
            }
            Retire(host);
        }
    }
    private async void Find(string text)
    {
        var host = _host; var vm = _vm;
        if (host is null || vm?.IsReady != true) return;
        try { await host.FindAsync(text); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            if (ReferenceEquals(_host, host) && ReferenceEquals(_vm, vm) && vm.IsCurrent)
                vm.Failed("Failed", "Native reader find is unavailable on this Runtime.");
            Retire(host);
        }
    }
    private void Zoom(double delta)
    {
        if (_vm?.IsReady != true) return;
        _zoom = Math.Clamp(_zoom + delta, .5, 2); _host?.SetZoom(_zoom);
    }
    private void Retire() { if (_host is { } host) Retire(host); }
    private void Retire(CatalogueReaderHost host)
    {
        host.Dispose();
        if (ReferenceEquals(_host, host)) { _host = null; BrowserSlot.Children.Clear(); }
    }
    private void Detach()
    {
        var vm = _vm;
        if (vm is not null)
        {
            vm.Closing -= Retire; vm.FindRequested -= Find; vm.ZoomRequested -= Zoom;
            vm.Close();
        }
        Retire(); _vm = null;
    }
    private void Close(object sender, RoutedEventArgs e) => Detach();
}
