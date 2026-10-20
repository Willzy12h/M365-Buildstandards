using System.Security.Cryptography;
using System.Text.Json;
namespace BuildStandard.Core.Reporting;
public sealed record ExchangeTraceWindow(DateTimeOffset Start,DateTimeOffset End);
/// <summary>Frozen read-only INT-128 message-trace selection. Preparing it performs no service call.</summary>
public sealed class ExchangeTraceQuery
{
    public DateTimeOffset FrozenNow {get;private init;}
    public IReadOnlyList<ExchangeTraceWindow> Windows {get;private init;}=[];
    public string? Sender {get;private init;}
    public string? Recipient {get;private init;}
    public string? InternetMessageId {get;private init;}
    public string? MessageTraceId {get;private init;}
    public bool IncludeSubject {get;private init;}
    public int MaximumRows {get;private init;}
    public string FilterDigest {get;private init;}="";
    public bool IsCurrentDayIncomplete {get;private init;}
    /// <summary>Query kind only; successful/complete collection still depends on every read and limit.</summary>
    public bool IsExactTraceQuery=>MessageTraceId is not null;
    private ExchangeTraceQuery() { }
    public static ExchangeTraceQuery Prepare(DateOnly start, DateOnly end, DateTimeOffset now,
        string? sender = null, string? recipient = null, string? internetMessageId = null,
        string? messageTraceId = null, bool includeSubject = false, int maximumRows = 5000)
    {
        now = now.ToUniversalTime();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (now < DateTimeOffset.MinValue.AddDays(90) || end < start || end > today)
            throw Bad("Choose an ordered UTC date interval that does not extend into the future.");
        var first = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        if (first < now.AddDays(-90)) throw Bad("The selected start is outside the documented 90-day trace retention window.");
        var last = end == today ? now : new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        if (last <= first) throw Bad("No elapsed interval is available for that selection yet.");
        if (maximumRows is < 1 or > 5000) throw Bad("Choose a global trace row limit from 1 to 5,000.");
        sender = Address(sender); recipient = Address(recipient);
        internetMessageId = Optional(internetMessageId); messageTraceId = Optional(messageTraceId);
        if (messageTraceId is not null && !ReportValues.IsCanonicalGuid(messageTraceId))
            throw Bad("MessageTraceId must be the exact canonical service GUID, not an Internet MessageId.");
        if (sender is null && recipient is null && internetMessageId is null && messageTraceId is null)
            throw Bad("Select an explicit sender, recipient, Internet MessageId or exact MessageTraceId; an unfiltered scan is unavailable.");
        var windows = new List<ExchangeTraceWindow>();
        for (var at = first; at < last;)
        {
            var until = last - at > TimeSpan.FromDays(10) ? at.AddDays(10) : last;
            windows.Add(new(at, until)); at = until;
        }
        if (windows.Count > 9) throw Bad("The interval exceeds nine bounded trace windows.");
        return new()
        {
            FrozenNow = now, Windows = windows.AsReadOnly(), Sender = sender, Recipient = recipient,
            InternetMessageId = internetMessageId, MessageTraceId = messageTraceId, IncludeSubject = includeSubject,
            MaximumRows = maximumRows, IsCurrentDayIncomplete = end == today,
            FilterDigest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                new object?[] { sender, recipient, internetMessageId, messageTraceId, includeSubject, maximumRows })))
        };
    }
    private static string? Optional(string? value)
    {
        if (value is null or "") return null;
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > 1024 || value.Any(char.IsControl))
            throw Bad("A trace filter must be exact bounded text, without surrounding whitespace or control characters.");
        return value;
    }
    private static string? Address(string? value)
    {
        value = Optional(value);
        if (value is not null && (value.Length > 320 || value.Count(c => c == '@') != 1
            || value.StartsWith('@') || value.EndsWith('@') || value.Any(c => char.IsWhiteSpace(c) || "*?<>,;".Contains(c))))
            throw Bad("Use one exact SMTP address per sender/recipient filter; display names, lists and wildcards are unavailable.");
        return value;
    }
    private static ConfigurationException Bad(string message) => new(message);
}
