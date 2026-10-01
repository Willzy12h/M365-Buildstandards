using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Graph.Auth;

namespace BDIT.TenantToolkit.Graph;

/// <summary>Transient identity facts, never an authenticated workspace or permission to deploy.</summary>
public sealed record DiscoveredTenant(string TenantId, string Name, string Domain, string Account,
    string OperatorObjectId, bool TokenHasWriteScopes)
{
    public TenantProfile NewProfile() => ProfileValidator.Validate(new TenantProfile
    {
        Company = Name, TenantId = TenantId, Domain = Domain
    }, DateTimeOffset.UtcNow);

    /// <summary>The second, tenant-pinned sign-in must still be the identity the engineer confirmed.</summary>
    public void VerifyConnection(TenantSession session)
    {
        if (session.Mode != SessionMode.Assessment || !session.TenantVerified || !session.OperatorVerified
            || !string.Equals(session.TenantId, TenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(session.AccountObjectId, OperatorObjectId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(session.OperatorObjectId, OperatorObjectId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("The tenant or account changed after Quick Connect discovery. Connection rejected; start Quick Connect again and confirm the intended organisation and account.");
    }
}

/// <summary>Bounded identity discovery only. Dispose temporary tokens before presenting a confirmation.</summary>
public static class TenantDiscoveryService
{
    internal static GraphRouteAllowList Routes() => GraphRouteAllowList.Only(new[]
    {
        new GraphRoute(GraphApi.V1, "/organization", "Organization.Read.All", null, "organization"),
        new GraphRoute(GraphApi.V1, "/me", "User.Read", null, "me")
    });

    public static async Task<DiscoveredTenant> DiscoverAsync(HttpClient http, ToolkitSettings settings,
        IntPtr parentWindow, IToolkitLog log, CancellationToken ct)
    {
        if (!settings.AllowMicrosoftGraphPowerShellFallback)
            throw new ConfigurationException("Quick Connect needs the Microsoft Graph PowerShell assessment fallback, which is disabled here. Use a saved client or enter the tenant and dedicated assessment application details.");
        var auth = await MsalAuthenticator.DiscoverAsync(parentWindow, settings.UseSystemBrowser,
            TimeSpan.FromMinutes(settings.SignInTimeoutMinutes), log, ct);
        try
        {
            var graph = new GraphClient(http, auth, auth.Outcome.TenantId, SessionMode.Assessment, Routes(),
                new GraphClientOptions { ReadTimeout = TimeSpan.FromSeconds(settings.GraphReadTimeoutSeconds),
                    MaxRetryAfter = TimeSpan.FromSeconds(settings.MaxRetryAfterSeconds) }, log);
            return await ReadIdentityAsync(graph, auth.Outcome, ct);
        }
        finally { await auth.DisconnectAsync(); }
    }

    internal static async Task<DiscoveredTenant> ReadIdentityAsync(IGraphClient graph, SignInOutcome token, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (graph.Mode != SessionMode.Assessment || !ProfileValidator.IsGuid(token.TenantId)
            || !ProfileValidator.IsGuid(token.AccountObjectId)
            || !string.Equals(graph.TenantId, token.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("Quick Connect did not receive a verified work or school identity.");
        var response = await graph.GetAsync(GraphApi.V1, "/organization?$select=id,displayName,verifiedDomains", ct);
        if (response["value"] is not JsonArray { Count: 1 } organisations || organisations[0] is not JsonObject org
            || !string.Equals(org["id"]?.GetValue<string>(), token.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("Quick Connect organisation does not match the signed-in tenant. Use the explicit customer tenant for partner or guest access.");
        var name = org["displayName"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(name)) throw new ConfigurationException("Microsoft did not return an organisation name. Use the manual connection details.");
        var domain = (org["verifiedDomains"] as JsonArray)?.OfType<JsonObject>()
            .Where(d => d["isDefault"]?.GetValue<bool>() == true)
            .Select(d => d["name"]?.GetValue<string>() ?? "").SingleOrDefault() ?? "";
        var me = await graph.GetAsync(GraphApi.V1, "/me?$select=id,displayName,userPrincipalName", ct);
        if (!string.Equals(me["id"]?.GetValue<string>(), token.AccountObjectId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("Quick Connect could not verify the signed-in account against Microsoft Graph.");
        var account = me["userPrincipalName"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(account)) throw new ConfigurationException("Microsoft did not return the signed-in account name. Use the manual connection details.");
        ct.ThrowIfCancellationRequested();
        return new DiscoveredTenant(token.TenantId, name, domain, account, token.AccountObjectId,
            new TenantSession { Scopes = token.Scopes.ToList() }.HasWriteScopes);
    }
}
