using BDIT.TenantToolkit.Graph.Auth;
using Microsoft.Identity.Client;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class PermissionAcquisitionTests
{
    [Theory]
    [InlineData(true, 1, false, 1, 0)]
    [InlineData(true, 1, true, 1, 1)]
    [InlineData(true, 0, false, 0, 1)]
    [InlineData(true, 2, false, 0, 1)]
    [InlineData(false, 1, false, 0, 1)]
    public async Task Access_preview_occurs_only_at_the_explicit_interactive_boundary(bool known, int accounts,
        bool expired, int expectedSilent, int expectedInteraction)
    {
        var silent = 0; var approvals = 0; var interactive = 0;
        var result = await ExplicitConnectAcquisition.AcquireAsync(known, Enumerable.Range(0, accounts).ToArray(),
            (_, token) => { silent++; if (expired) throw new MsalUiRequiredException("interaction_required", "synthetic"); return Task.FromResult("cached"); },
            (_, token) => { Assert.Equal(1, approvals); interactive++; return Task.FromResult("selected"); }, CancellationToken.None,
            _ => { approvals++; return Task.CompletedTask; });
        Assert.Equal(expectedSilent, silent); Assert.Equal(expectedInteraction, approvals); Assert.Equal(expectedInteraction, interactive);
        Assert.Equal(expectedInteraction == 0 ? "cached" : "selected", result);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Refused_or_cancelled_access_preview_never_reaches_Microsoft_interaction(bool afterCache)
    {
        var interactive = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExplicitConnectAcquisition.AcquireAsync(afterCache,
            afterCache ? new[] { "known" } : Array.Empty<string>(),
            (_, token) => Task.FromException<string>(new MsalUiRequiredException("interaction_required", "synthetic")),
            (_, token) => { interactive++; return Task.FromResult("not allowed"); }, CancellationToken.None,
            _ => throw new OperationCanceledException("Engineer declined the displayed request")));
        Assert.Equal(0, interactive);
    }

    [Theory]
    [InlineData("User.Read", true)] [InlineData("https://graph.microsoft.com/user.read", true)]
    [InlineData("Directory.Read.All", false)] [InlineData("User.Read,Directory.Read.All", false)] [InlineData("", false)]
    public void Actual_returned_scope_changes_never_reuse_initial_authorisation(string actual, bool same)
    {
        Assert.Equal(same, MsalAuthenticator.SameAuthorisationScopes(new[] { "User.Read" },
            actual.Length == 0 ? Array.Empty<string>() : actual.Split(',')));
    }
}
