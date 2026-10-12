using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Standards;

internal static partial class Program
{
    private static int HomeJourneySizes;
    private static int HomeJourneyPresses;
    private static int HomeAccessibleStatuses;
    private static int HomePrimaryContrastChecks;

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
                NotifyFixture(workspace);
                shell.Navigate("home"); content.UpdateLayout(); Pump();
                var home = shell.Page<HomeViewModel>(); home.Refresh(); content.UpdateLayout(); Pump();
                var primary = JourneyButton(content, home.NextStepCommand);
                CheckHomeButtonLabel(primary);
                var primaryBounds = VisibleBounds(primary, content);
                var primaryLabel = (TextBlock)primary.Content;
                if (!TryTextColours(primaryLabel, out var labelColour, out var surfaceColour)
                    || Contrast(labelColour, surfaceColour) < 4.5)
                    throw new InvalidOperationException("Home next-step text lacks readable 4.5:1 contrast at " + size);
                HomePrimaryContrastChecks++;
                CheckAccessibleHomeStatus(content, "Home next step guidance");
                CheckAccessibleHomeStatus(content, "Home context summary");
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
                NotifyFixture(workspace);
                shell.Navigate("home"); home.Refresh(); content.UpdateLayout(); Pump();
                if (!home.Projection.EvidenceText.Contains("Historical", StringComparison.Ordinal)
                    || !home.Projection.EvidenceText.Contains("incomplete", StringComparison.OrdinalIgnoreCase)
                    || !home.Projection.EvidenceText.Contains("01 Sep 2026 08:00 UTC", StringComparison.Ordinal))
                    throw new InvalidOperationException("Home lost historical/unknown evidence or its original time.");
                if (shell.TenantTitle != profile.Company || !shell.EvidenceStatus.Contains("Historical", StringComparison.Ordinal))
                    throw new InvalidOperationException("The Home fixture header did not receive the same client/evidence state.");
                CaptureReportState(content, output, "home-historical-incomplete", size, checkPageScrolling: false);
                var details = Descendants(content).OfType<Expander>().Single(e =>
                    AutomationProperties.GetName(e) == "Home context details");
                details.IsExpanded = true; content.UpdateLayout(); Pump();
                var evidence = CheckAccessibleHomeStatus(content, "Home evidence status");
                if (!new TextBlockAutomationPeer(evidence).GetName().Contains("01 Sep 2026 08:00 UTC", StringComparison.Ordinal))
                    throw new InvalidOperationException("Accessible Home evidence lost its original capture time.");
                evidence.BringIntoView(); content.UpdateLayout(); Pump();
                CaptureReportState(content, output, "home-historical-details", size, checkPageScrolling: false);
                // The rendered Home details and pinned header share both evidence sources, including refusals.
                var exchange = new TenantSnapshot { Id = Id(882), TenantId = Tenant,
                    CapturedAt = "2026-09-02T09:00:00Z", Complete = false };
                Set(workspace, nameof(Workspace.ExchangeSnapshot), exchange);
                Set(workspace, nameof(Workspace.ExchangeCapturedByTool), false);
                NotifyFixture(workspace); home.Refresh(); content.UpdateLayout(); Pump();
                var mixedName = new TextBlockAutomationPeer(evidence).GetName();
                if (!mixedName.StartsWith(shell.EvidenceStatus, StringComparison.Ordinal)
                    || !mixedName.Contains("Exchange: historical", StringComparison.Ordinal)
                    || !mixedName.Contains(exchange.CapturedAt, StringComparison.Ordinal)
                    || !mixedName.Contains("Exchange completeness: INCOMPLETE", StringComparison.Ordinal))
                    throw new InvalidOperationException("Rendered Home lost the header's separate Exchange source or completeness.");
                exchange.TenantId = Id(883);
                NotifyFixture(workspace); home.Refresh(); content.UpdateLayout(); Pump();
                var foreignName = new TextBlockAutomationPeer(evidence).GetName();
                if (!foreignName.StartsWith(shell.EvidenceStatus, StringComparison.Ordinal)
                    || !foreignName.Contains("Exchange: different tenant", StringComparison.Ordinal)
                    || foreignName.Contains(exchange.Id, StringComparison.Ordinal)
                    || foreignName.Contains(exchange.CapturedAt, StringComparison.Ordinal))
                    throw new InvalidOperationException("Rendered Home hides a foreign Exchange source or exposes its capture identity.");
                Set(workspace, nameof(Workspace.ExchangeSnapshot), (TenantSnapshot?)null);
                NotifyFixture(workspace); home.Refresh(); content.UpdateLayout(); Pump();
                details.IsExpanded = false; content.UpdateLayout(); Pump();
                var before = ToolkitJson.Serialize(snapshot);
                var cards = home.PrimaryTasks.Concat(home.Tasks).Concat(home.Shortcuts).ToArray();
                foreach (var card in cards)
                {
                    shell.Navigate("home"); content.UpdateLayout(); Pump();
                    var button = Descendants(content).OfType<Button>().Single(b =>
                        ReferenceEquals(b.Command, home.OpenTaskCommand) && ReferenceEquals(b.CommandParameter, card));
                    button.BringIntoView(); content.UpdateLayout(); Pump();
                    if (!button.Focusable || !KeyboardNavigation.GetIsTabStop(button))
                        throw new InvalidOperationException("Home card is not keyboard reachable: " + card.Title);
                    CheckHomeButtonLabel(button);
                    InvokeJourneyButton(shell, button, card.AccessibleName); HomeJourneyPresses++;
                    if (!shell.NavItems.Single(n => n.Key == card.PageKey).IsActive
                        || workspace.Session is not null || workspace.ApplicationSetup is not null
                        || workspace.Plan is not null || workspace.AcknowledgedSnapshotId is not null
                        || !ReferenceEquals(workspace.Profile, profile) || ToolkitJson.Serialize(workspace.Snapshot) != before)
                        throw new InvalidOperationException("Home navigation changed evidence or authority: " + card.Title);
                }
                // New docked reference uses only the saved original; no auth/run or current-client approval.
                var savedFile = workspace.Evidence.SaveSnapshot(snapshot);
                var savedBytes = File.ReadAllBytes(savedFile);
                shell.PinCurrentReferenceCommand.Execute(null); content.UpdateLayout(); Pump();
                if (!shell.PinnedReference.HasReference) throw new InvalidOperationException("Saved reference did not pin.");
                if (size.Width >= 1360 ? !shell.UseSideReference : !shell.UseDrawerReference)
                    throw new InvalidOperationException("Reference did not adapt to the actual window width.");
                CaptureReportState(content, output, "home-pinned-original", size, checkPageScrolling: false);
                shell.PinnedReference.DetailsExpanded = true;
                var nextClient = ToolkitJson.Deserialize<TenantProfile>(ToolkitJson.Serialize(profile));
                nextClient.Id = Guid.NewGuid().ToString(); nextClient.TenantId = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
                nextClient.Company = "Synthetic second client";
                workspace.ApplyProfileToSession(nextClient, save: false); content.UpdateLayout(); Pump();
                if (!shell.PinnedReference.HasMismatch || shell.PinnedReference.DetailsExpanded
                    || workspace.Plan is not null || workspace.AcknowledgedSnapshotId is not null
                    || !savedBytes.SequenceEqual(File.ReadAllBytes(savedFile)))
                    throw new InvalidOperationException("Reference context change lost provenance, exposed details or changed authority/evidence.");
                CaptureReportState(content, output, "home-pinned-different-context", size, checkPageScrolling: false);
                shell.ClosePinnedReferenceCommand.Execute(null); content.UpdateLayout(); Pump();
                if (shell.HasPinnedReference) throw new InvalidOperationException("Reference did not close.");
                HomeJourneySizes++;
            }
        }
        finally
        {
            window.DataContext = originalShell; content.DataContext = originalShell;
            originalShell.Navigate("home"); content.UpdateLayout(); Pump();
        }
    }

    private static void CheckHomeButtonLabel(Button button)
    {
        var visible = (button.Content as TextBlock)?.Text;
        var accessible = new ButtonAutomationPeer(button).GetName();
        if (string.IsNullOrWhiteSpace(visible) || !accessible.StartsWith(visible, StringComparison.Ordinal))
            throw new InvalidOperationException("Home button accessible name must start with its visible label: " + visible);
    }

    private static TextBlock CheckAccessibleHomeStatus(FrameworkElement content, string purpose)
    {
        var text = Descendants(content).OfType<TextBlock>().Single(t => AutomationProperties.GetHelpText(t) == purpose);
        var peer = new TextBlockAutomationPeer(text);
        if (string.IsNullOrWhiteSpace(text.Text) || peer.GetName() != text.Text)
            throw new InvalidOperationException(purpose + " does not expose its actual status through UI Automation.");
        HomeAccessibleStatuses++;
        return text;
    }
}
