using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Models;
namespace BDIT.TenantToolkit.Engine.Planning;

public static class CandidateReferenceValidator
{
    public static async Task ValidateAsync(IGraphClient graph, JsonObject payload, CancellationToken ct)
    {
        if (payload["selectedMobileAppIds"] is JsonArray apps)
            foreach (var id in apps)
            {
                if (!ProfileValidator.IsGuid(id?.ToString())) throw new SafetyViolationException("ESP requires valid target-tenant application IDs.");
                var app = await graph.GetAsync(GraphApi.Beta, "/deviceAppManagement/mobileApps/" + id, ct);
                if (app["publishingState"]?.ToString() != "published" || app["@odata.type"]?.ToString() == "#microsoft.graph.officeSuiteApp")
                    throw new SafetyViolationException("ESP blocking applications must be published; Microsoft 365 Apps must be installed after ESP.");
            }
        if (payload["settings"] is JsonArray settings)
            foreach (var instance in Instances(settings))
            {
                var id = instance["settingDefinitionId"]?.ToString() ?? throw new SafetyViolationException("Setting definition ID is missing.");
                var def = await graph.GetAsync(GraphApi.Beta, "/deviceManagement/configurationSettings/" + Uri.EscapeDataString(id), ct);
                if (def["id"]?.ToString() != id) throw new SafetyViolationException("Setting definition could not be resolved in this tenant.");
                if (instance["choiceSettingValue"] is JsonObject choice)
                {
                    var selected = choice["value"]?.ToString();
                    if (def["options"] is not JsonArray options || !options.OfType<JsonObject>().Any(o => o["itemId"]?.ToString() == selected))
                        throw new SafetyViolationException("A Settings Catalogue choice is not offered by the target tenant.");
                }
            }
    }
    private static IEnumerable<JsonObject> Instances(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("settingDefinitionId")) yield return obj;
            foreach (var child in obj.Select(p => p.Value).OfType<JsonNode>()) foreach (var found in Instances(child)) yield return found;
        }
        else if (node is JsonArray array)
            foreach (var child in array.OfType<JsonNode>()) foreach (var found in Instances(child)) yield return found;
    }
}
