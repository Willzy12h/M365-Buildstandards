using System.Text.Json.Serialization;
namespace BuildStandard.Core.Models;
public sealed class SecurityIncidentReportSelection
{
    [JsonRequired] public string StartUtc { get; set; } = "";
    [JsonRequired] public string EndUtc { get; set; } = "";
}
/// <summary>Whitelisted incident metadata only. No alert content, identities, comments or response instructions.</summary>
public sealed class SecurityIncidentRow : GraphReportRow
{
    [JsonRequired] public string? CreatedAt { get; set; }
    [JsonRequired] public string? ModifiedAt { get; set; }
    [JsonRequired] public string? Severity { get; set; }
    [JsonRequired] public string? Status { get; set; }
    [JsonRequired] public string? Classification { get; set; }
    [JsonRequired] public string? Determination { get; set; }
    [JsonRequired] public int? PriorityScore { get; set; }
    [JsonRequired] public string? RedirectIncidentId { get; set; }
    [JsonRequired] public string? IncidentWebUrl { get; set; }
}
