using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;

namespace BDIT.TenantToolkit.Engine.Recovery;

public static class RecoveryObjectReader
{
    public const string AssignmentsKey = "_toolkitAssignments";

    public static async Task<JsonObject> ReadAsync(IGraphClient graph, CollectionDefinition definition, string id, CancellationToken ct)
    {
        if (!ProfileValidator.IsGuid(id)) throw new SafetyViolationException("Recovery requires a recorded object GUID.");
        var path = definition.BasePath.TrimEnd('/') + "/" + id;
        var result = await graph.GetAsync(definition.ApiVersion, path, ct);
        if (!string.Equals(result["id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase))
            throw new SafetyViolationException("Graph returned an unexpected object ID.");
        result.Remove("@odata.context");
        if (definition.Assignments)
            result[AssignmentsKey] = Array(await graph.GetAllAsync(definition.ApiVersion, path + "/assignments", ct));
        if (!string.IsNullOrEmpty(definition.Relationship))
            result[definition.Relationship.Split('?')[0].Trim('/')] = Array(await graph.GetAllAsync(definition.ApiVersion, path + "/" + definition.Relationship, ct));
        if (!string.IsNullOrEmpty(definition.Children))
            result[definition.Children.Split('?')[0].Trim('/')] = Array(await graph.GetAllAsync(definition.ApiVersion, path + "/" + definition.Children, ct));
        return result;
    }

    public static bool IsInactive(CollectionDefinition definition, JsonObject obj) =>
        (!ConditionalAccessSafety.IsConditionalAccess(definition) || ConditionalAccessSafety.State(obj) == "disabled")
        && (!definition.Assignments || obj[AssignmentsKey] is JsonArray { Count: 0 });

    private static JsonArray Array(IReadOnlyList<JsonObject> items) => new(items.Select(i => (JsonNode?)i.DeepClone()).ToArray());
}
