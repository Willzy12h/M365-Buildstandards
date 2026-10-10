using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace BDIT.TenantToolkit.Graph.Setup;

/// <summary>A correlated browser response only. Actual grants must always be read again from Graph.</summary>
public sealed record AdminConsentCallbackResult(bool ApprovalReported, string Message);

/// <summary>
/// Temporary callback at the exact registered web redirect. Requires no URL reservation or administrator rights.
/// Separate from MSAL's native sign-in callback; no port-matching exemption is assumed for admin consent.
/// No token endpoint, consent grant or tenant write is called by this helper.
/// </summary>
public sealed class AdminConsentCallback : IDisposable
{
    private const int MaximumHeaderBytes = 16 * 1024;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationTokenRegistration _expiryRegistration;
    private readonly string _tenantId;
    private readonly byte[] _state;
    private int _started;
    private int _disposed;

    public Uri ConsentUri { get; }
    public Uri RedirectUri { get; }

    private AdminConsentCallback(string tenantId, string clientId, IEnumerable<string> explicitRequiredScopes)
    {
        _tenantId = tenantId;
        _state = Encoding.ASCII.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        // Validate IDs and exact scopes before reserving a local port.
        _ = ApplicationSetupService.AdminConsentUri(tenantId, clientId, explicitRequiredScopes);
        _lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _listener = new TcpListener(IPAddress.Loopback, new Uri(SetupRegistration.ConsentRedirect).Port) { ExclusiveAddressUse = true };
        try
        {
            _listener.Start(4);
            RedirectUri = new Uri(SetupRegistration.ConsentRedirect);
            ConsentUri = ApplicationSetupService.BuildAdminConsentUri(tenantId, clientId, explicitRequiredScopes, RedirectUri.AbsoluteUri, Encoding.ASCII.GetString(_state));
            _expiryRegistration = _lifetime.Token.Register(static listener => ((TcpListener)listener!).Stop(), _listener);
        }
        catch (SocketException ex)
        {
            _listener.Stop(); _lifetime.Dispose();
            throw new BDIT.TenantToolkit.Core.ConfigurationException("The registered consent callback port 8400 is unavailable. Close another tool consent window, then retry. Check again can still read the existing grants.", ex);
        }
        catch { _listener.Stop(); _lifetime.Dispose(); throw; }
    }

    public static AdminConsentCallback Create(string tenantId, string clientId, IEnumerable<string> explicitRequiredScopes)
    {
        ArgumentNullException.ThrowIfNull(explicitRequiredScopes);
        return new(tenantId, clientId, explicitRequiredScopes.ToArray());
    }

    /// <summary>Waits up to five minutes from creation. Cancellation or disposal closes the local listener.</summary>
    public async Task<AdminConsentCallbackResult> WaitAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("This approval callback has already been awaited.");
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        try
        {
            while (true)
            {
                wait.Token.ThrowIfCancellationRequested();
                using var client = await _listener.AcceptTcpClientAsync(wait.Token).ConfigureAwait(false);
                using var request = CancellationTokenSource.CreateLinkedTokenSource(wait.Token);
                request.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    using var stream = client.GetStream();
                    var header = await ReadHeadersAsync(stream, request.Token).ConfigureAwait(false);
                    var result = ParseCallback(header);
                    try
                    {
                        await RespondAsync(stream, result, request.Token).ConfigureAwait(false);
                        // Deliver a FIN after the complete response before disposing the socket.
                        // Immediate close with unread peer bytes can reset an otherwise valid callback.
                        client.Client.Shutdown(SocketShutdown.Send);
                        using var drain = CancellationTokenSource.CreateLinkedTokenSource(request.Token);
                        drain.CancelAfter(TimeSpan.FromMilliseconds(250));
                        var remaining = new byte[1024];
                        try { await stream.ReadAsync(remaining, drain.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (!request.IsCancellationRequested) { }
                    }
                    catch (Exception ex) when (result is not null && !wait.IsCancellationRequested && (ex is IOException or SocketException or OperationCanceledException))
                    {
                        // Correlation is already complete. Closing the browser must not discard its response.
                    }
                    wait.Token.ThrowIfCancellationRequested();
                    if (result is not null) return result;
                }
                catch (OperationCanceledException) when (!wait.IsCancellationRequested) { }
                catch (IOException) { }
                catch (SocketException) { }
            }
        }
        catch (Exception ex) when (wait.IsCancellationRequested && (ex is OperationCanceledException or SocketException or ObjectDisposedException or InvalidOperationException))
        {
            if (!ct.IsCancellationRequested && Volatile.Read(ref _disposed) == 0 && _lifetime.IsCancellationRequested)
                throw new TimeoutException("The browser approval response did not arrive within five minutes. Return to application setup to validate existing grants or open approval again.");
            throw new OperationCanceledException(wait.Token);
        }
        finally { Dispose(); }
    }

