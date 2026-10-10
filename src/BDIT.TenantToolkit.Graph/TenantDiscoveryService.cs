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

    /// <summary>The confirmed, tenant-pinned connection must still be the identity the engineer confirmed.</summary>
    public void VerifyConnection(TenantSession session)
    {
        if (session.Mode != SessionMode.Assessment || !session.TenantVerified || !session.OperatorVerified
            || !string.Equals(session.TenantId, TenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(session.AccountObjectId, OperatorObjectId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(session.OperatorObjectId, OperatorObjectId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("The tenant or account changed after Quick Connect discovery. Connection rejected; start Quick Connect again and confirm the intended organisation and account.");
    }
}

/// <summary>Bounded identity discovery. A retained result can be consumed once for assessment only.</summary>
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

    public static async Task<PendingTenantDiscovery> DiscoverRetainedAsync(HttpClient http, ToolkitSettings settings,
        StandardCatalogue standard, IReadOnlyList<string> readScopes, IntPtr parentWindow, IToolkitLog log, CancellationToken ct,
        Func<CancellationToken, Task>? beforeInteractive = null)
    {
        if (!settings.AllowMicrosoftGraphPowerShellFallback)
            throw new ConfigurationException("Quick Connect needs the enabled Microsoft Graph PowerShell assessment fallback.");
        if (readScopes.Any(s => s.Contains("Write", StringComparison.OrdinalIgnoreCase) || s.Contains("AccessAsUser", StringComparison.OrdinalIgnoreCase)))
            throw new ConfigurationException("Quick Connect cannot request write permissions.");
        var auth = await MsalAuthenticator.DiscoverAsync(parentWindow, settings.UseSystemBrowser,
            TimeSpan.FromMinutes(settings.SignInTimeoutMinutes), log, ct, readScopes, beforeInteractive);
        try
        {
            var graph = new GraphClient(http, auth, auth.Outcome.TenantId, SessionMode.Assessment, Routes(),
                new GraphClientOptions { ReadTimeout = TimeSpan.FromSeconds(settings.GraphReadTimeoutSeconds) }, log);
            var identity = await ReadIdentityAsync(graph, auth.Outcome, ct);
            ct.ThrowIfCancellationRequested();
            return new PendingTenantDiscovery(identity, standard.IntegrityDigest, auth);
        }
        catch { await auth.ReleaseAsync(); throw; }
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

/// <summary>In-memory, single-use assessment authentication; never persisted as approval.</summary>
public sealed class PendingTenantDiscovery : IAsyncDisposable
{
    private MsalAuthenticator? _auth;
    public DiscoveredTenant Identity { get; }
    public string StandardDigest { get; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    internal PendingTenantDiscovery(DiscoveredTenant identity, string standardDigest, MsalAuthenticator auth)
    { Identity = identity; StandardDigest = standardDigest; _auth = auth; }
    public static void VerifyContext(DiscoveredTenant identity, TenantProfile profile, string expectedDigest,
        string currentDigest, DateTimeOffset createdAt, DateTimeOffset now)
    {
        if (!string.Equals(identity.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase)
            || expectedDigest != currentDigest || createdAt > now.AddMinutes(1) || now - createdAt > TimeSpan.FromMinutes(5))
            throw new TenantMismatchException("Quick Connect confirmation expired or the tenant/standard changed. Start Quick Connect again.");
    }
    internal MsalAuthenticator Take(TenantProfile profile, StandardCatalogue standard)
    {
        VerifyContext(Identity, profile, StandardDigest, standard.IntegrityDigest, CreatedAt, DateTimeOffset.UtcNow);
        return Interlocked.Exchange(ref _auth, null) ?? throw new AuthenticationRequiredException("Quick Connect was already confirmed or cancelled.");
    }
    public async ValueTask DisposeAsync()
    { var auth = Interlocked.Exchange(ref _auth, null); if (auth is not null) await auth.ReleaseAsync(); }
}
