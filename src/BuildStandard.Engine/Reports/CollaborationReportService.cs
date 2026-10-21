using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;
/// <summary>Registered operational evidence only. Never a configuration snapshot or write authority.</summary>
public sealed class CollaborationReportService(IClock clock, string toolkitVersion)
{
    public async Task<OperationalContextReport> CaptureAsync(ICollaborationReportSource source, TenantSession session, Action requireCurrent, CancellationToken cancellation)
    {
        var tenant = session.TenantId; var account = session.AccountObjectId; var client = session.ClientId; var mode = session.Mode;
        var scopes = string.Join('\n', session.Scopes.OrderBy(s => s, StringComparer.Ordinal));
        void Current()
        {
            requireCurrent();
            if (session.TenantId != tenant || source.TenantId != tenant || session.AccountObjectId != account || session.OperatorObjectId != account
                || !session.TenantVerified || !session.OperatorVerified || session.ClientId != client || session.Mode != mode
                || scopes != string.Join('\n', session.Scopes.OrderBy(s => s, StringComparer.Ordinal)))
                throw new TenantMismatchException("The collaboration context changed before original evidence admission.");
        }
        Current();
        if (string.IsNullOrWhiteSpace(toolkitVersion) || toolkitVersion.Length > 128 || toolkitVersion.Any(char.IsControl))
            throw new ConfigurationException("Bounded toolkit provenance is required before a collaboration read.");
        var started = Timestamps.Format(clock.UtcNow);
        var captured = await new CollaborationInventoryCapture().CaptureAsync(source, session, Current, cancellation).ConfigureAwait(false);
        Current();
        var selected = OperationalContextSchema.CollaborationSections(captured.Selection);
        var report = new OperationalContextReport
        {
            ReportId = "collaboration-inventory", AccessIntent = "collaboration-inventory", TenantId = tenant, AccountObjectId = account,
            ClientId = client, PermissionMode = mode.ToString(), SourceMode = "live", AsOfUtc = started, StartedAt = started,
            EndedAt = Timestamps.Format(clock.UtcNow), ToolkitVersion = toolkitVersion,
            Sources = OperationalContextSchema.SourceDeclarations("collaboration-inventory").ToList(),
            CollaborationSelection = new()
            {
                Search = captured.Selection.Search, Sites = captured.Selection.Sites.ToList(), Groups = captured.Selection.Groups.ToList(),
                Libraries = captured.Selection.Libraries, Owners = captured.Selection.Owners, Members = captured.Selection.Members,
                ReturnedCounts = captured.Sources.Select(s => s.ReturnedCount).ToList()
            }
        };
        await Task.Run(() =>
        {
            for (var i = 0; i < selected.Count; i++)
            {
                var observed = captured.Sources[i];
                if (selected[i].Kind != observed.Kind || selected[i].Target != observed.Target)
                    throw new ConfigurationException("Captured source differs from the frozen selection.");
                report.Sections.Add(new()
                {
                    Id = selected[i].Id, Status = observed.Status, Error = observed.Error,
                    Limitations = [OperationalContextSchema.CollaborationNotice],
                    Rows = observed.Rows.Select(row => JsonSerializer.SerializeToNode(row, row.GetType(), RegisteredReportJson.Strict)!.AsObject()).ToList()
                });
            }
            report.Status = ReportEvidenceSchema.Overall(report.Sections.Select(s => s.Status));
            OperationalContextSchema.Seal(report);
        }, CancellationToken.None).ConfigureAwait(false);
        Current();
        return report;
    }
}
