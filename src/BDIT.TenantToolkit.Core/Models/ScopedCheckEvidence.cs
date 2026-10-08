using System.Text.Json.Serialization;

namespace BDIT.TenantToolkit.Core.Models;

/// <summary>INT-070: partial evidence, deliberately distinct from a normal configuration snapshot.</summary>
public sealed class ScopedCheckEvidence
{
    [JsonRequired] public int SchemaVersion { get; set; } = 1;
    [JsonRequired] public string Kind { get; set; } = "scopedCheck";
    [JsonRequired] public string Id { get; set; } = "";
    [JsonRequired] public string TenantId { get; set; } = "";
    [JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? AccountObjectId { get; set; }
    [JsonRequired] public string SourceMode { get; set; } = "";
    [JsonRequired] public string RecordedAt { get; set; } = "";
    [JsonRequired] public string CatalogueRelease { get; set; } = "";
    [JsonRequired] public string CatalogueDigest { get; set; } = "";
    [JsonRequired] public string ProfileId { get; set; } = "";
    [JsonRequired] public string ClientScopeDigest { get; set; } = "";
    [JsonRequired] public List<string> Areas { get; set; } = new();
    [JsonRequired] public List<string> ControlIds { get; set; } = new();
    [JsonRequired] public List<string> CollectionKeys { get; set; } = new();
    public ScopedSourceCapture? SourceCapture { get; set; }
    /// <summary>
    /// Historical review only: the separately captured or imported Exchange/Purview evidence that the derived capture
    /// carries in place of the source's own, as the full assessment uses it. Absent when none was used.
    /// </summary>
    public ScopedSourceCapture? SeparateExchange { get; set; }
    [JsonRequired] public TenantSnapshot Capture { get; set; } = new();
    [JsonRequired] public AssessmentResult Assessment { get; set; } = new();
    [JsonRequired] public string IntegrityDigest { get; set; } = "";
}

public sealed class ScopedSourceCapture
{
    [JsonRequired] public string Id { get; set; } = "";
    [JsonRequired] public string Sha256 { get; set; } = "";
}
