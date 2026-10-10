using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using Microsoft.Identity.Client;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class MsalProductionRenewalTests
{
    private sealed class SyntheticAccount(string identifier) : IAccount
    {
        public string Username => "engineer@test.example";
        public string Environment => "login.microsoftonline.com";
        public AccountId HomeAccountId { get; } = new(identifier, TestData.Operator, TestData.TenantA);
    }

    private static AuthenticationResult Reply(string change = "valid", bool fresh = false) => new(
        accessToken: fresh ? "synthetic-renewed-token" : "synthetic-initial-token", isExtendedLifeTimeToken: false,
        uniqueId: change == "operator" ? TestData.Emergency : TestData.Operator,
        expiresOn: DateTimeOffset.UtcNow.AddHours(1), extendedExpiresOn: DateTimeOffset.UtcNow.AddHours(1),
        tenantId: change == "tenant" ? TestData.TenantB : TestData.TenantA,
        account: change == "null-account" ? null! : new SyntheticAccount(change == "account" ? "other-home-account" : "initial-home-account"),
        idToken: "", scopes: change switch
        {
            "removed-scope" => new[] { "User.Read" },
            "new-scope" => new[] { "User.Read", "Policy.ReadWrite.ConditionalAccess", "Directory.ReadWrite.All" },
            "empty-scopes" => Array.Empty<string>(),
            "other-resource" => new[] { "https://outlook.office365.com/User.Read", "Policy.ReadWrite.ConditionalAccess" },
            _ => new[] { "User.Read", "Policy.ReadWrite.ConditionalAccess" }
        }, correlationId: Guid.Empty);

    [Theory]
    [InlineData("valid")] [InlineData("tenant")] [InlineData("account")] [InlineData("operator")]
    [InlineData("removed-scope")] [InlineData("new-scope")] [InlineData("empty-scopes")]
    [InlineData("other-resource")] [InlineData("null-account")]
    public async Task Production_renewal_checks_the_actual_reply_before_replacing_the_token_and_forwards_invalidation(string change)
    {
        var calls = 0; var invalidations = 0; var connectionInvalidations = 0;
        var initial = Reply(); var renewed = Reply(change, fresh: true);
        var authenticator = new MsalAuthenticator(PublicClientApplicationBuilder.Create(TestData.ClientId).Build(),
            initial.Scopes.ToArray(), TestData.TenantA, "", NullLog.Instance, initial, TestData.ClientId,
            (forced, ct) => { Assert.Equal(calls == 0, forced); ct.ThrowIfCancellationRequested(); calls++; return Task.FromResult(renewed); });
        authenticator.AuthorisationInvalidated += () => invalidations++;
        var connection = new ConnectedTenant(TestData.Session(), new FakeGraphClient(TestData.Standard()), authenticator);
        connection.AuthorisationInvalidated += () => connectionInvalidations++;
        Assert.Equal("synthetic-initial-token", await authenticator.GetAccessTokenAsync(CancellationToken.None));
        Assert.Equal(0, calls);
        if (change == "valid")
        {
            Assert.Equal("synthetic-renewed-token", await authenticator.GetAccessTokenAsync(true, CancellationToken.None));
            Assert.Equal("synthetic-renewed-token", await authenticator.GetAccessTokenAsync(CancellationToken.None));
            Assert.Equal(0, invalidations); Assert.Equal(0, connectionInvalidations); Assert.Equal(1, calls);
        }
        else
        {
            await Assert.ThrowsAsync<AuthenticationRequiredException>(() => authenticator.GetAccessTokenAsync(true, CancellationToken.None));
            Assert.Equal(1, invalidations); Assert.Equal(1, connectionInvalidations); Assert.Equal(1, calls);
            // The refused token is not cached as an apparent success on the next ordinary read.
            await Assert.ThrowsAsync<AuthenticationRequiredException>(() => authenticator.GetAccessTokenAsync(CancellationToken.None));
            Assert.Equal(2, calls);
        }
        await authenticator.ReleaseAsync();
        await Assert.ThrowsAsync<AuthenticationRequiredException>(() => authenticator.GetAccessTokenAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("interaction")] [InlineData("cancel")]
    public async Task Production_renewal_does_not_retry_an_interaction_requirement_or_cancellation(string failure)
    {
        using var stop = new CancellationTokenSource(); var calls = 0;
        var initial = Reply();
        var authenticator = new MsalAuthenticator(PublicClientApplicationBuilder.Create(TestData.ClientId).Build(),
            initial.Scopes.ToArray(), TestData.TenantA, "", NullLog.Instance, initial, TestData.ClientId,
            (_, ct) =>
            {
                calls++;
                if (failure == "interaction") throw new MsalUiRequiredException("interaction_required", "synthetic");
                stop.Cancel(); ct.ThrowIfCancellationRequested(); return Task.FromResult(Reply(fresh: true));
            });
        if (failure == "interaction")
            await Assert.ThrowsAsync<AuthenticationRequiredException>(() => authenticator.GetAccessTokenAsync(true, stop.Token));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => authenticator.GetAccessTokenAsync(true, stop.Token));
        Assert.Equal(1, calls);
        await authenticator.ReleaseAsync();
    }
}
