using System.Globalization;
using System.Reflection;
using BuildStandard.Cli;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using Xunit;

namespace BuildStandard.Tests;

public sealed class DoctorConsoleCancellationTests
{
    private sealed class Signals
    {
        internal List<ConsoleCancelEventHandler> Active { get; } = [];
        internal ConsoleCancelEventHandler? Last { get; private set; }
        internal int Added { get; private set; }
        internal int Removed { get; private set; }
        internal bool FailSubscribe { get; init; }
        internal bool FailUnsubscribe { get; init; }
        internal void Add(ConsoleCancelEventHandler handler)
        {
            Added++; Last = handler; Active.Add(handler);
            if (FailSubscribe) throw new ApplicationException(OfflinePackageFixture.Private);
        }
        internal void Remove(ConsoleCancelEventHandler handler)
        {
            Removed++;
            if (FailUnsubscribe) throw new ApplicationException(OfflinePackageFixture.Private);
            Active.Remove(handler);
        }
        internal bool Raise(ConsoleCancelEventHandler? queued = null)
        {
            // The runtime owns this event-argument constructor. Only tests instantiate it to deliver a fake event.
            var args = (ConsoleCancelEventArgs)Activator.CreateInstance(typeof(ConsoleCancelEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic, null, [ConsoleSpecialKey.ControlC], CultureInfo.InvariantCulture)!;
            foreach (var handler in queued is null ? Active.ToArray() : new[] { queued }) handler(null, args);
            return args.Cancel;
        }
        internal DoctorConsoleCancellation Scope() => new(Add, Remove);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(130)]
    public void Lifecycle_preserves_handler_exits_and_flushes_only_after_owned_scope_cleanup(int exit)
    {
        var signals = new Signals(); using var output = new StringWriter(); using var error = new StringWriter();
        var result = DoctorConsoleCancellation.Run(output, error, (token, stagedOutput, stagedError) =>
        {
            Assert.Single(signals.Active); Assert.True(token.CanBeCanceled);
            Assert.Empty(output.ToString()); Assert.Empty(error.ToString());
            stagedOutput.Write("safe result"); stagedError.Write("safe notice");
            return exit;
        }, signals.Scope);
        Assert.Equal(exit, result); Assert.Equal("safe result", output.ToString()); Assert.Equal("safe notice", error.ToString());
        Assert.Equal(1, signals.Added); Assert.Equal(1, signals.Removed); Assert.Empty(signals.Active);
        Assert.False(signals.Raise(signals.Last));
    }

    [Fact]
    public void Ctrl_C_cancels_actual_handler_without_a_second_diagnostic_service_and_returns_130()
    {
        using var fixture = new OfflinePackageFixture(); var signals = new Signals();
        using var output = new StringWriter(); using var error = new StringWriter();
        var result = DoctorConsoleCancellation.Run(output, error, (token, stagedOutput, stagedError) =>
        {
            Assert.True(signals.Raise()); Assert.True(signals.Raise());
            return DoctorCommand.Run(["--format", "json"], fixture.Root, stagedOutput, stagedError, token, fixture.Service());
        }, signals.Scope);
        Assert.Equal(130, result); Assert.Empty(error.ToString());
        var diagnostic = ToolkitJson.Deserialize<OfflinePrerequisiteResult>(output.ToString());
        Assert.Equal(OfflinePrerequisiteStatus.Cancelled, diagnostic.Status);
        Assert.All(diagnostic.Findings.Where(f => f.Applicability == OfflineFindingApplicability.Required), f => Assert.Equal(OfflineFindingDisposition.Unknown, f.Disposition));
        Assert.Empty(signals.Active); Assert.False(signals.Raise(signals.Last)); fixture.NoProbes();
    }

    [Fact]
    public void Callback_failure_is_sanitised_and_does_not_publish_staged_readiness()
    {
        var signals = new Signals(); using var output = new StringWriter(); using var error = new StringWriter();
        var result = DoctorConsoleCancellation.Run(output, error, (token, stagedOutput, _) =>
        {
            using var callback = token.Register(() => throw new ApplicationException(OfflinePackageFixture.Private));
            Assert.True(signals.Raise()); Assert.True(token.IsCancellationRequested);
            stagedOutput.Write("Ready"); return 0;
        }, signals.Scope);
        Assert.Equal(3, result); Assert.Empty(output.ToString()); Assert.Contains("No readiness was certified", error.ToString());
        Assert.DoesNotContain(OfflinePackageFixture.Private, error.ToString()); Assert.Empty(signals.Active);
        Assert.False(signals.Raise(signals.Last));
    }

    [Theory]
    [InlineData("subscribe")]
    [InlineData("unsubscribe")]
    [InlineData("command")]
    public void Setup_command_or_cleanup_private_failure_never_leaks_or_flushes_a_Ready_result(string stage)
    {
        var signals = new Signals { FailSubscribe = stage == "subscribe", FailUnsubscribe = stage == "unsubscribe" };
        using var output = new StringWriter(); using var error = new StringWriter();
        var result = DoctorConsoleCancellation.Run(output, error, (_, stagedOutput, _) =>
        {
            stagedOutput.Write("Ready");
            if (stage == "command") throw new ApplicationException(OfflinePackageFixture.Private);
            return 0;
        }, signals.Scope);
        Assert.Equal(3, result); Assert.Empty(output.ToString()); Assert.Contains("No readiness was certified", error.ToString());
        Assert.DoesNotContain(OfflinePackageFixture.Private, error.ToString()); Assert.Equal(1, signals.Removed);
        // Even an event whose removal failed is inert after disposal and cannot swallow a later command's Ctrl+C.
        Assert.False(signals.Raise(signals.Last));
    }

    [Fact]
    public void Queued_event_after_disposal_is_inert_and_scope_disposal_is_idempotent()
    {
        var signals = new Signals(); var scope = signals.Scope(); var token = scope.Token;
        scope.Dispose(); scope.Dispose();
        Assert.Equal(1, signals.Removed); Assert.False(signals.Raise(signals.Last));
        Assert.False(token.IsCancellationRequested); Assert.False(scope.Failed);
    }

    [Fact]
    public async Task Concurrent_cancellation_and_disposal_complete_without_a_disposed_source_callback_or_late_suppression()
    {
        var signals = new Signals(); var scope = signals.Scope();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var attemptedDispose = new ManualResetEventSlim();
        using var callback = scope.Token.Register(() => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); });
        var cancel = Task.Run(() => signals.Raise());
        Task? dispose = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            dispose = Task.Run(() => { attemptedDispose.Set(); scope.Dispose(); });
            Assert.True(attemptedDispose.Wait(TimeSpan.FromSeconds(10)));
        }
        finally { release.Set(); }
        Assert.True(await cancel);
        if (dispose is not null) await dispose;
        Assert.False(scope.Failed); Assert.True(scope.Token.IsCancellationRequested);
        Assert.Empty(signals.Active); Assert.False(signals.Raise(signals.Last));
    }

    [Fact]
    public void Output_failure_or_unusable_stderr_cannot_escape_into_general_raw_exception_handler()
    {
        var signals = new Signals(); using var output = new PrivateWriter(); using var error = new PrivateWriter();
        var result = DoctorConsoleCancellation.Run(output, error, (_, stagedOutput, _) => { stagedOutput.Write("safe result"); return 0; }, signals.Scope);
        Assert.Equal(3, result); Assert.Empty(signals.Active);
    }

    private sealed class PrivateWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(string? value) => throw new IOException(OfflinePackageFixture.Private);
        public override void WriteLine(string? value) => throw new IOException(OfflinePackageFixture.Private);
    }
}
