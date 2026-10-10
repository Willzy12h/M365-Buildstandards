using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Standards;

internal static partial class Program
{
    private static int HomeJourneySizes;
    private static int HomeJourneyPresses;

    private static void CheckHomeNativeJourneys(string source, string output, ShellViewModel originalShell,
        Window window, FrameworkElement content, BindingTrace traces)
    {
        var root = Path.Combine(output, "home-journey-" + Guid.NewGuid().ToString("N"));
        CopyFolder(Path.Combine(source, "standards"), Path.Combine(root, "standards"));
        StandardsManifest.Write(Path.Combine(root, "standards"), StandardsManifest.Generate(
            Path.Combine(root, "standards"), "Synthetic Home journey", DateTimeOffset.UtcNow));
        var paths = ToolkitPaths.Resolve(root); paths.EnsureWritableFolders();
        using var logger = new ToolkitLogger(paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(paths, ToolkitSettings.Load(paths.SettingsFile), logger, diagnostics: true);
        workspace.Initialise();
        if (workspace.Standard is null) throw new InvalidOperationException("Home fixture catalogue failed to load.");
        var shell = new ShellViewModel(workspace);
        var profile = new TenantProfile { Id = Id(880), TenantId = Tenant, Company = "Synthetic Home client", Domain = "home.synthetic.invalid" };
        try
        {
            window.DataContext = shell; content.DataContext = shell;
            foreach (var size in PageSizes)
            {
                traces.Context = "Home native journey " + size;
                ResizeOfflineJourney(window, content, size);
                Set(workspace, nameof(Workspace.Profile), (TenantProfile?)null);
                Set(workspace, nameof(Workspace.Snapshot), (TenantSnapshot?)null);
                Set(workspace, nameof(Workspace.SnapshotIsLive), false);
                shell.Navigate("home"); content.UpdateLayout(); Pump();
                var home = shell.Page<HomeViewModel>(); home.Refresh(); content.UpdateLayout(); Pump();
                var primary = JourneyButton(content, home.NextStepCommand);
                var primaryBounds = VisibleBounds(primary, content);
                if (!IsShown(primary, content) || primaryBounds.Height < primary.ActualHeight - 1
                    || primaryBounds.Width < primary.ActualWidth - 1)
                    throw new InvalidOperationException("Home's next-step button is not fully visible at " + size);
                if (home.Projection.NextPageKey != "connect" || !home.Projection.ConnectionText.Contains("Offline", StringComparison.Ordinal))
                    throw new InvalidOperationException("Cold Home invents a connection or skips client selection.");
                CaptureReportState(content, output, "home-cold", size, checkPageScrolling: false);
                InvokeJourneyButton(shell, primary, "Home cold next step"); HomeJourneyPresses++;
                if (!ReferenceEquals(shell.CurrentPage, shell.Page<ConnectViewModel>()) || workspace.Session is not null)
                    throw new InvalidOperationException("Home cold shortcut did not open Connect without signing in.");

                Set(workspace, nameof(Workspace.Profile), profile);
                var snapshot = new TenantSnapshot { Id = Id(881), TenantId = Tenant,
                    TenantName = profile.Company, PrimaryDomain = profile.Domain, CapturedAt = "2026-09-01T08:00:00Z",
                    StandardRelease = workspace.Standard.Release, ToolkitVersion = workspace.Version, Complete = false };
                Set(workspace, nameof(Workspace.Snapshot), snapshot);
                shell.Navigate("home"); home.Refresh(); content.UpdateLayout(); Pump();
                if (!home.Projection.EvidenceText.Contains("Historical", StringComparison.Ordinal)
                    || !home.Projection.EvidenceText.Contains("incomplete", StringComparison.OrdinalIgnoreCase)
                    || !home.Projection.EvidenceText.Contains("01 Sep 2026 08:00 UTC", StringComparison.Ordinal))
                    throw new InvalidOperationException("Home lost historical/unknown evidence or its original time.");
                CaptureReportState(content, output, "home-historical-incomplete", size, checkPageScrolling: false);
                var before = ToolkitJson.Serialize(snapshot);
                var cards = home.Tasks.Concat(home.Shortcuts).ToArray();
                foreach (var card in cards)
                {
                    shell.Navigate("home"); content.UpdateLayout(); Pump();
                    var button = Descendants(content).OfType<Button>().Single(b =>
                        ReferenceEquals(b.Command, home.OpenTaskCommand) && ReferenceEquals(b.CommandParameter, card));
                    button.BringIntoView(); content.UpdateLayout(); Pump();
                    if (!button.Focusable || !KeyboardNavigation.GetIsTabStop(button))
                        throw new InvalidOperationException("Home card is not keyboard reachable: " + card.Title);
                    InvokeJourneyButton(shell, button, card.AccessibleName); HomeJourneyPresses++;
                    if (!shell.NavItems.Single(n => n.Key == card.PageKey).IsActive
                        || workspace.Session is not null || workspace.ApplicationSetup is not null
                        || workspace.Plan is not null || workspace.AcknowledgedSnapshotId is not null
                        || !ReferenceEquals(workspace.Profile, profile) || ToolkitJson.Serialize(workspace.Snapshot) != before)
                        throw new InvalidOperationException("Home navigation changed evidence or authority: " + card.Title);
                }
                HomeJourneySizes++;
            }
        }
        finally
        {
            window.DataContext = originalShell; content.DataContext = originalShell;
            originalShell.Navigate("home"); content.UpdateLayout(); Pump();
        }
    }
}
