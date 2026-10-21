using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Exchange;
namespace BuildStandard.Engine.Standards.Authoring;

public sealed record CandidateEditableField(string Path, string OriginalJson, string ProposedJson, string ValueType);
/// <summary>Offline immutable editing operations shared by the native editor and other local callers.</summary>
public sealed class CandidateEditingService(CandidateBaselineLoader baselines, CandidateAuthoringService authoring)
{
    public IReadOnlyList<CandidateEditableField> Fields(StandardCandidateDraft draft, string controlId)
    {
        var snapshot = Snapshot(draft); var baseline = baselines.Load(snapshot);
        var control = baseline.FindControl(controlId) ?? throw new ConfigurationException("Select an existing control.");
        var fields = new List<CandidateEditableField>();
        if (control.Payload is not null)
        {
            var projection = new JsonObject { ["payload"] = control.Payload.DeepClone() };
            foreach (var change in snapshot.Changes.Where(c => c.ControlId == controlId && c.Path.StartsWith("/payload/", StringComparison.Ordinal)))
                CandidatePointer.Replace(projection, change.Path, change.ExpectedOriginal, change.Proposed);
            Walk(control.Payload, "/payload", fields, projection);
        }
        var used = control.Payload is null ? new HashSet<string>() : ParameterUsage.Keys(control.Payload);
        foreach (var parameter in baseline.Parameters.Where(p => p.HasDefault && used.Contains(p.Key)))
        {
            var path = "/parameters/" + Escape(parameter.Key) + "/default";
            var change = snapshot.Changes.SingleOrDefault(c => c.Path == path);
            Add(path, parameter.Default, change is null ? parameter.Default : change.Proposed, fields);
        }
        return fields;
    }
    public StandardCandidateDraft Set(StandardCandidateDraft draft, string controlId, string path, string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > CandidateDraftCodec.MaximumBytes) throw new ConfigurationException("Setting exceeds the candidate input bound.");
        JsonNode? proposed;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
            ExchangeCaptureSchema.RejectDuplicates(document.RootElement, StringComparer.OrdinalIgnoreCase);
            proposed = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 48 });
        }
        catch (JsonException ex) { throw new ConfigurationException("Enter one valid JSON setting value, without duplicate properties.", ex); }
        var snapshot = Snapshot(draft);
        var field = Fields(snapshot, controlId).SingleOrDefault(f => f.Path == path)
            ?? throw new ConfigurationException("Select one existing editable field. Routes, identities and unsupported settings are not editable.");
        snapshot.Changes.RemoveAll(c => Matches(c, controlId, path));
        var original = JsonNode.Parse(field.OriginalJson);
        if (CanonicalJson.Serialize(original) != CanonicalJson.Serialize(proposed))
            snapshot.Changes.Add(new(controlId, path, original, proposed));
        CandidateDraftCodec.Seal(snapshot);
        _ = authoring.Validate(snapshot);
        return snapshot;
    }
    public StandardCandidateDraft Remove(StandardCandidateDraft draft, string controlId, string path)
    {
        var snapshot = Snapshot(draft); _ = baselines.Load(snapshot);
        if (snapshot.Changes.RemoveAll(c => Matches(c, controlId, path)) == 0)
            throw new ConfigurationException("Select an existing proposed change to remove.");
        CandidateDraftCodec.Seal(snapshot); _ = authoring.Validate(snapshot); return snapshot;
    }
    private static StandardCandidateDraft Snapshot(StandardCandidateDraft draft) => CandidateDraftCodec.Read(CandidateDraftCodec.Serialize(draft));
    private static void Walk(JsonNode? node, string path, List<CandidateEditableField> fields, JsonObject projection)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, child) in obj)
            {
                if (key is "@odata.type" or "omaUri" or "settingDefinitionId" or "assignments" or "state" or "macOSRestriction") continue;
                Walk(child, path + "/" + Escape(key), fields, projection);
            }
        }
        else Add(path, node, CandidatePointer.Read(projection, path), fields); // Arrays are one setting, never indexed edits.
    }
    private static void Add(string path, JsonNode? original, JsonNode? proposed, List<CandidateEditableField> fields)
        => fields.Add(new(path, CanonicalJson.Serialize(original), CanonicalJson.Serialize(proposed), original?.GetValueKind().ToString() ?? "Null"));
    private static bool Matches(CandidateChange change, string control, string path)
        => change.Path == path && (path.StartsWith("/parameters/", StringComparison.Ordinal) || change.ControlId == control);
    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
