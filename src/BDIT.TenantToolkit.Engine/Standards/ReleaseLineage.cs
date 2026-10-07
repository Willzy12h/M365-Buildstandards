using System.Text.Json;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;

namespace BDIT.TenantToolkit.Engine.Standards;

/// <summary>
/// INT-051 release lineage: how each control of an earlier published release relates to the target release. It
/// supplements the published catalogues, whose bytes and digests never change. Read-only: lineage explains; it never
/// moves an ownership record, exception or manual result to another control.
/// </summary>
public sealed class ReleaseLineage
{
    public const string Folder = "lineage";
    public const string ManifestFile = "manifest.json";
    public static readonly string[] Relations = { "Unchanged", "Renamed", "Changed", "Replaced", "Retired", "Added" };
    public static readonly string[] Cardinalities = { "OneToOne", "OneToMany", "ManyToOne", "Added", "Retired" };

    public int SchemaVersion { get; set; }
    public string Description { get; set; } = "";
    public LineageEndpoint Target { get; set; } = new();
    public List<LineageSource> Sources { get; set; } = new();

    public LineageSource? Source(string release) =>
        Sources.FirstOrDefault(s => string.Equals(s.Release, release, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Loads the lineage whose target is <paramref name="targetRelease"/>, or null when none is shipped. A lineage file
    /// that is unlisted, changed, or pinned to catalogue bytes other than the verified ones is refused.
    /// </summary>
    public static ReleaseLineage? Load(string standardsDirectory, StandardsManifest standards, string targetRelease)
    {
        var folder = Path.Combine(standardsDirectory, Folder);
        var file = Path.Combine(folder, $"lineage-{targetRelease}.json");
        if (!File.Exists(file)) return null;
        var manifestPath = Path.Combine(folder, ManifestFile);
        if (!File.Exists(manifestPath)) throw new IntegrityException("standards/lineage/manifest.json is missing; release lineage cannot be verified.");
        JsonElement manifest;
        try { manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)).RootElement; }
        catch (JsonException ex) { throw new IntegrityException("standards/lineage/manifest.json could not be read: " + ex.Message); }
        if (!manifest.TryGetProperty("algorithm", out var algorithm) || algorithm.GetString() != "SHA-256"
            || !manifest.TryGetProperty("files", out var files) || !files.TryGetProperty(Path.GetFileName(file), out var expected))
            throw new IntegrityException($"Release lineage {Path.GetFileName(file)} is not listed in standards/lineage/manifest.json.");
        var bytes = File.ReadAllBytes(file);
        if (!string.Equals(CanonicalJson.Sha256Hex(bytes), expected.GetString(), StringComparison.OrdinalIgnoreCase))
            throw new IntegrityException($"Release lineage {Path.GetFileName(file)} does not match its recorded SHA-256.");
        var lineage = Parse(System.Text.Encoding.UTF8.GetString(bytes));
        lineage.CheckEndpoints(standards, targetRelease);
        return lineage;
    }

    // A new lineage reader refuses members it does not know; adding one needs a deliberate schema version.
    private static readonly JsonSerializerOptions Strict = new(ToolkitJson.Options) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static ReleaseLineage Parse(string json)
    {
        ReleaseLineage lineage;
        try { lineage = JsonSerializer.Deserialize<ReleaseLineage>(json, Strict) ?? throw new IntegrityException("Release lineage is empty."); }
        catch (JsonException ex) { throw new IntegrityException("Release lineage could not be read: " + ex.Message); }
        lineage.Validate();
        return lineage;
    }

    private void Validate()
    {
        if (SchemaVersion != 1) throw new IntegrityException($"Release lineage schema version {SchemaVersion} is not supported.");
        if (Target.Release.Length == 0) throw new IntegrityException("Release lineage has no target release.");
        if (Sources.Select(s => s.Release).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Sources.Count)
            throw new IntegrityException("Release lineage lists a source release twice.");
        foreach (var source in Sources)
        {
            if (source.UnlistedControls != "sameRequirement")
                throw new IntegrityException($"Release lineage from {source.Release} must declare how unlisted controls relate.");
            if (source.Relations.Select(r => r.SourceControl).Distinct(StringComparer.OrdinalIgnoreCase).Count() != source.Relations.Count)
                throw new IntegrityException($"Release lineage from {source.Release} lists a control twice.");
            foreach (var r in source.Relations)
            {
                if (!Relations.Contains(r.Relation) || !Cardinalities.Contains(r.Cardinality))
                    throw new IntegrityException($"Release lineage {source.Release} {r.SourceControl}: unknown relation or cardinality.");
                var consistent = r.Cardinality switch
                {
                    "Retired" => r.Relation == "Retired" && r.TargetControls.Count == 0,
                    "OneToOne" or "ManyToOne" => r.TargetControls.Count == 1,
                    "OneToMany" => r.TargetControls.Count >= 1,
                    _ => false
                };
                if (!consistent || r.TargetControls.Count != r.TargetNames.Count || r.Reason.Length == 0 || r.SemanticId.Length == 0)
                    throw new IntegrityException($"Release lineage {source.Release} {r.SourceControl}: relation, cardinality and targets disagree.");
            }
            // Two requirements may only arrive at one target when the lineage says so; otherwise one is unexplained.
            var claimed = source.Relations.Where(r => r.Cardinality == "OneToOne").SelectMany(r => r.TargetControls).ToList();
            if (claimed.Distinct(StringComparer.OrdinalIgnoreCase).Count() != claimed.Count)
                throw new IntegrityException($"Release lineage from {source.Release} maps two controls one-to-one onto the same target.");
        }
    }

    private void CheckEndpoints(StandardsManifest standards, string targetRelease)
    {
        bool Pinned(LineageEndpoint e) => standards.Files.TryGetValue(e.Release + ".json", out var digest)
            && string.Equals(digest, e.Sha256, StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(Target.Release, targetRelease, StringComparison.OrdinalIgnoreCase) || !Pinned(Target))
            throw new IntegrityException($"Release lineage target is not the verified {targetRelease} catalogue.");
        foreach (var source in Sources.Where(s => !Pinned(s)))
            throw new IntegrityException($"Release lineage source {source.Release} is not pinned to the verified catalogue bytes.");
    }
}

public class LineageEndpoint
{
    public string Release { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public sealed class LineageSource : LineageEndpoint
{
    public string UnlistedControls { get; set; } = "";
    public List<LineageRelation> Relations { get; set; } = new();

    public LineageRelation? Find(string controlId) =>
        Relations.FirstOrDefault(r => string.Equals(r.SourceControl, controlId, StringComparison.OrdinalIgnoreCase));
}

public sealed class LineageRelation
{
    public string SourceControl { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string Relation { get; set; } = "";
    public string Cardinality { get; set; } = "";
    public List<string> TargetControls { get; set; } = new();
    public List<string> TargetNames { get; set; } = new();
    public string SemanticId { get; set; } = "";
    public string Reason { get; set; } = "";
}
