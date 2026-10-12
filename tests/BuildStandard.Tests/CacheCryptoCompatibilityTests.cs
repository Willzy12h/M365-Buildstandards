using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Graph.Auth;
using Microsoft.Identity.Client;
using Xunit;

namespace BuildStandard.Tests;

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
            ["AccessToken"] = new JsonObject(), ["IdToken"] = new JsonObject(),
            ["RefreshToken"] = new JsonObject
            {
                [home + "-login.microsoftonline.com-refreshtoken-" + TestData.ClientId + "--"] = new JsonObject
                {
                    ["home_account_id"] = home, ["environment"] = "login.microsoftonline.com",
                    ["credential_type"] = "RefreshToken", ["client_id"] = TestData.ClientId,
                    ["secret"] = "synthetic-unusable-refresh-marker"
                }
            },
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
        using var http = new NoNetworkFactory();
        var application = PublicClientApplicationBuilder.Create(TestData.ClientId).WithHttpClientFactory(http).Build();
        TokenCacheProtection.Attach(application.UserTokenCache, file, NullLog.Instance);

        var accounts = await application.GetAccountsAsync();

        Assert.Equal("synthetic@example.invalid", Assert.Single(accounts).Username);
        Assert.Equal(0, http.Requests);
        Assert.Equal(original, File.ReadAllBytes(file));
        Assert.Single(Directory.EnumerateFiles(root.Root, "*.cache"));
    }

    private sealed class NoNetworkFactory : IMsalHttpClientFactory, IDisposable
    {
        private readonly RefusingHandler _handler = new();
        private readonly HttpClient _client;
        public NoNetworkFactory() => _client = new HttpClient(_handler);
        public int Requests => _handler.Requests;
        public HttpClient GetHttpClient() => _client;
        public void Dispose() => _client.Dispose();
        private sealed class RefusingHandler : HttpMessageHandler
        {
            public int Requests { get; private set; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests++;
                return Task.FromException<HttpResponseMessage>(new InvalidOperationException("Synthetic cache compatibility must not send HTTP."));
            }
        }
    }
}
