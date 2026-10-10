using System.Text.Json.Nodes;

namespace BDIT.TenantToolkit.Core.Models;

/// <summary>Engineer-imported observations, never an authorisation to write. Provenance is claimed, not signed.</summary>
public sealed class ExchangeCapture
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ExchangeTenantId { get; set; } = "";
    public string? PurviewTenantId { get; set; }
    public bool Delegated { get; set; }
    public string Domain { get; set; } = "";
    public string CapturedAt { get; set; } = "";
    public string ModuleVersion { get; set; } = "";
    public string Source { get; set; } = "";
    public Dictionary<string, ExchangeCollectionCapture> Collections { get; set; } = new(StringComparer.Ordinal);
    public List<DnsObservation> Dns { get; set; } = new();
}

public sealed class ExchangeCollectionCapture
{
    public string Status { get; set; } = CaptureStatus.NotAttempted;
    public string Command { get; set; } = "";
    public string? Error { get; set; }
    public List<JsonObject> Items { get; set; } = new();
}

public enum DnsRecordKind { Cname, Txt }
public enum DnsResultStatus { Error, NoRecords, Present }

public sealed class DnsObservation
{
    public string Name { get; set; } = "";
    public DnsRecordKind Kind { get; set; }
    public DnsResultStatus Status { get; set; }
    public List<string> Records { get; set; } = new();
    public string QueriedAt { get; set; } = "";
    public string? Error { get; set; }
}

public interface IDnsLookup
{
    Task<DnsObservation> QueryAsync(string name, DnsRecordKind kind, CancellationToken cancellationToken);
}
