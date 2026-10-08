using BDIT.TenantToolkit.Graph.Auth;
using Microsoft.Identity.Client;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ExplicitConnectAcquisitionTests
{
    [Theory]
    [InlineData(true, 1, 1, 0)]
    [InlineData(true, 0, 0, 1)]
    [InlineData(true, 2, 0, 1)]
    [InlineData(false, 1, 0, 1)]
    public async Task Prompt_counts_preserve_known_account_reuse_without_ambiguous_selection(bool known, int accounts, int expectedSilent, int expectedInteractive)
    {
        var silent = 0; var prompts = 0;
        var result = await ExplicitConnectAcquisition.AcquireAsync(known, Enumerable.Range(0, accounts).ToArray(),
            account => { silent++; return Task.FromResult("cached"); },
            reason => { prompts++; return Task.FromResult("selected"); }, CancellationToken.None);
        Assert.Equal(expectedSilent, silent); Assert.Equal(expectedInteractive, prompts);
        Assert.Equal(expectedSilent > 0 ? "cached" : "selected", result);
    }

    [Fact]
    public async Task Expired_or_interaction_required_access_prompts_once_at_explicit_connect()
    {
        var silent = 0; var prompts = 0;
        var result = await ExplicitConnectAcquisition.AcquireAsync(true, new[] { "confirmed" },
            account => { silent++; throw new MsalUiRequiredException("interaction_required", "synthetic"); },
            reason => { prompts++; Assert.Equal(ConnectInteractionReason.MicrosoftInteractionRequired, reason); return Task.FromResult("selected"); }, CancellationToken.None);
        Assert.Equal("selected", result); Assert.Equal(1, silent); Assert.Equal(1, prompts);
    }

    [Fact]
    public async Task Cancellation_after_silent_attempt_never_opens_an_interactive_prompt()
    {
        using var stop = new CancellationTokenSource(); var prompts = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExplicitConnectAcquisition.AcquireAsync(true, new[] { "confirmed" },
            account => { stop.Cancel(); throw new MsalUiRequiredException("interaction_required", "synthetic"); },
            reason => { prompts++; return Task.FromResult("selected"); }, stop.Token));
        Assert.Equal(0, prompts);
    }

    [Fact]
    public async Task A_silent_failure_other_than_interaction_required_is_not_retried_interactively()
    {
        var prompts = 0;
        await Assert.ThrowsAsync<MsalServiceException>(() => ExplicitConnectAcquisition.AcquireAsync(true, new[] { "confirmed" },
            account => throw new MsalServiceException("service_unavailable", "synthetic"),
            reason => { prompts++; return Task.FromResult("selected"); }, CancellationToken.None));
        Assert.Equal(0, prompts);
    }

    [Fact]
    public async Task Cancelling_interactive_selection_does_not_retry_it()
    {
        var prompts = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExplicitConnectAcquisition.AcquireAsync(false, Array.Empty<string>(),
            account => Task.FromResult("unused"),
            reason => { prompts++; throw new OperationCanceledException(); }, CancellationToken.None));
        Assert.Equal(1, prompts);
    }
}
