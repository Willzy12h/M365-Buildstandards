using System.Text.Json.Serialization;

namespace BuildStandard.Core.Models;

// Separate from historical ExchangeCapture DNS semantics. No deployment authority.
public enum DomainDnsType { TXT, MX, CNAME }
public enum DomainDnsState { Observed, NameError, NoData, Failed, Limited, Cancelled }
public sealed record DomainDnsAnswer(string Owner, DomainDnsType Type, string Section, long? Ttl,
    IReadOnlyList<string>? TextChunks, string? Exchange, int? Preference, string? Target)
{
    [JsonIgnore] public string? Text => TextChunks is null ? null : string.Concat(TextChunks);
}
public sealed record DomainDnsObservation(string Name, DomainDnsType Type, DomainDnsState State,
    string StartedAt, string CompletedAt, string Resolver, string AdapterVersion,
    IReadOnlyList<DomainDnsAnswer> Answers, int? NativeErrorCode, string? NegativeOwner, string? Reason);

public enum DomainReadinessCheck { Mx, Spf, WindowsEnrolment }
public enum DomainReadinessState { Complete, Partial, Failed, Cancelled }
public enum DomainReadinessMeaning { Observed, ObservedMatch, ReviewFinding, NotConfigured, UnableToCheck }
public sealed record DomainReadinessResult(DomainReadinessCheck Check,DomainReadinessMeaning Meaning,string Summary,
    IReadOnlyList<string> Details,string DnsOwnerNextStep);
public sealed record DomainSpfReference(string SourceDomain,string OriginalTerm,string TargetDomain,int Depth,string ObservedAt,string State);

/// <summary>INT-126 DNS observations only. Never TenantSnapshot, permission, plan approval or execution evidence.</summary>
public sealed class DomainReadinessReport
{
    [JsonRequired] public string Kind { get; set; }="domainReadinessReport";
    [JsonRequired] public int SchemaVersion { get; set; }=1;
    [JsonRequired] public string Id { get; set; }="";
    [JsonRequired] public string RunId { get; set; }="";
    [JsonRequired] public string Domain { get; set; }="";
    public string? TenantId { get; set; }
    public string? AccountObjectId { get; set; }
    [JsonRequired] public string SourceMode { get; set; }="localDns";
    [JsonRequired] public string Cloud { get; set; }="Public";
    [JsonRequired] public string StartedAt { get; set; }="";
    [JsonRequired] public string CompletedAt { get; set; }="";
    [JsonRequired] public string ToolkitVersion { get; set; }="";
    [JsonRequired] public string AdapterVersion { get; set; }="1";
    [JsonRequired] public List<DomainReadinessCheck> RequestedChecks { get; set; }=[];
    [JsonRequired] public List<string> ExpectedMx { get; set; }=[];
    public string? ExpectedMxSource { get; set; }
    [JsonRequired] public DomainReadinessState Status { get; set; }=DomainReadinessState.Partial;
    [JsonRequired] public List<DomainDnsObservation> Observations { get; set; }=[];
    [JsonRequired] public List<DomainSpfReference> References { get; set; }=[];
    [JsonRequired] public List<DomainReadinessResult> Results { get; set; }=[];
    [JsonRequired] public List<string> Limitations { get; set; }=[];
    [JsonRequired] public string IntegrityDigest { get; set; }="";
}
