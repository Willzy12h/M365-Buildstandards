using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
namespace BuildStandard.Engine.Standards.Authoring;

/// <summary>Local source inspection. An unresolved draft is saveable, never valid for candidate export.</summary>
public sealed class CandidatePolicyImportService(CandidateBaselineLoader baselines, CandidateAuthoringService authoring)
{
    private static readonly Regex GuidText = new(@"(?i)(?<![0-9a-f])[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?![0-9a-f])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly string[] AssignmentFields = ["assignments", "groupAssignments", "targetAssignments", "_assignments", "_toolkitAssignments"];

    /// <summary>Strict local mapping input; values name existing typed parameters, never replacement literals.</summary>
    public static IReadOnlyDictionary<string, string> ReadParameterMappings(string file)
    {
        var input = JsonNode.Parse(CandidateImportSource.ReadFile(file).Json)!.AsObject();
        if (input.Count > 200) throw Bad("At most 200 explicit parameter mappings are supported.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, value) in input)
        {
            _ = CandidatePointer.Parse(path);
            if (value is not JsonValue scalar || !scalar.TryGetValue<string>(out var parameter) || string.IsNullOrWhiteSpace(parameter))
                throw Bad("Each mapping must name an existing typed parameter as a non-empty string.");
            result.Add(path, parameter);
        }
        return result;
    }

    public StandardCandidateDraft ImportFile(StandardCandidateDraft draft, string controlId, string file, string name,
        IReadOnlyDictionary<string, string>? parameterMappings = null)
    {
        var result = CandidateDraftCodec.Read(CandidateDraftCodec.Serialize(draft));
        var baseline = baselines.Load(result);
        var control = baseline.Controls.SingleOrDefault(c => c.Id == controlId) ?? throw Bad("Select an exact existing control.");
        var original = control.Payload ?? throw Bad("This control has no supported import recipe.");
        if (result.Changes.Any(c => c.ControlId == controlId) || result.Imports.Any(i => i.ControlId == controlId))
            throw Bad("Keep the existing control edits or return to an unedited draft before importing this control. Imports do not silently replace edits.");
        var source = CandidateImportSource.ReadFile(file);
        var input = JsonNode.Parse(source.Json)!.AsObject();
        var type = original["@odata.type"]?.ToString();
        if (string.IsNullOrEmpty(type) || input["@odata.type"]?.ToString() != type)
            throw Bad("The source must match this control's exact registered type; switching recipes is unavailable.");
        var prepared = DevicePolicyImporter.SupportsControl(controlId)
            ? DevicePolicyImporter.Prepare(baseline, controlId, source.Json, name)
            : PolicyImporter.Prepare(baseline, controlId, source.Json, name);
        // Unresolved references may be saved for review, but cannot hide secrets, unsupported shapes
        // or a candidate safety violation. These checks run before storing any imported changes.
        WritePayloadGuard.Assert(baseline.FindCollection(control.Collection)!, prepared.Payload);
        CandidateAuthoringService.AssertImportStructure(original, prepared.Payload, baseline);
        var mappings = new Dictionary<string, string>(parameterMappings ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        foreach (var path in mappings.Keys) _ = CandidatePointer.Parse(path);
        var used = new HashSet<string>(StringComparer.Ordinal);
        result.Imports.Add(new(source.Sha256, controlId, type));
        result.AssignmentObservations.Add(Assignments(input, source.Sha256, controlId));
        var payload = Resolve(prepared.Payload, original, "/payload", "")!.AsObject();
        if (mappings.Keys.Any(path => !used.Contains(path))) throw Bad("A parameter mapping does not identify a changed foreign reference in this source.");
        foreach (var (key, proposed) in payload)
        {
            if (CanonicalJson.Serialize(proposed) == CanonicalJson.Serialize(original[key])) continue;
            result.Changes.Add(new(controlId, "/payload/" + Escape(key), original[key]?.DeepClone(), proposed?.DeepClone()));
        }
        CandidateDraftCodec.Seal(result);
        if (result.Unresolved.Count == 0) _ = authoring.Validate(result);
        return result;

        JsonNode? Resolve(JsonNode? node, JsonNode? previous, string path, string key)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var text))
            {
                if (text.Contains("{{", StringComparison.Ordinal) || text.Contains("}}", StringComparison.Ordinal))
                    throw Bad("Source template expressions are not accepted. Select a declared parameter explicitly instead.");
                if (previous is JsonValue old && old.TryGetValue<string>(out var oldText) && text == oldText) return node.DeepClone();
                var foreign = GuidText.IsMatch(text) || CandidateAuthoringService.IsForeignReferenceField(key);
                if (!foreign) return node.DeepClone();
                if (mappings.TryGetValue(path, out var parameter))
                {
                    if (!Guid.TryParse(text, out _)) throw Bad("Only a whole GUID reference can map to a guid parameter; embedded identifiers remain unresolved.");
                    return Map(parameter, "guid", path);
                }
                result.Unresolved.Add(new(source.Sha256, controlId, path, "Foreign reference requires an explicit existing typed parameter. No source identifier has been adopted."));
                return node.DeepClone();
            }
            if (node is JsonArray array)
            {
                if (mappings.TryGetValue(path, out var parameter))
                {
                    if (!GuidArray(array) || previous is not JsonArray before || !GuidArray(before))
                        throw Bad("Only a whole supported GUID list can map to a guidList parameter.");
                    return Map(parameter, "guidList", path);
                }
                var clone = new JsonArray();
                for (var i = 0; i < array.Count; i++)
                    clone.Add(Resolve(array[i], previous is JsonArray prior && i < prior.Count ? prior[i] : null, path + "/" + i, key));
                return clone;
            }
            if (node is JsonObject obj)
            {
                var clone = new JsonObject();
                foreach (var (childKey, child) in obj)
                    clone[childKey] = Resolve(child, previous is JsonObject prior ? prior[childKey] : null, path + "/" + Escape(childKey), childKey);
                return clone;
            }
            return node?.DeepClone();
        }
        JsonNode Map(string parameter, string expectedType, string path)
        {
            if (!baseline.Parameters.Any(p => p.Key == parameter && p.Type == expectedType))
                throw Bad("Select an exact existing " + expectedType + " parameter for this reference.");
            used.Add(path); return JsonValue.Create("{{" + parameter + "}}")!;
        }
    }
    private static bool GuidArray(JsonArray array) => array.Count > 0 && array.All(n => n is JsonValue v && v.TryGetValue<string>(out var text) && Guid.TryParse(text, out _));
    private static CandidateAssignmentObservation Assignments(JsonObject input, string digest, string control)
    {
        var present = AssignmentFields.Where(input.ContainsKey).ToArray();
        var known = present.Length > 0 && present.All(key => input[key] is JsonArray array && array.All(n => n is JsonObject));
        var count = known ? present.Sum(key => input[key]!.AsArray().Count) : 0;
        var status = !known ? "Unknown" : count == 0 ? "ReturnedEmpty" : "ReturnedNotApplied";
        var detail = !known ? "Assignment data was absent, null or malformed; this is not an empty assignment inventory."
            : $"Source returned {count} assignment objects; none are imported or applied. This is an unverified source claim, not target-tenant state.";
        if (input.ContainsKey("roleScopeTagIds")) detail += " Foreign role scope tags were discarded separately.";
        return new(digest, control, status, detail);
    }
    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static ConfigurationException Bad(string message) => new("Candidate policy import: " + message);
}
