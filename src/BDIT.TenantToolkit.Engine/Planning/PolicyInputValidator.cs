using System.Text.Json;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Planning;

public static class PolicyInputValidator
{
    public static void ValidateUsed(JsonObject template, StandardCatalogue standard, IReadOnlyDictionary<string, JsonNode?> values)
    {
        var json = template.ToJsonString();
        foreach (var p in standard.Parameters.Where(p => json.Contains("{{" + p.Key + "}}", StringComparison.Ordinal)))
        {
            if (!values.TryGetValue(p.Key, out var value) || value is null) continue; // Resolver reports missing inputs.
            var valid = p.Type switch
            {
                "guid" => value is JsonValue g && g.TryGetValue<string>(out var id) && ProfileValidator.IsGuid(id),
                // An optional list may legitimately be empty - an Enrolment Status Page with no blocking application,
                // for example - but a required one carries the identities a policy depends on and must not be.
                "guidList" => value is JsonArray a && (a.Count > 0 || !p.Required) && a.All(n => n is JsonValue v && v.TryGetValue<string>(out var id) && ProfileValidator.IsGuid(id)),
                "string" => value is JsonValue s && s.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) && text.Length <= 4000,
                "integer" => value is JsonValue i && i.TryGetValue<int>(out _),
                "boolean" => value?.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
                "jsonArray" => value is JsonArray { Count: > 0 },
                _ => false
            };
            if (!valid) throw new MissingParameterException(p.Label + " (" + p.Type + ")");
        }
    }
}
