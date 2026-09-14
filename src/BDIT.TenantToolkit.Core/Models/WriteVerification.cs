using System.Text.Json.Nodes;

namespace BDIT.TenantToolkit.Core.Models;

/// <summary>A new read-only observation. Original write evidence is never rewritten to imply earlier verification.</summary>
public sealed class WriteVerification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string SourceRunId { get; set; } = "";
    public string SourceDigest { get; set; } = "";
    public string ControlId { get; set; } = "";
    public string ObjectId { get; set; } = "";
    public string At { get; set; } = "";
    public RunActor Actor { get; set; } = new();
    public bool HistoricalAcknowledged { get; set; }
    public bool Verified { get; set; }
    public bool ObjectAbsent { get; set; }
    public JsonObject? ObservedObject { get; set; }
    public string MappingsBeforeDigest { get; set; } = "";
    public string MappingsAfterDigest { get; set; } = "";
    public string Detail { get; set; } = "";
    public string IntegrityDigest { get; set; } = "";
}
