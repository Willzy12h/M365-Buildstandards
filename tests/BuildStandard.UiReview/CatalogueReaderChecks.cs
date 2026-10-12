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
        var records = new List<object>();
        // Private construction is confined to this hostile synthetic fixture. Production consumes T8's verified descriptor.
        var html = Encoding.UTF8.GetBytes("""
            <!doctype html><html><body><h1 id="CA-001">Safe catalogue fixture</h1><h2 id="index">Index</h2>
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
            var current = true; var pending = ""; var observed = "";
            AwaitReader(host.OpenAsync(fixture, "CA-001", () => current, c => grid.Children.Add(c), _ => { }, u => pending=u, a => observed=a));
            if (host.Control?.CoreWebView2 is not { } core) throw new InvalidOperationException("Hosted reader has no ready browser.");
            AwaitReader(host.FindAsync("SCRIPT EXECUTED MARKER"));
            if (core.Find.MatchCount != 0) throw new InvalidOperationException("Host allowed injected page script to change the document.");
            AwaitReader(host.FindAsync("Safe catalogue fixture"));
            if (core.Find.MatchCount != 1) throw new InvalidOperationException("Safe fixture text was not rendered/searchable through native Find.");
            if (host.DeniedRequests < 2) throw new InvalidOperationException("Injected frame/resource refusal was not observed.");
            foreach (var url in new[] { "file:///C:/not-a-reader-document", "data:text/html,blocked", "https://reader.buildstandard.example/catalogue.html?extra=1", "https://learn.microsoft.com/en-us/entra/" })
            {
                var before=host.DeniedRequests; core.Navigate(url); Pump();
                var timer=Stopwatch.StartNew(); while(host.DeniedRequests==before && timer.Elapsed<TimeSpan.FromSeconds(5)){Pump();Thread.Sleep(10);}
                if(host.DeniedRequests<=before || pending.Length!=0) throw new InvalidOperationException("Unexpected/programmatic navigation was not refused without external-link admission.");
            }
            core.Navigate(ReaderNavigationPolicy.DocumentUrl+"#index");
            var anchorTimer=Stopwatch.StartNew(); while(observed!="index" && anchorTimer.Elapsed<TimeSpan.FromSeconds(5)){Pump();Thread.Sleep(10);}
            if(observed!="index") throw new InvalidOperationException("Same-document SourceChanged did not report the observed anchor.");
            var exact=workspace.Exporter.SaveHtml(fixture);
            if(!File.ReadAllBytes(exact).SequenceEqual(html) || CanonicalJson.Sha256Hex(File.ReadAllBytes(exact))!=fixture.HtmlSha256)
                throw new InvalidOperationException("Hosted document's explicit save was not the admitted bytes.");
            using(var image=File.Create(Path.Combine(output,"reader-negative-fixture.png"))) AwaitReader(core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image));
            records.Add(new { Case="hostile synthetic fixture", ScriptMutation="refused", FrameAndResource="refused", ProgrammaticExternalLink="refused", Fragment="observed", ExactSave="passed", host.RuntimeVersion, host.DeniedRequests });
            current=false; host.Dispose(); grid.Children.Clear();
            var exitTimer=Stopwatch.StartNew(); while(host.CleanupStatus!="Reader cache retired" && exitTimer.Elapsed<TimeSpan.FromSeconds(15)){Pump();Thread.Sleep(10);}
            records.Add(new { Case="superseded owned reader retirement", host.CleanupStatus });
            if(host.CleanupStatus!="Reader cache retired") throw new InvalidOperationException("Owned profile retirement was not proved; cache cleanup incomplete.");
        }
        window.Content=nativeContent;
        shell.Navigate("standard"); Pump();
        var standard=shell.Page<BuildStandard.App.ViewModels.StandardViewModel>();
        standard.Search="CA-001"; standard.Selected=standard.Controls.Single(c=>c.Id=="CA-001");
        if(!standard.ViewReferenceCommand.CanExecute(null)) throw new InvalidOperationException("Native selected-control reference entry is unavailable.");
        standard.ViewReferenceCommand.Execute(null);
        var readyTimer=Stopwatch.StartNew();
        while((shell.CurrentPage is not BuildStandard.App.ViewModels.CatalogueReaderViewModel candidate || candidate.State is "Preparing" or "Loading")
            && readyTimer.Elapsed<TimeSpan.FromSeconds(45)){Pump();Thread.Sleep(10);}
        if(shell.CurrentPage is not BuildStandard.App.ViewModels.CatalogueReaderViewModel reader || reader.State!="Ready")
            throw new InvalidOperationException("Native catalogue reader did not reach Ready: "+(shell.CurrentPage as BuildStandard.App.ViewModels.CatalogueReaderViewModel)?.Status);
        var browser=Descendants((FrameworkElement)window.Content).OfType<Microsoft.Web.WebView2.Wpf.WebView2>().Single();
        using(var image=File.Create(Path.Combine(output,"reader-engineer-catalogue.png"))) AwaitReader(browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image));
        SaveWindowImage((FrameworkElement)window.Content,Path.Combine(output,"reader-native-chrome.png")); // HWND content is separately captured above; never claim a combined browser bitmap.
        var beforeFiles=Directory.GetFiles(workspace.Paths.ReportsDirectory,"*.html").ToHashSet(StringComparer.OrdinalIgnoreCase);
        reader.SaveCommand.Execute(null);
        var saveTimer=Stopwatch.StartNew(); while(reader.Saved.Length==0 && saveTimer.Elapsed<TimeSpan.FromSeconds(15)){Pump();Thread.Sleep(10);}
        var saved=Directory.GetFiles(workspace.Paths.ReportsDirectory,"*.html").Except(beforeFiles,StringComparer.OrdinalIgnoreCase).Single();
        if(!File.ReadAllBytes(saved).SequenceEqual(reader.Document!.CopyHtmlBytes())) throw new InvalidOperationException("Native Save HTML differed from the displayed descriptor.");
        records.Add(new { Case="native View reference and actual T8 Engineer catalogue", reader.Document.CatalogueRelease,reader.Document.CatalogueDigest,reader.Document.HtmlSha256,reader.Document.PresentationVersion, Runtime=browser.CoreWebView2.Environment.BrowserVersionString, ExactNativeSave="passed" });
        reader.BackCommand.Execute(null); Pump();
        if(!ReferenceEquals(shell.CurrentPage,standard) || standard.Selected?.Id!="CA-001" || standard.Search!="CA-001" || reader.IsCurrent)
            throw new InvalidOperationException("Back failed to preserve unchanged native control/filter or invalidate the reader.");
        records.Add(new { Case="native unchanged-context Back", SelectionAndFilter="preserved", Authentication="not invoked" });
        File.WriteAllText(Path.Combine(output,"reader-verification.json"),JsonSerializer.Serialize(new { Source="exact checked-out CI source", SyntheticOnly=true, TenantCalls=0, Records=records },new JsonSerializerOptions{WriteIndented=true}));
        window.Close(); return 0;
    }
}
