using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Engine.Naming;
using BuildStandard.Engine.Planning;
namespace BuildStandard.Engine.Standards.Authoring;

/// <summary>Pure local candidate validation. Returned catalogues confer no publication or execution authority.</summary>
public sealed class CandidateAuthoringService(CandidateBaselineLoader baselines)
{
    private static readonly Regex GuidText = new(@"(?i)(?<![0-9a-f])[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?![0-9a-f])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    public StandardCatalogue Validate(StandardCandidateDraft draft)
    {
        var input = CandidateDraftCodec.Read(CandidateDraftCodec.Serialize(draft));
        var baseline = baselines.Load(input);
        if (input.Unresolved.Count != 0) throw Bad("Resolve every imported reference before candidate validation.");
        foreach (var source in input.Imports)
        {
            var control = baseline.Controls.SingleOrDefault(c => c.Id == source.ControlId);
            var definition = baseline.FindCollection(control?.Collection);
            var expectedType = control?.Payload?["@odata.type"]?.ToString();
            if (control is null || definition is null || !definition.Writable || !definition.Assignments
                || ConditionalAccessSafety.IsConditionalAccess(definition) || string.IsNullOrWhiteSpace(expectedType)
                || source.PolicyType != expectedType)
                throw Bad("An import must identify an exact existing Intune candidate control and its registered Graph type.");
        }
        var candidate = ToolkitJson.Deserialize<StandardCatalogue>(ToolkitJson.Serialize(baseline));
        var edited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in input.Changes)
        {
            var control = candidate.Controls.SingleOrDefault(c => c.Id == change.ControlId) ?? throw Bad("Select an exact existing control ID.");
            var parts = CandidatePointer.Parse(change.Path);
            if (parts[0] == "parameters")
            {
                if (parts.Count != 3 || parts[2] != "default") throw Bad("Only an existing parameter default may be changed.");
                var parameter = candidate.Parameters.SingleOrDefault(p => p.Key == parts[1]) ?? throw Bad("Unknown parameter.");
                if (!parameter.HasDefault) throw Bad("This parameter has no reviewable default; client identity inputs cannot gain one.");
                if (CanonicalJson.Serialize(parameter.Default) != CanonicalJson.Serialize(change.ExpectedOriginal)) throw Bad("The default's expected original value is stale.");
                var defaultText = CanonicalJson.Serialize(change.Proposed);
                if (defaultText.Contains("{{", StringComparison.Ordinal) || defaultText.Contains("}}", StringComparison.Ordinal))
                    throw Bad("A reviewable default must be a literal value, not a template expression.");
                RequireShape(parameter.Default, change.Proposed, candidate);
                parameter.Default = change.Proposed?.DeepClone();
                parameter.ReviewedOn = ""; // Editing a value is not evidence of a new vendor review.
                PolicyInputDefaults.AssertReviewable(parameter);
                PolicyInputValidator.ValidateUsed(new JsonObject { ["value"] = "{{" + parameter.Key + "}}" }, candidate,
                    new Dictionary<string, JsonNode?> { [parameter.Key] = parameter.Default });
                continue;
            }
            if (parts[0] != "payload" || parts.Count < 2 || !control.HasRecipe || control.Payload is null)
                throw Bad("Edit an existing supported recipe setting; metadata, routes, access and capability changes are unavailable.");
            var definition = candidate.FindCollection(control.Collection) ?? throw Bad("Control has no registered collection.");
            if (!definition.Writable) throw Bad("This recipe has no supported candidate write adapter.");
            var root = new JsonObject { ["payload"] = control.Payload.DeepClone() };
            var original = CandidatePointer.Read(root, change.Path);
            RequireShape(original, change.Proposed, candidate);
            CandidatePointer.Replace(root, change.Path, change.ExpectedOriginal, change.Proposed);
            var payload = root["payload"]!.AsObject();
            var initial = baseline.FindControl(control.Id)!.Payload!;
            if (ConditionalAccessSafety.IsConditionalAccess(definition)
                && CanonicalJson.Serialize(initial["conditions"]) != CanonicalJson.Serialize(payload["conditions"]))
                throw Bad("Preserve the exact reviewed Conditional Access targeting and exclusions; this editor cannot change them.");
            if (CanonicalJson.Serialize(initial["@odata.type"]) != CanonicalJson.Serialize(payload["@odata.type"]))
                throw Bad("The registered Graph object type cannot change.");
            if (CanonicalJson.Serialize(initial[definition.NameProperty]) != CanonicalJson.Serialize(payload[definition.NameProperty]))
                NamingConvention.RequireAuthored(control, payload[definition.NameProperty]?.GetValue<string>() ?? "");
            RequireMacOsBlocked(payload);
            AssertTemplate(candidate, definition, initial, payload);
            control.Payload = payload;
            edited.Add(control.Id);
        }
        foreach (var control in candidate.Controls.Where(c => c.Payload is not null))
        {
            var defaults = candidate.Parameters.Where(p => p.HasDefault).ToDictionary(p => p.Key, p => p.Default, StringComparer.Ordinal);
            PolicyInputValidator.ValidateUsed(control.Payload!, candidate, defaults);
            if (edited.Contains(control.Id)) AssertTemplate(candidate, candidate.FindCollection(control.Collection)!, baseline.FindControl(control.Id)!.Payload!, control.Payload!);
        }
        candidate.Status = "Candidate"; candidate.Release = "candidate"; candidate.Description = input.Title;
        candidate.PublishedOn = ""; candidate.IntegrityDigest = ""; candidate.SourceFileName = "";
        StandardsLoader.Validate(candidate);
        candidate.Release = "candidate-" + CanonicalJson.Sha256Value(candidate)[..24];
        return candidate;
    }

