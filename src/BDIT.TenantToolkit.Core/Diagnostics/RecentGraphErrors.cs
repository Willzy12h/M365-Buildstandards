using System.Text.RegularExpressions;

namespace BDIT.TenantToolkit.Core.Diagnostics;

/// <summary>
/// One failed Graph request, reduced to fields that cannot carry tenant data: the declared route root (never the
/// requested path, which can hold object IDs), HTTP status, Graph's error code and Microsoft's correlation IDs.
/// </summary>
public sealed record GraphErrorRecord(DateTimeOffset OccurredAt, string Method, string Route, int Status, string? ErrorCode, string? RequestId, string? ClientRequestId);

/// <summary>
/// In-memory record of the most recent Graph failures, for the optional support-bundle section (CLA-20261006-10).
/// Nothing is written to disk. Every field is checked against a strict shape on the way in, so free text from a
/// response can never reach the bundle.
/// </summary>
public sealed class RecentGraphErrors
{
    private static readonly Regex Guid = new(@"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z", RegexOptions.CultureInvariant);
    private static readonly Regex GuidAnywhere = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant);
    private static readonly Regex RouteShape = new(@"\A/[A-Za-z0-9/._-]{1,200}\z", RegexOptions.CultureInvariant);
    private static readonly Regex CodeShape = new(@"\A[A-Za-z0-9_.]{1,80}\z", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Methods = new(StringComparer.Ordinal) { "GET", "POST", "PATCH", "PUT", "DELETE" };

    private readonly int _capacity;
    private readonly Queue<GraphErrorRecord> _records = new();
    private readonly object _gate = new();

    public RecentGraphErrors(int capacity = 20) => _capacity = Math.Max(1, capacity);

    /// <summary>The process-wide record the Graph client writes to.</summary>
    public static RecentGraphErrors Shared { get; } = new();

    public void Record(DateTimeOffset at, string method, string route, int status, string? errorCode, string? requestId, string? clientRequestId)
    {
        var record = new GraphErrorRecord(at.ToUniversalTime(),
            Methods.Contains(method) ? method : "OTHER",
            RouteShape.IsMatch(route) && !GuidAnywhere.IsMatch(route) ? route : "(route withheld)",
            status is >= 0 and <= 999 ? status : 0,
            errorCode is not null && CodeShape.IsMatch(errorCode) ? errorCode : null,
            OnlyGuid(requestId), OnlyGuid(clientRequestId));
        lock (_gate)
        {
            _records.Enqueue(record);
            while (_records.Count > _capacity) _records.Dequeue();
        }
    }

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<GraphErrorRecord> Snapshot() { lock (_gate) return _records.ToArray(); }

    public static string? OnlyGuid(string? value) => value is not null && Guid.IsMatch(value.Trim()) ? value.Trim().ToLowerInvariant() : null;
}
