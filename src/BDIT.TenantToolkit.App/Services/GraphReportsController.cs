using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Graph;

namespace BDIT.TenantToolkit.App.Services;

/// <summary>Companion orchestration over the existing workspace. Report evidence never becomes a snapshot.</summary>
public sealed class GraphReportsController(Workspace workspace)
{
    public sealed record Outcome(ReportEvidence Evidence, string File, string? NotSavedReason);
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
        if (!s.TenantVerified || !ProfileValidator.IsGuid(s.AccountObjectId)
            || !string.Equals(connection.Graph.TenantId, s.TenantId, StringComparison.OrdinalIgnoreCase)
            || connection.Graph.Mode != s.Mode)
            throw new TenantMismatchException("Connect with a verified tenant and account before reading reports.");
        if (!Timestamps.TryParse(s.TokenExpiresAt, out var expiry) || expiry <= DateTimeOffset.UtcNow)
            throw new ToolkitException("The current session has expired or its expiry is unknown. Use Connect deliberately; Reports never signs in.");
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

    public async Task<Outcome> ReadAsync(string reportId, ReportParameters parameters, bool acknowledged, string expectedContext,
        Func<bool>? selectionStillCurrent = null)
    {
        RequireIdle(expectedContext);
        if (!acknowledged) throw new ToolkitException("Acknowledge the experimental read and its limitations for this selection first. This grants no consent.");
        var connection = RequireLive();
        var definition = GraphReportRegistry.Find(reportId);
        var frozen = new ReportParameters { Start = parameters.Start, End = parameters.End };
        ReportEvidenceSchema.ValidateParameters(definition, frozen);
        if (frozen.End is not null && Timestamps.TryParse(frozen.End, out var end) && end > DateTimeOffset.UtcNow)
            throw new ConfigurationException("A live log range cannot end in the future.");
        Outcome? result = null;
        void AssertCurrent()
        {
            RequireContext(expectedContext);
            if (!ReferenceEquals(workspace.Connection, connection) || !ReferenceEquals(GraphReportRegistry.Find(reportId), definition)
                || selectionStillCurrent?.Invoke() == false)
                throw new TenantMismatchException("The report or connection selection changed during the read. No result was accepted or saved.");
            RequireLive();
        }
        await workspace.RunExclusiveAsync("Reading " + definition.Name, async progress =>
        {
            AssertCurrent();
            progress.Report("Reusing the verified session; registered read-only routes, at most five minutes and 5,000 rows per section.");
            var report = await new GraphReportService(SystemClock.Instance).CaptureAsync(connection.ForReports(), connection.Session,
                definition.Id, frozen, workspace.OperationToken);
            AssertCurrent(); // Includes cancellation results: preserve their explicit states, never a snapshot.
            string file = ""; string? reason = null;
            try { file = workspace.Evidence.SaveReport(report); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ToolkitException)
            { reason = SensitiveDataScrubber.Scrub(ex.Message); }
            result = new(report, file, reason);
        });
        RequireContext(expectedContext);
        return result ?? throw new ToolkitException("No report result was accepted.");
    }

    public async Task<string> ExportAsync(ReportEvidence report, ExportFormat format, bool suppliedFile, string expectedContext)
    {
        RequireIdle(expectedContext);
        ReportEvidenceSchema.Validate(report, workspace.Profile!.TenantId);
        return await workspace.ExportAsync(() =>
        {
            RequireContext(expectedContext);
            ReportEvidenceSchema.Validate(report, workspace.Profile!.TenantId);
            return workspace.Exporter.ExportReport(report, format, suppliedFile);
        }, "Exporting the complete report", "Visual filters do not limit this export; preserving all original rows and read states.");
    }
}
