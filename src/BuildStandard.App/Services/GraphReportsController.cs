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
        Presentation = workspace.CapturePresentationContext().Generation,
        Standard = workspace.Standard?.IntegrityDigest, Evidence = workspace.Snapshot?.Id,
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

    internal Func<ConnectedTenant, Action, IOperationalReportSource> OperationalSourceFactory { get; set; } =
        (connection, current) => ((GraphClient)connection.Graph).ForOperationalReports(connection.Session, "service-health", current);
    internal Func<ConnectedTenant,string,Action,IOperationalReportSource> PartnerSourceFactory {get;set;} =
        (_,_,_)=>throw new ConfigurationException("Partner native source is not implemented yet.");
    public string OperationalReadBlocker
    {
        get
        {
            var blocker = ReadBlocker;
            if (blocker.Length != 0) return blocker;
            return RequireLive().Session.Scopes.Any(scope => scope is "ServiceHealth.Read.All" or "https://graph.microsoft.com/ServiceHealth.Read.All") ? ""
                : "No ServiceHealth.Read.All scope was returned for this connection. Review optional service-health access in Connect and reconnect deliberately. This page never signs in or grants consent.";
        }
    }
    public async Task<OperationalContextReport> ReadOperationalAsync(bool acknowledged, string expectedContext,
        Func<bool> selectionStillCurrent, Func<OperationalContextReport, Task> retainOriginal,
        string reportId="service-health",string? customerTenantId=null,bool partnerDeclared=false)
    {
        if(reportId!="service-health")throw new ConfigurationException("The selected native operational report is not implemented yet.");
        RequireIdle(expectedContext);
        if (!acknowledged) throw new ToolkitException("Acknowledge this exact experimental service-health read and its limitations first.");
        if (OperationalReadBlocker.Length != 0) throw new ToolkitException(OperationalReadBlocker);
        var connection = RequireLive();
        void AssertCurrent()
        {
            RequireContext(expectedContext);
            if (!ReferenceEquals(workspace.Connection, connection) || !selectionStillCurrent())
                throw new TenantMismatchException("The operational read context changed. No result was admitted for the current client.");
            RequireLive();
        }
        OperationalContextReport? result = null;
        await workspace.RunExclusiveAsync("Reading service-health context", async progress =>
        {
            AssertCurrent();
            progress.Report("Read-only registered overview and issue GETs; five minutes,100requests,5,000rows and16MiB. Advisories do not prove this tenant's availability.");
            var source = OperationalSourceFactory(connection, AssertCurrent);
            using var owned = source as IDisposable;
            result = await new ServiceHealthReportService(SystemClock.Instance, workspace.Version).CaptureAsync(source, connection.Session, AssertCurrent, workspace.OperationToken);
            // A returned admitted original belongs to the captured source. Preserve it before current-view admission,
            // including the dispatcher transition after capture; ownership continues through serialisation/projection.
            progress.Report("Retaining original operational evidence. Closing waits for the original; Stop does not discard already captured observations.");
            await retainOriginal(result);
            AssertCurrent();
        });
        AssertCurrent();
        return result ?? throw new ToolkitException("No operational result was admitted.");
    }
    public async Task<string> ExportOperationalAsync(OperationalContextReport report, ExportFormat format, bool suppliedFile,
        byte[] originalBytes, string expectedContext)
    {
        RequireIdle(expectedContext);
        var file = await workspace.ExportAsync(() =>
        {
            RequireContext(expectedContext);
            if (report.TenantId != workspace.Profile!.TenantId) throw new TenantMismatchException("The operational report belongs to another source tenant.");
            return workspace.Exporter.ExportOperationalContext(report, format, suppliedFile, originalBytes);
        }, "Exporting the complete operational report", "Preserving original source context and every read state; visual filters do not limit exports.");
        RequireContext(expectedContext); return file;
    }

    internal Func<ConnectedTenant, Action, IUsageReportSource> UsageSourceFactory { get; set; } =
        (connection, current) => ((GraphClient)connection.Graph).ForUsageReports(connection.Session, current);

    public string UsageReadBlocker
    {
        get
        {
            var blocker = ReadBlocker;
            if (blocker.Length != 0) return blocker;
            return RequireLive().Session.Scopes.Any(scope => string.Equals(scope, "Reports.Read.All", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scope, "https://graph.microsoft.com/Reports.Read.All", StringComparison.OrdinalIgnoreCase)) ? ""
                : "This connection has no returned Reports.Read.All access. Enable optional usage-report read access in Connect, review the registration/consent requirements, then reconnect deliberately. This page never signs in or grants access.";
        }
    }
    public async Task<UsageReviewReport> ReadUsageAsync(IReadOnlyList<string> sources, string period, bool acknowledged,
        string expectedContext, Func<bool> selectionStillCurrent, Func<UsageReviewReport,Task>? retainOriginal = null)
    {
        RequireIdle(expectedContext);
        if (!acknowledged) throw new ToolkitException("Acknowledge the exact experimental usage selection and access first; this grants no consent.");
        if (UsageReadBlocker.Length != 0) throw new ToolkitException(UsageReadBlocker);
        var ids = sources.ToArray();
        if (ids.Length is < 1 or > 4 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ConfigurationException("Choose one to four different registered usage sources.");
        foreach (var id in ids) _ = UsageReportRegistry.Find(id).Route(period);
        var connection = RequireLive();
        void AssertCurrent()
        {
            RequireContext(expectedContext);
            if (!ReferenceEquals(workspace.Connection, connection) || !selectionStillCurrent())
                throw new TenantMismatchException("The usage selection or connection changed. No result was admitted for this client.");
            RequireLive();
        }
        UsageReviewReport? result = null;
        await workspace.RunExclusiveAsync("Reading selected usage reports", async progress =>
        {
            AssertCurrent();
            progress.Report("Read-only: selected fixed reports, five minutes total,10MiB per original CSV; privacy remains Unknown and no licences change.");
            var source = UsageSourceFactory(connection, AssertCurrent);
            using var owned = source as IDisposable;
            result = await new UsageReportService(SystemClock.Instance, workspace.Version).CaptureAsync(
                source, connection.Session, ids, period, AssertCurrent, workspace.OperationToken);
            AssertCurrent();
            // The capture remains owned while its original bytes and native projection are prepared. Normal close
            // waits for this whole operation; a changed current view never discards an already captured original.
            if(retainOriginal is not null)
            {
                progress.Report("Retaining original usage evidence. Stop does not discard sources already captured; closing waits for recovery bytes.");
                await retainOriginal(result);
            }
            AssertCurrent();
        });
        AssertCurrent();
        return result ?? throw new ToolkitException("No usage result was admitted.");
    }
    public async Task<string> ExportUsageAsync(UsageReviewReport report, ExportFormat format, bool suppliedFile,
        byte[] originalBytes, string expectedContext)
    {
        RequireIdle(expectedContext);
        _ = UsageReviewSchema.Read(UsageReviewSchema.Serialise(report), workspace.Profile!.TenantId);
        var file = await workspace.ExportAsync(() =>
        {
            RequireContext(expectedContext);
            return workspace.Exporter.ExportUsageReview(report, format, suppliedFile, originalBytes);
        }, "Exporting complete usage evidence", "Filters do not limit exports. No usage identities are joined to licences.");
        RequireContext(expectedContext); return file;
    }

    public async Task<string> ExportAsync(Envelope report, ExportFormat format, bool suppliedFile, string expectedContext)
    {
        RequireIdle(expectedContext);
        GraphReportPresentation.Validate(report, workspace.Profile!.TenantId);
        var file = await workspace.ExportAsync(() =>
        {
            RequireContext(expectedContext);
            GraphReportPresentation.Validate(report, workspace.Profile!.TenantId);
            return report.Export(workspace.Exporter, format, suppliedFile);
        }, "Exporting the complete report", "Visual filters do not limit this export; preserving all original rows and read states.");
        RequireContext(expectedContext);
        return file;
    }
}
