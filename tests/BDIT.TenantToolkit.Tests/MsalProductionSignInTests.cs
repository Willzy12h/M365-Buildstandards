using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Graph.Auth;
using Microsoft.Identity.Client;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class MsalProductionSignInTests
{
    private sealed class SyntheticAccount : IAccount
    {
        public string Username => "engineer@test.example";
        public string Environment => "login.microsoftonline.com";
        public AccountId HomeAccountId { get; } = new("synthetic-home-account", TestData.Operator, TestData.TenantA);
    }

    [Fact]
    public async Task Configured_sign_in_timeout_reaches_acquisition_without_consuming_permission_review_time()
    {
        var settings = ToolkitSettings.Validate(new ToolkitSettings { SignInTimeoutMinutes = 1 });
        var budget = TimeSpan.FromMinutes(settings.SignInTimeoutMinutes);

        async Task CheckAcquisition(TimeSpan duration, bool expectedTimeout)
        {
            var clock = new ConnectAcquisitionBudgetTests.ManualTime(); var previews = 0; var acquisitions = 0;
            var request = new SignInRequest
            {
                TenantId = TestData.TenantA, ClientId = TestData.ClientId, Scopes = new[] { "User.Read" },
                UseSystemBrowser = true, Timeout = budget,
                BeforeInteractive = token =>
                {
                    previews++; clock.Advance(TimeSpan.FromMinutes(20));
                    token.ThrowIfCancellationRequested(); return Task.CompletedTask;
                }
            };
            Task<MsalAuthenticator> SignIn() => MsalAuthenticator.SignInAsync(request, NullLog.Instance, CancellationToken.None,
                token =>
                {
                    acquisitions++; clock.Advance(duration);
                    return Task.FromResult(new AuthenticationResult("synthetic-sign-in-token", false, TestData.Operator,
                        DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddHours(1), TestData.TenantA,
                        new SyntheticAccount(), "", new[] { "User.Read" }, Guid.Empty));
                }, clock);

            if (expectedTimeout)
                await Assert.ThrowsAsync<AuthenticationRequiredException>(SignIn);
            else
            {
                var authenticator = await SignIn();
                Assert.Equal(TestData.TenantA, authenticator.Outcome.TenantId);
                Assert.Equal(TestData.Operator, authenticator.Outcome.AccountObjectId);
                await authenticator.ReleaseAsync();
            }
            Assert.Equal(1, previews); Assert.Equal(1, acquisitions);
        }

        // The same real sign-in path accepts an in-budget reply after a long preview, then refuses a late reply.
        await CheckAcquisition(TimeSpan.FromSeconds(30), expectedTimeout: false);
        await CheckAcquisition(budget + TimeSpan.FromTicks(1), expectedTimeout: true);
    }
}
