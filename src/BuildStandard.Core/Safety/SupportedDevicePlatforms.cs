using System.Text.Json.Nodes;
using BuildStandard.Core.Models;

namespace BuildStandard.Core.Safety;

/// <summary>Product support boundary, not a tenant access-denial policy. Inventory remains readable.</summary>
public static class SupportedDevicePlatforms
{
    public const string MacOsReason = "macOS is unsupported: inventory only. macOS deployment and compliance requirements are blocked in this toolkit; use the Windows-only successor standard. There is no enable switch.";

    public static void AssertSupported(CollectionDefinition definition, JsonObject payload)
    {
        var type = payload["@odata.type"]?.ToString() ?? "";
        if (type.Contains("macOS", StringComparison.OrdinalIgnoreCase)
            || ContainsPlatform(payload["platforms"], "macOS"))
            throw new SafetyViolationException(MacOsReason);

        if (!ConditionalAccessSafety.IsConditionalAccess(definition)
            || !ContainsPlatform(payload["grantControls"]?["builtInControls"], "compliantDevice")) return;
        var platforms = payload["conditions"]?["platforms"];
        var included = platforms?["includePlatforms"];
        var excluded = platforms?["excludePlatforms"];
        // No platform condition applies to every platform. Explicit all also includes macOS.
        var couldIncludeMac = included is null || included is JsonArray { Count: 0 }
            || ContainsPlatform(included, "macOS") || ContainsPlatform(included, "all")
            || ContainsPlatform(included, "unknownFutureValue");
        if (couldIncludeMac && !ContainsPlatform(excluded, "macOS"))
            throw new SafetyViolationException(MacOsReason);
    }

    private static bool ContainsPlatform(JsonNode? node, string value) => node switch
    {
        JsonArray array => array.Any(x => string.Equals(x?.ToString(), value, StringComparison.OrdinalIgnoreCase)),
        JsonValue scalar => scalar.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)),
        _ => false
    };
}
