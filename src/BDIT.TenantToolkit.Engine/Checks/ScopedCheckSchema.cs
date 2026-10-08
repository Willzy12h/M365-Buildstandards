using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;

namespace BDIT.TenantToolkit.Engine.Checks;

/// <summary>Strict reader for the separate partial-evidence contract. Digests detect modification, not authenticity.</summary>
public static class ScopedCheckSchema
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Strict = MakeOptions();

    private static JsonSerializerOptions MakeOptions()
    {
        var options = new JsonSerializerOptions(ToolkitJson.Options)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            MaxDepth = 64
        };
        options.Converters.Clear();
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    public static ScopedCheckEvidence Read(string json, StandardCatalogue catalogue, TenantProfile profile)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw Bad("The scoped evidence exceeds the 8 MiB reader limit.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            RejectDuplicateMembers(document.RootElement);
            var evidence = JsonSerializer.Deserialize<ScopedCheckEvidence>(json, Strict) ?? throw Bad("Scoped evidence is empty.");
            Validate(evidence, catalogue, profile);
            return evidence;
        }
        catch (JsonException ex) { throw Bad("Invalid scoped evidence JSON: " + ex.Message); }
    }

    public static void Seal(ScopedCheckEvidence evidence, StandardCatalogue catalogue, TenantProfile profile)
    {
        evidence.IntegrityDigest = EvidenceIntegrity.Compute(evidence);
        Validate(evidence, catalogue, profile);
    }

    public static void Validate(ScopedCheckEvidence e, StandardCatalogue catalogue, TenantProfile profile)
    {
        if (e.SchemaVersion != 1 || e.Kind != "scopedCheck") throw Bad("Unsupported scoped evidence schema or kind.");
        GuidValue(e.Id, "record ID"); GuidValue(e.TenantId, "tenant ID"); GuidValue(e.ProfileId, "profile ID");
        Digest(e.CatalogueDigest, "catalogue digest"); Digest(e.ClientScopeDigest, "client-scope digest"); Digest(e.IntegrityDigest, "record digest");
        if (e.RecordedAt is null || !Timestamps.TryParse(e.RecordedAt, out _) || !e.RecordedAt.EndsWith('Z')) throw Bad("The record needs a UTC time.");
        if (!string.Equals(e.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase) || e.ProfileId != profile.Id
            || e.ClientScopeDigest != ReviewedClientScope.Digest(profile)) throw Bad("Scoped evidence belongs to a different tenant or reviewed client scope.");
        if (e.CatalogueRelease != catalogue.Release || e.CatalogueDigest != catalogue.IntegrityDigest)
            throw Bad("Scoped evidence belongs to a different verified catalogue.");
        if (e.SourceMode == "liveScoped")
        {
            GuidValue(e.AccountObjectId, "verified live account ID");
            if (e.SourceCapture is not null) throw Bad("A historical source cannot be claimed as a live scoped read.");
        }
        else if (e.SourceMode == "historicalFiltered")
        {
            if (e.AccountObjectId is not null) GuidValue(e.AccountObjectId, "historical account ID");
            if (e.SourceCapture is null) throw Bad("Historical filtering needs the original source reference.");
            GuidValue(e.SourceCapture.Id, "source capture ID"); Digest(e.SourceCapture.Sha256, "source capture digest");
        }
        else throw Bad("Unknown scoped evidence source mode.");
        CanonicalList(e.ControlIds, "control IDs", 1000, true);
        CanonicalList(e.CollectionKeys, "collection keys", 128, false);
        CanonicalList(e.Areas, "areas", 16, false);
        var controls = ControlInstances.All(catalogue, profile).Where(c => e.ControlIds.Contains(c.Id)).ToList();
        if (controls.Count != e.ControlIds.Count) throw Bad("The selection contains an unknown control instance.");
        var dependencies = e.ControlIds.SelectMany(id => CheckSelection.ForControl(catalogue, profile, id).CollectionKeys)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (!e.CollectionKeys.SequenceEqual(dependencies)) throw Bad("The recorded dependencies do not match the selected controls.");
        var areas = controls.Select(ControlAreas.For)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (!e.Areas.SequenceEqual(areas)) throw Bad("The recorded areas do not match the selected controls.");
        if (e.Capture is null || e.Assessment is null) throw Bad("Scoped capture/assessment data is missing.");
        GuidValue(e.Capture.Id, "derived capture ID"); GuidValue(e.Assessment.Id, "assessment ID");
        if (e.Capture.Id == e.Id || e.Assessment.Id == e.Id || e.Assessment.Id == e.Capture.Id || e.SourceCapture?.Id == e.Capture.Id)
            throw Bad("Scoped records and derived captures need their own IDs.");
        if (e.Capture.Complete || e.Assessment.SnapshotComplete) throw Bad("Scoped evidence can never claim complete before-evidence.");
        if (!string.Equals(e.Capture.TenantId, e.TenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(e.Assessment.TenantId, e.TenantId, StringComparison.OrdinalIgnoreCase)
            || e.Assessment.SnapshotId != e.Capture.Id || e.Assessment.Release != e.CatalogueRelease
            || e.Assessment.StandardDigest != e.CatalogueDigest)
            throw Bad("Scoped capture and assessment identity bindings do not match.");
        if (!Timestamps.TryParse(e.Capture.CapturedAt, out _) || !Timestamps.TryParse(e.Assessment.AssessedAt, out _))
            throw Bad("Scoped capture and assessment times are missing or invalid.");
        if (e.Capture.Collections is null || e.Assessment.Findings is null
            || !e.Capture.Collections.Keys.Order(StringComparer.Ordinal).SequenceEqual(e.CollectionKeys)
            || e.Assessment.Findings.Any(f => f is null)
            || !e.Assessment.Findings.Select(f => f.ControlId).Order(StringComparer.Ordinal).SequenceEqual(e.ControlIds))
            throw Bad("Scoped evidence contains missing, duplicate or unrelated collections/findings.");
        foreach (var c in e.Capture.Collections.Values)
        {
            if (c is null || c.Status is not (CaptureStatus.Collected or CaptureStatus.Error or CaptureStatus.NotAttempted)
                || c.Items is null || c.Count != c.Items.Count || c.Count < 0)
                throw Bad("A scoped collection has invalid status or object counts.");
            if (c.Status != CaptureStatus.Collected && (c.Items.Count != 0 || string.IsNullOrWhiteSpace(c.Error)))
                throw Bad("Failed/unattempted reads need an explicit reason and cannot claim returned objects.");
            if (c.Status == CaptureStatus.Collected && !string.IsNullOrEmpty(c.Error) && !c.DetailIncomplete)
                throw Bad("Collection errors cannot be labelled complete reads.");
        }
        if (!EvidenceIntegrity.Verify(e.Capture, e.Capture.IntegrityDigest) || !EvidenceIntegrity.Verify(e, e.IntegrityDigest))
            throw Bad("Scoped evidence failed its integrity check.");
    }

    private static void RejectDuplicateMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Bad("Duplicate JSON member: " + property.Name);
                RejectDuplicateMembers(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateMembers(item);
    }

    private static void CanonicalList(List<string>? values, string what, int maximum, bool required)
    {
        if (values is null || values.Count > maximum || (required && values.Count == 0)
            || values.Any(string.IsNullOrWhiteSpace) || !values.SequenceEqual(values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)))
            throw Bad("Invalid or non-canonical " + what + ".");
    }
    private static void GuidValue(string? value, string what)
    {
        if (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty) throw Bad("Invalid " + what + ".");
    }
    private static void Digest(string? value, string what)
    {
        if (value is not { Length: 64 } || !value.All(Uri.IsHexDigit)) throw Bad("Invalid " + what + ".");
    }
    private static ConfigurationException Bad(string message) => new(message);
}
