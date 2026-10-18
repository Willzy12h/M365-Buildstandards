namespace BuildStandard.Core.Models;

// Separate from historical ExchangeCapture DNS semantics. No deployment authority.
public enum DomainDnsType { TXT, MX, CNAME }
public enum DomainDnsState { Observed, NameError, NoData, Failed, Limited, Cancelled }
public sealed record DomainDnsAnswer(string Owner, DomainDnsType Type, string Section, long? Ttl,
    IReadOnlyList<string>? TextChunks, string? Exchange, int? Preference, string? Target)
{
    public string? Text => TextChunks is null ? null : string.Concat(TextChunks);
}
public sealed record DomainDnsObservation(string Name, DomainDnsType Type, DomainDnsState State,
    string StartedAt, string CompletedAt, string Resolver, string AdapterVersion,
    IReadOnlyList<DomainDnsAnswer> Answers, int? NativeErrorCode, string? NegativeOwner, string? Reason);
