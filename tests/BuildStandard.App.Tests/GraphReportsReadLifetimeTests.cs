using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

/// <summary>AST-20261010-22: Connect-time expiry is provenance; the guarded provider owns silent-only token renewal.</summary>
public sealed class GraphReportsReadLifetimeTests
{
    [Fact]
    public async Task An_elapsed_original_connection_timestamp_does_not_block_the_existing_read_provider()
    {
        using var f = new ConnectedReportsFixture();
        var session = f.Workspace.Session!;
        session.TokenExpiresAt = "2020-01-01T00:00:00Z";
        var context = f.Controller.ContextKey;
        var result = await f.Read();
        Assert.Equal(ReportReadState.Collected, result.Evidence.Schema1!.Status);
        Assert.Equal(1, f.Tokens.Calls); Assert.Equal(1, f.Handler.Calls);
        Assert.True(File.Exists(result.File));
        Assert.Same(session, f.Workspace.Session);
        Assert.Equal("2020-01-01T00:00:00Z", session.TokenExpiresAt);
        Assert.Equal(context, f.Controller.ContextKey);
    }

    [Fact]
    public async Task Natural_original_token_expiry_during_a_read_does_not_discard_a_stable_verified_context()
    {
        using var f = new ConnectedReportsFixture();
        var expiry = DateTimeOffset.UtcNow.AddSeconds(2);
        var session = f.Workspace.Session!;
        session.TokenExpiresAt = Timestamps.Format(expiry);
        var originalExpiry = session.TokenExpiresAt;
        var context = f.Controller.ContextKey;
        f.Handler.BeforeResponse = () =>
        {
            // Only time passes: no context fact is mutated and no authentication is attempted by this fixture.
            var remaining = expiry - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero) Thread.Sleep(remaining + TimeSpan.FromMilliseconds(30));
        };
        var result = await f.Read();
        Assert.True(DateTimeOffset.UtcNow >= expiry);
        Assert.Equal(ReportReadState.Collected, result.Evidence.Schema1!.Status);
        Assert.Equal(1, f.Tokens.Calls); Assert.Equal(1, f.Handler.Calls);
        Assert.True(File.Exists(result.File));
        Assert.Same(session, f.Workspace.Session); Assert.Equal(originalExpiry, session.TokenExpiresAt);
        Assert.Equal(context, f.Controller.ContextKey);
    }

    [Fact]
    public async Task Interaction_required_by_the_existing_provider_is_saved_only_as_failed_evidence_without_retry()
    {
        using var f = new ConnectedReportsFixture();
        f.Workspace.Session!.TokenExpiresAt = "2020-01-01T00:00:00Z";
        var context = f.Controller.ContextKey;
        // The provider transport/state machine is separately exercised by MsalProductionRenewalTests.
        // Here its deliberate failure crosses the real Graph client and report collector boundary.
        f.Tokens.BeforeAcquisition = () => throw new AuthenticationRequiredException("Microsoft requires a fresh interactive sign-in. No automatic interactive retry was performed.");
        var result = await f.Read();
        Assert.Equal(ReportReadState.Failed, result.Evidence.Schema1!.Status);
        Assert.Empty(result.Evidence.Schema1!.Sections.Single().Rows);
        Assert.Contains("interactive sign-in", result.Evidence.Schema1!.Sections.Single().Error);
        Assert.True(File.Exists(result.File));
        Assert.Equal(1, f.Tokens.Calls); Assert.Equal(0, f.Handler.Calls);
        Assert.True(f.Workspace.Session.OperatorVerified); Assert.Equal(context, f.Controller.ContextKey);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("permissions")]
    public async Task Provider_authorisation_invalidation_refuses_save_without_retry(string changed)
    {
        using var f = new ConnectedReportsFixture();
        f.Workspace.Session!.TokenExpiresAt = "2020-01-01T00:00:00Z";
        f.Tokens.BeforeAcquisition = () =>
        {
            // Workspace's existing AuthorisationInvalidated handler clears this verified fact.
            // Actual MSAL identity/scope checks and event forwarding are covered in MsalProductionRenewalTests.
            f.Workspace.Session.OperatorVerified = false;
            throw new AuthenticationRequiredException("Returned " + changed + " changed during silent renewal. No interactive retry was performed.");
        };
        await Assert.ThrowsAnyAsync<ToolkitException>(() => f.Read());
        Assert.Equal(1, f.Tokens.Calls); Assert.Equal(0, f.Handler.Calls);
        Assert.False(f.Workspace.Session.OperatorVerified);
        Assert.False(Directory.Exists(Path.Combine(f.Root.Paths.TenantDirectory(f.Workspace.Session.TenantId), "report-evidence")));
    }
}
