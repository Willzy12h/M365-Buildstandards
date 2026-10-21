using System.Text.Json.Nodes;
namespace BuildStandard.Core.Safety;

/// <summary>A fixed request description, not approval or permission to send it. Execution must revalidate the registry and evidence.</summary>
public sealed record LifecycleOperation(string ActionId, string UserId, string? GroupId, string? SkuId,
    string Method, string Path, string? PayloadJson, string WriteScope);

/// <summary>INT-137: existing cloud-member actions only. Never accepts a caller-supplied route, body or scope.</summary>
public static class LifecycleActionRegistry
{
    public static IReadOnlyList<string> ActionIds { get; } = Array.AsReadOnly(new[]
    { "add-member", "remove-member", "assign-license", "remove-license", "enable-account", "disable-account", "revoke-sessions" });

    public static LifecycleOperation Create(string actionId, string userId, string? groupId = null, string? skuId = null, string[]? disabledPlans = null)
    {
        if (!ActionIds.Contains(actionId, StringComparer.Ordinal)) throw Bad("Choose one registered lifecycle action.");
        var user = GuidId(userId); var membership = actionId is "add-member" or "remove-member";
        var licensing = actionId is "assign-license" or "remove-license";
        if (!membership && groupId is not null || !licensing && skuId is not null
            || actionId != "assign-license" && disabledPlans is not null)
            throw Bad("Inputs for an unrelated lifecycle action are not accepted.");
        var group = membership ? GuidId(groupId) : null; var sku = licensing ? GuidId(skuId) : null;
        if (disabledPlans is { Length: > 1000 }) throw Bad("Too many disabled service plans.");
        var plans = (disabledPlans ?? []).Select(GuidId).Order(StringComparer.Ordinal).ToArray();
        if (plans.Distinct(StringComparer.Ordinal).Count() != plans.Length) throw Bad("Duplicate disabled service plans are ambiguous.");
        var method = "POST"; var path = "/users/" + user; JsonObject? body = null; string scope;
        switch (actionId)
        {
            case "add-member":
                path = "/groups/" + group + "/members/$ref";
                body = new() { ["@odata.id"] = "https://graph.microsoft.com/v1.0/directoryObjects/" + user }; scope = "GroupMember.ReadWrite.All"; break;
            case "remove-member":
                method = "DELETE"; path = "/groups/" + group + "/members/" + user + "/$ref"; scope = "GroupMember.ReadWrite.All"; break;
            case "assign-license":
                path += "/assignLicense"; scope = "LicenseAssignment.ReadWrite.All";
                body = new() { ["addLicenses"] = new JsonArray(new JsonObject { ["skuId"] = sku, ["disabledPlans"] = new JsonArray(plans.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()) }), ["removeLicenses"] = new JsonArray() }; break;
            case "remove-license":
                path += "/assignLicense"; scope = "LicenseAssignment.ReadWrite.All";
                body = new() { ["addLicenses"] = new JsonArray(), ["removeLicenses"] = new JsonArray(sku) }; break;
            case "enable-account": case "disable-account":
                method = "PATCH"; scope = "User.EnableDisableAccount.All"; body = new() { ["accountEnabled"] = actionId == "enable-account" }; break;
            case "revoke-sessions": path += "/revokeSignInSessions"; scope = "User.RevokeSessions.All"; break;
            default: throw Bad("Unregistered lifecycle action.");
        }
        return new(actionId, user, group, sku, method, path, body?.ToJsonString(), scope);
    }
    /// <summary>Rebuild a proposed request before every trusted boundary; DTO construction or deserialisation never authorises it.</summary>
    public static LifecycleOperation RequireRegistered(LifecycleOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            string[]? plans = null;
            if (operation.ActionId == "assign-license")
            {
                if (operation.PayloadJson is null || System.Text.Encoding.UTF8.GetByteCount(operation.PayloadJson) > 65536)
                    throw Bad("Missing or oversized lifecycle payload.");
                using var document = System.Text.Json.JsonDocument.Parse(operation.PayloadJson, new System.Text.Json.JsonDocumentOptions { MaxDepth = 8 });
                plans = document.RootElement.GetProperty("addLicenses")[0].GetProperty("disabledPlans").EnumerateArray().Select(value => value.GetString()!).ToArray();
            }
            var expected = Create(operation.ActionId, operation.UserId, operation.GroupId, operation.SkuId, plans);
            if (expected != operation) throw Bad("Lifecycle request changed or is not exactly registry-generated. Prepare and review a fresh request.");
            return expected;
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        { throw Bad("Malformed lifecycle request; no route or body was admitted."); }
    }
    private static string GuidId(string? text)
    {
        if (!Guid.TryParseExact(text, "D", out var value) || value == Guid.Empty) throw Bad("Select an exact non-empty GUID; names, URLs and path fragments cannot identify a lifecycle target.");
        return value.ToString("D");
    }
    private static ConfigurationException Bad(string message) => new(message);
}
