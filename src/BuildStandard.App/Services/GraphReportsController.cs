using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using BuildStandard.Graph;
using Envelope = BuildStandard.Engine.Reports.ReportEnvelopeDispatcher.Envelope;

namespace BuildStandard.App.Services;

/// <summary>Companion orchestration over the existing workspace. Report evidence never becomes a snapshot.</summary>
public sealed class GraphReportsController(Workspace workspace)
{
    public sealed record Outcome(Envelope Evidence, string File, string? NotSavedReason);
    public string ContextKey => ToolkitJson.Serialize(new
    {
        workspace.Profile, Session = workspace.Session,
        ProfileReference = workspace.Profile is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(workspace.Profile),
        ConnectionReference = workspace.Connection is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(workspace.Connection),
        GraphTenant = workspace.Connection?.Graph.TenantId, GraphMode = workspace.Connection?.Graph.Mode
    });

    public string ReadBlocker
    {
        get
        {
            try { RequireLive(); return workspace.Idle ? "" : "Wait for the current operation to finish."; }
            catch (ToolkitException ex) { return ex.Message; }
        }
    }

    private ConnectedTenant RequireLive()
    {
        var connection = workspace.RequireConnection();
        var s = connection.Session;
        if (!s.TenantVerified || !s.OperatorVerified || !ProfileValidator.IsGuid(s.AccountObjectId)
            || !string.Equals(connection.Graph.TenantId, s.TenantId, StringComparison.OrdinalIgnoreCase)
            || connection.Graph.Mode != s.Mode)
            throw new TenantMismatchException("Reconnect deliberately with a verified tenant and account before reading reports.");
        // This is the expiry observed at Connect, not the lifetime of the verified read context.
        // The existing provider silently renews same-identity/same-scope access without changing this timestamp.
        // Experimental tenant-write authority retains its separate original-expiry guard.
        if (!Timestamps.TryParse(s.TokenExpiresAt, out _))
            throw new ToolkitException("The connection's original token expiry is unknown or malformed. Use Connect deliberately; Reports never signs in.");
        if (connection.Graph is not GraphClient)
            throw new ToolkitException("Reports require the existing guarded Graph connection; use Connect first.");
        return connection;
    }

    public void RequireContext(string expectedContext)
    {
        if (ContextKey != expectedContext) throw new TenantMismatchException("The selected client, account or permission context changed. Review the new context and try again.");
        if (workspace.Profile is null) throw new ToolkitException("Select a client in Connect first.");
    }

    private void RequireIdle(string context)
    {
        RequireContext(context);
        if (!workspace.Idle) throw new ToolkitException("Wait for the current operation to finish.");
    }

    public Task<Outcome> ReadAsync(string reportId, ReportParameters parameters, bool acknowledged, string expectedContext,
        Func<bool>? selectionStillCurrent = null)
    {
        var definition = GraphReportRegistry.Find(reportId);
        var frozen = new ReportParameters { Start = parameters.Start, End = parameters.End };
        ReportEvidenceSchema.ValidateParameters(definition, frozen);
        if (frozen.End is not null && Timestamps.TryParse(frozen.End, out var end) && end > DateTimeOffset.UtcNow)
            throw new ConfigurationException("A live log range cannot end in the future.");
        return RunReadAsync(GraphReportPresentation.Find(1, reportId), acknowledged, expectedContext, selectionStillCurrent,
            async connection => ReportEnvelopeDispatcher.Wrap(await new GraphReportService(SystemClock.Instance).CaptureAsync(
                connection.ForReports(), connection.Session, definition.Id, frozen, workspace.OperationToken), connection.Session.TenantId));
    }

    public Task<Outcome> ReadExpiryAsync(ApplicationExpiryParameters parameters, bool acknowledged, string expectedContext,
        Func<bool>? selectionStillCurrent = null)
    {
        var frozen = new ApplicationExpiryParameters { HorizonDays = parameters.HorizonDays };
        ApplicationReportEvidenceSchema.ValidateParameters(frozen);
        var choice = GraphReportPresentation.Find(2, "application-expiry");
        return RunReadAsync(choice, acknowledged, expectedContext, selectionStillCurrent,
            async connection => ReportEnvelopeDispatcher.Wrap(await new ApplicationReportService(SystemClock.Instance).CaptureExpiryAsync(
                ((GraphClient)connection.Graph).ForApplicationReports(choice.SchemaVersion, choice.Id), connection.Session,
                frozen, workspace.OperationToken), connection.Session.TenantId));
    }

    public Task<Outcome> ReadConsentAsync(bool acknowledged, string expectedContext, Func<bool>? selectionStillCurrent = null)
    {
        var choice = GraphReportPresentation.Find(2, "application-consent");
        return RunReadAsync(choice, acknowledged, expectedContext, selectionStillCurrent,
            async connection => ReportEnvelopeDispatcher.Wrap(await new ApplicationReportService(SystemClock.Instance).CaptureConsentAsync(
                ((GraphClient)connection.Graph).ForApplicationReports(choice.SchemaVersion, choice.Id), connection.Session,
                new ApplicationConsentParameters(), workspace.OperationToken), connection.Session.TenantId));
    }

    private async Task<Outcome> RunReadAsync(GraphReportPresentation choice, bool acknowledged, string expectedContext,
        Func<bool>? selectionStillCurrent, Func<ConnectedTenant, Task<Envelope>> capture)
    {
        RequireIdle(expectedContext);
        if (!acknowledged) throw new ToolkitException("Acknowledge the experimental read and its limitations for this selection first. This grants no consent.");
        var connection = RequireLive();
        Outcome? result = null;
        void AssertCurrent()
        {
            RequireContext(expectedContext);
            if (!ReferenceEquals(workspace.Connection, connection) || !ReferenceEquals(GraphReportPresentation.Find(choice.SchemaVersion, choice.Id), choice)
                || selectionStillCurrent?.Invoke() == false)
                throw new TenantMismatchException("The report or connection selection changed during the read. No result was accepted or saved.");
            RequireLive();
        }
        await workspace.RunExclusiveAsync("Reading " + choice.Name, async progress =>
        {
            AssertCurrent();
            progress.Report("Reusing the verified session; registered read-only routes, at most five minutes and 5,000 rows per section.");
            var report = await capture(connection);
            AssertCurrent(); // Includes cancellation results: preserve their explicit states, never a snapshot.
            string file = ""; string? reason = null;
            try { file = report.Schema1 is { } legacy ? workspace.Evidence.SaveReport(legacy) : workspace.Evidence.SaveApplicationReport(report.Schema2!); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ToolkitException)
            { reason = SensitiveDataScrubber.Scrub(ex.Message); }
            result = new(report, file, reason);
        });
        RequireContext(expectedContext);
        if (selectionStillCurrent?.Invoke() == false)
            throw new ToolkitException("The report selection changed after capture. Any saved record retains its original context; no result was accepted for this selection.");
        return result ?? throw new ToolkitException("No report result was accepted.");
    }

    public async Task<string> ExportAsync(Envelope report, ExportFormat format, bool suppliedFile, string expectedContext)
    {
        RequireIdle(expectedContext);
        GraphReportPresentation.Validate(report, workspace.Profile!.TenantId);
        return await workspace.ExportAsync(() =>
        {
            RequireContext(expectedContext);
            GraphReportPresentation.Validate(report, workspace.Profile!.TenantId);
            return report.Export(workspace.Exporter, format, suppliedFile);
        }, "Exporting the complete report", "Visual filters do not limit this export; preserving all original rows and read states.");
    }
}
