using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildStandard.App.ViewModels;
using BuildStandard.Core.Models;

internal static partial class Program
{
    // Routed WPF keyboard input on the owned synthetic window; no authentication or executor is invoked.
    // Physical keyboard, Narrator and the unaided engineer journey remain H1 acceptance.
    private static void CheckCaPlanKeyboardJourney(ShellViewModel shell, Window window, FrameworkElement content, string output)
    {
        SeedWorkspace(shell.Workspace);
        var finding = shell.Workspace.Assessment!.Findings.Single(f => f.ControlId == "CA-001");
        var originalStatus = finding.Status;
        var plan = shell.Workspace.Plan!;
        // The generic harness seeds the first four recipes (prerequisite groups). This dedicated
        // journey needs its explicit synthetic CA row; it must not rely on catalogue ordering.
        var added = plan.Rows.Any(r => r.ControlId == "CA-001") ? null : new PlanRow
        {
            ControlId = "CA-001", Name = "Synthetic CA-001 keyboard candidate", Collection = "conditionalAccess",
            Action = PlanAction.Create, SafeState = "disabled", Reason = "Dedicated offline keyboard fixture; no execution is possible.",
            Payload = plan.Rows.First().Payload!.DeepClone().AsObject()
        };
        if (added is not null) plan.Rows.Add(added);
        try
        {
            finding.Status = FindingStatus.Missing; // Explicit synthetic candidate, not live evidence or write approval.
            shell.Navigate("plan");
            var vm = shell.Page<PlanViewModel>(); vm.FilterArea = "All areas"; vm.Refresh();
            var ca = vm.VisibleControls.Single(c => c.ControlId == "CA-001");
            content.UpdateLayout(); Pump();
            var grid = Descendants(content).OfType<DataGrid>().Single(g => System.Windows.Automation.AutomationProperties.GetName(g) == "Controls to include in the plan");
            grid.ScrollIntoView(ca); content.UpdateLayout(); Pump();
            var row = grid.ItemContainerGenerator.ContainerFromItem(ca) as DataGridRow ?? throw new InvalidOperationException("CA-001 row was not realised.");
            var tick = Descendants(row).OfType<CheckBox>().Single();
            if (!ca.Eligible || !tick.IsEnabled || !tick.Focus()) throw new InvalidOperationException("CA-001 cannot receive keyboard selection.");
            var source = PresentationSource.FromVisual(tick) ?? throw new InvalidOperationException("The CA-001 checkbox has no owned native source.");
            var before = ca.IsSelected;
            tick.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
            tick.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
            Pump();
            if (ca.IsSelected == before) throw new InvalidOperationException("Space did not update the actual CA-001 selection binding.");
            ca.IsSelected = before;
            var tabs = Descendants(content).OfType<TabControl>().First(t => t.Items.OfType<TabItem>().Any(i => i.Header?.ToString() == "2  Review exact changes"));
            tabs.SelectedIndex = 1; vm.SelectedRow = vm.Rows.Single(r => r.ControlId == "CA-001");
            content.UpdateLayout(); Pump();
            var payloadTab = Descendants(content).OfType<TabItem>().Single(t => t.Header?.ToString() == "Exact payload");
            payloadTab.IsSelected = true; content.UpdateLayout(); Pump();
            var payload = Descendants(content).OfType<TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Payload to be written");
            var exact = vm.SelectedRow.Payload!.ToJsonString(BuildStandard.Core.Json.ToolkitJson.Options);
            if (!payload.IsReadOnly || !payload.Focusable || payload.Text != exact || !exact.Contains("disabled", StringComparison.Ordinal)
                || !exact.Contains(Id(5), StringComparison.Ordinal) || !exact.Contains(Id(6), StringComparison.Ordinal))
                throw new InvalidOperationException("Exact disabled CA-001 payload and exclusions are not available in the native review.");
            if (!ReferenceEquals(shell.Workspace.Plan, plan)) throw new InvalidOperationException("Presentation journey replaced the stored plan.");
            CheckConfirmationDialog(shell.Workspace, output); // Exact changes and explicit unchecked gate; never click Deploy.
            File.WriteAllText(Path.Combine(output, "ca001-native-keyboard-journey.json"), JsonSerializer.Serialize(new
            { source = "synthetic owned WPF window", control = "CA-001", spaceSelectionBinding = "passed", exactDisabledPayload = "passed",
              preservedExclusions = "passed", explicitConfirmation = "passed", tenantCalls = 0, deploymentExecuted = false, humanAcceptance = "unrun" }));
        }
        finally { if (added is not null) plan.Rows.Remove(added); finding.Status = originalStatus; shell.Page<PlanViewModel>().Refresh(); }
    }
}
