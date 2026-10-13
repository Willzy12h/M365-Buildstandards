using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using BuildStandard.App.Services.Reader;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;
using Microsoft.Web.WebView2.Core;

internal static partial class Program
{
    private static void AwaitReader(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(45)) { Pump(); Thread.Sleep(10); }
        if (!task.IsCompleted) throw new TimeoutException("Hosted reader did not finish within its bounded synthetic check.");
        task.GetAwaiter().GetResult();
    }
    private static int CheckHostedCatalogueReader(BuildStandard.App.Services.Workspace workspace, BuildStandard.App.ViewModels.ShellViewModel shell, Window window, string output)
    {
        Directory.CreateDirectory(output);
        window.ShowInTaskbar=false; window.ShowActivated=true;
        window.WindowStartupLocation=WindowStartupLocation.Manual; window.Left=-10000; window.Top=-10000;
        var records = new List<object>();
        // Private construction is confined to this hostile synthetic fixture. Production consumes T8's verified descriptor.
        var html = Encoding.UTF8.GetBytes("""
            <!doctype html><html><head><meta http-equiv="refresh" content="1;url=https://learn.microsoft.com/en-us/entra/"></head><body><h1 id="CA-001">Safe catalogue fixture</h1><h2 id="index">Index</h2>
            <script>document.body.innerText='SCRIPT EXECUTED MARKER';</script>
            <iframe src="https://reader.buildstandard.example/catalogue.html"></iframe>
            <img src="https://blocked.buildstandard.example/never.png">
            <a href="https://learn.microsoft.com/en-us/entra/">Microsoft reference</a></body></html>
            """);
        var constructor = typeof(CatalogueDocument).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var fixture = (CatalogueDocument)constructor.Invoke(["synthetic-host-test", new string('a',64), "synthetic-negative-only", DateTimeOffset.UtcNow,
            html, new[] { "CA-001", "index" }, new[] { "https://learn.microsoft.com/en-us/entra/" }]);
        var nativeContent = window.Content;
        var grid = new Grid(); window.Content = grid; window.Width = 1180; window.Height = 760; window.Show(); Pump();
        using (var host = new CatalogueReaderHost())
        {
            var current = true; var pending = ""; var observed = ""; var failure = "";
            AwaitReader(host.OpenAsync(fixture, "CA-001", () => current, c => grid.Children.Add(c), n => failure=n, u => pending=u, a => observed=a));
            if (host.Control?.CoreWebView2 is not { } core) throw new InvalidOperationException("Hosted reader has no ready browser.");
            AwaitReader(host.FindAsync("SCRIPT EXECUTED MARKER"));
            if (core.Find.MatchCount != 0) throw new InvalidOperationException("Host allowed injected page script to change the document.");
            AwaitReader(host.FindAsync("Safe catalogue fixture"));
            if (core.Find.MatchCount != 1) throw new InvalidOperationException("Safe fixture text was not rendered/searchable through native Find.");
            if (host.DeniedRequests < 2) throw new InvalidOperationException("Injected frame/resource refusal was not observed.");
            // Native Core.Navigate is itself classified user-initiated by the Runtime. Exercise the
            // host-owned programmatic admission, not a false assumption about that SDK flag.
            foreach (var url in new[] { "file:///C:/not-a-reader-document", "data:text/html,blocked", "https://reader.buildstandard.example/catalogue.html?extra=1", "https://learn.microsoft.com/en-us/entra/" })
            {
                var before=host.DeniedRequests;
                if (host.TryNavigateDocument(url)) throw new InvalidOperationException("Host admitted a forbidden programmatic document target.");
                Pump();
                if(host.DeniedRequests<=before || pending.Length!=0) throw new InvalidOperationException($"Unexpected/programmatic navigation was not refused without external-link admission: requested={url}; denials={before}->{host.DeniedRequests}; pending={pending}; source={core.Source}.");
            }
            // A script-free meta refresh originates inside browser content without a user gesture.
            var refreshTimer=Stopwatch.StartNew(); while(host.DeniedNonUserNavigations == 0 && refreshTimer.Elapsed<TimeSpan.FromSeconds(5)){Pump();Thread.Sleep(10);}
            if (host.DeniedNonUserNavigations == 0 || pending.Length != 0 || !new ReaderNavigationPolicy(["CA-001", "index"], []).AllowsDocument(core.Source))
                throw new InvalidOperationException($"Non-user browser refresh was not observed refused: nonUserDenials={host.DeniedNonUserNavigations}; pending={pending}; source={core.Source}.");
            if (!host.TryNavigateDocument(ReaderNavigationPolicy.DocumentUrl+"#index")) throw new InvalidOperationException("Valid same-document anchor was refused.");
            var anchorTimer=Stopwatch.StartNew(); while(observed!="index" && anchorTimer.Elapsed<TimeSpan.FromSeconds(5)){Pump();Thread.Sleep(10);}
            if(observed!="index") throw new InvalidOperationException("Same-document SourceChanged did not report the observed anchor.");
            var exact=workspace.Exporter.SaveHtml(fixture);
            if(!File.ReadAllBytes(exact).SequenceEqual(html) || CanonicalJson.Sha256Hex(File.ReadAllBytes(exact))!=fixture.HtmlSha256)
                throw new InvalidOperationException("Hosted document's explicit save was not the admitted bytes.");
            using(var image=File.Create(Path.Combine(output,"reader-negative-fixture.png"))) AwaitReader(core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image));
            records.Add(new { Case="hostile synthetic fixture", ScriptMutation="refused", FrameAndResource="refused", ProgrammaticExternalLink="refused", Fragment="observed", ExactSave="passed", host.RuntimeVersion, host.DeniedRequests, host.DeniedNonUserNavigations });
            // Terminate only this isolated fixture's main renderer, never a browser/shared process or process tree.
            // The production host has no process-termination API. The environment owns a fresh GUID profile and one controller.
            var infos=core.Environment.GetProcessExtendedInfosAsync(); AwaitReader(infos);
            var renderer=infos.Result.Single(info => info.ProcessInfo.Kind==CoreWebView2ProcessKind.Renderer
                && info.AssociatedFrameInfos.Any(frame=>frame.ParentFrameInfo is null && frame.FrameId==core.FrameId
                    && new ReaderNavigationPolicy(["CA-001", "index"], []).AllowsDocument(frame.Source))).ProcessInfo;
            if(renderer.ProcessId==core.BrowserProcessId) throw new InvalidOperationException("Refusing to terminate the browser process.");
            using(var process=Process.GetProcessById(checked((int)renderer.ProcessId)))
            {
                _=process.StartTime; // Resolve the exact owned process before the final environment membership check.
                if(!core.Environment.GetProcessInfos().Any(info=>info.ProcessId==renderer.ProcessId && info.Kind==CoreWebView2ProcessKind.Renderer))
                    throw new InvalidOperationException("Owned renderer changed; no process was terminated.");
                process.Kill();
            }
            var failureTimer=Stopwatch.StartNew(); while(failure.Length==0 && failureTimer.Elapsed<TimeSpan.FromSeconds(10)){Pump();Thread.Sleep(10);}
            if(!failure.Contains("render/process failed",StringComparison.Ordinal) || host.Control.Visibility==Visibility.Visible)
                throw new InvalidOperationException("Owned renderer failure did not hide content and announce its native failure.");
            records.Add(new { Case="isolated synthetic main-renderer failure", HiddenAndAnnounced="passed", BrowserOrSharedProcessTerminated=false });
            current=false; host.Dispose(); grid.Children.Clear();
            var exitTimer=Stopwatch.StartNew(); while(host.CleanupStatus!="Reader cache retired" && exitTimer.Elapsed<TimeSpan.FromSeconds(15)){Pump();Thread.Sleep(10);}
            records.Add(new { Case="superseded owned reader retirement", host.CleanupStatus });
            if(host.CleanupStatus!="Reader cache retired") throw new InvalidOperationException("Owned profile retirement was not proved; cache cleanup incomplete.");
        }
        window.Content=nativeContent;
        shell.Navigate("standard"); Pump();
        // Exercise the real native view's exception boundary before any Runtime/browser is needed.
        // Corruption is injected into a private synthetic descriptor, never through a production mutation API.
        var damaged = new CatalogueDocumentService(workspace.Paths).Create(workspace.RequireStandard());
        var backing = (byte[])typeof(CatalogueDocument).GetField("_html", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(damaged)!;
        backing[0] ^= 1;
        Exception? invalidUnhandled = null;
        System.Windows.Threading.DispatcherUnhandledExceptionEventHandler invalidTrap = (_, e) =>
        {
            if (e.Exception is InvalidDataException) { invalidUnhandled = e.Exception; e.Handled = true; }
        };
        window.Dispatcher.UnhandledException += invalidTrap;
        try
        {
            AwaitReader(shell.ShowCatalogueReferenceAsync(damaged, "CA-001", workspace.CapturePresentationContext()));
            var invalidTimer = Stopwatch.StartNew();
            while (shell.CurrentPage is BuildStandard.App.ViewModels.CatalogueReaderViewModel pendingInvalid
                && pendingInvalid.State is "Preparing" or "Loading" && invalidUnhandled is null
                && invalidTimer.Elapsed < TimeSpan.FromSeconds(10)) { Pump(); Thread.Sleep(10); }
            if (shell.CurrentPage is not BuildStandard.App.ViewModels.CatalogueReaderViewModel invalidReader
                || invalidReader.State != "Invalid" || invalidUnhandled is not null)
                throw new InvalidOperationException("Invalid catalogue was not contained in the native reader: state="
                    + (shell.CurrentPage as BuildStandard.App.ViewModels.CatalogueReaderViewModel)?.State
                    + "; dispatcher exception=" + invalidUnhandled?.GetType().Name);
            var invalidView = Descendants((FrameworkElement)window.Content).OfType<BuildStandard.App.Views.CatalogueReaderView>().Single();
            var retired = typeof(BuildStandard.App.Views.CatalogueReaderView).GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(invalidView) is null;
            if (!retired || invalidReader.SaveCommand.CanExecute(null)
                || Descendants(invalidView).OfType<Microsoft.Web.WebView2.Wpf.WebView2>().Any())
                throw new InvalidOperationException("Invalid document retained its host, browser or Save authority.");
            records.Add(new { Case = "native invalid catalogue document", InvalidState = "passed", GenericDispatcherError = false, BrowserCreated = false, HostRetired = true });
        }
        finally { window.Dispatcher.UnhandledException -= invalidTrap; shell.Navigate("standard"); Pump(); }
        var standard=shell.Page<BuildStandard.App.ViewModels.StandardViewModel>();
        standard.Search="CA-001"; standard.Selected=standard.Controls.Single(c=>c.Id=="CA-001");
        if(!standard.ViewReferenceCommand.CanExecute(null)) throw new InvalidOperationException("Native selected-control reference entry is unavailable.");
        standard.ViewReferenceCommand.Execute(null);
        var readyTimer=Stopwatch.StartNew();
        while((shell.CurrentPage is not BuildStandard.App.ViewModels.CatalogueReaderViewModel candidate || candidate.State is "Preparing" or "Loading")
            && readyTimer.Elapsed<TimeSpan.FromSeconds(45)){Pump();Thread.Sleep(10);}
        if(shell.CurrentPage is not BuildStandard.App.ViewModels.CatalogueReaderViewModel reader || reader.State!="Ready")
            throw new InvalidOperationException("Native catalogue reader did not reach Ready: "+(shell.CurrentPage as BuildStandard.App.ViewModels.CatalogueReaderViewModel)?.Status);
        var pageRoot=(FrameworkElement)window.Content;
        Pump();
        var nativeProblems=new List<string>();
        foreach(var label in new[]{"Find","Zoom in","Zoom out"})
        {
            var button=Descendants(pageRoot).OfType<Button>().Single(b=>b.Content?.ToString()==label);
            if(!button.IsEnabled) nativeProblems.Add("Ready reader native control remained disabled: "+label);
        }
        var selector=Descendants(pageRoot).OfType<ComboBox>().Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Page in the selected navigation group");
        if(selector.SelectedItem is not BuildStandard.App.ViewModels.NavItem {Key:"standard"})
            nativeProblems.Add($"Reader route selector is empty or different: shell={shell.ActiveNavItem?.Key}; selected={selector.SelectedItem}; items={selector.Items.Count}.");
        if(nativeProblems.Count>0) throw new InvalidOperationException(string.Join(" | ",nativeProblems));
        var browser=Descendants(pageRoot).OfType<Microsoft.Web.WebView2.Wpf.WebView2>().Single();
        reader.FindText="Require MFA"; reader.FindCommand.Execute(null);
        var findTimer=Stopwatch.StartNew(); while(browser.CoreWebView2.Find.MatchCount==0 && findTimer.Elapsed<TimeSpan.FromSeconds(5)){Pump();Thread.Sleep(10);}
        if(browser.CoreWebView2.Find.MatchCount==0) throw new InvalidOperationException("Native Find command did not reach the displayed catalogue.");
        var originalZoom=browser.ZoomFactor; reader.ZoomInCommand.Execute(null); Pump();
        if(browser.ZoomFactor<=originalZoom) throw new InvalidOperationException("Native zoom command did not reach the current browser.");
        reader.ZoomOutCommand.Execute(null); Pump();
        using(var sizing=new NativeHarnessTrackBounds(window,pageRoot,PageSizes))
        {
        foreach(var size in PageSizes)
        {
            FitNativeReportClientSize(window,pageRoot,size);
            var where=$"hosted-reader {(int)size.Width}x{(int)size.Height}";
            RecordUnnamedControls(pageRoot,where); RecordLowContrast(pageRoot,where); RecordClipping(pageRoot,where);
            CheckKeyboardReach(window,pageRoot,where);
            if(browser.ActualHeight<120) throw new InvalidOperationException($"Contained reader work area is cramped at {where}: {browser.ActualHeight}px.");
            SaveWindowImage(pageRoot,Path.Combine(output,$"reader-native-chrome-{(int)size.Width}x{(int)size.Height}.png"));
            using(var capture=File.Create(Path.Combine(output,$"reader-document-{(int)size.Width}x{(int)size.Height}.png")))
                AwaitReader(browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,capture));
        }
        }
        if(UnnamedControls.Count+LowContrast.Count+ClippedElements.Count+KeyboardProblems.Count>0)
            throw new InvalidOperationException("Hosted native presentation failures: "+string.Join(" | ",UnnamedControls.Concat(LowContrast).Concat(ClippedElements).Concat(KeyboardProblems)));
        records.Add(new { Case="native reader presentation at all three logical sizes", AccessibleNames="passed", Contrast="passed", Clipping="passed", NativeKeyboardReach="passed", HumanNarratorAndPhysicalScaling="unrun" });
        using(var image=File.Create(Path.Combine(output,"reader-engineer-catalogue.png"))) AwaitReader(browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image));
        SaveWindowImage((FrameworkElement)window.Content,Path.Combine(output,"reader-native-chrome.png")); // HWND content is separately captured above; never claim a combined browser bitmap.
        var beforeFiles=Directory.GetFiles(workspace.Paths.ReportsDirectory,"*.html").ToHashSet(StringComparer.OrdinalIgnoreCase);
        reader.SaveCommand.Execute(null);
        var saveTimer=Stopwatch.StartNew(); while(reader.Saved.Length==0 && saveTimer.Elapsed<TimeSpan.FromSeconds(15)){Pump();Thread.Sleep(10);}
        var saved=Directory.GetFiles(workspace.Paths.ReportsDirectory,"*.html").Except(beforeFiles,StringComparer.OrdinalIgnoreCase).Single();
        if(!File.ReadAllBytes(saved).SequenceEqual(reader.Document!.CopyHtmlBytes())) throw new InvalidOperationException("Native Save HTML differed from the displayed descriptor.");
        records.Add(new { Case="native View reference and actual T8 Engineer catalogue", reader.Document.CatalogueRelease,reader.Document.CatalogueDigest,reader.Document.HtmlSha256,reader.Document.PresentationVersion, Runtime=browser.CoreWebView2.Environment.BrowserVersionString, ExactNativeSave="passed" });
        // Retry is a deliberate new owned session, even when WPF reuses the same data-template view.
        var previousReader=reader; var previousBrowser=browser;
        reader.Failed("Failed","Synthetic native retry scenario; no tenant operation.");
        if(!reader.RetryCommand.CanExecute(null)) throw new InvalidOperationException("Current failed reader cannot offer deliberate Retry.");
        reader.RetryCommand.Execute(null);
        var retryTimer=Stopwatch.StartNew();
        while((shell.CurrentPage is not BuildStandard.App.ViewModels.CatalogueReaderViewModel replacement || ReferenceEquals(replacement,previousReader) || replacement.State is "Preparing" or "Loading")
            && retryTimer.Elapsed<TimeSpan.FromSeconds(45)){Pump();Thread.Sleep(10);}
        reader=shell.CurrentPage as BuildStandard.App.ViewModels.CatalogueReaderViewModel ?? throw new InvalidOperationException("Retry left the native reader route.");
        if(ReferenceEquals(reader,previousReader) || reader.State!="Ready" || previousReader.IsCurrent)
            throw new InvalidOperationException("Deliberate native Retry did not display a fresh current session: "+reader.State+" · "+reader.Status);
        browser=Descendants((FrameworkElement)window.Content).OfType<Microsoft.Web.WebView2.Wpf.WebView2>().Single();
        if(ReferenceEquals(browser,previousBrowser)) throw new InvalidOperationException("Retry reused the retired controller.");
        records.Add(new { Case="deliberate native Retry", FreshController="passed", PreviousTicket="invalidated", Authentication="not invoked" });
        reader.BackCommand.Execute(null); Pump();
        if(!ReferenceEquals(shell.CurrentPage,standard) || standard.Selected?.Id!="CA-001" || standard.Search!="CA-001" || reader.IsCurrent)
            throw new InvalidOperationException("Back failed to preserve unchanged native control/filter or invalidate the reader.");
        records.Add(new { Case="native unchanged-context Back", SelectionAndFilter="preserved", Authentication="not invoked" });
        File.WriteAllText(Path.Combine(output,"reader-verification.json"),JsonSerializer.Serialize(new { Source="exact checked-out CI source", SourceCommit=Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "not supplied", SyntheticOnly=true, TenantCalls=0, Records=records },new JsonSerializerOptions{WriteIndented=true}));
        window.Close(); return 0;
    }
}
