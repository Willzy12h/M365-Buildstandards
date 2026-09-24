using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.App.Views;

/// <summary>
/// Checks that go beyond "the page draws": what an engineer can read, reach with the keyboard, and press.
///
/// Each check records problems rather than throwing, so one run reports every defect it finds, and each keeps a count
/// of what it inspected so that a check which looked at nothing fails instead of passing.
/// </summary>
internal static partial class Program
{
    // ---- contrast ------------------------------------------------------------------------------------------------

    private static readonly List<string> LowContrast = new();
    private static int TextContrastInspected;
    private static int BoundariesInspected;

    /// <summary>
    /// WCAG 2.2 AA, measured on the rendered page rather than the palette: text needs 4.5:1 against what is actually
    /// behind it (3:1 when large), and the edge of an input field needs 3:1 so the engineer can see where to type.
    /// Disabled controls are exempt, as WCAG exempts them. The background is found by walking up to the first element
    /// that paints one, blending any translucency over what lies beneath it.
    /// </summary>
    private static void RecordLowContrast(FrameworkElement content, string where)
    {
        foreach (var text in Descendants(content).OfType<TextBlock>())
        {
            if (string.IsNullOrWhiteSpace(text.Text) || !text.IsEnabled || !IsShown(text, content)) continue;
            if (!TryTextColours(text, out var fore, out var back)) continue;
            TextContrastInspected++;
            var ratio = Contrast(fore, back);
            var large = text.FontSize >= 24 || (text.FontSize >= 18.66 && text.FontWeight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight());
            if (ratio >= (large ? 3.0 : 4.5) - 0.005) continue;
            LowContrast.Add($"  {where} · text '{Shorten(text.Text)}' is {ratio:0.00}:1 ({Hex(fore)} on {Hex(back)}), needs {(large ? "3" : "4.5")}:1");
        }
        foreach (var field in Descendants(content).OfType<Control>().Where(c => c is TextBox or PasswordBox or ComboBox))
        {
            if (field.TemplatedParent is Control || !field.IsEnabled || !IsShown(field, content)) continue;
            if (field.BorderThickness.Left <= 0 || field.BorderBrush is not SolidColorBrush edge) continue;
            if (!TryBackdrop(VisualTreeHelper.GetParent(field), out var behind)) continue;
            BoundariesInspected++;
            var edgeColour = Blend(Opaque(edge.Color), edge.Color.A / 255.0 * edge.Opacity, behind);
            var ratio = Contrast(edgeColour, behind);
            if (ratio >= 3.0 - 0.005) continue;
            // A filled field is also visible by its fill, so either the edge or the fill may carry the boundary.
            if (field.Background is SolidColorBrush fill && Contrast(Blend(Opaque(fill.Color), fill.Color.A / 255.0 * fill.Opacity, behind), behind) >= 3.0) continue;
            var name = System.Windows.Automation.AutomationProperties.GetName(field);
            LowContrast.Add($"  {where} · {field.GetType().Name} '{name}' edge is {ratio:0.00}:1 ({Hex(edgeColour)} on {Hex(behind)}), needs 3:1");
        }
    }

    private static bool TryTextColours(TextBlock text, out Color fore, out Color back)
    {
        fore = back = default;
        if (text.Foreground is not SolidColorBrush brush) return false;
        var alpha = brush.Color.A / 255.0 * brush.Opacity;
        for (DependencyObject? node = text; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (Paints(node))
            {
                if (!TryBackdrop(node, out back)) return false;
                fore = Blend(Opaque(brush.Color), alpha, back);
                return true;
            }
            if (node is UIElement ui) alpha *= ui.Opacity;
        }
        return false;
    }

    private static Brush? BackgroundOf(DependencyObject node) => node switch
    {
        Panel panel => panel.Background,
        Border border => border.Background,
        Control control => control.Background,
        TextBlock text => text.Background,
        _ => null
    };

    private static bool Paints(DependencyObject node) =>
        BackgroundOf(node) is { } brush && brush.Opacity > 0 && brush is not SolidColorBrush { Color.A: 0 };