    private AdminConsentCallbackResult? ParseCallback(string? header)
    {
        if (header is null) return null;
        var lines = header.Split("\r\n", StringSplitOptions.None);
        var first = lines[0].Split(' ');
        if (first.Length != 3 || first[0] != "GET" || first[2] is not ("HTTP/1.0" or "HTTP/1.1")) return null;
        var prefix = RedirectUri.AbsolutePath + "?";
        if (!first[1].StartsWith(prefix, StringComparison.Ordinal) || first[1].Contains('#')) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0 || !headers.TryAdd(line[..colon], line[(colon + 1)..].Trim())) return null;
        }
        if (!headers.TryGetValue("Host", out var host) || !string.Equals(host, RedirectUri.Authority, StringComparison.OrdinalIgnoreCase)) return null;
        if (headers.ContainsKey("Transfer-Encoding") || (headers.TryGetValue("Content-Length", out var length) && length != "0")) return null;
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in first[1][prefix.Length..].Split('&'))
        {
            var equals = pair.IndexOf('=');
            if (equals <= 0) return null;
            string key, value;
            try { key = Uri.UnescapeDataString(pair[..equals].Replace('+', ' ')); value = Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' ')); }
            catch (UriFormatException) { return null; }
            if (!query.TryAdd(key, value)) return null;
        }
        // Admin consent returns metadata, never tokens or an authorisation code. Never echo any query value.
        if (query.Keys.Any(k => k is not ("state" or "tenant" or "admin_consent" or "scope" or "error" or "error_description" or "error_uri"))) return null;
        if (!query.TryGetValue("state", out var state) || state.Length != _state.Length
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(state), _state)) return null;
        if (query.TryGetValue("tenant", out var tenant) && !string.Equals(tenant, _tenantId, StringComparison.OrdinalIgnoreCase)) return null;
        if (query.ContainsKey("error")) return new(false, "Microsoft returned an unsuccessful approval response. Review administrator access and the displayed permissions, then validate grants or try approval again.");
        if (!query.ContainsKey("tenant") || !query.TryGetValue("admin_consent", out var approval) || !string.Equals(approval, "True", StringComparison.OrdinalIgnoreCase)) return null;
        return new(true, "The browser reported approval for this tenant. Graph validation is still required to confirm the actual grants.");
    }

    private static async Task<string?> ReadHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var bytes = new byte[MaximumHeaderBytes];
        var length = 0;
        while (length < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(length), ct).ConfigureAwait(false);
            if (read == 0) return null;
            length += read;
            for (var i = Math.Max(0, length - read - 3); i + 3 < length; i++)
                if (bytes[i] == 13 && bytes[i + 1] == 10 && bytes[i + 2] == 13 && bytes[i + 3] == 10)
                {
                    for (var j = 0; j < i; j++) if (bytes[j] > 127) return null;
                    return Encoding.ASCII.GetString(bytes, 0, i);
                }
        }
        return null;
    }

    internal static async Task RespondAsync(Stream stream, AdminConsentCallbackResult? result, CancellationToken ct)
    {
        var title = result is null ? "Approval response not accepted" : "Return to the M365 Toolkit";
        var message = result is null ? "This local request did not match the active approval request. Continue in the Microsoft approval window."
            : result.ApprovalReported ? "The approval response has been received. Return to the toolkit while it checks the actual permissions in Microsoft Graph."
            : "Microsoft did not report successful approval. Return to the toolkit to review the next step.";
        const string style = "*{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;padding:32px;background:#0b1930;color:#14243a;font-family:Segoe UI,Arial,sans-serif;line-height:1.6}main{width:100%;max-width:640px;border-top:6px solid #2196dc;border-radius:12px;background:#fff;padding:40px;box-shadow:0 20px 60px #0004}.brand{margin:0 0 24px;color:#126caa;font-size:13px;font-weight:700;letter-spacing:1.8px}h1{margin:0 0 18px;font-size:28px;line-height:1.25}p{margin:0 0 18px}.foot{margin:28px 0 0;padding-top:18px;border-top:1px solid #dce5ee;color:#536478;font-size:14px}@media(max-width:480px){body{padding:16px}main{padding:28px}h1{font-size:24px}}";
        var body = Encoding.UTF8.GetBytes($"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>{title}</title><style>{style}</style></head><body><main><p class=\"brand\">M365 BUILDSTANDARD · M365 TOOLKIT</p><h1>{title}</h1><p>{message}</p><p class=\"foot\">You can close this browser tab.</p></main></body></html>");
        var status = result is null ? "400 Bad Request" : "200 OK";
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store, no-transform\r\nPragma: no-cache\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _listener.Stop();
        _expiryRegistration.Dispose();
        _lifetime.Dispose();
    }
}
