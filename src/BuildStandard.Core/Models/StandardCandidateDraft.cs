using System.Text.Json.Nodes;
namespace BuildStandard.Core.Models;

/// <summary>Offline authoring inputs, not a deployed catalogue, assessment or execution plan.</summary>
public sealed class StandardCandidateDraft
{
    public required string Kind { get; set; }
    public required int SchemaVersion { get; set; }
    public required CandidateBaseline Baseline { get; set; }
    public required string Title { get; set; }
    public required List<CandidateChange> Changes { get; set; }
    public required List<CandidateImportReference> Imports { get; set; }
    public required List<CandidateUnresolvedInput> Unresolved { get; set; }
    public required List<CandidateAssignmentObservation> AssignmentObservations { get; set; }
    public required string ContentDigest { get; set; }
    public DateTimeOffset? SavedAtUtc { get; set; }
}
public sealed record CandidateBaseline(string Release, string FileSha256, int CatalogueSchemaVersion);
/// <summary>Path is relative to the selected control; /parameters/key/default is catalogue-wide.</summary>
public sealed record CandidateChange(string ControlId, string Path, JsonNode? ExpectedOriginal, JsonNode? Proposed);
public sealed record CandidateImportReference(string Sha256, string ControlId, string PolicyType);
public sealed record CandidateUnresolvedInput(string SourceSha256, string ControlId, string Path, string Reason);
/// <summary>Source claim only; assignments are never applied by authoring.</summary>
public sealed record CandidateAssignmentObservation(string SourceSha256, string ControlId, string Status, string Detail);
