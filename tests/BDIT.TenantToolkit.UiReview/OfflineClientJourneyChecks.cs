using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Standards;

/// <summary>Real saved-client and historical-read buttons in a cold workspace; no synthetic connection is injected.</summary>
internal static partial class Program
{
    private static int OfflineClientJourneySizes;
    private static int OfflineClientJourneyPresses;

    private static void CheckOfflineClientNativeJourney(string source, string output, ShellViewModel originalShell,
        Window window, FrameworkElement content, BindingTrace traces)
    {
        var root = Path.Combine(output, "offline-client-journey-" + Guid.NewGuid().ToString("N"));
        CopyFolder(Path.Combine(source, "standards"), Path.Combine(root, "standards"));
        StandardsManifest.Write(Path.Combine(root, "standards"), StandardsManifest.Generate(Path.Combine(root, "standards"), "Synthetic offline journey", DateTimeOffset.UtcNow));
        var paths = ToolkitPaths.Resolve(root); paths.EnsureWritableFolders();
        using var logger = new ToolkitLogger(paths.LogsDirectory, LogLevel.Debug);
        var seed = new Workspace(paths, new ToolkitSettings(), logger, true); seed.Initialise();
        var profile = new TenantProfile { Id = Id(900), TenantId = Tenant, Company = "Synthetic handed-over client", Domain = "example.invalid",
            Parameters = new TenantParameters { EmergencyAccountIds = new() { Id(5), Id(6) } } };
        seed.Evidence.SaveProfiles(new[] { profile });
        var standard = seed.RequireStandard();
        var snapshot = new TenantSnapshot { Id = Id(901), TenantId = Tenant, TenantName = profile.Company,
            PrimaryDomain = profile.Domain, CapturedAt = Stamp, CapturedBy = "synthetic@example.invalid", Complete = true,
            StandardRelease = standard.Release, ToolkitVersion = seed.Version };
        foreach (var (key, definition) in standard.Collections)
            snapshot.Collections[key] = new CollectionCapture { Status = CaptureStatus.Collected, Api = definition.Api,
                Path = definition.Path, Count = 0, Items = new() };
        seed.Evidence.SaveSnapshot(snapshot);
        var control = standard.Controls.First(c => c.Collection == "conditionalAccess");
        seed.Evidence.SaveRun(new DeploymentRun { Id = Id(902), TenantId = Tenant, Release = standard.Release,
            ToolkitVersion = seed.Version, StartedAt = Stamp, EndedAt = Stamp, Status = RunStatus.ReviewRequired,
            Results = new() { new RunResult { ControlId = control.Id, Name = "Synthetic uncertain candidate", Collection = "conditionalAccess",
                PlannedAction = nameof(PlanAction.Create), ObjectId = Id(903), Status = ResultStatus.Error,
                WriteAcceptance = WriteAcceptance.Unknown, Configuration = ConfigurationVerification.Unknown } } });
        try
        {
            foreach (var size in PageSizes)
            {
                var workspace = new Workspace(paths, new ToolkitSettings(), logger, true); workspace.Initialise();
                var shell = new ShellViewModel(workspace);
                if (workspace.Profile is not null || workspace.Connection is not null || workspace.ApplicationSetup is not null)
                    throw new InvalidOperationException("Cold-start offline fixture already has an active client/session.");
                window.DataContext = shell; content.DataContext = shell;
                ResizeOfflineJourney(window, content, size);
                traces.Context = "cold-start saved client " + size;
                shell.Navigate("connect"); content.UpdateLayout(); Pump();
                var connect = shell.Page<ConnectViewModel>();
                var clients = Descendants(content).OfType<ComboBox>().Single(c => ReferenceEquals(c.ItemsSource, connect.Profiles));
                clients.SelectedItem = connect.Profiles.Single(); content.UpdateLayout(); Pump();
                if (connect.Selected?.Id != profile.Id || workspace.Profile is not null)
                    throw new InvalidOperationException("Saved client selection did not fill the real form without implicit activation.");
                connect.EditTenantId = Id(904); // An unsaved form must not choose another offline target.
                var button = JourneyButton(content, connect.ReviewOfflineCommand);
                button.BringIntoView(); content.UpdateLayout(); Pump();
                if (!button.Focus() || !button.IsKeyboardFocusWithin)
                    throw new InvalidOperationException("Review saved evidence offline cannot receive keyboard focus.");
                CaptureReportState(content, output, "offline-saved-client", size, checkPageScrolling: false);
                InvokeJourneyButton(shell, button, "Review saved evidence offline");
                if (workspace.Profile?.TenantId != profile.TenantId || workspace.Connection is not null || workspace.ApplicationSetup is not null
                    || workspace.Snapshot is not null || workspace.Plan is not null || workspace.ExperimentalChangesEnabled)
                    throw new InvalidOperationException("Offline activation selected the unsaved form or retained session/evidence authority.");
                content.UpdateLayout(); Pump();
                var configuration = shell.Page<ConfigurationViewModel>();
                if (shell.CurrentPage != configuration) throw new InvalidOperationException("Offline action did not open saved configuration review.");
                configuration.SelectedStored = configuration.StoredSnapshots.Single(s => s.Id == snapshot.Id);
                foreach (var expander in Descendants(content).OfType<Expander>().Where(e => e.Header?.ToString() == "Capture details, exports and saved captures"))
                    expander.IsExpanded = true;
                content.UpdateLayout(); Pump();
                InvokeJourneyButton(shell, JourneyButton(content, configuration.LoadStoredCommand), "Open saved capture");
                if (workspace.Snapshot?.Id != snapshot.Id || workspace.Snapshot.CapturedAt != snapshot.CapturedAt
                    || workspace.SnapshotIsLive || workspace.Assessment is null || workspace.Plan is not null)
                    throw new InvalidOperationException("Opening saved evidence lost its original identity/time or created live/plan authority.");
                shell.Navigate("overview"); content.UpdateLayout(); Pump();
                var evidence = Descendants(content).OfType<TextBlock>().Single(t => t.Name == "EvidenceSourceStatus");
                if (evidence.Text != shell.EvidenceStatus || !evidence.Text.Contains("Historical evidence", StringComparison.Ordinal)
                    || !IsShown(evidence, content) || shell.ModeBadge != "NOT CONNECTED" || !shell.HeaderHasDetails || shell.HeaderHasConnection)
                    throw new InvalidOperationException("Persistent historical evidence status or independent access badge failed after navigation.");
                if (!shell.DetailsText.Contains(profile.Id, StringComparison.Ordinal) || !shell.DetailsText.Contains(profile.TenantId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Offline copyable identity details lost saved profile/tenant identity.");
                CaptureReportState(content, output, "offline-historical-header", size, checkPageScrolling: false);
                shell.Navigate("recovery"); content.UpdateLayout(); Pump();
                var recovery = shell.Page<RecoveryViewModel>();
                InvokeJourneyButton(shell, JourneyButton(content, recovery.LoadRegisterCommand), "Load local change register");
                if (recovery.Changes.Count != 1 || recovery.PreviewDeleteCommand.CanExecute(null) || recovery.ExecuteCommand.CanExecute(null)
                    || recovery.ReverifyDeploymentCommand.CanExecute(null))
                    throw new InvalidOperationException("Offline change register disappeared or enabled a connected recovery action.");
                var reason = Descendants(content).OfType<TextBlock>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Recovery preview prerequisites");
                reason.BringIntoView(); content.UpdateLayout(); Pump();
                if (!reason.Text.Contains("offline", StringComparison.OrdinalIgnoreCase) || !IsShown(reason, content))
                    throw new InvalidOperationException("Offline Recovery has no reachable preview prerequisite explanation.");
                CaptureReportState(content, output, "offline-change-register", size, checkPageScrolling: false);
                shell.Navigate("assessment"); var assessment = shell.Page<AssessmentViewModel>(); assessment.Search = "synthetic-unmatched-search";
                content.UpdateLayout(); Pump();
                if (assessment.Findings.Count != 0 || !assessment.ViewStateText.Contains("No findings match", StringComparison.Ordinal)
                    || !assessment.ViewStateText.Contains("Historical evidence", StringComparison.Ordinal))
                    throw new InvalidOperationException("Empty filtered historical assessment has no current-state explanation.");
                CaptureReportState(content, output, "offline-filtered-assessment", size, checkPageScrolling: false);
                CheckNativeSameContextDrafts(shell, content, control.Id);
                if (workspace.Connection is not null || workspace.ApplicationSetup is not null || workspace.SnapshotIsLive || workspace.Plan is not null)
                    throw new InvalidOperationException("Local navigation acquired connection or deployment authority.");
                OfflineClientJourneySizes++;
                Console.WriteLine($"Cold-start saved-client native journey: {size.Width}x{size.Height}; historical capture, persistent identity/evidence, offline Recovery and empty-filter guidance passed; no connection/setup.");
            }
        }
        finally
        {
            window.DataContext = originalShell; content.DataContext = originalShell;
            ResizeOfflineJourney(window, content, new Size(1480, 940)); content.UpdateLayout(); Pump();
        }
    }

    private static void ResizeOfflineJourney(Window window, FrameworkElement content, Size size)
    {
        // Measure current chrome after layout; an outer-window size is not a client-content size.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            content.UpdateLayout(); Pump();
            var width = size.Width - content.ActualWidth; var height = size.Height - content.ActualHeight;
            if (Math.Abs(width) <= 1 && Math.Abs(height) <= 1) return;
            window.Width += width; window.Height += height; content.UpdateLayout(); Pump();
        }
        throw new InvalidOperationException($"Offline journey client size is {content.ActualWidth}x{content.ActualHeight}, expected {size.Width}x{size.Height}.");
    }
    private static void CheckNativeSameContextDrafts(ShellViewModel shell, FrameworkElement content, string control)
    {
        var workspace = shell.Workspace;
        workspace.SaveManualCheck(control, "Pass", "Synthetic saved manual evidence.");
        shell.Navigate("checks"); content.UpdateLayout(); Pump();
        var manual = shell.Page<ManualChecksViewModel>();
        var checks = Descendants(content).OfType<DataGrid>().Single(g => ReferenceEquals(g.ItemsSource, manual.Rows));
        checks.SelectedItem = manual.Rows.Single(r => r.ControlId == control); content.UpdateLayout(); Pump();
        var note = Descendants(content).OfType<TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Manual check evidence");
        note.Text = "Synthetic unfinished manual draft."; Pump();
        workspace.ApplyProfileToSession(workspace.Profile!, save: false); content.UpdateLayout(); Pump();
        if (manual.Selected?.ControlId != control || manual.EditNote != "Synthetic unfinished manual draft." || note.Text != manual.EditNote)
            throw new InvalidOperationException("The real manual-check binding discarded a same-context unsaved draft.");
        workspace.SaveDeviation(new Deviation { ControlId = control, Kind = DeviationKind.ApprovedDeviation,
            Reason = "Synthetic approved exception evidence.", ApprovedBy = "Synthetic approver", ReviewBy = "2099-01-01" });
        shell.Navigate("deviations"); content.UpdateLayout(); Pump();
        var deviations = shell.Page<DeviationsViewModel>();
        var register = Descendants(content).OfType<DataGrid>().Single(g => ReferenceEquals(g.ItemsSource, deviations.Items));
        register.SelectedItem = deviations.Items.Single(d => d.ControlId == control); content.UpdateLayout(); Pump();
        var reason = Descendants(content).OfType<TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Reason for deviation");
        reason.Text = "Synthetic unfinished exception draft."; Pump();
        workspace.ApplyProfileToSession(workspace.Profile!, save: false); content.UpdateLayout(); Pump();
        if (deviations.Selected?.ControlId != control || deviations.ControlId != control
            || deviations.Reason != "Synthetic unfinished exception draft." || reason.Text != deviations.Reason)
            throw new InvalidOperationException("The real exception binding discarded a same-context unsaved draft or control identity.");
        workspace.RunAssessment(); // Restore the historical full assessment after the local profile notifications.
    }
    private static Button JourneyButton(FrameworkElement content, ICommand command) => Descendants(content).OfType<Button>()
        .Single(b => ReferenceEquals(b.Command, command));
    private static void InvokeJourneyButton(ShellViewModel shell, Button button, string label)
    {
        button.BringIntoView(); Pump();
        if (!button.IsEnabled || button.Command?.CanExecute(button.CommandParameter) != true)
            throw new InvalidOperationException(label + " is disabled in its populated local journey.");
        var peer = new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke(); Pump();
        var clock = Stopwatch.StartNew();
        while ((button.Command as AsyncCommand)?.IsRunning == true || shell.Workspace.Busy)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new InvalidOperationException(label + " exceeded its local deadline.");
            Pump(); Thread.Sleep(10);
        }
        Pump();
        if (shell.ErrorMessage.Length != 0) throw new InvalidOperationException(label + " failed: " + shell.ErrorMessage);
        OfflineClientJourneyPresses++;
    }
}
