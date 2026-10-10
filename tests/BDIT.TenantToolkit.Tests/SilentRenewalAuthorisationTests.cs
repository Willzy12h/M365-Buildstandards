using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Graph.Auth;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class SilentRenewalAuthorisationTests
{
    private sealed record Reply(string Tenant, string Account, string ObjectId, string[] Scopes);
    private static Reply Valid() => new("tenant", "known-account", "operator", new[] { "User.Read", "Policy.ReadWrite.ConditionalAccess" });

    [Theory]
    [InlineData("valid")] [InlineData("tenant")] [InlineData("account")] [InlineData("operator")]
    [InlineData("removed-scope")] [InlineData("new-scope")] [InlineData("other-resource")] [InlineData("empty-scopes")]
    public async Task Actual_renewal_reply_is_verified_before_it_can_replace_cached_authorisation(string change)
    {
        var initial = Valid(); var reply = change switch
        {
            "tenant" => initial with { Tenant = "other" }, "account" => initial with { Account = "other" },
            "operator" => initial with { ObjectId = "other" }, "removed-scope" => initial with { Scopes = new[] { "User.Read" } },
            "new-scope" => initial with { Scopes = new[] { "User.Read", "Policy.ReadWrite.ConditionalAccess", "Directory.ReadWrite.All" } },
            "other-resource" => initial with { Scopes = new[] { "https://outlook.office365.com/User.Read" } },
            "empty-scopes" => initial with { Scopes = Array.Empty<string>() }, _ => initial
        };
        var calls = 0; var invalidations = 0;
        Task<Reply> Acquire() => SilentRenewalAcquisition.AcquireAsync(() => { calls++; return Task.FromResult(reply); },
            r => r.Tenant == initial.Tenant && r.Account == initial.Account && r.ObjectId == initial.ObjectId,
            r => r.Scopes, initial.Scopes, () => invalidations++, CancellationToken.None);
        if (change == "valid") { Assert.Same(reply, await Acquire()); Assert.Equal(0, invalidations); }
        else { await Assert.ThrowsAsync<AuthenticationRequiredException>(Acquire); Assert.Equal(1, invalidations); }
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cancelling_a_renewal_does_not_publish_a_reply_or_retry_it()
    {
        using var stop = new CancellationTokenSource(); var calls = 0; var invalidations = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SilentRenewalAcquisition.AcquireAsync(() =>
        { calls++; stop.Cancel(); return Task.FromResult(Valid()); }, _ => true, r => r.Scopes, Valid().Scopes, () => invalidations++, stop.Token));
        Assert.Equal(1, calls); Assert.Equal(0, invalidations);
    }
}
