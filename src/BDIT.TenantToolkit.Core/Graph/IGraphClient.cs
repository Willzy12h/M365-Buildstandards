using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Core.Graph;

public enum GraphWriteMethod { Post, Patch }

/// <summary>
/// Minimal Microsoft Graph contract used by the engine. Paths are relative to the API version root, for example
/// "/identity/conditionalAccess/policies?$select=id". Implementations enforce session mode, route allow-lists and
/// payload safety; callers must still treat every write as a deliberate, reviewed action.
/// </summary>
public interface IGraphClient
{
    string TenantId { get; }
    SessionMode Mode { get; }

    Task<JsonObject> GetAsync(GraphApi api, string path, CancellationToken ct);

    /// <summary>Follows @odata.nextLink pages. Throws if a page cannot be read; never returns a partial list silently.</summary>
    Task<IReadOnlyList<JsonObject>> GetAllAsync(GraphApi api, string path, CancellationToken ct);

    /// <summary>
    /// Bounded read: follows @odata.nextLink pages but stops requesting pages once <paramref name="maxItems"/> items have
    /// been yielded. Items are yielded as pages arrive, so a caller keeps what it has received when a later page fails or
    /// is cancelled; an early stop or exception is always an incomplete read. Implementations that cannot page
    /// incrementally fall back to the complete <see cref="GetAllAsync"/> read and its limits.
    /// </summary>
    async IAsyncEnumerable<JsonObject> GetBoundedAsync(GraphApi api, string path, int maxItems, [EnumeratorCancellation] CancellationToken ct)
    {
        if (maxItems <= 0) throw new ArgumentOutOfRangeException(nameof(maxItems), "A bounded read needs a positive item limit.");
        foreach (var item in (await GetAllAsync(api, path, ct)).Take(maxItems)) yield return item;
    }

    /// <summary>Creates or updates an object. Never retried. Only WriteNotSentException confirms no request was sent; arbitrary exceptions remain uncertain.</summary>
    Task<JsonObject> WriteAsync(GraphApi api, GraphWriteMethod method, string path, JsonObject payload, CancellationToken ct);

    /// <summary>Dedicated full PUT preserving a reviewed device registration policy. Never retries or disables LAPS.</summary>
    Task EnableEntraLapsAsync(JsonObject reviewedBefore, CancellationToken ct) =>
        throw new WriteDeniedException("This Graph implementation does not support Entra LAPS enablement.");

    Task ApplyReviewedChangeAsync(ReviewedChangePlan plan, CancellationToken ct) =>
        throw new WriteDeniedException("This Graph implementation does not support reviewed tenant changes.");

    Task<JsonObject> WriteWin32ContentAsync(string appId, string? versionId, string? fileId, Win32ContentAction action, JsonObject payload, CancellationToken ct) =>
        throw new WriteDeniedException("This Graph implementation does not support Win32 package publishing.");
    Task UploadEncryptedPackageAsync(Uri storageUri, Stream content, long length, CancellationToken ct) =>
        throw new WriteDeniedException("This Graph implementation does not support encrypted package upload.");

    /// <summary>Separate recovery boundary; implementations must restrict supported routes/actions and never retry writes.</summary>
    Task RecoverAsync(GraphApi api, RecoveryAction action, string path, JsonObject? payload, CancellationToken ct) =>
        throw new WriteDeniedException("This Graph implementation does not support recovery writes.");
}
