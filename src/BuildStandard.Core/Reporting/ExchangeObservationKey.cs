using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace BuildStandard.Core.Reporting;

/// <summary>
/// INT-128 deterministic local observation keys. These identify a typed observation tuple, never a Microsoft object,
/// an effective permission, ownership or an execution target. Source identities must still be retained separately.
/// </summary>
public static class ExchangeObservationKey
{
    public static string TargetRead(string reportId, string exchangeGuid)
    {
        if (ExchangeReportRegistry.Find(reportId).ReportSchemaVersion != 2)
            throw new ConfigurationException("Target observation keys require an exact registered report2 family.");
        return Hash(reportId, GuidIdentity(exchangeGuid));
    }

    public static string TransportRead(string scope,string? requestedRuleGuid)
    {
        if(scope=="OrganisationInventory"&&requestedRuleGuid is null)return Hash("exo-transport-rules",scope,null);
        if(scope=="SelectedRule"&&requestedRuleGuid is not null)return Hash("exo-transport-rules",scope,GuidIdentity(requestedRuleGuid));
        throw new ConfigurationException("Transport diagnostic scope and requested GUID do not match.");
    }

    public static string InboxRule(string mailboxGuid, string ruleIdentity) =>
        Hash(GuidIdentity(mailboxGuid), Identity(ruleIdentity));

    public static string MailboxPermission(string mailboxGuid, string kind, string principalNamespace,
        string principal, IReadOnlyList<string>? rights, bool? inherited, bool? deny)
    {
        if (kind is not ("FullAccess" or "SendAs" or "SendOnBehalf"))
            throw new ConfigurationException("Choose an exact registered mailbox permission kind.");
        if (rights is { Count: > 100 }) throw new ConfigurationException("A permission observation exceeds its 100-value limit.");
        return Hash(GuidIdentity(mailboxGuid), kind, Identity(principalNamespace), Identity(principal),
            rights?.Select(Identity).ToArray(), inherited, deny);
    }

    public static string CalendarPermission(string mailboxGuid, string folderIdentity,
        string principalNamespace, string principal) =>
        Hash(GuidIdentity(mailboxGuid), Identity(folderIdentity), Identity(principalNamespace), Identity(principal));

    public static string TraceInterval(string startUtc,string endUtc,string filterDigest)
    {
        if(filterDigest is not {Length:64}||filterDigest.Any(c=>c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))throw new ConfigurationException("Trace filter digest must be exact lowercase SHA256.");
        return Hash("exo-message-trace",Identity(startUtc),Identity(endUtc),filterDigest);
    }

    public static string MessageTrace(string traceId, string recipient, string receivedUtc)
    {
        // Keep the original UTC representation; do not invent a timestamp or conflate recipients/Internet MessageId.
        if (receivedUtc is null || !receivedUtc.EndsWith('Z') || !DateTimeOffset.TryParse(receivedUtc,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var received) || received.Offset != TimeSpan.Zero)
            throw new ConfigurationException("A trace observation needs its returned UTC time.");
        return Hash(GuidIdentity(traceId), Identity(recipient), Identity(receivedUtc));
    }

    public static string GroupMember(string groupGuid, string memberNamespace, string memberIdentity) =>
        Hash(GuidIdentity(groupGuid), Identity(memberNamespace), Identity(memberIdentity));

    private static string GuidIdentity(string value) => ReportValues.IsCanonicalGuid(value)
        ? value : throw new ConfigurationException("An exact canonical source GUID is required; no identity is fabricated.");

    private static string Identity(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 1024
        && !value.Any(char.IsControl) ? value
        : throw new ConfigurationException("Source identity is missing, contains control characters or exceeds 1,024 characters.");

    // Exact typed JSON arrays preserve field/list boundaries, invariant booleans and explicit nulls. No delimiter join,
    // case folding, display-name lookup, numeric conversion or random identity generation is involved.
    private static string Hash(params object?[] tuple) => "sha256:" + Convert.ToHexStringLower(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(tuple)));
}
