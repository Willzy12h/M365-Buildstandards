using System.IO;
using System.Windows;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BuildStandard.App.Services.Reader;

/// <summary>Lazy document-only browser host. Authoritative operations remain outside this component.</summary>
public sealed class CatalogueReaderHost : IDisposable
{
    private static readonly Guid Instance = Guid.NewGuid();
    private ReaderSessionLease? _lease;
    private CoreWebView2Environment? _environment;
    private WebView2? _web;
    private bool _retiring, _browserExited, _initialising, _environmentRequested;
    private ReaderNavigationPolicy? _policy;
    private Func<bool>? _current;
    private Action<string>? _notice;
    private readonly Action<string>? _cleanupDiagnostic;
    private System.Windows.Threading.DispatcherTimer? _retirementTimer;
    public CatalogueReaderHost(Action<string>? cleanupDiagnostic = null) => _cleanupDiagnostic = cleanupDiagnostic;
    private TaskCompletionSource<bool>? _navigation;
    public WebView2? Control => _web;
    public int DeniedRequests { get; private set; }
    public string Stage { get; private set; } = "Closed";
    public string RuntimeVersion { get; private set; } = "Not initialised";
    public string CleanupStatus { get; private set; } = "No reader cache created";
    public async Task OpenAsync(CatalogueDocument document, string anchor, Func<bool> current,
        Action<WebView2> attach, Action<string> notice, Action<string> pendingReference, Action<string> observedAnchor)
    {
        if (_web is not null || _retiring) throw new InvalidOperationException("Use a fresh host for each reader session.");
        _current = current; _notice = notice; _initialising = true;
        try
        {
        Stage = "Document";
        var bytes = document.CopyHtmlBytes();
        if (CanonicalJson.Sha256Hex(bytes) != document.HtmlSha256) throw new InvalidDataException("Catalogue document byte identity changed.");
        _policy = new(document.Anchors, document.RendererReferences);
        var fragment = document.RequireAnchor(anchor);
        // Runtime detection is deliberately after the native View reference action, never application startup.
        Stage = "Configuration";
        ReaderRuntimeConfiguration.RequireCompatible(Environment.GetEnvironmentVariable, ApplicableRegistryOverride);
        var options = new CoreWebView2EnvironmentOptions { ReleaseChannels = CoreWebView2ReleaseChannels.Stable };
        Stage = "Runtime";
        RuntimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString(null, options);
        if (!current()) return;
        Stage = "Cache";
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildStandard", "Reader");
        _lease = new(root, Instance);
        var html = Path.Combine(_lease.ContentDirectory, "catalogue.html");
        using (var stream = new FileStream(html, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Write(bytes); stream.Flush(true); stream.Position = 0;
            if (CanonicalJson.Sha256Hex(ReadBytes(stream)) != document.HtmlSha256) throw new InvalidDataException("Reader content write did not match the admitted document.");
        }
        Stage = "Host";
        _web = new WebView2 { Visibility = Visibility.Hidden };
        attach(_web);
        _environmentRequested = true;
        _environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _lease.ProfileDirectory, options: options);
        if (!string.Equals(Path.GetFullPath(_environment.UserDataFolder), Path.GetFullPath(_lease.ProfileDirectory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Runtime selected a different profile folder. Reader remains unavailable.");
        _environment.BrowserProcessExited += BrowserExited;
        if (!current() || _retiring) { Dispose(); return; }
        await _web.EnsureCoreWebView2Async(_environment);
        if (!current() || _retiring) { Dispose(); return; }
        var core = _web.CoreWebView2;
        var settings = core.Settings;
        settings.IsScriptEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsWebMessageEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = true;
        // Required API probe. Unsupported Runtime restrictions/Find must fail closed.
        _ = core.Find;
        core.SetVirtualHostNameToFolderMapping("reader.buildstandard.example", _lease.ContentDirectory, CoreWebView2HostResourceAccessKind.Deny);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.NavigationStarting += (_, e) =>
        {
            if (current() && !_retiring && _policy.AllowsDocument(e.Uri) && !e.IsRedirected) return;
            e.Cancel = true; DeniedRequests++;
            if (!_retiring && _policy.AllowsReference(e.Uri, e.IsUserInitiated && !e.IsRedirected, current())) pendingReference(e.Uri);
        };
        core.FrameNavigationStarting += (_, e) => { e.Cancel = true; DeniedRequests++; };
        core.NewWindowRequested += (_, e) => { e.Handled = true; DeniedRequests++; };
        core.DownloadStarting += (_, e) => { e.Cancel = true; DeniedRequests++; };
        core.PermissionRequested += (_, e) => { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; DeniedRequests++; };
        core.LaunchingExternalUriScheme += (_, e) => { e.Cancel = true; DeniedRequests++; };
        core.WebResourceRequested += (_, e) =>
        {
            if (current() && !_retiring && e.ResourceContext == CoreWebView2WebResourceContext.Document
                && _policy.AllowsResource(e.Request.Uri) && e.Request.Method == "GET") return;
            e.Response = _environment.CreateWebResourceResponse(Stream.Null, 403, "Forbidden", "Content-Type: text/plain"); DeniedRequests++;
        };
        core.SourceChanged += (_, _) =>
        {
            if (!current() || _retiring || !_policy.AllowsDocument(core.Source)) return;
            var fragmentValue = new Uri(core.Source).Fragment;
            observedAnchor(fragmentValue.Length > 1 ? Uri.UnescapeDataString(fragmentValue[1..]) : "Catalogue index");
        };
        core.ProcessFailed += (_, _) => Fail("Reader render/process failed. Return to the native catalogue or retry deliberately.");
        _navigation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        core.NavigationCompleted += (_, e) =>
        {
            if (_retiring || !current()) { _navigation?.TrySetCanceled(); return; }
            if (_navigation?.Task.IsCompleted == true) return;
            if (e.IsSuccess && _policy.AllowsDocument(core.Source))
            { _web.Visibility = Visibility.Visible; _navigation?.TrySetResult(true); }
            else { _web.Visibility = Visibility.Hidden; _navigation?.TrySetException(new IOException("Catalogue document navigation failed.")); }
        };
        Stage = "Navigation";
        core.Navigate(ReaderNavigationPolicy.DocumentUrl + fragment);
        await _navigation.Task.WaitAsync(TimeSpan.FromSeconds(30));
        if (!current() || _retiring) Dispose();
        }
        finally
        {
            _initialising = false;
            if (_retiring)
            {
                if (!_environmentRequested) _browserExited = true;
                else if (_environment is not null)
                {
                    try { if (_environment.GetProcessInfos().Count == 0) _browserExited = true; }
                    catch (System.Runtime.InteropServices.COMException) { /* Retirement is unproven. */ }
                }
                Retire();
            }
        }
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool ApplicableRegistryOverride()
    {
        var app = Path.GetFileName(Environment.ProcessPath ?? "BuildStandard.App.exe");
        foreach (var hive in new[] { Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryHive.LocalMachine })
        foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry32, Microsoft.Win32.RegistryView.Registry64 })
        {
            using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
            foreach (var setting in new[] { "BrowserExecutableFolder", "UserDataFolder", "AdditionalBrowserArguments", "ReleaseChannelPreference", "ReleaseChannels", "ChannelSearchKind" })
            {
                using var key = root.OpenSubKey(@"Software\Policies\Microsoft\Edge\WebView2\" + setting, writable: false);
                if (key is null) continue;
                foreach (var id in new[] { "*", app })
                    if (key.GetValue(id) is { } value && !string.IsNullOrEmpty(value.ToString())) return true;
            }
        }
        return false;
    }
    private static byte[] ReadBytes(Stream stream) { using var copy = new MemoryStream(); stream.CopyTo(copy); return copy.ToArray(); }
    public async Task FindAsync(string text)
    {
        if (_retiring || _current?.Invoke() != true || _web?.CoreWebView2 is null || _environment is null) return;
        var options = _environment.CreateFindOptions(); options.FindTerm = text; options.SuppressDefaultFindDialog = false;
        await _web.CoreWebView2.Find.StartAsync(options);
    }
    public void SetZoom(double value) { if (!_retiring && _current?.Invoke() == true && _web is not null) _web.ZoomFactor = Math.Clamp(value, .5, 2); }
    private void Fail(string message)
    {
        if (_web is not null) _web.Visibility = Visibility.Hidden;
        _navigation?.TrySetException(new IOException(message));
        if (!_retiring && _current?.Invoke() == true) _notice?.Invoke(message);
    }
    private void BrowserExited(object? sender, CoreWebView2BrowserProcessExitedEventArgs e)
    {
        _browserExited = true;
        if (_retiring) Retire();
    }
    private void Retire()
    {
        if (_lease is null) { CleanupStatus = "No reader cache created"; return; }
        if (_initialising || !_browserExited)
        {
            CleanupStatus = "Reader cache retirement pending";
            if (_retirementTimer is null)
            {
                _retirementTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                _retirementTimer.Tick += (_, _) =>
                {
                    _retirementTimer.Stop();
                    if (!_browserExited) { CleanupStatus = "Reader cache cleanup incomplete"; _cleanupDiagnostic?.Invoke(CleanupStatus); _lease?.Dispose(); }
                };
                _retirementTimer.Start();
            }
            return;
        }
        _retirementTimer?.Stop();
        CleanupStatus = _lease.Retire(true) ? "Reader cache retired" : "Reader cache cleanup incomplete";
        _cleanupDiagnostic?.Invoke(CleanupStatus);
        if (_environment is not null) _environment.BrowserProcessExited -= BrowserExited;
        _lease.Dispose();
    }
    public void Dispose()
    {
        if (_retiring) return;
        _retiring = true;
        _navigation?.TrySetCanceled();
        if (_web is not null) { _web.Visibility = Visibility.Collapsed; _web.Dispose(); }
        // Only a proved browser exit (all associated processes exited) admits deletion.
        if (!_environmentRequested && !_initialising) _browserExited = true;
        Retire();
    }
}
