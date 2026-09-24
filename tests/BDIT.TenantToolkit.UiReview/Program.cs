using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.App.Views;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using BDIT.TenantToolkit.Graph.Setup;
using BDIT.TenantToolkit.Engine.Standards;

internal static partial class Program
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string Operator = "22222222-2222-2222-2222-222222222222";
    private static string Id(int number) => "aaaaaaaa-aaaa-aaaa-aaaa-" + number.ToString("D12");
    private static readonly string Stamp = DateTimeOffset.UtcNow.ToString("O");

    [STAThread]
    private static int Main(string[] args)
    {
        var source = Path.GetFullPath(args.Length > 0 ? args[0] : Environment.CurrentDirectory);
        var output = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine(Environment.CurrentDirectory, "dist", "ui-review"));
        Directory.CreateDirectory(output);
        var fixtureRoot = Path.Combine(output, "synthetic-portable-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        CopyFolder(Path.Combine(source, "standards"), Path.Combine(fixtureRoot, "standards"));
        StandardsManifest.Write(Path.Combine(fixtureRoot, "standards"), StandardsManifest.Generate(
            Path.Combine(fixtureRoot, "standards"), "Offline UI harness — copied synthetic fixture only", DateTimeOffset.UtcNow));
        CopyFolder(Path.Combine(source, "config"), Path.Combine(fixtureRoot, "config"));
        var traces = new BindingTrace();
        PresentationTraceSources.DataBindingSource.Listeners.Add(traces);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning | SourceLevels.Error;
        var records = new List<object>();
        try
        {
            // Use a base Application and load the product resource dictionary; never invoke production startup or authentication.
            var app = new Application();
            var document = System.Xml.Linq.XDocument.Load(Path.Combine(source, "src", "BDIT.TenantToolkit.App", "App.xaml"));
            var resource = new System.Xml.Linq.XElement(document.Descendants(System.Xml.Linq.XName.Get("ResourceDictionary", "http://schemas.microsoft.com/winfx/2006/xaml/presentation")).First());
            resource.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
            resource.SetAttributeValue(System.Xml.Linq.XNamespace.Xmlns + "infra", "clr-namespace:BDIT.TenantToolkit.App.Infrastructure;assembly=BDIT.TenantToolkit.App");
            foreach (var element in resource.Descendants().Where(e => e.Name.NamespaceName == "clr-namespace:BDIT.TenantToolkit.App.Infrastructure"))
                element.Name = System.Xml.Linq.XName.Get(element.Name.LocalName, "clr-namespace:BDIT.TenantToolkit.App.Infrastructure;assembly=BDIT.TenantToolkit.App");
            app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resource.ToString());
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var paths = ToolkitPaths.Resolve(fixtureRoot); paths.EnsureWritableFolders();
            using var logger = new ToolkitLogger(paths.LogsDirectory, LogLevel.Debug);
            var workspace = new Workspace(paths, ToolkitSettings.Load(paths.SettingsFile), logger, diagnostics: true);
            workspace.Initialise();
            if (workspace.Standard is null) throw new InvalidOperationException("Synthetic standards did not load: " + workspace.StandardError);
            var shell = new ShellViewModel(workspace);
            var window = new BDIT.TenantToolkit.App.MainWindow { DataContext = shell };
            if (window.Content is not FrameworkElement content) throw new InvalidOperationException("MainWindow has no root FrameworkElement.");
            content.DataContext = shell;
            SeedWorkspace(workspace);
            SeedConnect(shell.Page<ConnectViewModel>());
            VerifyInputRefresh(shell);
            // Pages that are captured as images for human review. Every page is still materialised and binding-checked
            // below; these are the ones a reviewer is asked to look at, so the set includes the pages where an engineer
            // enters client inputs and reads the build standard.
            var focus = new HashSet<string>(new[] { "overview", "recovery", "connect", "setup", "assessment", "plan", "deploy", "automation", "standard" }, StringComparer.Ordinal);
            foreach (var size in PageSizes)
            {
                foreach (var nav in shell.NavItems)
                {
                    traces.Context = nav.Key + " " + size.Width + "x" + size.Height;
                    Set(workspace, nameof(Workspace.ApplicationSetup), nav.Key == "setup" ? SyntheticSetup() : null);
                    typeof(Workspace).GetMethod("Notify", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(workspace, null);
                    shell.Navigate(nav.Key);
                    SeedPage(shell, nav.Key);
                    content.Width = size.Width; content.Height = size.Height;
                    content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
                    Pump(); content.UpdateLayout(); Pump();
                    var visualCount = Descendants(content).Count();
                    var controls = Descendants(content).OfType<UserControl>().ToList();
                    if (visualCount < 50 || controls.Count == 0) throw new InvalidOperationException("Page failed to materialise its visual tree: " + nav.Key);
                    var pageAtSize = nav.Key + " " + (int)size.Width + "x" + (int)size.Height;
                    RecordUnnamedControls(content, pageAtSize);
                    RecordCollapsedColumns(content, pageAtSize);
                    RecordLowContrast(content, pageAtSize);
                    RecordClipping(content, pageAtSize);
                    if (focus.Contains(nav.Key))
                    {
                        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(content);
                        var file = Path.Combine(output, nav.Key + "-" + (int)size.Width + "x" + (int)size.Height + ".png");
                        using var stream = File.Create(file); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
                        var pixels = new byte[(int)size.Width * (int)size.Height * 4]; bitmap.CopyPixels(pixels, (int)size.Width * 4, 0);
                        var colours = new HashSet<int>();
                        for (var i = 0; i < pixels.Length; i += 1024) colours.Add(BitConverter.ToInt32(pixels, i));
                        if (colours.Count < 4) throw new InvalidOperationException("Rendered image appears empty: " + file);
                    }
                    if (nav.Key is "standard" or "plan" or "automation")
                        CapturePrerequisites(content, size, output, nav.Key);
                    if (nav.Key == "automation")
                        CaptureAssignmentPopulations(shell, content, size, output);
                    if (nav.Key == "setup")
                    {
                        var results = Descendants(content).OfType<ItemsControl>().Single(g => System.Windows.Automation.AutomationProperties.GetName(g) == "Application setup results");
                        Pump(); content.UpdateLayout();
                        results.BringIntoView(); Pump(); content.UpdateLayout();
                        SaveImage(content, size, Path.Combine(output, $"setup-writes-{(int)size.Width}x{(int)size.Height}.png"));
                    }
                    records.Add(new { page = nav.Key, size = new { width = size.Width, height = size.Height }, visualCount, viewTypes = controls.Select(c => c.GetType().Name).Distinct().ToArray() });
                    if (nav.Key is "connect" or "setup" or "overview" or "recovery")
                    {
                        var scroller = Descendants(controls[0]).OfType<ScrollViewer>().First();
                        scroller.ScrollToEnd(); content.UpdateLayout(); Pump();
                        SaveImage(content, size, Path.Combine(output, nav.Key + "-bottom-" + (int)size.Width + "x" + (int)size.Height + ".png"));
                        scroller.ScrollToTop(); content.UpdateLayout(); Pump();
                        if (nav.Key == "recovery")
                        {
                            scroller.ScrollToVerticalOffset(scroller.ScrollableHeight / 2); content.UpdateLayout(); Pump();
                            SaveImage(content, size, Path.Combine(output, "recovery-verification-" + (int)size.Width + "x" + (int)size.Height + ".png"));
                            scroller.ScrollToTop(); content.UpdateLayout(); Pump();
                        }
                    }
                    if (nav.Key == "plan")
                    {
                        var tabs = Descendants(controls[0]).OfType<TabControl>().First();
                        tabs.SelectedIndex = 1; content.UpdateLayout(); Pump();
                        RecordUnnamedControls(content, "plan-review " + (int)size.Width + "x" + (int)size.Height);
                        RecordCollapsedColumns(content, "plan-review " + (int)size.Width + "x" + (int)size.Height);
                        RecordLowContrast(content, "plan-review " + (int)size.Width + "x" + (int)size.Height);
                        RecordClipping(content, "plan-review " + (int)size.Width + "x" + (int)size.Height);
                        CapturePrerequisites(content, size, output, "plan-review");
                        SaveImage(content, size, Path.Combine(output, "plan-review-" + (int)size.Width + "x" + (int)size.Height + ".png"));
                    }
                }
            }
            // A check that inspects nothing passes, and these two did exactly that before IsShown replaced IsVisible.
            // The Plan table is the one S1 was raised against, so it must be among the grids actually measured.
            if (NamedControlsInspected == 0 || GridsInspected.Count == 0)
                throw new InvalidOperationException($"The interface checks inspected {NamedControlsInspected} controls and {GridsInspected.Count} tables; a check that looks at nothing cannot pass.");
            if (!GridsInspected.Contains("Controls to include in the plan") || !GridsInspected.Contains("Planned actions"))
                throw new InvalidOperationException("The Plan page tables were not measured: " + string.Join(", ", GridsInspected));
            CheckConfirmationDialog(workspace, output);

            // Keyboard and command checks need a real window: focus only moves inside one that has been shown.
            window.ShowInTaskbar = false; window.ShowActivated = true;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000; window.Top = -10000; window.Width = 1480; window.Height = 940;
            content.Width = double.NaN; content.Height = double.NaN;
            window.Show(); Pump();
            foreach (var nav in shell.NavItems)
            {
                traces.Context = "keyboard " + nav.Key;
                shell.Navigate(nav.Key);
                SeedPage(shell, nav.Key);
                CheckKeyboardReach(window, content, nav.Key);
            }
            PressCommands(shell, content, traces);
            WriteReviewNotes(output);

            // Each check must have looked at something, or its silence means nothing.
            var inspected = new (string Check, bool Ran)[]
            {
                ("text contrast", TextContrastInspected > 0), ("input boundary contrast", BoundariesInspected > 0),
                ("clipping", ClippingInspected > 0), ("final confirmation dialog", DialogChecks > 0),
                ("keyboard", KeyboardPagesWalked == shell.NavItems.Count && KeyboardStopsReached > shell.NavItems.Count),
                ("commands", CommandsCompleted >= 10)
            };
            foreach (var (check, ran) in inspected.Where(c => !c.Ran))
                throw new InvalidOperationException($"The {check} check inspected nothing; a check that looks at nothing cannot pass.");

            var problems = new List<string>();
            var minimum = new Size(window.MinWidth, window.MinHeight);
            if (minimum.Width > SmallestWorkArea.Width || minimum.Height > SmallestWorkArea.Height)
                problems.Add($"The main window cannot be made smaller than {minimum.Width}x{minimum.Height}, but a 1920x1080 laptop "
                    + $"screen at the 150% scaling Windows recommends for it leaves {SmallestWorkArea.Width}x{SmallestWorkArea.Height} "
                    + "above the taskbar, so the bottom of every page is off-screen.");
            if (UnnamedControls.Count > 0)
                problems.Add($"{UnnamedControls.Count} control(s) announce only their type to a screen reader. Give each an "
                    + "AutomationProperties.Name, or text content:" + Environment.NewLine + string.Join(Environment.NewLine, UnnamedControls));
            if (CollapsedColumns.Count > 0)
                problems.Add($"{CollapsedColumns.Count} table column(s) are drawn narrower than designed, so their content is hidden "
                    + "even when scrolled to. A DataGrid with a star column squeezes every column to fit before it scrolls; "
                    + "ColumnSizing.KeepDesignedWidths prevents it:" + Environment.NewLine + string.Join(Environment.NewLine, CollapsedColumns));
            if (LowContrast.Count > 0)
                problems.Add($"{LowContrast.Count} piece(s) of text or input edge are below the WCAG AA contrast minimum:"
                    + Environment.NewLine + string.Join(Environment.NewLine, LowContrast.Distinct()));
            if (ClippedElements.Count > 0)
                problems.Add($"{ClippedElements.Count} element(s) are cut off by layout, so part of them cannot be seen or scrolled to:"
                    + Environment.NewLine + string.Join(Environment.NewLine, ClippedElements.Distinct()));
            if (KeyboardProblems.Count > 0)
                problems.Add($"{KeyboardProblems.Count} keyboard problem(s):" + Environment.NewLine + string.Join(Environment.NewLine, KeyboardProblems.Distinct()));
            if (CommandProblems.Count > 0)
                problems.Add($"{CommandProblems.Count} command problem(s):" + Environment.NewLine + string.Join(Environment.NewLine, CommandProblems.Distinct()));
            if (problems.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine + Environment.NewLine, problems));
            Console.WriteLine($"Interface checks: {NamedControlsInspected} operable controls named; {GridsInspected.Count} tables measured, every column readable.");
            Console.WriteLine($"Contrast: {TextContrastInspected} text elements and {BoundariesInspected} input edges meet WCAG AA. Clipping: {ClippingInspected} elements, none cut off.");
            Console.WriteLine($"Keyboard: {KeyboardPagesWalked} pages walked with Tab, {KeyboardStopsReached} stops, every operable control reached. Final confirmation dialog: {DialogChecks} checks passed.");
            Console.WriteLine($"Commands: {CommandsCompleted} completed and {CommandsRefused} refused with a message, no defect raised; {CommandRegister.Count(c => c.Handling != Press)} not pressed by design.");
            Pump();
            // Exercise synchronous idle shutdown on a real off-screen window; direct Close from Closing is illegal in WPF.
            Set<ConnectedTenant?>(workspace, nameof(Workspace.Connection), null);
            Set<ApplicationSetupService?>(workspace, nameof(Workspace.ApplicationSetup), null);
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.ShowInTaskbar = false; window.ShowActivated = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000; window.Top = -10000;
            window.Show(); window.Close();
            var deadline = Stopwatch.StartNew();
            while (!closed && deadline.Elapsed < TimeSpan.FromSeconds(5)) { Pump(); Thread.Sleep(10); }
            if (!closed || !workspace.ShutdownComplete || !string.IsNullOrEmpty(shell.ErrorMessage))
                throw new InvalidOperationException("Idle window did not close cleanly: " + shell.ErrorMessage);
            File.WriteAllText(Path.Combine(output, "binding-errors.txt"), string.Join(Environment.NewLine, traces.Messages));
            File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new { status = traces.Messages.Count == 0 ? "Passed" : "Binding issues", offline = true, tenantCalls = 0, inputFormRefreshChecked = true, prerequisiteBindingsChecked = true, accessibleNamesChecked = true, readableColumnsChecked = true, contrastChecked = true, clippingChecked = true, keyboardReachChecked = true, confirmationDialogChecked = true, commandsPressed = CommandsCompleted + CommandsRefused, assignmentPopulationSelectionChecked = true, idleWindowClosed = closed, records, bindingIssues = traces.Messages }, new JsonSerializerOptions { WriteIndented = true }));
            logger.Flush();
            Console.WriteLine($"Rendered {Directory.GetFiles(output, "*.png").Length} synthetic page images; constructed {records.Count} page/size combinations. Binding issues: {traces.Messages.Count}. Output: {output}");
            return traces.Messages.Count == 0 ? 0 : 2;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "harness-error.txt"), ex.ToString());
            File.WriteAllText(Path.Combine(output, "binding-errors.txt"), string.Join(Environment.NewLine, traces.Messages));
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    /// <summary>
    /// The default window, the size S1 was measured at, and the smallest the window may be made. The last is what a
    /// 1920x1080 laptop at 150% scaling offers - WPF lays out in device-independent units, so rendering at these logical
    /// sizes is what an engineer at that scaling sees, without needing a high-DPI display on the build machine.
    /// </summary>
    private static readonly Size[] PageSizes = { new(1480, 940), new(1180, 760), new(1180, 640) };

    /// <summary>1920x1080 at 150% is 1280x720 device-independent units; the taskbar takes 48 of them.</summary>
    private static readonly Size SmallestWorkArea = new(1280, 672);

    private static void SeedWorkspace(Workspace workspace)
    {
        var standard = workspace.RequireStandard();
        var profile = new TenantProfile { Id = Id(1), TenantId = Tenant, Company = "Synthetic client — UI review only", Domain = "example.invalid", AssessmentClientId = Id(3), DeploymentClientId = Id(4),
            Parameters = new TenantParameters { EmergencyAccountIds = new() { Id(5), Id(6) } } };
        workspace.ApplyProfileToSession(profile, save: false);
        var session = new TenantSession { TenantId = Tenant, TenantName = profile.Company, PrimaryDomain = profile.Domain, Account = "engineer@example.invalid", AccountObjectId = Operator,
            ClientId = profile.DeploymentClientId, ClientLabel = "Synthetic deployment application", Mode = SessionMode.Deployment, TenantVerified = true, ConnectedAt = Stamp,
            OperatorObjectId = Operator, OperatorDisplayName = "Synthetic engineer", OperatorUpn = "engineer@example.invalid", OperatorVerified = true,
            Scopes = ApplicationSetupService.RequiredScopes(standard, SessionMode.Deployment), Notices = new() { "OFFLINE SYNTHETIC UI FIXTURE. No live tenant connection or writes." } };
        Set(workspace, nameof(Workspace.Connection), new ConnectedTenant(session, new OfflineGraph(), authenticator: null));
        Set(workspace, nameof(Workspace.Licences), new LicenceInventory
        {
            TenantId = Tenant, CapturedAt = Stamp, SubscriptionsComplete = true, UsersComplete = true,
            Subscriptions = new() { JsonNode.Parse("""{"skuId":"aaaaaaaa-aaaa-aaaa-aaaa-000000000200","skuPartNumber":"SYNTHETIC_BUSINESS_PREMIUM","capabilityStatus":"Enabled","appliesTo":"User","prepaidUnits":{"enabled":25,"warning":0,"suspended":0},"consumedUnits":18,"servicePlans":[{"servicePlanId":"aaaaaaaa-aaaa-aaaa-aaaa-000000000201","servicePlanName":"AAD_PREMIUM","provisioningStatus":"Success"}]}""")!.AsObject() },
            Users = new() { JsonNode.Parse("""{"id":"22222222-2222-2222-2222-222222222222","displayName":"Synthetic engineer","userPrincipalName":"engineer@example.invalid","accountEnabled":true,"userType":"Member","assignedLicenses":[{"skuId":"aaaaaaaa-aaaa-aaaa-aaaa-000000000200","disabledPlans":[]}],"assignedPlans":[{"servicePlanId":"aaaaaaaa-aaaa-aaaa-aaaa-000000000201","capabilityStatus":"Enabled"}]}""")!.AsObject() }
        });
        var snapshot = new TenantSnapshot { Id = Id(10), TenantId = Tenant, TenantName = profile.Company, PrimaryDomain = profile.Domain, CapturedAt = Stamp, Complete = true, StandardRelease = standard.Release,
            IdentitySource = "Synthetic fixture — not tenant evidence", CapturedBy = session.Account };
        foreach (var (key, definition) in standard.Collections)
            snapshot.Collections[key] = new CollectionCapture { Status = CaptureStatus.Collected, Api = definition.Api, Path = definition.Path };
        if (snapshot.Collections.TryGetValue("conditionalAccess", out var ca))
        {
            ca.Items.Add(JsonNode.Parse("""{"id":"aaaaaaaa-aaaa-aaaa-aaaa-000000000100","displayName":"Synthetic Require MFA","state":"disabled","conditions":{"users":{"includeUsers":["All"],"excludeUsers":["aaaaaaaa-aaaa-aaaa-aaaa-000000000005"]}},"grantControls":{"operator":"OR","builtInControls":["mfa"]}}""")!.AsObject()); ca.Count = 1;
        }
        Set(workspace, nameof(Workspace.Snapshot), snapshot); Set(workspace, nameof(Workspace.SnapshotIsLive), true);
        var assessment = new AssessmentResult { Id = Id(11), TenantId = Tenant, TenantName = profile.Company, PrimaryDomain = profile.Domain, CapturedAt = Stamp, AssessedAt = Stamp, AssessedBy = session.Account,
            Release = standard.Release, StandardDigest = standard.IntegrityDigest, SnapshotId = snapshot.Id, SnapshotComplete = true, Limitations = new() { "Offline UI fixture only. No collection or assessment was performed against a real tenant." } };
        var statuses = new[] { FindingStatus.Missing, FindingStatus.PartialMatch, FindingStatus.SettingsMatchNotEnforced, FindingStatus.RequiresManualReview, FindingStatus.Compliant, FindingStatus.UnableToAssess };
        foreach (var (control, index) in standard.Controls.Select((c, i) => (c, i)))
            assessment.Findings.Add(new ControlFinding { ControlId = control.Id, Name = control.Name, Category = control.Category, Collection = control.Collection, Severity = "High", Status = statuses[index % statuses.Length],
                Reason = index % 2 == 0 ? "Synthetic missing configuration. Review the candidate and exclusions before deployment." : "Synthetic partial match. Targeting and effective protection still require engineer review.",
                DesiredState = "Reviewed build standard configuration", EngineerAction = "Inspect settings, confirm scope and record evidence.", BusinessImpact = "Illustrative review finding only." });
        assessment.Summary = new AssessmentSummary { Total = assessment.Findings.Count, Missing = 8, PartialMatch = 8, SettingsMatchNotEnforced = 8, RequiresManualReview = 7, Compliant = 7, UnableToAssess = 7 };
        Set(workspace, nameof(Workspace.Assessment), assessment);
        var plan = new DeploymentPlan { Id = Id(12), TenantId = Tenant, TenantName = profile.Company, ProfileId = profile.Id, Release = standard.Release, CreatedAt = Stamp, OperatorAccount = session.Account,
            PlanDigest = new string('a', 64), SnapshotId = snapshot.Id, Rows = standard.Controls.Where(c => c.HasRecipe).Take(4).Select((c, i) => new PlanRow { ControlId = c.Id, Name = c.Name, Collection = c.Collection ?? "", Action = i < 2 ? PlanAction.Create : PlanAction.Manual,
                Reason = "Synthetic reviewed candidate; no actual plan execution is possible in this harness.", SafeState = "disabled", ExpectedProductionState = "enabled after review", ExpectedProductionAssignment = "approved pilot scope",
                Payload = new JsonObject { ["displayName"] = "Synthetic " + c.Name, ["state"] = "disabled", ["conditions"] = new JsonObject { ["users"] = new JsonObject { ["includeUsers"] = new JsonArray("All"), ["excludeUsers"] = new JsonArray(Id(5), Id(6)) } } }, Warnings = new() { "Emergency accounts and delegated creator remain excluded until deliberately reviewed." } }).ToList() };
        Set(workspace, nameof(Workspace.Plan), plan);
        var run = new DeploymentRun { Id = Id(13), TenantId = Tenant, TenantName = profile.Company, StartedAt = Stamp, EndedAt = Stamp, BeforeSnapshotId = snapshot.Id, AfterSnapshotId = Id(14), AfterComplete = false,
            Status = RunStatus.ReviewRequired, Error = "Synthetic verification failure illustrates honest result reporting.", Release = standard.Release,
            Results = plan.Rows.Select((r, i) => new RunResult { ControlId = r.ControlId, Name = r.Name, Collection = r.Collection, PlannedAction = r.Action.ToString(), Status = i == 0 ? ResultStatus.Completed : i == 1 ? ResultStatus.Error : ResultStatus.NotRun,
                WriteAcceptance = i < 2 ? WriteAcceptance.Accepted : WriteAcceptance.NotAttempted, Configuration = i == 0 ? ConfigurationVerification.Pass : ConfigurationVerification.Unknown,
                Verification = "Functional verification pending", Reason = "Offline synthetic result — no write occurred." }).ToList() };
        Set(workspace, nameof(Workspace.LastRun), run);
        workspace.Logger.Info("UI review", "OFFLINE SYNTHETIC DATA. No authentication, Graph collection or tenant write was called.");
    }

    private static void SeedConnect(ConnectViewModel vm)
    {
        vm.EditCompany = "Synthetic client — UI review only"; vm.EditTenantId = Tenant; vm.EditDomain = "example.invalid";
        vm.EditAssessmentClientId = Id(3); vm.EditDeploymentClientId = Id(4); vm.AccountQuery = "emergency@example.invalid";
        vm.AccountMatches.Add(new ExclusionAccount { TenantId = Tenant, ObjectId = Id(5), DisplayName = "Emergency access account 01", UserPrincipalName = "emergency01@example.invalid", ResolvedAt = Stamp });
        vm.AccountMatches.Add(new ExclusionAccount { TenantId = Tenant, ObjectId = Id(6), DisplayName = "Emergency access account 02", UserPrincipalName = "emergency02@example.invalid", ResolvedAt = Stamp });
        vm.SelectedAccount = vm.AccountMatches.FirstOrDefault(); vm.ExclusionReason = "Synthetic emergency access account review.";
    }

    private static void SeedPage(ShellViewModel shell, string key)
    {
        if (key == "connect" && shell.Page<ConnectViewModel>().AccountMatches.Count == 0) SeedConnect(shell.Page<ConnectViewModel>());
        if (key == "recovery")
        {
            var vm = shell.Page<RecoveryViewModel>();
            vm.Changes.Clear();
            vm.Changes.Add(new ChangeRegisterRow(Id(300), "CA-001", "Synthetic Require MFA", "conditionalAccess", "Create", Id(301), Stamp, "Accepted", "Unknown", "No recovery attempted"));
            vm.Selected = vm.Changes[0];
            typeof(RecoveryViewModel).GetProperty(nameof(RecoveryViewModel.Plan))!.SetValue(vm, new RecoveryPlan
            {
                Id = Id(302), TenantId = Tenant, SourceRunId = Id(300), ControlId = "CA-001", Name = "Synthetic Require MFA", Collection = "conditionalAccess", ObjectId = Id(301),
                CreatedAt = Stamp, Action = RecoveryAction.DisableConditionalAccess, DriftDetected = true,
                Consequence = "Disable this toolkit-created Conditional Access policy. Its current targeting and exclusions remain stored, but enforcement stops. Other policies can still affect sign-in.",
                CurrentObject = new JsonObject { ["id"] = Id(301), ["displayName"] = "Synthetic Require MFA", ["state"] = "enabled", ["conditions"] = new JsonObject { ["users"] = new JsonObject { ["includeUsers"] = new JsonArray("All"), ["excludeUsers"] = new JsonArray(Id(5)) } } },
                Payload = new JsonObject { ["state"] = "disabled" }
            });
        }
        if (key == "plan")
        {
            var vm = shell.Page<PlanViewModel>();
            vm.SelectedRow = vm.Rows.FirstOrDefault();
            vm.SelectedControl = vm.Controls.First(c => c.ControlId == "ENR-003");
        }
        if (key == "standard")
        {
            var vm = shell.Page<StandardViewModel>();
            vm.Selected = vm.Controls.First(c => c.Id == "CFG-WIN-003");
            vm.ClientName = "Synthetic client — UI review only";
        }
        if (key == "automation")
        {
            var vm = shell.Page<AutomationViewModel>();
            vm.SelectedControl = vm.Controls.First(c => c.Id == "CFG-WIN-002");
            // One supplied value and one rejected value, so the reviewer sees both states of the generated form.
            var list = vm.InputFields.FirstOrDefault(f => f.Type == "guidList");
            if (list is not null) { list.Value = Id(11) + ", " + Id(12); list.TryRead(out _); }
            var text = vm.InputFields.FirstOrDefault(f => f.Type == "guid");
            if (text is not null) { text.Value = "not-an-object-id"; text.TryRead(out _); }
        }
        if (key == "setup")
        {
            var vm = shell.Page<ApplicationSetupViewModel>();
            vm.TenantId = Tenant; vm.NamePrefix = "Synthetic"; vm.AssessmentClientId = Id(3); vm.DeploymentClientId = Id(4);
            var plan = new ApplicationSetupPlan { TenantId = Tenant, TenantName = "Synthetic client — UI review only", OperatorId = Operator, OperatorName = "engineer@example.invalid", CreatedAt = DateTimeOffset.UtcNow,
                StandardRelease = shell.Workspace.RequireStandard().Release, PlanHash = new string('a', 64) };
            foreach (var mode in new[] { SessionMode.Assessment, SessionMode.Deployment })
            {
                var row = new ApplicationSetupRow { Mode = mode, DisplayName = "Synthetic Tenant " + mode, Status = "Create", Reason = "OFFLINE preview only. No application will be created.",
                    Permissions = ApplicationSetupService.RequiredScopes(shell.Workspace.RequireStandard(), mode).Select((s, i) => new SetupPermission { Id = Id(200 + i), Name = s, AdminConsentRequired = true, Description = "Synthetic permission description for layout review." }).ToList(),
                    ApplicationPayload = new JsonObject { ["displayName"] = "Synthetic Tenant " + mode, ["signInAudience"] = "AzureADMyOrg", ["publicClient"] = new JsonObject { ["redirectUris"] = new JsonArray("http://localhost") } } };
                plan.Rows.Add(row);
            }
            typeof(ApplicationSetupViewModel).GetField("_plan", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, plan);
            vm.PlanRows.Clear(); foreach (var row in plan.Rows) vm.PlanRows.Add(row); vm.SelectedRow = vm.PlanRows[0];
            vm.Results.Clear();
            vm.Results.Add(new ApplicationSetupItemResult { Mode = SessionMode.Deployment, DisplayName = "Synthetic Deployment Tool", ClientId = Id(4),
                Status = "Partially completed — review", ConfigurationVerification = "Incomplete", Reason = "Synthetic delayed homepage readback; no tenant writes.",
                AdditionalWrites = new() {
                    new() { Action = "Configure registration", Acceptance = "Accepted" },
                    new() { Action = "Set original tool icon", Acceptance = "Accepted" },
                    new() { Action = "Configure enterprise app", Acceptance = "Accepted" }
                } });
            vm.Validations.Clear();
            foreach (var mode in new[] { SessionMode.Assessment, SessionMode.Deployment })
            {
                var scopes = ApplicationSetupService.RequiredScopes(shell.Workspace.RequireStandard(), mode);
                var ready = mode == SessionMode.Assessment;
                vm.Validations.Add(new ApplicationPermissionValidation { Mode = mode, ClientId = ready ? Id(3) : Id(4),
                    ConfigurationValid = true, ConsentComplete = ready, AssignmentRequired = true, EngineerAssignmentConfirmed = true,
                    EngineerAssignmentStatus = "Synthetic engineer assignment verified",
                    RequiredScopes = scopes, ConfiguredScopes = scopes.ToList(), GrantedScopes = ready ? scopes.ToList() : scopes.Take(scopes.Count - 2).ToList(),
                    Issues = ready ? new() : new() { "Two required permissions are still missing; approve deployment consent, then validate." } });
            }
            typeof(ApplicationSetupViewModel).GetField("_validatedContext", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, typeof(ApplicationSetupViewModel).GetProperty("ValidationContext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm));
            if (!vm.ContinueAssessmentCommand.CanExecute(null) || vm.ContinueDeploymentCommand.CanExecute(null))
                throw new InvalidOperationException("Setup handoff did not distinguish ready assessment from missing deployment consent.");
        }
    }

    private static void VerifyInputRefresh(ShellViewModel shell)
    {
        // A same-tenant catalogue/profile reload must refresh the form; ordinary page refresh must retain unsaved edits.
        var workspace = shell.Workspace;
        var originalStandard = workspace.RequireStandard();
        var originalProfile = workspace.Profile!;
        var vm = shell.Page<AutomationViewModel>();
        try
        {
            var catalogue = ToolkitJson.Deserialize<StandardCatalogue>(ToolkitJson.Serialize(originalStandard));
            catalogue.Parameters.Add(new ParameterDefinition { Key = "syntheticReloadInput", Type = "string", Label = "Synthetic reload input" });
            Set(workspace, nameof(Workspace.Standard), catalogue);
            vm.Refresh();
            var field = vm.InputFields.Single(f => f.Key == "syntheticReloadInput");
            field.Value = "unsaved edit";
            vm.Refresh();
            if (vm.InputFields.Single(f => f.Key == field.Key).Value != "unsaved edit")
                throw new InvalidOperationException("An ordinary refresh discarded an unsaved policy input.");
            var profile = ToolkitJson.Deserialize<TenantProfile>(ToolkitJson.Serialize(originalProfile));
            profile.Parameters.PolicyInputs ??= new();
            profile.Parameters.PolicyInputs[field.Key] = JsonValue.Create("reloaded profile value");
            Set(workspace, nameof(Workspace.Profile), profile);
            vm.Refresh();
            if (vm.InputFields.Single(f => f.Key == field.Key).Value != "reloaded profile value")
                throw new InvalidOperationException("Reloading the same tenant did not refresh saved policy inputs.");
        }
        finally
        {
            Set(workspace, nameof(Workspace.Standard), originalStandard);
            Set(workspace, nameof(Workspace.Profile), originalProfile);
            vm.Refresh();
        }
        if (vm.InputFields.Any(f => f.Key == "syntheticReloadInput"))
            throw new InvalidOperationException("Switching back to the original catalogue left a stale policy input.");
    }

    private static void CapturePrerequisites(FrameworkElement content, Size size, string output, string page)
    {
        var panel = Descendants(content).OfType<PrerequisitePanel>().FirstOrDefault(p => p.Items?.Count > 0)
            ?? throw new InvalidOperationException("Expected prerequisite guidance was not bound on " + page);
        var expander = Descendants(panel).OfType<Expander>().Single();
        expander.IsExpanded = true;
        content.UpdateLayout(); Pump();
        var title = panel.Items![0].Title;
        if (!Descendants(panel).OfType<TextBlock>().Any(t => t.Text == title))
            throw new InvalidOperationException("Expanded prerequisite detail did not materialise on " + page);
        panel.BringIntoView(); content.UpdateLayout(); Pump();
        var heading = Descendants(panel).OfType<TextBlock>().First(t => t.Text == title);
        var visibleHeading = VisibleBounds(heading, content);
        if (visibleHeading.Width < 80 || visibleHeading.Height < heading.ActualHeight - 1)
            throw new InvalidOperationException("Prerequisite title is clipped or outside the viewport on " + page);
        if (page is "standard" or "plan" or "plan-review") AssertUsableControlList(content, page);
        SaveImage(content, size, Path.Combine(output, $"{page}-prerequisites-{(int)size.Width}x{(int)size.Height}.png"));
        expander.IsExpanded = false;
        content.UpdateLayout(); Pump();
    }

    private static Rect VisibleBounds(FrameworkElement element, FrameworkElement root)
    {
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        bounds.Intersect(new Rect(root.RenderSize));
        for (DependencyObject? ancestor = VisualTreeHelper.GetParent(element); ancestor is not null && ancestor != root;
             ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is FrameworkElement frame && (frame.ClipToBounds || frame is ScrollContentPresenter))
                bounds.Intersect(frame.TransformToAncestor(root).TransformBounds(new Rect(frame.RenderSize)));
        }
        return bounds;
    }

    /// <summary>
    /// True when an element is actually drawn on the page being checked. <see cref="UIElement.IsVisible"/> cannot answer
    /// that here: WPF reports it only for elements inside a window that has been shown, and the harness lays pages out
    /// off-screen without showing one, so IsVisible is false for everything. Both checks below used it at first and so
    /// inspected nothing and passed. This walks the visual ancestry for a collapsed or hidden element instead, and
    /// requires the element to have been given a size by layout.
    /// </summary>
    private static bool IsShown(FrameworkElement element, FrameworkElement root)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        for (DependencyObject? node = element; node is not null && !ReferenceEquals(node, root); node = VisualTreeHelper.GetParent(node))
            if (node is UIElement ui && ui.Visibility != Visibility.Visible) return false;
        return true;
    }

    private static bool InsideDataGridCell(DependencyObject element)
    {
        for (var node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is DataGridCell) return true;
        return false;
    }

    private static readonly List<string> UnnamedControls = new();
    private static int NamedControlsInspected;

    /// <summary>
    /// Every control an engineer can operate must announce what it is. A screen reader reads the accessible name; with
    /// none, a grid of deployment runs and a grid of licence assignments are both announced as "data grid", and the
    /// engineer cannot tell which one they are in.
    ///
    /// This asks the automation peer rather than reading the XAML attribute, because that is what the screen reader
    /// asks. A checkbox whose content is text already answers from its content; a button whose content is a panel does
    /// not. Two kinds of control are left out, deliberately: those inside a table cell, which a screen reader reaches
    /// through the table and announces by column header, and the parts of a control's own template, which its owner
    /// names. Controls produced by a data template - the generated input form, for example - are still checked.
    /// </summary>
    private static void RecordUnnamedControls(FrameworkElement content, string where)
    {
        foreach (var element in Descendants(content).OfType<FrameworkElement>())
        {
            if (element is not (TextBox or PasswordBox or ComboBox or ListBox or DataGrid or CheckBox or RadioButton)) continue;
            if (element.TemplatedParent is Control) continue;
            if (!IsShown(element, content) || InsideDataGridCell(element)) continue;
            NamedControlsInspected++;
            var peer = UIElementAutomationPeer.CreatePeerForElement(element);
            if (!string.IsNullOrWhiteSpace(peer?.GetName())) continue;
            UnnamedControls.Add($"  {where} · {element.GetType().Name}" + (string.IsNullOrEmpty(element.Name) ? "" : " '" + element.Name + "'"));
        }
    }

    /// <summary>The DataGridColumnHeader style's MinWidth. A narrower column clips its own header.</summary>
    private const double ReadableColumnWidth = 70;
    private static readonly List<string> CollapsedColumns = new();
    private static readonly HashSet<string> GridsInspected = new(StringComparer.Ordinal);

    /// <summary>
    /// Records every shown table column drawn narrower than it was designed. A DataGrid with a star-sized column tries
    /// to fit the viewport before it scrolls, and does it by squeezing every column towards the 20px default - fixed
    /// ones included. Measured at the minimum window size before ColumnSizing existed, a column declared 100px wide was
    /// drawn at 20 and the Plan page's Explanation column at 20. The table still scrolls, so a render can look complete;
    /// the column simply cannot be read.
    ///
    /// Two tests, both independent of ColumnSizing so that removing it is caught: a column with a declared pixel width is
    /// never drawn narrower than that width, and no column is narrower than its header.
    /// </summary>
    private static void RecordCollapsedColumns(FrameworkElement content, string where)
    {
        foreach (var grid in Descendants(content).OfType<DataGrid>())
        {
            if (!IsShown(grid, content)) continue;
            var name = System.Windows.Automation.AutomationProperties.GetName(grid);
            GridsInspected.Add(name);
            foreach (var column in grid.Columns)
            {
                if (column.Visibility != Visibility.Visible) continue;
                var designed = column.Width.IsAbsolute ? Math.Max(column.Width.Value, ReadableColumnWidth) : ReadableColumnWidth;
                if (column.ActualWidth >= designed - 0.5) continue;
                CollapsedColumns.Add($"  {where} · {name} · '{column.Header}' is {column.ActualWidth:0}px, designed {designed:0}px");
            }
        }
    }

    private static void AssertUsableControlList(FrameworkElement content, string page)
    {
        var name = page == "standard" ? "Standard controls" : page == "plan" ? "Controls to include in the plan" : "Planned actions";
        var list = Descendants(content).OfType<ItemsControl>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == name);
        var viewport = Descendants(list).OfType<ScrollContentPresenter>().First();
        var visible = VisibleBounds(viewport, content);
        var rows = Descendants(list).OfType<FrameworkElement>().Where(e => e is DataGridRow or ListBoxItem)
            .Count(e => VisibleBounds(e, content).Height >= 18);
        if (visible.Height < 100 || visible.Width < 250 || rows < 2)
            throw new InvalidOperationException($"Expanded guidance left the {page} list unusable: {visible.Width:0}x{visible.Height:0}, {rows} visible rows.");
    }

    private static void CaptureAssignmentPopulations(ShellViewModel shell, FrameworkElement content, Size size, string output)
    {
        var vm = shell.Page<AutomationViewModel>();
        vm.SelectedControl = vm.Controls.First(c => c.Id == "CMP-WIN-001");
        vm.Kind = ReviewedChangeKind.AssignGroups;
        vm.IncludedGroups = "";
        vm.ExcludedGroups = Id(40);
        var tabs = Descendants(content).OfType<TabControl>().First();
        tabs.SelectedIndex = 3;
        content.UpdateLayout(); Pump();
        var population = Descendants(content).OfType<ComboBox>().Single(c =>
            System.Windows.Automation.AutomationProperties.GetName(c) == "Assignment population");
        foreach (var target in new[] { AssignmentPopulation.AllUsers, AssignmentPopulation.AllDevices })
        {
            vm.SelectedPopulation = target;
            content.UpdateLayout(); Pump();
            if ((population.Parent as FrameworkElement)?.Visibility != Visibility.Visible || !Equals(population.SelectedItem, target))
                throw new InvalidOperationException("Built-in assignment population selection was not displayed.");
            population.BringIntoView(); content.UpdateLayout(); Pump();
            SaveImage(content, size, Path.Combine(output, $"automation-target-{target}-{(int)size.Width}x{(int)size.Height}.png"));
        }
        vm.Kind = ReviewedChangeKind.SecureCompliance;
        content.UpdateLayout(); Pump();
        if ((population.Parent as FrameworkElement)?.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Assignment population remained visible for a non-assignment action.");
        vm.SelectedPopulation = AssignmentPopulation.Groups;
        vm.ExcludedGroups = "";
        tabs.SelectedIndex = 0;
        content.UpdateLayout(); Pump();
    }

    private static ApplicationSetupService SyntheticSetup() => new(new HttpClient(new NoNetworkHandler()), new NoTokens(),
        new SignInOutcome { TenantId = Tenant, AccountObjectId = Operator, Account = "engineer@example.invalid", Scopes = ApplicationSetupService.SetupScopes }, NullLog.Instance);
    private static void SaveImage(FrameworkElement content, Size size, string file)
    {
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        using var stream = File.Create(file); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
    }
    private static void Set<T>(Workspace workspace, string property, T value) => typeof(Workspace).GetProperty(property)!.SetValue(workspace, value);
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var folder in Directory.EnumerateDirectories(source)) CopyFolder(folder, Path.Combine(destination, Path.GetFileName(folder)));
    }
    private sealed class BindingTrace : TraceListener
    {
        public string Context { get; set; } = "startup";
        public List<string> Messages { get; } = new();
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(Context + ": " + message); }
        public override void WriteLine(string? message) => Write(message);
    }
    private sealed class NoTokens : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken ct) => throw new InvalidOperationException("Authentication is forbidden in the offline UI harness.");
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) => GetAccessTokenAsync(ct);
    }
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new InvalidOperationException("Network is forbidden in the offline UI harness.");
    }
    private sealed class OfflineGraph : IGraphClient
    {
        public string TenantId => Tenant;
        public SessionMode Mode => SessionMode.Deployment;
        public Task<JsonObject> GetAsync(GraphApi api, string path, CancellationToken ct) => throw new InvalidOperationException("Graph calls are forbidden in the offline UI harness.");
        public Task<IReadOnlyList<JsonObject>> GetAllAsync(GraphApi api, string path, CancellationToken ct) => throw new InvalidOperationException("Graph calls are forbidden in the offline UI harness.");
        public Task<JsonObject> WriteAsync(GraphApi api, GraphWriteMethod method, string path, JsonObject payload, CancellationToken ct) => throw new InvalidOperationException("Tenant writes are forbidden in the offline UI harness.");
    }
}
