using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Models;
namespace BuildStandard.Engine.Identity;
public enum ObjectLookupKind { Group, NamedLocation }
public sealed record ResolvedTenantObject(string TenantId, string ObjectId, string DisplayName, ObjectLookupKind Kind)
{
    public string Label => DisplayName + " · " + ObjectId;
}
public static class ObjectResolver
{
    public static async Task<IReadOnlyList<ResolvedTenantObject>> SearchAsync(IGraphClient graph, string tenantId, ObjectLookupKind kind, string query, CancellationToken ct)
    {
        if (!ProfileValidator.IsGuid(tenantId) || !string.Equals(graph.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("Object lookup must use the connected tenant.");
        query = query.Trim();
        if (query.Length < 2 || query.Length > 200 || query.Any(char.IsControl))
            throw new ConfigurationException("Enter an exact object ID or at least two characters of its name.");
        var path = kind switch { ObjectLookupKind.Group => "/groups", ObjectLookupKind.NamedLocation => "/identity/conditionalAccess/namedLocations", _ => throw new ConfigurationException("Unsupported object lookup kind.") };
        var exact = ProfileValidator.IsGuid(query);
        var rows = new List<JsonObject>();
        if (exact) rows.Add(await graph.GetAsync(GraphApi.V1, path + "/" + Uri.EscapeDataString(query) + "?$select=id,displayName", ct));
        else
        {
            path += "?$select=id,displayName";
            if (kind == ObjectLookupKind.Group) path += "&$filter=" + Uri.EscapeDataString("startswith(displayName,'" + query.Replace("'", "''") + "')") + "&$top=100";
            // Named-location name filtering is local: do not assume that endpoint supports the group filter.
            await foreach (var row in graph.GetBoundedAsync(GraphApi.V1, path, 1001, ct)) rows.Add(row);
            if (rows.Count > 1000) throw new ConfigurationException("Lookup exceeded 1,000 objects and is incomplete. Use an exact object ID; no partial choice list was accepted.");
        }
        ct.ThrowIfCancellationRequested();
        if (!string.Equals(graph.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)) throw new TenantMismatchException("The lookup tenant changed.");
        var result = new List<ResolvedTenantObject>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var id = row["id"]?.GetValue<string>() ?? "";
            if (!ProfileValidator.IsGuid(id) || !ids.Add(id) || exact && !string.Equals(id, query, StringComparison.OrdinalIgnoreCase))
                throw new ConfigurationException("Lookup returned an invalid, duplicate or unexpected object identity. No selection was accepted.");
            var name = row["displayName"]?.GetValue<string>();
            if (!exact && kind == ObjectLookupKind.NamedLocation && string.IsNullOrWhiteSpace(name))
                throw new ConfigurationException("A named location did not return its name. Search by exact ID; name matching is unknown, not empty.");
            if (exact || kind == ObjectLookupKind.Group || name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                result.Add(new(tenantId, id, string.IsNullOrWhiteSpace(name) ? "Name unavailable" : name, kind));
        }
        return result;
    }
}
