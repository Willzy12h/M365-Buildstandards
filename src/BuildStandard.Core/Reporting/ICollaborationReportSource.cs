namespace BuildStandard.Core.Reporting;
/// <summary>Fixed collaboration reads bound to one immutable selection. No arbitrary URL.</summary>
public interface ICollaborationReportSource
{
    string TenantId { get; }
    CollaborationReadSelection Selection { get; }
    Task<OperationalReportPage> ReadCollaborationNextAsync(CollaborationReadKind kind, string value, CancellationToken cancellation);
}
