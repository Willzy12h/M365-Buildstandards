using System.Text.Json.Nodes;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
namespace BuildStandard.Core.Graph;
public sealed record LifecycleReadPage(IReadOnlyList<JsonObject> Rows,bool HasMore);
/// <summary>Fixed existing-user before-read source. No arbitrary URI, sign-in, mutation or approval API.</summary>
public interface ILifecycleReadSource : IDisposable
{
    string TenantId { get; }
    string AccountObjectId { get; }
    string ClientId { get; }
    SessionMode Mode { get; }
    LifecycleOperation Operation { get; }
    int RequestCount { get; }
    Task<LifecycleReadPage> ReadNextAsync(string sourceId,CancellationToken ct);
}
