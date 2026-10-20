using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using BuildStandard.App.Infrastructure;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Scripts.Runner;
using BuildStandard.Graph;

internal static partial class Program
{
    private static int ReadRunOutcomeViewportsMeasured;

    /// <summary>Actual command/bindings with a synthetic runner delegate. No host, module, Graph or Microsoft sign-in.</summary>
    private static void CaptureReadRunPartial(ShellViewModel shell, FrameworkElement content, Size size, string output)
    {
        var pages = (IDictionary<string, PageViewModel>)typeof(ShellViewModel).GetField("_pages", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(shell)!;
        var original = pages["run-reports"];
        var calls = 0; var populated = false;
        Func<(ConnectedTenant? Connection, TenantProfile? Profile)> current = () => (shell.Workspace.Connection, shell.Workspace.Profile);
        Func<ScriptEntry, ReadRunContext, IReadOnlyDictionary<string, string?>, string, IProgress<string>?, CancellationToken, Task<ReadRunResult>> run =
            (entry, context, inputs, _, _, token) =>
            {
                calls++; token.ThrowIfCancellationRequested();
                var definition = ScriptCatalogue.ReportFor(entry.Manifest); var now = SystemClock.Instance.UtcNow;
                var evidence = new ExchangeReportEvidence
                {
                    Id = Guid.NewGuid().ToString("D"), RunId = Guid.NewGuid().ToString("D"), ReportId = definition.Id,
                    TenantId = context.TenantId, InitiatingAccountObjectId = context.AccountObjectId, ObservedAccountUpn = context.AccountUpn,
                    Resource = definition.Resource, SourceMode = "live", StartedAt = Timestamps.Format(now.AddSeconds(-1)), EndedAt = Timestamps.Format(now),
                    ToolkitVersion = "0.0.0-synthetic-native", AdapterVersion = ExchangeReportRegistry.AdapterVersion, ModuleVersion = "3.9.0", RuntimeVersion = "7.4.6",
                    ScriptSha256 = entry.Manifest.ScriptSha256, ManifestSha256 = entry.ManifestSha256, RunnerTemplateSha256 = entry.Manifest.Execution!.RunnerTemplateSha256,
                    Parameters = ScriptReadRequest.Bind(entry, context, inputs, now).Select(p => new ExchangeReportParameter { Name = p.Name, Type = p.Type, Value = p.Value }).ToList(),
                    SourceCommands = [.. definition.SourceCommands], Limitations = [definition.Limitations], Status = ReportReadState.Partial,
                    Sections = [new ReportSection { Id = definition.Sections[0].Id, Status = ReportReadState.Partial, Error = populated ? "Synthetic target read is incomplete; other observed fields retain their own meanings." : "Synthetic read stopped before any rows returned; this is incomplete, never an empty successful query.", Limitations = ["Synthetic offline native fixture, not tenant evidence."], Rows = populated ? [ExchangeReportEvidenceSchema.Row(new MailboxForwardingReportRow
                    {
                        Id = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", Name = "Synthetic report mailbox", PrimarySmtpAddress = "report@synthetic.example",
                        ReadStatus = ReportReadState.Partial, Error = "Synthetic SMTP target was not returned.",
                        ForwardingAddressReadStatus = ReportReadState.Collected, ForwardingAddress = null,
                        ForwardingSmtpAddressReadStatus = ReportReadState.Failed, ForwardingSmtpAddress = null,
                        DeliverToMailboxAndForwardReadStatus = ReportReadState.Collected, DeliverToMailboxAndForward = false
                    })] : [] }]
                };
                if (definition.ReportSchemaVersion == 2) PopulateInboxNativeFixture(evidence);
                ExchangeReportEvidenceSchema.Seal(evidence);
                return Task.FromResult(new ReadRunResult(evidence.RunId, ReadRunOutcome.Completed, "Synthetic process completed with a Partial report.", evidence, false));
            };
        bool failSave = false;
        Func<ExchangeReportEvidence, string> save = evidence => failSave ? throw new IOException("Synthetic local save failure") : shell.Workspace.Evidence.SaveExchangeReport(evidence);
        var workflow = (ReadOnlyRunWorkflow)Activator.CreateInstance(typeof(ReadOnlyRunWorkflow), BindingFlags.NonPublic | BindingFlags.Instance, null,
            new object[] { current, ScriptCatalogue.Shipped, SystemClock.Instance, run, save }, null)!;
        var page = (RunReportsViewModel)Activator.CreateInstance(typeof(RunReportsViewModel), BindingFlags.NonPublic | BindingFlags.Instance, null,
            new object[] { shell, workflow }, null)!;
        try
        {
            pages["run-reports"] = page; shell.Navigate("run-reports");
            page.Selected = page.Items.Single(i => i.Id == "exo.mailbox-size-quota"); page.ExperimentalReadEnabled = true; page.PrepareReview();
            if (page.RunCommand.CanExecute(null)) throw new InvalidOperationException("Synthetic review granted implicit approval.");
            page.Approved = true; var command = (AsyncCommand)page.RunCommand; command.Execute(null);
            var deadline = Stopwatch.StartNew();
            while (command.IsRunning) { if (deadline.Elapsed > TimeSpan.FromSeconds(10)) throw new InvalidOperationException("Synthetic read-only command did not complete."); Pump(); }
            if (calls != 1 || workflow.Result?.Evidence?.Status != ReportReadState.Partial || !File.Exists(workflow.SavedFile)
                || !workflow.Output.Contains("Report read status: Partial", StringComparison.Ordinal) || page.SaveEvidenceCommand.CanExecute(null))
                throw new InvalidOperationException("Partial read/evidence was misrepresented or the immutable save guard failed.");
            var copied = "";
            typeof(RunReportsViewModel).GetProperty("PutOnClipboard", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, (Action<string>)(text => copied = text));
            page.CopyOutcomeCommand.Execute(null); if (copied != workflow.Output) throw new InvalidOperationException("Outcome copy omitted the read state.");
            page.CopyEvidenceCommand.Execute(null); if (copied != workflow.EvidenceJson) throw new InvalidOperationException("Evidence copy changed the sealed report.");
            content.UpdateLayout(); Pump();
            var result = Descendants(content).OfType<TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Read-only run outcome");
            result.BringIntoView(); content.UpdateLayout(); Pump();
            ReadRunOutcomeViewportsMeasured++;
            if (VisibleBounds(result, content).Height < 120) CrampedCurrentPages.Add($"  read-only partial outcome {size.Width}x{size.Height} · needs at least 120 pixels of readable outcome.");
            var label = "read-only partial outcome " + size.Width + "x" + size.Height;
            RecordUnnamedControls(content, label); RecordLowContrast(content, label); RecordClipping(content, label);
            SaveImage(content, size, Path.Combine(output, "run-reports-partial-" + (int)size.Width + "x" + (int)size.Height + ".png"));
            populated = true; page.Selected = page.Items.Single(i => i.Id == "exo.mailbox-forwarding-settings");
            page.PrepareReview(); page.Approved = true; command.Execute(null); deadline.Restart();
            while (command.IsRunning) { if (deadline.Elapsed > TimeSpan.FromSeconds(10)) throw new InvalidOperationException("Synthetic typed-row run did not complete."); Pump(); }
            if (page.OutcomeRows?.Count != 1 || !page.OutcomeRows[0].Row.ItemArray.Contains("Not set (observed)")
                || !page.OutcomeRows[0].Row.ItemArray.Contains("false") || !page.OutcomeRows[0].Row.ItemArray.Contains("Unknown / not returned"))
                throw new InvalidOperationException("Native report rows lost explicit unset, false or unknown meaning.");
            content.UpdateLayout(); Pump();
            var table = Descendants(content).OfType<DataGrid>().Single(g => System.Windows.Automation.AutomationProperties.GetName(g) == "Read-only Exchange report rows");
            table.BringIntoView(); content.UpdateLayout(); Pump();
            if (VisibleBounds(table,content).Height < 100) CrampedCurrentPages.Add($"  report rows {size.Width}x{size.Height} · needs at least 100 pixels of visible rows.");
            RecordUnnamedControls(content,"typed Exchange report rows");RecordLowContrast(content,"typed Exchange report rows");RecordClipping(content,"typed Exchange report rows");
            SaveImage(content,size,Path.Combine(output,"run-reports-typed-rows-"+(int)size.Width+"x"+(int)size.Height+".png"));
            page.SelectedExportFormat=BuildStandard.Engine.Reports.ExportFormat.Html;
            var exportCommand=(AsyncCommand)page.ExportReportCommand;exportCommand.Execute(null);deadline.Restart();
            while(exportCommand.IsRunning) { if(deadline.Elapsed>TimeSpan.FromSeconds(10))throw new InvalidOperationException("Synthetic report export did not complete.");Pump(); }
            if(!File.Exists(page.LastExport) || !File.ReadAllText(page.LastExport).Contains("Not set (observed)"))
                throw new InvalidOperationException("Native report export did not preserve the admitted rows.");

            page.Selected = page.Items.Single(item => item.Id == "exo.inbox-rule-observations");
            page.Fields.Single(field => field.Name == "MailboxGuids").Value = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
            page.PrepareReview(); page.Approved = true; command.Execute(null); deadline.Restart();
            while(command.IsRunning) { if(deadline.Elapsed>TimeSpan.FromSeconds(10))throw new InvalidOperationException("Synthetic inbox report did not complete.");Pump(); }
            if(page.OutcomeSections.Count!=2 || page.SelectedOutcomeSection?.Id!="target-reads" || page.OutcomeRows?.Count!=1)
                throw new InvalidOperationException("Native multi-section report lost its target diagnostics.");
            content.UpdateLayout();Pump();
            SaveImage(content,size,Path.Combine(output,"run-reports-inbox-targets-"+(int)size.Width+"x"+(int)size.Height+".png"));
            var originalEvidence=workflow.EvidenceJson;var originalCalls=calls;
            page.SelectedOutcomeSection=page.OutcomeSections.Single(section=>section.Id=="rules");page.OnlyNeedsReview=true;
            if(page.OutcomeRows?.Count!=1 || calls!=originalCalls || workflow.EvidenceJson!=originalEvidence)
                throw new InvalidOperationException("The local inbox filter lost unknowns or repeated/changed collection.");
            content.UpdateLayout();Pump();table.BringIntoView();content.UpdateLayout();Pump();
            if(VisibleBounds(table,content).Height<100)CrampedCurrentPages.Add($"  inbox observations {size.Width}x{size.Height} · needs at least100 pixels of visible rows.");
            RecordUnnamedControls(content,"inbox observations");RecordLowContrast(content,"inbox observations");RecordClipping(content,"inbox observations");
            SaveImage(content,size,Path.Combine(output,"run-reports-inbox-observations-"+(int)size.Width+"x"+(int)size.Height+".png"));

            populated = false; page.Selected = page.Items.Single(i => i.Id == "exo.mailbox-size-quota");
            failSave = true; page.PrepareReview(); page.Approved = true; command.Execute(null);
            deadline.Restart();
            while (command.IsRunning) { if (deadline.Elapsed > TimeSpan.FromSeconds(10)) throw new InvalidOperationException("Synthetic recovery run did not complete."); Pump(); }
            if (!page.HasPendingEvidence || page.CanReview || !page.SaveEvidenceCommand.CanExecute(null))
                throw new InvalidOperationException("Unsaved original report was not retained or another review was allowed.");
            content.UpdateLayout(); Pump();
            var recovery = Descendants(content).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Save original unsaved report");
            recovery.BringIntoView(); content.UpdateLayout(); Pump();
            if (VisibleBounds(recovery, content).Height < 20) CrampedCurrentPages.Add($"  report recovery {size.Width}x{size.Height} · recovery action is not visible.");
            RecordUnnamedControls(content, "report recovery"); RecordLowContrast(content, "report recovery"); RecordClipping(content, "report recovery");
            SaveImage(content, size, Path.Combine(output, "run-reports-recovery-" + (int)size.Width + "x" + (int)size.Height + ".png"));
            var window = Window.GetWindow(content) ?? throw new InvalidOperationException("Recovery window missing.");
            var closing = new System.ComponentModel.CancelEventArgs();
            typeof(BuildStandard.App.MainWindow).GetMethod("OnClosing", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.Invoke(window, [window, closing]); Pump();
            if (!closing.Cancel || shell.Workspace.ShutdownComplete || !page.HasPendingEvidence)
                throw new InvalidOperationException("Window close abandoned an unsaved original report.");
            workflow.DiscardEvidence(workflow.PendingEvidenceId, confirmed: true); page.Refresh(); shell.ClearErrorCommand.Execute(null);

        }
        finally
        {
            // A synthetic seam is never left installed for later command checks, and no real Run is pressed.
            pages["run-reports"] = original; shell.Navigate("run-reports");
        }
    }
}
