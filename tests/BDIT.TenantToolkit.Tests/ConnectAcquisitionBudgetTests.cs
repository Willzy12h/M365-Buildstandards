using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Graph.Auth;
using Microsoft.Identity.Client;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ConnectAcquisitionBudgetTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Reading_permissions_does_not_consume_the_Microsoft_acquisition_budget(bool knownAccount)
    {
        var clock = new ManualTime(); var prompts = 0; var previews = 0;
        var result = await ExplicitConnectAcquisition.AcquireAsync(knownAccount, new[] { "known" },
            (_, token) => { clock.Advance(TimeSpan.FromMinutes(1)); throw new MsalUiRequiredException("interaction_required", "synthetic"); },
            (_, token) => { token.ThrowIfCancellationRequested(); prompts++; clock.Advance(TimeSpan.FromMinutes(3)); token.ThrowIfCancellationRequested(); return Task.FromResult("connected"); },
            CancellationToken.None,
            token => { previews++; clock.Advance(TimeSpan.FromMinutes(20)); token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            Budget, clock);
        Assert.Equal("connected", result); Assert.Equal(1, prompts); Assert.Equal(1, previews);
    }

    [Fact]
    public async Task The_silent_and_interactive_attempts_share_one_budget_after_a_long_preview()
    {
        var clock = new ManualTime(); var prompts = 0;
        await Assert.ThrowsAsync<AuthenticationRequiredException>(() => ExplicitConnectAcquisition.AcquireAsync(true, new[] { "known" },
            (_, token) => { clock.Advance(TimeSpan.FromMinutes(4)); throw new MsalUiRequiredException("interaction_required", "synthetic"); },
            (_, token) => { prompts++; clock.Advance(TimeSpan.FromMinutes(2)); token.ThrowIfCancellationRequested(); return Task.FromResult("too late"); },
            CancellationToken.None, token => { clock.Advance(TimeSpan.FromMinutes(20)); return Task.CompletedTask; }, Budget, clock));
        Assert.Equal(1, prompts);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Timed_out_acquisition_never_returns_a_token_or_retries(bool silent)
    {
        var clock = new ManualTime(); var prompts = 0; var previews = 0;
        await Assert.ThrowsAsync<AuthenticationRequiredException>(() => ExplicitConnectAcquisition.AcquireAsync(silent, new[] { "known" },
            (_, token) => { clock.Advance(Budget); return Task.FromResult("expired silent"); },
            (_, token) => { prompts++; clock.Advance(Budget); return Task.FromResult("expired interactive"); },
            CancellationToken.None, token => { previews++; return Task.CompletedTask; }, Budget, clock));
        Assert.Equal(silent ? 0 : 1, prompts); Assert.Equal(silent ? 0 : 1, previews);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Cancelled_or_declined_preview_remains_cancellation_with_no_interaction(bool decline)
    {
        var clock = new ManualTime(); using var stop = new CancellationTokenSource(); var prompts = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExplicitConnectAcquisition.AcquireAsync(false, Array.Empty<string>(),
            (_, token) => Task.FromResult("unused"), (_, token) => { prompts++; return Task.FromResult("not allowed"); }, stop.Token,
            token => { clock.Advance(TimeSpan.FromMinutes(20)); if (decline) throw new OperationCanceledException("Permission review declined"); stop.Cancel(); return Task.CompletedTask; },
            Budget, clock));
        Assert.Equal(0, prompts);
    }

    [Fact]
    public async Task Cancellation_during_acquisition_remains_cancellation_even_at_its_deadline()
    {
        var clock = new ManualTime(); using var stop = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExplicitConnectAcquisition.AcquireAsync(false, Array.Empty<string>(),
            (_, token) => Task.FromResult("unused"), (_, token) => { stop.Cancel(); clock.Advance(Budget); token.ThrowIfCancellationRequested(); return Task.FromResult("not allowed"); },
            stop.Token, acquisitionTimeout: Budget, timeProvider: clock));
    }

    // Deterministic acquisition time and timers: no network, wall-clock sleeps or additional test package.
    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;
        private readonly List<Timer> _timers = [];
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state); _timers.Add(timer); timer.Change(dueTime, period); return timer;
        }
        public void Advance(TimeSpan time)
        {
            _ticks += time.Ticks;
            foreach (var timer in _timers.ToArray()) timer.FireIfDue();
        }
        private sealed class Timer(ManualTime owner, TimerCallback callback, object? state) : ITimer
        {
            private long _due = long.MaxValue;
            private bool _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
                if (_disposed) return false;
                _due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner._ticks + dueTime.Ticks;
                return true;
            }
            public void FireIfDue()
            {
                if (_disposed || _due > owner._ticks) return;
                _due = long.MaxValue; callback(state);
            }
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
