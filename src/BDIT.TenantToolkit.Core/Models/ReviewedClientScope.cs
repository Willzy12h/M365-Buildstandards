using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Json;

namespace BDIT.TenantToolkit.Core.Models;

/// <summary>
/// INT-056 (CLA-20261006-17): the digest INT-049/050 records bind to, so a later review can tell whether the client
/// inputs it relied on have changed. Only fields that label the client, or record when and by whom something was
/// resolved, are left out; any change to tenant, domain, applications, parameters, offices, policy inputs or
/// exclusions changes the digest and projects review. INT-059: the account lists are sets, so their order is not
/// material either.
/// </summary>
public static class ReviewedClientScope
{
    /// <summary>JSON names of the profile fields that only label the client or record when it was edited.</summary>
    public static readonly IReadOnlyList<string> NonMaterialFields = new[] { "company", "notes", "createdAt", "updatedAt" };

    /// <summary>
    /// JSON names of exclusion account fields that only record when, and by whom, the account was last resolved.
    /// Resolving the same account again restamps them without changing which account is excluded or why.
    /// </summary>
    public static readonly IReadOnlyList<string> NonMaterialExclusionFields = new[] { "resolvedAt", "selectedBy" };

    public static string Digest(TenantProfile profile)
    {
        var node = ToolkitJson.ToNode(profile) as JsonObject ?? throw new ConfigurationException("The client profile could not be read.");
        foreach (var field in NonMaterialFields) node.Remove(field);
        if (node["exclusionAccounts"] is JsonArray accounts)
        {
            foreach (var account in accounts.OfType<JsonObject>())
                foreach (var field in NonMaterialExclusionFields) account.Remove(field);
            node["exclusionAccounts"] = SortedByText(accounts);
        }
        if (node["parameters"] is JsonObject parameters)
            foreach (var list in new[] { "emergencyAccountIds", "additionalExclusionAccountIds" })
                if (parameters[list] is JsonArray ids) parameters[list] = SortedByText(ids);
        return CanonicalJson.Sha256(node);
    }

    private static JsonArray SortedByText(JsonArray items)
    {
        var sorted = items.Select(i => i?.DeepClone()).OrderBy(i => CanonicalJson.Serialize(i), StringComparer.Ordinal).ToList();
        return new JsonArray(sorted.ToArray());
    }
}
