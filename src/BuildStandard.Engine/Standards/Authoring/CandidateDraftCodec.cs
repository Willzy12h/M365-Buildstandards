using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Exchange;
namespace BuildStandard.Engine.Standards.Authoring;

/// <summary>Integrity is deterministic content identity, not trust, validation or publication.</summary>
public static class CandidateDraftCodec
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(ToolkitJson.Options)
    {
        PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never, MaxDepth = 48,
        RespectRequiredConstructorParameters = true
    };
    public static StandardCandidateDraft ReadFile(string file)
    {
        BuildStandard.Engine.Reports.ReportEnvelopeDispatcher.RefuseLinks(file);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw Bad("Draft exceeds 8 MiB.");
        using var reader = new BinaryReader(stream);
        var bytes = reader.ReadBytes(MaximumBytes + 1);
        if (bytes.Length > MaximumBytes) throw Bad("Draft exceeds 8 MiB.");
        try { return Read(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF')); }
        catch (DecoderFallbackException) { throw Bad("Draft must be valid UTF-8."); }
    }
    public static StandardCandidateDraft Read(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw Bad("Draft exceeds 8 MiB.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
            ExchangeCaptureSchema.RejectDuplicates(document.RootElement, StringComparer.OrdinalIgnoreCase);
            var draft = JsonSerializer.Deserialize<StandardCandidateDraft>(json, Options) ?? throw Bad("Draft is null.");
            Validate(draft);
            if (!Hash(draft.ContentDigest) || draft.ContentDigest != Digest(draft)) throw Bad("Draft content digest does not match.");
            return draft;
        }
        catch (JsonException ex) { throw new ConfigurationException("Invalid candidate draft JSON.", ex); }
    }
    public static string Serialize(StandardCandidateDraft draft)
    {
        Validate(draft);
        if (!Hash(draft.ContentDigest) || draft.ContentDigest != Digest(draft)) throw Bad("Reseal changed draft content before saving; saving does not validate it.");
        var json = JsonSerializer.Serialize(draft, Options);
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw Bad("Draft exceeds 8 MiB.");
        return json;
    }
    public static void Seal(StandardCandidateDraft draft)
    {
        Validate(draft);
        var digest = Digest(draft);
        // Validate the complete serialised size before changing caller state.
        var node = JsonSerializer.SerializeToNode(draft, Options)!.AsObject();
        node["contentDigest"] = digest;
        if (Encoding.UTF8.GetByteCount(node.ToJsonString(Options)) > MaximumBytes) throw Bad("Draft exceeds 8 MiB.");
        draft.ContentDigest = digest;
    }
    private static string Digest(StandardCandidateDraft draft)
    {
        var node = JsonSerializer.SerializeToNode(draft, Options)!.AsObject();
        node.Remove("contentDigest"); node.Remove("savedAtUtc");
        return CanonicalJson.Sha256(node);
    }
    private static void Validate(StandardCandidateDraft d)
    {
        if (d.Kind != "standard-candidate-draft" || d.SchemaVersion != 1 || d.Baseline is null
            || !Text(d.Baseline.Release, 40) || !Hash(d.Baseline.FileSha256) || d.Baseline.CatalogueSchemaVersion is not (3 or 4 or 5)
            || !Text(d.Title, 200) || d.Changes is null || d.Imports is null || d.Unresolved is null || d.AssignmentObservations is null)
            throw Bad("Unsupported or incomplete draft identity.");
        if (d.Changes.Count > 1000 || d.Imports.Count > 100 || d.Unresolved.Count > 200 || d.AssignmentObservations.Count > 100)
            throw Bad("Draft exceeds its change/import/unresolved/assignment bounds.");
        if (d.SavedAtUtc is { Offset: var offset } && offset != TimeSpan.Zero) throw Bad("Saved time must be UTC.");
        var controls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in d.Changes)
        {
            if (c is null || !Control(c.ControlId)) throw Bad("A change has no valid control identity.");
            controls.Add(c.ControlId);
            CandidatePointer.Parse(c.Path);
        }
        foreach (var group in d.Changes.GroupBy(c => c.ControlId, StringComparer.OrdinalIgnoreCase))
            CandidatePointer.RequireNonOverlapping(group.Select(c => c.Path));
        // Parameter defaults are global even when selected through different controls.
        CandidatePointer.RequireNonOverlapping(d.Changes.Where(c => c.Path.StartsWith("/parameters/", StringComparison.OrdinalIgnoreCase)).Select(c => c.Path));
        var imports = new HashSet<string>(StringComparer.Ordinal);
        foreach (var import in d.Imports)
        {
            if (import is null || !Hash(import.Sha256) || !Control(import.ControlId) || !Text(import.PolicyType, 100)
                || !imports.Add(import.Sha256 + ":" + import.ControlId)) throw Bad("Invalid or duplicate source import.");
            controls.Add(import.ControlId);
        }
        foreach (var unresolved in d.Unresolved)
        {
            if (unresolved is null || !Control(unresolved.ControlId) || !Hash(unresolved.SourceSha256)
                || !imports.Contains(unresolved.SourceSha256 + ":" + unresolved.ControlId) || !Text(unresolved.Reason, 2048))
                throw Bad("An unresolved input must identify its recorded source/control and reason.");
            CandidatePointer.Parse(unresolved.Path);
        }
        foreach (var assignment in d.AssignmentObservations)
            if (assignment is null || !Control(assignment.ControlId) || !Hash(assignment.SourceSha256)
                || !imports.Contains(assignment.SourceSha256 + ":" + assignment.ControlId)
                || assignment.Status is not ("Unknown" or "ReturnedEmpty" or "ReturnedNotApplied") || !Text(assignment.Detail, 2048))
                throw Bad("Assignments require explicit source claims; unavailable is Unknown, never empty.");
        if (controls.Count > 100) throw Bad("Draft exceeds 100 controls.");
    }
    private static bool Text(string? text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= max && !text.Any(char.IsControl);
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Control(string? value) => Text(value, 100) && value!.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    private static ConfigurationException Bad(string message) => new("Candidate draft: " + message);
}
