using System.Text.Json.Nodes;
namespace BuildStandard.Core.Reporting;
/// <summary>A fixed report source owns its cursors. Callers cannot supply URLs; pages are not yet evidence.</summary>
public interface IOperationalReportSource
{
    string TenantId { get; }
    string ReportId { get; }
    /// <summary>Frozen selected customer for partner reads; health has no customer parameter.</summary>
    string? CustomerTenantId => null;
    Task<OperationalReportPage> ReadNextAsync(OperationalReadKind kind, string? relationshipId, CancellationToken cancellation);
}
public sealed record OperationalReportPage(IReadOnlyList<JsonObject> Items, bool HasMore);
