using System.Net;
using System.Net.Sockets;
using System.Text;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Graph.Setup;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>Synthetic localhost traffic only. No Microsoft sign-in, browser or tenant calls.</summary>
public sealed class AdminConsentCallbackTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string Client = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public void UriUsesExplicitTenantScopesAndRandomStateWithNativeLocalhostRedirect()
    {
        using var first = Create();
        using var second = Create();
        Assert.Equal("login.microsoftonline.com", first.ConsentUri.Host);
        Assert.Equal($"/{Tenant}/v2.0/adminconsent", first.ConsentUri.AbsolutePath);
        var query = Query(first.ConsentUri);
        Assert.Equal(Client, query["client_id"]);
        Assert.Equal("https://graph.microsoft.com/User.Read https://graph.microsoft.com/Directory.Read.All", query["scope"]);
        Assert.Equal(first.RedirectUri.AbsoluteUri, query["redirect_uri"]);
        Assert.Equal("localhost", first.RedirectUri.Host);
        Assert.NotEqual(80, first.RedirectUri.Port);
        Assert.Equal(64, query["state"].Length);
        Assert.NotEqual(query["state"], Query(second.ConsentUri)["state"]);
        Assert.Throws<ConfigurationException>(() => AdminConsentCallback.Create("common", Client, new[] { "User.Read" }));
        Assert.Throws<ConfigurationException>(() => AdminConsentCallback.Create(Tenant, Client, new[] { ".default" }));
    }

    [Fact]
    public async Task CorrelatedApprovalCompletesWithNoClaimOfVerifiedGrants()
    {
        using var callback = Create();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var wait = callback.WaitAsync(timeout.Token);
        var response = await Send(callback, Success(callback), timeout.Token);
        var result = await wait;
        Assert.True(result.ApprovalReported);
        Assert.Contains("Graph validation is still required", result.Message);
        Assert.StartsWith("HTTP/1.1 200 OK", response);
        Assert.Contains("actual permissions", response);
        Assert.Contains("Cache-Control: no-store", response);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => callback.WaitAsync(timeout.Token));
    }

    [Fact]
    public async Task WrongStateWrongTenantDuplicateStateAndPostDoNotCompleteApproval()
    {
        using var callback = Create();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var wait = callback.WaitAsync(timeout.Token);
        var good = Success(callback);
        var badRequests = new[]
        {
            good.Replace(Query(callback.ConsentUri)["state"], new string('A', 64), StringComparison.Ordinal),
            good.Replace(Tenant, Client, StringComparison.Ordinal),
            good + "&state=" + Query(callback.ConsentUri)["state"],
            good + "&access_token=must-never-appear"
        };
        foreach (var query in badRequests)
        {
            await AssertRejectedAsync(callback, query, timeout.Token);
            Assert.False(wait.IsCompleted);
        }
        await AssertRejectedAsync(callback, good, timeout.Token, "POST");
        Assert.False(wait.IsCompleted);
        Assert.StartsWith("HTTP/1.1 200 OK", await Send(callback, good, timeout.Token));
        Assert.True((await wait).ApprovalReported);
    }

    [Fact]
    public async Task DeniedResponseDoesNotReflectBrowserSuppliedErrorText()
    {
        using var callback = Create();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var wait = callback.WaitAsync(timeout.Token);
        var query = "state=" + Query(callback.ConsentUri)["state"] + "&error=access_denied&error_description=%3Cscript%3Euntrusted%3C%2Fscript%3E";
        var response = await Send(callback, query, timeout.Token);
        var result = await wait;
        Assert.False(result.ApprovalReported);
        Assert.DoesNotContain("untrusted", result.Message);
        Assert.StartsWith("HTTP/1.1 200 OK", response);
    }

    [Fact]
    public async Task GeneratedResponseNeverReflectsMetadataAndHasFixedSecurityHeadersAndBranding()
    {
        // Inspect bytes emitted by our writer before local HTTP filtering software can alter them.
        const string untrusted = "state=synthetic-state&access_token=must-never-appear&error_description=<script>untrusted</script>";
        foreach (var result in new AdminConsentCallbackResult?[] { new(true, untrusted), new(false, untrusted), null })
        {
            using var stream = new MemoryStream();
            await AdminConsentCallback.RespondAsync(stream, result, CancellationToken.None);
            var response = Encoding.UTF8.GetString(stream.ToArray());
            Assert.StartsWith(result is null ? "HTTP/1.1 400 Bad Request" : "HTTP/1.1 200 OK", response);
            Assert.DoesNotContain("synthetic-state", response);
            Assert.DoesNotContain("must-never-appear", response);
            Assert.DoesNotContain("untrusted", response);
            Assert.DoesNotContain("<script", response);
            Assert.Contains("Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'", response);
            Assert.Contains("Cache-Control: no-store, no-transform", response);
            Assert.Contains("Referrer-Policy: no-referrer", response);
            Assert.Contains("X-Content-Type-Options: nosniff", response);
            Assert.Contains("BLUE DIAMOND IT", response);
            Assert.Contains("Segoe UI", response);
        }
    }

    [Fact]
    public async Task CancellationAndDisposalEndWaitingAndReleaseListener()
    {
        using var cancelled = Create();
        using var ct = new CancellationTokenSource();
        var pending = cancelled.WaitAsync(ct.Token);
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        using var connection = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => connection.ConnectAsync(IPAddress.Loopback, cancelled.RedirectUri.Port));

        using var disposed = Create();
        var other = disposed.WaitAsync(CancellationToken.None);
        disposed.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => other);
    }

    [Fact]
    public async Task OversizedRequestIsRejectedAndValidRequestCanStillComplete()
    {
        using var callback = Create();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var wait = callback.WaitAsync(timeout.Token);
        await AssertRejectedAsync(callback, "state=" + new string('x', 17 * 1024), timeout.Token);
        Assert.False(wait.IsCompleted);
        Assert.StartsWith("HTTP/1.1 200 OK", await Send(callback, Success(callback), timeout.Token));
        Assert.True((await wait).ApprovalReported);
    }

    [Fact]
    public async Task ValidCallbackStillCompletesWhenBrowserClosesWithoutReadingHtml()
    {
        using var callback = Create();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var wait = callback.WaitAsync(timeout.Token);
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, callback.RedirectUri.Port, timeout.Token);
            using var stream = client.GetStream();
            var request = Encoding.ASCII.GetBytes($"GET /?{Success(callback)} HTTP/1.1\r\nHost: {callback.RedirectUri.Authority}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(request, timeout.Token);
            client.Client.Shutdown(SocketShutdown.Send);
        }
        Assert.True((await wait).ApprovalReported);
    }

    private static AdminConsentCallback Create() => AdminConsentCallback.Create(Tenant, Client, new[] { "User.Read", "Directory.Read.All" });
    private static string Success(AdminConsentCallback callback) => "admin_consent=True&tenant=" + Tenant + "&state=" + Query(callback.ConsentUri)["state"];
    private static Dictionary<string, string> Query(Uri uri) => uri.Query[1..].Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
    private static async Task AssertRejectedAsync(AdminConsentCallback callback, string query, CancellationToken ct, string method = "GET")
    {
        try { Assert.StartsWith("HTTP/1.1 400 Bad Request", await Send(callback, query, ct, method)); }
        catch (Exception ex) when ((ex is IOException or HttpRequestException) && IsResetOrAbort(ex))
        {
            // Rejected input may be closed by TCP or local HTTP filtering before 400 reaches the client.
            // Callers must still prove approval is pending and the next valid callback succeeds with 200.
        }
    }
    private static bool IsResetOrAbort(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is SocketException { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted }) return true;
        return false;
    }
    private static async Task<string> Send(AdminConsentCallback callback, string query, CancellationToken ct, string method = "GET")
    {
        // Honour Content-Length/chunked completion instead of requiring TCP EOF after a complete response.
        // Local filtering software can rewrite framing; incomplete valid responses must still throw.
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false, AllowAutoRedirect = false,
            ConnectCallback = async (_, token) =>
            {
                // The listener binds IPv4 loopback. Avoid repeated IPv6 fallback delays in this bounded test.
                // The request URI and Host header remain localhost and are still validated by production code.
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(IPAddress.Loopback, callback.RedirectUri.Port, token); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            }
        };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(callback.RedirectUri, "?" + query))
        { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        request.Headers.ConnectionClose = true;
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        return $"HTTP/{response.Version} {(int)response.StatusCode} {response.ReasonPhrase}\r\n{response.Headers}{response.Content.Headers}\r\n{await response.Content.ReadAsStringAsync(ct)}";
    }
}
