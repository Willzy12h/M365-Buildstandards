using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Graph;

/// <summary>
/// Some Graph relationships are returned by a documented parent GET rather than a standalone list endpoint.
/// Normalise these reads without changing the catalogue, logical collection identity or any write route.
/// Missing, malformed or truncated embedded data is never an empty successful collection.
/// </summary>
internal static class ExpandedGraphCollections
{
    private const string Policy = "/policies/authenticationMethodsPolicy";
    private const string Methods = Policy + "/authenticationMethodConfigurations";
    private const string Compliance = "/deviceManagement/deviceCompliancePolicies/";
    private const string Schedule = "/scheduledActionsForRule";
    private const string Actions = "scheduledActionConfigurations";

    public static async Task<IReadOnlyList<JsonObject>?> TryReadAsync(GraphClient graph, GraphApi api,
        string path, int maxItems, CancellationToken ct)
    {
        var queryAt = path.IndexOf('?', StringComparison.Ordinal);
        var basePath = queryAt < 0 ? path : path[..queryAt];
        var query = queryAt < 0 ? "" : path[queryAt..];
        string? parent = null, property = null, expectedId = null;
        if (basePath.Equals(Methods, StringComparison.OrdinalIgnoreCase))
        { parent = Policy; property = "authenticationMethodConfigurations"; expectedId = "authenticationMethodsPolicy"; }
        else if (basePath.Equals(Methods + "/FIDO2/passkeyProfiles", StringComparison.OrdinalIgnoreCase))
        { parent = Methods + "/fido2"; property = "passkeyProfiles"; expectedId = "Fido2"; }
        if (parent is not null)
        {
            if (query is not ("" or "?$top=1"))
                throw Incomplete(path, "This embedded relationship supports a complete read or the access-check probe only.");
            var source = await graph.GetAsync(api, parent, ct); // Independently enforces the parent's route allow-list.
            if (!string.Equals(Text(source["id"]), expectedId, StringComparison.OrdinalIgnoreCase))
                throw Incomplete(parent, "Parent response identity did not match the requested policy or method.");
            if (source[property!] is not JsonArray values || source[property + "@odata.nextLink"] is not null
                || source["@odata.nextLink"] is not null)
                throw Incomplete(parent, $"The complete {property} array was not returned. Absence does not mean no configured objects.");
            if (values.Count > maxItems) throw Incomplete(parent, "Embedded collection item limit exceeded.");
            var items = new List<JsonObject>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                ct.ThrowIfCancellationRequested();
                if (value is not JsonObject item || string.IsNullOrWhiteSpace(Text(item["id"])) || !ids.Add(Text(item["id"])!))
                    throw Incomplete(parent, "Embedded collection contains an invalid or duplicate object identity.");
                items.Add((JsonObject)item.DeepClone());
            }
            return query.Length == 0 ? items : items.Take(1).ToList();
        }

        // Intune does not consistently accept $expand on this relationship. Read the rules and each rule's
        // action configurations explicitly, with ordinary pagination/timeout/cancellation on every request.
        if (basePath.StartsWith(Compliance, StringComparison.OrdinalIgnoreCase)
            && basePath.EndsWith(Schedule, StringComparison.OrdinalIgnoreCase)
            && query == "?$expand=" + Actions
            && ProfileValidator.IsGuid(basePath[Compliance.Length..^Schedule.Length]))
        {
            var rules = await graph.GetAllAsync(api, basePath, ct);
            foreach (var rule in rules)
            {
                var id = Text(rule["id"]);
                if (!ProfileValidator.IsGuid(id ?? "")) throw Incomplete(basePath, "Scheduled rule identity is missing or invalid.");
                var actions = await graph.GetAllAsync(api, basePath + "/" + id + "/" + Actions, ct);
                rule[Actions] = new JsonArray(actions.Select(a => (JsonNode)a.DeepClone()).ToArray());
            }
            return rules;
        }
        return null;
    }

    private static string? Text(JsonNode? value) => value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : null;
    private static GraphRequestException Incomplete(string path, string message) => new(200, "GET", path, "incomplete", message);
}
