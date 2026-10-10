using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Identity;

/// <summary>Resolves explicit user search input in the authenticated tenant. Callers must select a result.</summary>
public static class AccountResolver
{
    public static async Task<IReadOnlyList<ExclusionAccount>> SearchAsync(IGraphClient graph, string tenantId, string query, CancellationToken ct)
    {
        if (!ProfileValidator.IsGuid(tenantId) || !string.Equals(graph.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("Account lookup must use the connected tenant.");
        query = query.Trim();
        if (query.Length < 2 || query.Length > 200 || query.Any(char.IsControl))
            throw new ConfigurationException("Enter a user principal name, object ID, or at least two characters of a display name.");
        const string select = "id,displayName,userPrincipalName,accountEnabled,userType";
        IReadOnlyList<JsonObject> matches;
        if (ProfileValidator.IsGuid(query))
            matches = new[] { await graph.GetAsync(GraphApi.V1, "/users/" + Uri.EscapeDataString(query) + "?$select=" + select, ct) };
        else
        {
            var escaped = query.Replace("'", "''");
            var filter = query.Contains('@') ? "userPrincipalName eq '" + escaped + "'" : "startswith(displayName,'" + escaped + "')";
            matches = await graph.GetAllAsync(GraphApi.V1, "/users?$select=" + select + "&$filter=" + Uri.EscapeDataString(filter) + "&$top=50", ct);
        }
        if (matches.Count > 100) throw new ConfigurationException("Too many matching users. Enter a more specific name or full user principal name.");
        return matches.Select(user =>
        {
            var id = user["id"]?.GetValue<string>() ?? "";
            if (!ProfileValidator.IsGuid(id)) throw new ConfigurationException("Microsoft Graph returned a user without a valid object ID.");
            return new ExclusionAccount { TenantId = tenantId, ObjectId = id, DisplayName = user["displayName"]?.GetValue<string>() ?? "(no display name)",
                UserPrincipalName = user["userPrincipalName"]?.GetValue<string>() ?? "", ResolvedAt = Timestamps.Format(DateTimeOffset.UtcNow) };
        }).ToList();
    }
}