    /// <summary>The opaque colour seen behind <paramref name="from"/>: its own background if it paints one, else its ancestors'.</summary>
    private static bool TryBackdrop(DependencyObject? from, out Color colour)
    {
        colour = default;
        for (var node = from; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (!Paints(node)) continue;
            if (BackgroundOf(node) is not SolidColorBrush solid) return false;
            var alpha = solid.Color.A / 255.0 * solid.Opacity * ((node as UIElement)?.Opacity ?? 1);
            if (alpha >= 0.999) { colour = Opaque(solid.Color); return true; }
            if (!TryBackdrop(VisualTreeHelper.GetParent(node), out var under)) return false;
            colour = Blend(Opaque(solid.Color), alpha, under);
            return true;
        }
        return false;
    }

    private static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);
    private static Color Blend(Color top, double alpha, Color under) => Color.FromRgb(
        (byte)Math.Round(top.R * alpha + under.R * (1 - alpha)),
        (byte)Math.Round(top.G * alpha + under.G * (1 - alpha)),
        (byte)Math.Round(top.B * alpha + under.B * (1 - alpha)));

    internal static double Contrast(Color a, Color b)
    {
        var x = Luminance(a); var y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte value)
        {
            var s = value / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    private static string Shorten(string text) => text.Length <= 40 ? text.Replace('\n', ' ') : text[..40].Replace('\n', ' ') + "…";

    // ---- clipping ------------------------------------------------------------------------------------------------

    private static readonly List<string> ClippedElements = new();
    private static int ClippingInspected;

    /// <summary>
    /// Records text and controls that layout has cut off: an element arranged smaller than the space it needs is given
    /// a layout clip by WPF, and whatever falls outside it is not drawn. Content hidden because it has been scrolled out
    /// of view is not clipped in this sense - it is still reachable - so this finds only content that cannot be seen at
    /// all at this window size. Text that trims with an ellipsis is exempt; trimming is a deliberate, visible choice.
    /// </summary>
    private static void RecordClipping(FrameworkElement content, string where)
    {
        foreach (var element in Descendants(content).OfType<FrameworkElement>())
        {
            if (element is not (TextBlock or ButtonBase or TextBox or ComboBox)) continue;
            if (element.TemplatedParent is Control && element is not TextBlock) continue;
            if (!IsShown(element, content)) continue;
            if (element is TextBlock { TextTrimming: not TextTrimming.None }) continue;
            if (element is TextBlock block && string.IsNullOrWhiteSpace(block.Text)) continue;
            // Table cells trim to their column by design, and ColumnSizing holds the columns to a readable width.
            if (InsideDataGridCell(element)) continue;
            ClippingInspected++;
            var clip = LayoutInformation.GetLayoutClip(element);
            if (clip is null || clip.IsEmpty()) continue;
            // An element arranged smaller than it needs keeps its full RenderSize; the layout clip is the part drawn.
            var visible = clip.Bounds;
            var lostWidth = element.RenderSize.Width - visible.Width;
            var lostHeight = element.RenderSize.Height - visible.Height;
            if (lostWidth < 2 && lostHeight < 2) continue;
            var label = element is TextBlock t ? "text '" + Shorten(t.Text) + "'"
                : element.GetType().Name + " '" + (System.Windows.Automation.AutomationProperties.GetName(element) is { Length: > 0 } n ? n : (element as ContentControl)?.Content?.ToString() ?? "") + "'";
            ClippedElements.Add($"  {where} · {label} is cut off by {Math.Max(lostWidth, 0):0}x{Math.Max(lostHeight, 0):0}px");
        }
    }

    // ---- keyboard ------------------------------------------------------------------------------------------------

    private static readonly List<string> KeyboardProblems = new();
    private static int KeyboardStopsReached;
    private static int KeyboardPagesWalked;
    private static readonly List<string> TabOrder = new();

    /// <summary>
    /// Presses Tab through the page, on a real window, and records every operable control that is never reached.
    /// An engineer who cannot use a mouse - or who is working through a remote session where it is unreliable - can
    /// only use what Tab reaches. Controls inside a table or a list are reached by the arrow keys once the table or
    /// list has focus, so for those it is the table or list itself that must be reached.
    /// Also records focusable buttons and choices that would show no focus indicator, because a control the keyboard
    /// reaches invisibly is nearly as unusable as one it never reaches.
    /// </summary>
    private static void CheckKeyboardReach(Window window, FrameworkElement content, string page)
    {
        content.UpdateLayout(); Pump();
        var expected = Descendants(content).OfType<Control>().Where(c => IsOperable(c, content)).ToList();
        foreach (var control in expected.Where(c => c is ButtonBase or ComboBox && c.FocusVisualStyle is null))
            KeyboardProblems.Add($"  {page} · {Describe(control)} shows no focus indicator");

        Keyboard.ClearFocus();
        window.Activate(); Pump();
        if (!content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)) || Keyboard.FocusedElement is not DependencyObject start)
        {
            KeyboardProblems.Add($"  {page} · keyboard focus could not be placed on the page at all");
            return;
        }
        KeyboardPagesWalked++;
        var reached = new HashSet<DependencyObject>();
        var order = new List<string>();
        var current = start;
        for (var step = 0; step < 2000; step++)
        {
            KeyboardStopsReached++;
            for (DependencyObject? node = current; node is not null; node = VisualTreeHelper.GetParent(node)) reached.Add(node);
            if (current is Control stop && IsDescendant(stop, content)) order.Add(Describe(stop));
            if (current is not UIElement element || !element.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next))) break;
            Pump();
            if (Keyboard.FocusedElement is not DependencyObject next || ReferenceEquals(next, start) || ReferenceEquals(next, current)) break;
            current = next;
        }
        TabOrder.Add(page + ":" + Environment.NewLine + string.Join(Environment.NewLine, order.Select((o, i) => $"  {i + 1,3}. {o}")));
        foreach (var control in expected.Where(c => !reached.Contains(c)))
            KeyboardProblems.Add($"  {page} · {Describe(control)} cannot be reached with Tab");
    }

    private static bool IsOperable(Control control, FrameworkElement content)
    {
        if (!control.IsEnabled || !IsShown(control, content)) return false;
        // Parts of a control's own template are reached through that control; an Expander is operated by its header toggle.
        if (control.TemplatedParent is Control owner && !(owner is Expander && control is ToggleButton)) return false;
        for (var node = VisualTreeHelper.GetParent(control); node is not null && !ReferenceEquals(node, content); node = VisualTreeHelper.GetParent(node))
            if (node is DataGrid or ComboBox) return false;
        return control is ButtonBase or TextBox or PasswordBox or ComboBox or ListBox or DataGrid
            || control is TabItem { IsSelected: true };
    }

    private static bool IsDescendant(DependencyObject element, DependencyObject root)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, root)) return true;
        return false;
    }

    private static string Describe(Control control)
    {
        var name = System.Windows.Automation.AutomationProperties.GetName(control);
        if (string.IsNullOrWhiteSpace(name)) name = (control as HeaderedContentControl)?.Header?.ToString() ?? (control as ContentControl)?.Content as string ?? "";
        if (string.IsNullOrWhiteSpace(name) && control is ContentControl { Content: DependencyObject inner })
            name = string.Join(" ", Descendants(inner).OfType<TextBlock>().Select(t => t.Text).Where(t => t.Length > 0).Take(2));
        if (string.IsNullOrWhiteSpace(name) && control.TemplatedParent is Control owner) name = "part of " + owner.GetType().Name + " " + System.Windows.Automation.AutomationProperties.GetName(owner);
        return control.GetType().Name + (string.IsNullOrWhiteSpace(name) ? "" : " '" + Shorten(name) + "'");
    }

    // ---- final confirmation dialog -------------------------------------------------------------------------------

    private static int DialogChecks;

    /// <summary>
    /// The last thing between an engineer and a tenant write. It is built here from the synthetic plan exactly as the
    /// Deploy page builds it, laid out at its default and minimum sizes, and its one safety behaviour is exercised:
    /// the deploy button stays disabled until the connected tenant ID is typed in full. Nothing is deployed - the dialog
    /// is closed without a result, and the harness's Graph client refuses every call regardless.
    /// </summary>
    private static void CheckConfirmationDialog(Workspace workspace, string output)
    {
        var profile = workspace.Profile ?? throw new InvalidOperationException("The synthetic client is not loaded.");
        var plan = workspace.Plan ?? throw new InvalidOperationException("The synthetic plan is not loaded.");
        var dialog = new ConfirmTenantDialog(profile, plan)
        {
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000
        };
        try
        {
            if (dialog.Content is not FrameworkElement root) throw new InvalidOperationException("The confirmation dialog has no content.");
            var typed = Part<TextBox>(dialog, "TypedTenantIdBox");
            var deploy = Part<Button>(dialog, "DeployButton");
            var changes = Part<ListBox>(dialog, "ChangeList");
            if (changes.Items.Count != plan.WriteRows.Count())
                throw new InvalidOperationException($"The confirmation dialog lists {changes.Items.Count} change(s) for a plan with {plan.WriteRows.Count()} write(s).");

            foreach (var size in new[] { new Size(dialog.Width, dialog.Height), new Size(dialog.MinWidth, dialog.MinHeight) })
            {
                var where = $"confirm-dialog {(int)size.Width}x{(int)size.Height}";
                dialog.Width = size.Width; dialog.Height = size.Height;
                dialog.Show(); Pump(); root.UpdateLayout(); Pump();
                RecordUnnamedControls(root, where);
                RecordLowContrast(root, where);
                RecordClipping(root, where);
                var bounds = VisibleBounds(deploy, root);
                if (bounds.Width < deploy.ActualWidth - 1 || bounds.Height < deploy.ActualHeight - 1)
                    throw new InvalidOperationException($"The deploy button is not fully visible in the confirmation dialog at {where}.");
                SaveWindowImage(root, Path.Combine(output, $"confirm-dialog-{(int)size.Width}x{(int)size.Height}.png"));
                DialogChecks++;
            }

            // The engineer must be able to start typing straight away.
            dialog.Activate(); Pump();
            var focused = Keyboard.FocusedElement as DependencyObject ?? FocusManager.GetFocusedElement(dialog) as DependencyObject;
            if (!ReferenceEquals(focused, typed))
                KeyboardProblems.Add($"  confirm-dialog · focus starts on {(focused as Control is { } c ? Describe(c) : focused?.GetType().Name ?? "nothing")}, not the tenant ID box");

            // The gate itself: nothing short of the exact tenant ID enables the write.
            var gate = new (string Text, bool Enabled)[]
            {
                ("", false),
                (profile.TenantId[..^1], false),
                (profile.TenantId + "0", false),
                ("00000000-0000-0000-0000-000000000000", false),
                (profile.Company, false),
                (profile.TenantId, true),
                ("  " + profile.TenantId.ToUpperInvariant() + "  ", true),
                ("", false)
            };
            foreach (var (text, enabled) in gate)
            {
                typed.Text = text; Pump();
                if (deploy.IsEnabled != enabled)
                    throw new InvalidOperationException($"The confirmation dialog's deploy button is {(deploy.IsEnabled ? "enabled" : "disabled")} after typing '{text}'.");
                DialogChecks++;
            }
            if (deploy.IsDefault) throw new InvalidOperationException("The deploy button is the default button, so Enter would deploy.");
        }
        finally
        {
            dialog.Close(); Pump();
        }
    }

    private static T Part<T>(FrameworkElement owner, string name) where T : class =>
        owner.FindName(name) as T ?? throw new InvalidOperationException($"The confirmation dialog has no {typeof(T).Name} named {name}.");

    private static void SaveWindowImage(FrameworkElement root, string file)
    {
        var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, width, height));
        }
        bitmap.Render(visual);
        using var stream = File.Create(file);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
    }

    // ---- commands ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every command on every page, and what the harness does with it. A command added to a page without being added
    /// here fails the run, so this list cannot silently fall behind the interface.
    ///
    /// Only commands that stay inside this installation are pressed: saving, exporting, planning, navigating. Anything
    /// that would reach a tenant, open a prompt, a file picker, Explorer or a browser, or touch the system clipboard is
    /// listed with the reason it is not pressed. The Graph client, token source and HTTP handler in this harness refuse
    /// every call in any case; a pressed command that reaches one of them is recorded as a defect in this list.
    /// </summary>
    private static readonly (string Command, string Handling)[] CommandRegister =
    {
        ("ShellViewModel.NavigateCommand", Press),
        ("ShellViewModel.ClearErrorCommand", Press),
        ("ShellViewModel.CopyCommand", Clipboard),
        ("ShellViewModel.CopyDetailsCommand", Clipboard),
        ("ShellViewModel.DisconnectCommand", "ends the session; covered by the idle-close check"),
        ("ShellViewModel.CancelOperationCommand", Disabled),

        ("OverviewViewModel.ConnectCommand", Press),
        ("OverviewViewModel.RefreshLicencesCommand", TenantRead),
        ("OverviewViewModel.LoadAssignmentsCommand", TenantRead),

        ("ConnectViewModel.SaveProfileCommand", Press),
        ("ConnectViewModel.AddExclusionCommand", Press),
        ("ConnectViewModel.RemoveExclusionCommand", Press),
        ("ConnectViewModel.ApplyExclusionsCommand", Press),
        ("ConnectViewModel.OpenSetupCommand", Press),
        ("ConnectViewModel.NewProfileCommand", Press),
        ("ConnectViewModel.DeleteProfileCommand", Prompt),
        ("ConnectViewModel.ConnectAssessmentCommand", SignIn),
        ("ConnectViewModel.ConnectDeploymentCommand", SignIn),
        ("ConnectViewModel.ConnectSelectedCommand", SignIn),
        ("ConnectViewModel.CheckAccessCommand", TenantRead),
        ("ConnectViewModel.SearchAccountsCommand", TenantRead),
        ("ConnectViewModel.CopyApplicationCommand", Clipboard),
        ("ConnectViewModel.CopyAccessCommand", Clipboard),

        ("ApplicationSetupViewModel.ApplyIdsCommand", Press),
        ("ApplicationSetupViewModel.ConnectCommand", SignIn),
        ("ApplicationSetupViewModel.PreviewCommand", TenantRead),
        ("ApplicationSetupViewModel.CreateCommand", TenantWrite),
        ("ApplicationSetupViewModel.ValidateCommand", TenantRead),
        ("ApplicationSetupViewModel.AssessmentConsentCommand", Browser),
        ("ApplicationSetupViewModel.DeploymentConsentCommand", Browser),
        ("ApplicationSetupViewModel.ContinueAssessmentCommand", SignIn),
        ("ApplicationSetupViewModel.ContinueDeploymentCommand", SignIn),
        ("ApplicationSetupViewModel.CheckDelegatedAdminCommand", SignIn),
        ("ApplicationSetupViewModel.DisconnectCommand", "ends the setup session"),
        ("ApplicationSetupViewModel.OpenEntraCommand", Browser),

        ("ConfigurationViewModel.ExportJsonCommand", Press),
        ("ConfigurationViewModel.ExportCsvCommand", Press),
        ("ConfigurationViewModel.ExportXlsxCommand", Press),
        ("ConfigurationViewModel.LoadStoredCommand", Press),
        ("ConfigurationViewModel.CaptureCommand", TenantRead),
        ("ConfigurationViewModel.OpenExportCommand", Explorer),
        ("ConfigurationViewModel.CopySummaryCommand", Clipboard),

        ("AssessmentViewModel.ReassessCommand", Press),
        ("AssessmentViewModel.ExportHtmlCommand", Press),
        ("AssessmentViewModel.ExportMarkdownCommand", Press),
        ("AssessmentViewModel.ExportJsonCommand", Press),
        ("AssessmentViewModel.ExportCsvCommand", Press),
        ("AssessmentViewModel.ExportXlsxCommand", Press),
        ("AssessmentViewModel.ExportClientCommand", Press),
        ("AssessmentViewModel.OpenExportCommand", Explorer),
        ("AssessmentViewModel.CopyDetailCommand", Clipboard),

        ("DeviationsViewModel.SaveCommand", Press),
        ("DeviationsViewModel.NewCommand", Press),
        ("DeviationsViewModel.DeleteCommand", Prompt),

        ("PlanViewModel.SelectEligibleCommand", Press),
        ("PlanViewModel.BuildPlanCommand", Press),
        ("PlanViewModel.ClearSelectionCommand", Press),

        ("DeployViewModel.ExportBeforeCommand", Press),
        ("DeployViewModel.AcknowledgeCommand", Press),
        ("DeployViewModel.ExportRunHtmlCommand", Press),
        ("DeployViewModel.ExportRunJsonCommand", Press),
        ("DeployViewModel.ExportRunXlsxCommand", Press),
        ("DeployViewModel.EnableDeploymentCommand", Prompt),
        ("DeployViewModel.CaptureCommand", TenantRead),
        ("DeployViewModel.DeployCommand", TenantWrite),
        ("DeployViewModel.PauseCommand", Disabled),
        ("DeployViewModel.ResumeCommand", Disabled),
        ("DeployViewModel.StopCommand", Disabled),

        ("RecoveryViewModel.LoadRegisterCommand", Press),
        ("RecoveryViewModel.PreviewDeleteCommand", TenantRead),
        ("RecoveryViewModel.PreviewRestoreCommand", TenantRead),
        ("RecoveryViewModel.PreviewDisableCommand", TenantRead),
        ("RecoveryViewModel.ExecuteCommand", TenantWrite),
        ("RecoveryViewModel.ReverifyDeploymentCommand", TenantRead),
        ("RecoveryViewModel.ReverifyRecoveryCommand", TenantRead),

        ("HistoryViewModel.RefreshCommand", Press),
        ("HistoryViewModel.CompareCommand", Press),
        ("HistoryViewModel.ExportRunHtmlCommand", Press),
        ("HistoryViewModel.ExportRunJsonCommand", Press),
        ("HistoryViewModel.ExportRunXlsxCommand", Press),
        ("HistoryViewModel.ExportDriftHtmlCommand", Press),
        ("HistoryViewModel.ExportDriftMarkdownCommand", Press),
        ("HistoryViewModel.ExportDriftXlsxCommand", Press),
        ("HistoryViewModel.OpenSnapshotCommand", Press),

        ("ManualChecksViewModel.SaveCommand", Press),

        ("StandardViewModel.SelectReleaseCommand", Press),
        ("StandardViewModel.ExportDocumentCommand", Press),
        ("StandardViewModel.ExportDocumentMarkdownCommand", Press),

        ("AutomationViewModel.SaveInputsCommand", Press),
        ("AutomationViewModel.ImportCommand", Press),
        ("AutomationViewModel.LoadCandidateCommand", Press),
        ("AutomationViewModel.ChooseImportCommand", FilePicker),
        ("AutomationViewModel.ChoosePackageCommand", FilePicker),
        ("AutomationViewModel.PreviewCommand", TenantRead),
        ("AutomationViewModel.ExecuteCommand", TenantWrite),
        ("AutomationViewModel.PreviewLapsCommand", TenantRead),
        ("AutomationViewModel.ExecuteLapsCommand", TenantWrite),
        ("AutomationViewModel.ReverifyCommand", TenantRead),
        ("AutomationViewModel.ReverifyLapsCommand", TenantRead),
        ("AutomationViewModel.CaptureCommand", TenantRead),
        ("AutomationViewModel.PreviewPackageCommand", TenantRead),
        ("AutomationViewModel.PublishPackageCommand", TenantWrite),
        ("AutomationViewModel.ReverifyPackageCommand", TenantRead),
        ("AutomationViewModel.CheckReadinessCommand", TenantRead),

        ("SettingsViewModel.OpenReportsCommand", Explorer),
        ("SettingsViewModel.OpenLogsCommand", Explorer),
        ("SettingsViewModel.OpenDataCommand", Explorer),
        ("SettingsViewModel.OpenConfigCommand", Explorer),
        ("SettingsViewModel.OpenStandardsCommand", Explorer),
    };

    private const string Press = "press";
    private const string Disabled = "must be disabled while nothing is running";
    private const string TenantRead = "reads the tenant";
    private const string TenantWrite = "writes to the tenant";
    private const string SignIn = "signs in to a tenant";
    private const string Prompt = "asks for confirmation in a message box";
    private const string FilePicker = "opens a file picker";
    private const string Explorer = "opens File Explorer";
    private const string Browser = "opens a web browser";
    private const string Clipboard = "writes to the system clipboard";

    /// <summary>Exception types that mean a defect when a command raises them, whatever the command then shows the engineer.</summary>
    private static readonly Type[] DefectExceptions =
    {
        typeof(NullReferenceException), typeof(ArgumentNullException), typeof(ArgumentOutOfRangeException),
        typeof(IndexOutOfRangeException), typeof(InvalidCastException), typeof(KeyNotFoundException),
        typeof(NotImplementedException), typeof(FormatException), typeof(DivideByZeroException), typeof(OverflowException)
    };

    private static readonly List<string> CommandProblems = new();
    private static readonly List<string> CommandLog = new();
    private static int CommandsCompleted;
    private static int CommandsRefused;

    private static void PressCommands(ShellViewModel shell, FrameworkElement content, BindingTrace traces)
    {
        var workspace = shell.Workspace;
        var register = CommandRegister.ToDictionary(r => r.Command, r => r.Handling, StringComparer.Ordinal);
        var pages = (IDictionary<string, PageViewModel>)typeof(ShellViewModel).GetField("_pages", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(shell)!;
        var owners = new List<(string Key, object ViewModel)> { ("overview", shell) };
        owners.AddRange(pages.Select(p => (p.Key, (object)p.Value)));

        var found = new List<(string Name, string Page, ICommand Command)>();
        foreach (var (key, vm) in owners)
            foreach (var property in vm.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType)))
                found.Add(($"{vm.GetType().Name}.{property.Name}", key, (ICommand)property.GetValue(vm)!));

        foreach (var name in found.Select(f => f.Name).Where(n => !register.ContainsKey(n)))
            CommandProblems.Add($"  {name} is not in the harness command register; add it with how it is to be handled");
        foreach (var name in register.Keys.Where(n => found.All(f => f.Name != n)))
            CommandProblems.Add($"  {name} is in the harness command register but no longer exists");

        // Commands are pressed in register order, so a page's save runs before its reset and planning before clearing.
        var byName = found.ToDictionary(f => f.Name, StringComparer.Ordinal);
        var exceptions = new List<Exception>();
        var listening = false;
        EventHandler<FirstChanceExceptionEventArgs> listener = (_, e) => { if (listening) lock (exceptions) exceptions.Add(e.Exception); };
        AppDomain.CurrentDomain.FirstChanceException += listener;
        try
        {
            foreach (var (name, handling) in CommandRegister)
            {
                if (!byName.TryGetValue(name, out var entry)) continue;
                if (handling == Disabled)
                {
                    if (entry.Command.CanExecute(null)) CommandProblems.Add($"  {name} is enabled while nothing is running");
                    CommandLog.Add($"{name}: disabled, as it must be");
                    continue;
                }
                if (handling != Press) { CommandLog.Add($"{name}: not pressed - {handling}"); continue; }

                var parameters = name == "ShellViewModel.NavigateCommand" ? shell.NavItems.Cast<object?>().ToArray() : new object?[] { null };
                foreach (var parameter in parameters)
                {
                    var label = parameter is NavItem item ? $"{name}({item.Key})" : name;
                    shell.Navigate(entry.Page);
                    SeedPage(shell, entry.Page);
                    content.UpdateLayout(); Pump();
                    if (!entry.Command.CanExecute(parameter)) { CommandLog.Add($"{label}: not enabled with the synthetic data"); continue; }

                    var saved = SaveState(workspace);
                    traces.Context = "command " + label;
                    ClearError(shell);
                    lock (exceptions) exceptions.Clear();
                    listening = true;
                    try
                    {
                        entry.Command.Execute(parameter);
                        var clock = Stopwatch.StartNew();
                        while (((entry.Command as AsyncCommand)?.IsRunning ?? false) || workspace.Busy)
                        {
                            if (clock.Elapsed > TimeSpan.FromSeconds(60)) { CommandProblems.Add($"  {label} did not finish within 60 seconds"); break; }
                            Pump(); Thread.Sleep(10);
                        }
                        Pump();
                    }
                    finally { listening = false; }

                    List<Exception> raised;
                    lock (exceptions) raised = exceptions.ToList();
                    foreach (var ex in raised)
                    {
                        if (ex.Message.Contains("forbidden in the offline UI harness", StringComparison.Ordinal))
                            CommandProblems.Add($"  {label} reached the tenant: {ex.Message}");
                        else if (DefectExceptions.Any(t => t.IsInstanceOfType(ex)) && IsOurs(ex))
                            CommandProblems.Add($"  {label} raised {ex.GetType().Name}: {ex.Message} (at {ex.TargetSite?.DeclaringType?.Name}.{ex.TargetSite?.Name})");
                    }
                    if (shell.ErrorMessage.Length > 0) { CommandsRefused++; CommandLog.Add($"{label}: refused - {shell.ErrorMessage}"); }
                    else { CommandsCompleted++; CommandLog.Add($"{label}: completed"); }

                    // The page must still draw after the command, with the state it left behind.
                    content.UpdateLayout(); Pump();
                    ClearError(shell);
                    RestoreState(workspace, saved);
                }
            }
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= listener;
            traces.Context = "after commands";
        }
    }

    /// <summary>A defect-type exception counts when it was thrown from this product's code, or from the runtime on its behalf.</summary>
    private static bool IsOurs(Exception ex)
    {
        var assembly = ex.TargetSite?.DeclaringType?.Assembly.GetName().Name ?? "";
        if (assembly.StartsWith("BDIT.", StringComparison.Ordinal)) return true;
        if (ex is NullReferenceException) return true;
        // Argument and format exceptions are raised inside the runtime, so the product frame is further down the stack.
        return new StackTrace(ex, false).GetFrames().Any(f => f.GetMethod()?.DeclaringType?.Assembly.GetName().Name?.StartsWith("BDIT.", StringComparison.Ordinal) == true)
            || (ex.StackTrace?.Contains("BDIT.TenantToolkit", StringComparison.Ordinal) ?? false);
    }

    private static readonly string[] StateProperties =
    {
        nameof(Workspace.Standard), nameof(Workspace.Profile), nameof(Workspace.Connection), nameof(Workspace.Snapshot),
        nameof(Workspace.SnapshotIsLive), nameof(Workspace.Assessment), nameof(Workspace.Plan),
        nameof(Workspace.AcknowledgedSnapshotId), nameof(Workspace.LastRun), nameof(Workspace.ApplicationSetup)
    };

    /// <summary>Commands move the workspace on - a stored snapshot loaded for review, a new plan. Each is pressed from the same starting state.</summary>
    private static Dictionary<string, object?> SaveState(Workspace workspace) =>
        StateProperties.ToDictionary(p => p, p => typeof(Workspace).GetProperty(p)!.GetValue(workspace));

    private static void RestoreState(Workspace workspace, Dictionary<string, object?> state)
    {
        foreach (var (property, value) in state) typeof(Workspace).GetProperty(property)!.SetValue(workspace, value);
        typeof(Workspace).GetMethod("Notify", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(workspace, null);
        Pump();
    }

    private static void ClearError(ShellViewModel shell)
    {
        if (shell.ErrorMessage.Length > 0) shell.ClearErrorCommand.Execute(null);
    }

    private static void WriteReviewNotes(string output)
    {
        File.WriteAllText(Path.Combine(output, "commands.txt"), string.Join(Environment.NewLine, CommandLog));
        File.WriteAllText(Path.Combine(output, "tab-order.txt"), string.Join(Environment.NewLine + Environment.NewLine, TabOrder));
    }
}
