using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Graph.Auth;
using Microsoft.Identity.Client;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class WindowsCompatibilityFactAttribute : FactAttribute
{
    public WindowsCompatibilityFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows DPAPI acceptance requires the native Windows run.";
    }
}

public sealed class CacheCryptoCompatibilityTests
{
    [WindowsCompatibilityFact]
    [SupportedOSPlatform("windows")]
    public async Task A_cache_protected_with_the_historical_entropy_is_read_without_network_or_migration()
    {
        using var root = new TempRoot();
        var uid = "11111111-1111-1111-1111-111111111111";
        var tenant = TestData.TenantA;
        var home = uid + "." + tenant;
        var cache = new JsonObject
        {
            ["AccessToken"] = new JsonObject(), ["RefreshToken"] = new JsonObject(), ["IdToken"] = new JsonObject(),
            ["AppMetadata"] = new JsonObject(),
            ["Account"] = new JsonObject
            {
                [home + "-login.microsoftonline.com-" + tenant] = new JsonObject
                {
                    ["home_account_id"] = home, ["environment"] = "login.microsoftonline.com", ["realm"] = tenant,
                    ["local_account_id"] = uid, ["username"] = "synthetic@example.invalid", ["authority_type"] = "MSSTS"
                }
            }
        };
        var plain = Encoding.UTF8.GetBytes(cache.ToJsonString());
        var original = ProtectedData.Protect(plain, Encoding.UTF8.GetBytes("BDIT.TenantToolkit.TokenCache.v1"), DataProtectionScope.CurrentUser);
        var file = Path.Combine(root.Root, "msal-assessment.cache");
        File.WriteAllBytes(file, original);
        var http = new NoNetworkFactory();
        var application = PublicClientApplicationBuilder.Create(TestData.ClientId).WithHttpClientFactory(http).Build();
        TokenCacheProtection.Attach(application.UserTokenCache, file, NullLog.Instance);

        var accounts = await application.GetAccountsAsync();

        Assert.Equal("synthetic@example.invalid", Assert.Single(accounts).Username);
        Assert.Equal(0, http.Calls);
        Assert.Equal(original, File.ReadAllBytes(file));
        Assert.Single(Directory.EnumerateFiles(root.Root, "*.cache"));
    }

    private sealed class NoNetworkFactory : IMsalHttpClientFactory
    {
        public int Calls { get; private set; }
        public HttpClient GetHttpClient()
        {
            Calls++;
            throw new InvalidOperationException("Synthetic cache compatibility must not request an HTTP client.");
        }
    }
}