    private static void AssertTemplate(StandardCatalogue standard, CollectionDefinition definition, JsonObject original, JsonObject payload)
    {
        var preserved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (field, type) in new[] { ("settings", "jsonArray"), ("packageIdentifier", "string") })
        {
            if (CanonicalJson.Serialize(original[field]) != CanonicalJson.Serialize(payload[field])) continue;
            if (payload[field] is JsonValue value && value.TryGetValue<string>(out var text)
                && standard.Parameters.Any(p => p.Type == type && text == "{{" + p.Key + "}}")) preserved.Add(field);
        }
        // Only exact, declared, unchanged baseline inputs are deferred to execution-time resolution.
        WritePayloadGuard.AssertCatalogueTemplate(definition, payload, preserved);
    }

    // Import inspection may retain unresolved identifier values, but never unsupported structure. This
    // is not candidate validation: Validate still performs reference checks and refuses every unresolved draft.
    internal static void AssertImportStructure(JsonObject original, JsonObject proposed, StandardCatalogue standard)
    {
        RequireMacOsBlocked(proposed);
        RequireShape(original, proposed, standard, inspectForeignReferences: false);
    }

    private static void RequireMacOsBlocked(JsonObject payload)
    {
        if (payload["@odata.type"]?.ToString() == "#microsoft.graph.deviceEnrollmentPlatformRestrictionsConfiguration"
            && payload["macOSRestriction"]?["platformBlocked"]?.GetValueKind() != JsonValueKind.True)
            throw Bad("macOS enrolment must remain blocked; there is no authoring enable switch.");
    }

    private static void RequireShape(JsonNode? original, JsonNode? proposed, StandardCatalogue standard, bool inspectForeignReferences = true, string fieldKey = "")
    {
        if (proposed is JsonValue stringValue && stringValue.TryGetValue<string>(out var text))
        {
            var old = original is JsonValue oldValue && oldValue.TryGetValue<string>(out var oldText) ? oldText : "";
            if (text != old && (text.Contains("{{", StringComparison.Ordinal) || text.Contains("}}", StringComparison.Ordinal)))
            {
                var parameter = standard.Parameters.SingleOrDefault(p => text == "{{" + p.Key + "}}");
                if (parameter is null || !CompatibleParameter(parameter.Type, original)
                    || inspectForeignReferences && IsForeignReferenceField(fieldKey) && parameter.Type is not ("guid" or "guidList")) throw Bad("Use one existing parameter of the matching type, not a new expression.");
                return;
            }
            if (inspectForeignReferences && text != old && IsForeignReferenceField(fieldKey))
                throw Bad("A changed foreign object reference needs an explicit existing typed parameter mapping.");
            if (inspectForeignReferences)
                foreach (Match guid in GuidText.Matches(text))
                    if (!GuidText.Matches(old).Any(m => string.Equals(m.Value, guid.Value, StringComparison.OrdinalIgnoreCase)))
                        throw Bad("A changed foreign object identifier needs an explicit existing typed parameter mapping.");
        }
        if (original is null || proposed is null)
        {
            if (original is not null || proposed is not null) throw Bad("Unknown/null settings cannot acquire an unregistered value type.");
            return;
        }
        if (original is JsonObject before && proposed is JsonObject after)
        {
            if (!before.Select(p => p.Key).Order(StringComparer.Ordinal).SequenceEqual(after.Select(p => p.Key).Order(StringComparer.Ordinal)))
                throw Bad("The supported setting shape cannot gain or lose properties.");
            foreach (var (key, value) in before)
            {
                if (key is "@odata.type" or "omaUri" or "settingDefinitionId"
                    && CanonicalJson.Serialize(value) != CanonicalJson.Serialize(after[key])) throw Bad("Registered types and setting routes cannot change.");
                RequireShape(value, after[key], standard, inspectForeignReferences, key);
            }
            return;
        }
        if (original is JsonArray oldArray && proposed is JsonArray newArray)
        {
            if (oldArray.Count != newArray.Count) throw Bad("Array shape changes need a supported import adapter; edit the complete existing array.");
            for (var i = 0; i < oldArray.Count; i++) RequireShape(oldArray[i], newArray[i], standard, inspectForeignReferences, fieldKey);
            return;
        }
        if (original.GetValueKind() != proposed.GetValueKind()
            && !(original.GetValueKind() is JsonValueKind.True or JsonValueKind.False && proposed.GetValueKind() is JsonValueKind.True or JsonValueKind.False))
            throw Bad("The setting's registered value type cannot change.");
    }
    internal static bool IsForeignReferenceField(string key) => key is "tenantId" or "groupId" or "userId" or "directoryObjectId"
        or "notificationTemplateId" or "notificationMessageCCList";

    private static bool CompatibleParameter(string type, JsonNode? node) => type switch
    {
        "string" or "mailDomain" or "guid" => node is JsonValue value && value.TryGetValue<string>(out _),
        "integer" => node is JsonValue value && value.TryGetValue<int>(out _),
        "boolean" => node?.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        "guidList" => node is JsonArray { Count: > 0 } array && array.All(item => item is JsonValue value
            && value.TryGetValue<string>(out var id) && Guid.TryParse(id, out _)),
        // A generic JSON array declaration cannot preserve registered setting IDs or object shape.
        "jsonArray" => false,
        _ => false
    };
    private static ConfigurationException Bad(string message) => new("Candidate validation: " + message);
}
