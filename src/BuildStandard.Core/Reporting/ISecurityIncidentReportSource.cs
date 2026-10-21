namespace BuildStandard.Core.Reporting;
/// <summary>One reviewed UTC incident window. No alert expansion, arbitrary URL or response action.</summary>
public interface ISecurityIncidentReportSource
{
    string TenantId { get; }
    SecurityIncidentReadSelection IncidentSelection { get; }
    Task<OperationalReportPage> ReadIncidentsNextAsync(CancellationToken cancellation);
}
