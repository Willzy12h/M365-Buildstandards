using System.Text.Json;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Core.Safety;

public static class PolicyCandidateSafety
{
    public static void Assert(CollectionDefinition def, JsonObject payload)
    {
        if (payload.ContainsKey("groupAssignments") || payload.ContainsKey("targetAssignments") || payload.ContainsKey("isAssigned"))
            throw new SafetyViolationException("Candidate payload cannot carry assignment state.");
        if (payload["@odata.type"]?.ToString() == "#microsoft.graph.winGetApp")
        {
            var id = payload["packageIdentifier"]?.ToString() ?? "";
            if (id.Length is < 8 or > 30 || !id.All(char.IsAsciiLetterOrDigit))
                throw new SafetyViolationException("Provide a Microsoft Store package identifier, not a community winget ID or URL.");
            if (def.ApiVersion != GraphApi.Beta) throw new SafetyViolationException("Microsoft Store candidates use the declared beta application route.");
        }
        if (payload["@odata.type"]?.ToString() == "#microsoft.graph.win32LobApp")
        {
            if (payload.ContainsKey("committedContentVersion")) throw new SafetyViolationException("Content publication requires the dedicated package workflow.");
            foreach (var key in new[] { "fileName", "installCommandLine", "uninstallCommandLine" })
                if (string.IsNullOrWhiteSpace(payload[key]?.ToString())) throw new SafetyViolationException("Win32 candidates need installer, installation and removal instructions.");
            if (payload["rules"] is not JsonArray rules || !rules.OfType<JsonObject>().Any(r => r["ruleType"]?.ToString() == "detection"))
                throw new SafetyViolationException("Win32 candidates require explicit detection rules.");
        }
        if (def.BasePath == "/deviceManagement/configurationPolicies")
        {
            if (payload["platforms"]?.ToString() != "windows10" || payload["technologies"]?.ToString() != "mdm"
                || payload["templateReference"] is not JsonObject reference || reference["templateId"]?.ToString() != "")
                throw new SafetyViolationException("Only Windows MDM Settings Catalogue policies without an endpoint-security template are supported.");
            AssertSettings(payload["settings"]);
        }
    }
    public static void AssertSettings(JsonNode? node)
    {
        if (node is not JsonArray { Count: > 0 } settings) throw new SafetyViolationException("Provide reviewed Settings Catalogue instances.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in settings)
        {
            if (item is not JsonObject setting || setting["settingInstance"] is not JsonObject instance)
                throw new SafetyViolationException("Each catalogue setting needs a settingInstance.");
            var id = instance["settingDefinitionId"]?.ToString() ?? "";
            if (id.Length == 0 || !ids.Add(id)) throw new SafetyViolationException("Setting definition IDs must be present and unique.");
            Walk(instance);
        }
    }
    private static void Walk(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj)
            {
                if (key is "secretValue" or "encryptedValue" or "secretReference" || key.Contains("@odata.bind", StringComparison.Ordinal))
                    throw new SafetyViolationException("Secret and external-reference settings are not supported by the import route.");
                if (key == "@odata.type" && value?.ToString().Contains("Secret", StringComparison.OrdinalIgnoreCase) == true)
                    throw new SafetyViolationException("Secret settings are not supported.");
                if (key == "settingDefinitionId" && (value is not JsonValue v || !v.TryGetValue<string>(out var id)
                    || id.Length > 1024 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')))
                    throw new SafetyViolationException("Invalid setting definition identifier.");
                Walk(value);
            }
        }
        else if (node is JsonArray a) foreach (var child in a) Walk(child);
    }
}
