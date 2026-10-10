using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class GraphInterruptionValidationTests
{
    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task An_operator_stop_interrupts_real_backoff_without_acquiring_another_token(int status)
    {
        using var stop = new CancellationTokenSource();
        using var handler = new Handler((_, _) => Task.FromResult(Response(status)));
        using var http = new HttpClient(handler);
        var tokens = new Tokens();
        var log = new BackoffLog();
        var client = Client(http, tokens, true, log);
        var operation = client.GetAsync(GraphApi.V1, "/organization", stop.Token);
        await log.Observed.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(handler.Paths);
        Assert.False(operation.IsCompleted); // Real 30-second Delay; no sleeps or elapsed-time pass threshold.
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Single(handler.Paths);
        Assert.Equal(1, tokens.Calls);
        Assert.Equal(0, tokens.Renewals);
    }

    [Fact]
    public async Task Cancelling_a_throttled_second_page_preserves_the_returned_row_without_replaying_page_one()
    {
        using var stop = new CancellationTokenSource();
        var page = 0;
        using var handler = new Handler((_, _) => Task.FromResult(++page == 1
            ? Response(200, """{"value":[{"id":"synthetic-first"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/auditLogs/signIns?$skiptoken=synthetic-page2"}""")
            : Response(429)));
        using var http = new HttpClient(handler);
        var tokens = new Tokens();
        var log = new BackoffLog();
        var report = Client(http, tokens, true, log).ForReports();
        await using var iterator = report.GetBoundedAsync(GraphApi.V1, "/auditLogs/signIns", 5000, stop.Token).GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        var first = iterator.Current;
        var second = iterator.MoveNextAsync().AsTask();
        await log.Observed.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, handler.Paths.Count);
        Assert.False(second.IsCompleted);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("synthetic-first", first["id"]!.GetValue<string>());
        Assert.Equal(new[] { "/v1.0/auditLogs/signIns", "/v1.0/auditLogs/signIns?$skiptoken=synthetic-page2" }, handler.Paths);
        Assert.Equal(2, tokens.Calls);
    }

    [Fact]
    public async Task A_transient_failure_between_two_401_responses_does_not_reset_the_single_silent_renewal()
    {
        var statuses = new Queue<int>([401, 503, 401]);
        using var handler = new Handler((_, _) => Task.FromResult(Response(statuses.Dequeue())));
        using var http = new HttpClient(handler);
        var tokens = new Tokens();
        await Assert.ThrowsAsync<AuthenticationRequiredException>(() => Client(http, tokens, false)
            .GetAsync(GraphApi.V1, "/organization", CancellationToken.None));
        Assert.Equal(3, handler.Paths.Count);
        Assert.Equal(3, tokens.Calls);
        Assert.Equal(1, tokens.Renewals);
        Assert.Empty(statuses);
    }

    [Fact]
    public async Task Cancelling_after_write_dispatch_remains_ambiguous_and_sends_exactly_once()
    {
        using var stop = new CancellationTokenSource();
        using var handler = new Handler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("Synthetic dispatch cannot return success.");
        });
        using var http = new HttpClient(handler);
        var tokens = new Tokens();
        var operation = Client(http, tokens, false).WriteAsync(GraphApi.V1, GraphWriteMethod.Post,
            "/identity/conditionalAccess/policies", new JsonObject { ["displayName"] = "Synthetic candidate", ["state"] = "disabled" }, stop.Token);
        await handler.Dispatched.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(handler.Paths);
        Assert.False(operation.IsCompleted);
        stop.Cancel();
        await Assert.ThrowsAsync<AmbiguousWriteException>(() => operation.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Single(handler.Paths);
        Assert.Equal(1, tokens.Calls);
        Assert.Equal(new[] { HttpMethod.Post }, handler.Methods);
    }

    private static GraphClient Client(HttpClient http, Tokens tokens, bool sleep, IToolkitLog? log = null) => new(http, tokens,
        TestData.TenantA, SessionMode.Deployment, GraphRouteAllowList.FromStandard(TestData.Standard()),
        new GraphClientOptions { Sleep = sleep, MaxRetryAfter = TimeSpan.FromSeconds(30), WriteTimeout = TimeSpan.FromMinutes(1) }, log ?? NullLog.Instance);

    private static HttpResponseMessage Response(int status, string body = "{}")
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (status is 429 or 503) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
        return response;
    }

    private sealed class Tokens : IAccessTokenProvider
    {
        public int Calls { get; private set; }
        public int Renewals { get; private set; }
        public Task<string> GetAccessTokenAsync(CancellationToken ct) => GetAccessTokenAsync(false, ct);
        public async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
        {
            await Task.Yield(); // Explicitly exercise a provider that cannot dispatch synchronously.
            ct.ThrowIfCancellationRequested();
            Calls++;
            if (forceRefresh) Renewals++;
            return "synthetic-offline-token";
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _dispatched = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Dispatched => _dispatched.Task;
        public List<string> Paths { get; } = [];
        public List<HttpMethod> Methods { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            Methods.Add(request.Method);
            var response = respond(request, cancellationToken);
            _dispatched.TrySetResult();
            return response;
        }
    }

    private sealed class BackoffLog : IToolkitLog
    {
        private readonly TaskCompletionSource _observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Observed => _observed.Task;
        public void Log(LogLevel level, string category, string message, string? tenantId = null,
            string? controlId = null, Exception? exception = null)
        {
            // Wait past the read's cancellation checks, at the observable decision to back off.
            // A cancelled dispatched request alone would not prove cancellation inside the delay.
            if (level == LogLevel.Warning && category == "Graph"
                && (message.Contains("throttled (429)", StringComparison.Ordinal)
                    || message.Contains("retrying after", StringComparison.Ordinal)))
                _observed.TrySetResult();
        }
    }
}
