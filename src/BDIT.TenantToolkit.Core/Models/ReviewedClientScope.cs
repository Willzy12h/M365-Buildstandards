using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Json;

namespace BDIT.TenantToolkit.Core.Models;

/// <summary>
/// INT-056 (CLA-20261006-17): the digest INT-049/050 records bind to, so a later review can tell whether the client
/// inputs it relied on have changed. Only fields that label the client are left out; any change to tenant, domain,
/// applications, parameters, offices, policy inputs or exclusions changes the digest and projects review.
/// </summary>
public static class ReviewedClientScope
{
    /// <summary>JSON names of the profile fields that only label the client or record when it was edited.</summary>
    public static readonly IReadOnlyList<string> NonMaterialFields = new[] { "company", "notes", "createdAt", "updatedAt" };

    public static string Digest(TenantProfile profile)
    {
        var node = ToolkitJson.ToNode(profile) as JsonObject ?? throw new ConfigurationException("The client profile could not be read.");
        foreach (var field in NonMaterialFields) node.Remove(field);
        return CanonicalJson.Sha256(node);
    }
}
