using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Graph;
using BuildStandard.Graph.Auth;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ServiceSignInTimeoutTests
{
    private sealed class AcquisitionCapturedException : Exception;

    private sealed class RefuseHttp : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            throw new InvalidOperationException("Synthetic timeout mapping check must stop before HTTP.");
        }
    }

    [Theory]
    [InlineData(1, SessionMode.Assessment)]
    [InlineData(9, SessionMode.Assessment)]
    [InlineData(15, SessionMode.Assessment)]
    [InlineData(1, SessionMode.Deployment)]
    [InlineData(9, SessionMode.Deployment)]
    [InlineData(15, SessionMode.Deployment)]
    public async Task Connection_maps_configured_minutes_to_the_actual_sign_in_request(int minutes, SessionMode mode)
    {
        using var root = new TempRoot();
        using var handler = new RefuseHttp();
        using var http = new HttpClient(handler);
        using var stop = new CancellationTokenSource();
        var settings = ToolkitSettings.Validate(new ToolkitSettings { SignInTimeoutMinutes = minutes,
            DeploymentClientId = TestData.ClientId });
        var service = new TenantConnectionService(settings, root.Paths, http, NullLog.Instance, "synthetic");
        var profile = new TenantProfile { TenantId = TestData.TenantA, Company = "Synthetic client" };
        SignInRequest? captured = null;
        Task<MsalAuthenticator> Capture(SignInRequest request, IToolkitLog log, CancellationToken token)
        {
            captured = request;
            Assert.Same(NullLog.Instance, log);
            Assert.Equal(stop.Token, token);
            throw new AcquisitionCapturedException();
        }

        await Assert.ThrowsAsync<AcquisitionCapturedException>(() => service.ConnectAsync(profile, mode,
            new StandardCatalogue(), null, stop.Token, null, null, Capture));

        Assert.NotNull(captured);
        Assert.Equal(TimeSpan.FromMinutes(minutes), captured.Timeout);
        Assert.Equal(mode, captured.Mode);
        Assert.Equal(TestData.TenantA, captured.TenantId);
        Assert.Equal(0, handler.Requests);
        Assert.False(File.Exists(captured.CacheFile));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(9, false)]
    [InlineData(15, false)]
    [InlineData(1, true)]
    [InlineData(9, true)]
    [InlineData(15, true)]
    public async Task Both_discovery_services_map_configured_minutes_to_acquisition(int minutes, bool retained)
    {
        using var handler = new RefuseHttp();
        using var http = new HttpClient(handler);
        using var stop = new CancellationTokenSource();
        var settings = ToolkitSettings.Validate(new ToolkitSettings { SignInTimeoutMinutes = minutes });
        TimeSpan? captured = null;
        var calls = 0;
        Task<MsalAuthenticator> Capture(TimeSpan timeout, CancellationToken token)
        {
            calls++;
            captured = timeout;
            Assert.Equal(stop.Token, token);
            throw new AcquisitionCapturedException();
        }

        if (retained)
            await Assert.ThrowsAsync<AcquisitionCapturedException>(() => TenantDiscoveryService.DiscoverRetainedAsync(
                http, settings, new StandardCatalogue(), new[] { "User.Read", "Organization.Read.All" },
                NullLog.Instance, stop.Token, Capture));
        else
            await Assert.ThrowsAsync<AcquisitionCapturedException>(() => TenantDiscoveryService.DiscoverAsync(
                http, settings, NullLog.Instance, stop.Token, Capture));

        Assert.Equal(TimeSpan.FromMinutes(minutes), captured);
        Assert.Equal(1, calls);
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_fallback_still_refuses_both_discovery_routes_before_acquisition(bool retained)
    {
        using var http = new HttpClient();
        var settings = new ToolkitSettings { AllowMicrosoftGraphPowerShellFallback = false };
        var calls = 0;
        Task<MsalAuthenticator> Capture(TimeSpan timeout, CancellationToken token)
        { calls++; throw new AcquisitionCapturedException(); }
        if (retained)
            await Assert.ThrowsAsync<ConfigurationException>(() => TenantDiscoveryService.DiscoverRetainedAsync(http,
                settings, new StandardCatalogue(), new[] { "User.Read" }, NullLog.Instance, CancellationToken.None, Capture));
        else
            await Assert.ThrowsAsync<ConfigurationException>(() => TenantDiscoveryService.DiscoverAsync(http,
                settings, NullLog.Instance, CancellationToken.None, Capture));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("Policy.ReadWrite.ConditionalAccess")]
    [InlineData("EWS.AccessAsUser.All")]
    public async Task Retained_discovery_still_refuses_write_scopes_before_acquisition(string scope)
    {
        using var http = new HttpClient();
        var calls = 0;
        Task<MsalAuthenticator> Capture(TimeSpan timeout, CancellationToken token)
        { calls++; throw new AcquisitionCapturedException(); }
        await Assert.ThrowsAsync<ConfigurationException>(() => TenantDiscoveryService.DiscoverRetainedAsync(http,
            new ToolkitSettings(), new StandardCatalogue(), new[] { scope }, NullLog.Instance, CancellationToken.None, Capture));
        Assert.Equal(0, calls);
    }
}
