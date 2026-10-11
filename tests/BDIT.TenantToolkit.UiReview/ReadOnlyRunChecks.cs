using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Engine.Scripts;
using BDIT.TenantToolkit.Engine.Scripts.Runner;
using BDIT.TenantToolkit.Graph;

internal static partial class Program
{
    private static int ReadRunOutcomeViewportsMeasured;

    /// <summary>Actual command/bindings with a synthetic runner delegate. No host, module, Graph or Microsoft sign-in.</summary>
    private static void CaptureReadRunPartial(ShellViewModel shell, FrameworkElement content, Size size, string output)
    {
        var pages = (IDictionary<string, PageViewModel>)typeof(ShellViewModel).GetField("_pages", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(shell)!;
        var original = pages["run-reports"];
        var calls = 0;
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
                    Sections = [new ReportSection { Id = "mailboxes", Status = ReportReadState.Partial, Error = "Synthetic read stopped before any rows returned; this is incomplete, never an empty successful query.", Limitations = ["Synthetic offline native fixture, not tenant evidence."], Rows = [] }]
                };
                ExchangeReportEvidenceSchema.Seal(evidence);
                return Task.FromResult(new ReadRunResult(evidence.RunId, ReadRunOutcome.Completed, "Synthetic process completed with a Partial report.", evidence, false));
            };
        Func<ExchangeReportEvidence, string> save = shell.Workspace.Evidence.SaveExchangeReport;
        var workflow = (ReadOnlyRunWorkflow)Activator.CreateInstance(typeof(ReadOnlyRunWorkflow), BindingFlags.NonPublic | BindingFlags.Instance, null,
            new object[] { current, ScriptCatalogue.Shipped, SystemClock.Instance, run, save }, null)!;
        var page = (RunReportsViewModel)Activator.CreateInstance(typeof(RunReportsViewModel), BindingFlags.NonPublic | BindingFlags.Instance, null,
            new object[] { shell, workflow }, null)!;
        try
        {
            pages["run-reports"] = page; shell.Navigate("run-reports");
            page.Selected = page.Items.Single(); page.ExperimentalReadEnabled = true; page.PrepareReview();
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
        }
        finally
        {
            // A synthetic seam is never left installed for later command checks, and no real Run is pressed.
            pages["run-reports"] = original; shell.Navigate("run-reports");
        }
    }
}
