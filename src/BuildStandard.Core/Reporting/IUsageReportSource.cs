namespace BuildStandard.Core.Reporting;
/// <summary>Fixed usage CSV reads only. A returned byte array is not admitted evidence or verified identity/privacy.</summary>
public interface IUsageReportSource
{
    string TenantId { get; }
    Task<byte[]> DownloadAsync(string reportId,string period,CancellationToken cancellation);
}
