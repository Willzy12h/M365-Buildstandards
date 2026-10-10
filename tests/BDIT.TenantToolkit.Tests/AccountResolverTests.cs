using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Identity;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class AccountResolverTests
{
    [Fact]
    public async Task CrossTenantLookupFailsBeforeAnyRead()
    {
        var graph = new FakeGraphClient(TestData.Standard());
        await Assert.ThrowsAsync<TenantMismatchException>(() => AccountResolver.SearchAsync(graph, TestData.TenantB, "admin@test.example", default));
        Assert.Empty(graph.Reads);
    }

    [Fact]
    public async Task SearchEscapesOdataAndReturnsAllMatchesForExplicitSelection()
    {
        var graph = new FakeGraphClient(TestData.Standard());
        graph.Add("/users", new JsonObject { ["id"] = TestData.Emergency, ["displayName"] = "O'Brien", ["userPrincipalName"] = "account1@test.example" });
        graph.Add("/users", new JsonObject { ["id"] = TestData.Operator, ["displayName"] = "O'Brien", ["userPrincipalName"] = "account2@test.example" });
        var results = await AccountResolver.SearchAsync(graph, TestData.TenantA, "O'Brien", default);
        Assert.Equal(2, results.Count);
        Assert.All(results, a => Assert.Equal(TestData.TenantA, a.TenantId));
        Assert.Contains(graph.Reads, path => Uri.UnescapeDataString(path).Contains("startswith(displayName,'O''Brien')", StringComparison.Ordinal));
        Assert.Empty(graph.Writes);
    }

    [Fact]
    public async Task SignInAddressUsesExactFilterIncludingGuestAndDollarNames()
    {
        var graph = new FakeGraphClient(TestData.Standard());
        await AccountResolver.SearchAsync(graph, TestData.TenantA, "$admin#EXT#@test.example", default);
        Assert.Contains(graph.Reads, path => Uri.UnescapeDataString(path).Contains("userPrincipalName eq '$admin#EXT#@test.example'", StringComparison.Ordinal));
    }

    [Fact]
    public void ExclusionMetadataCannotCrossTenant()
    {
        var profile = TestData.Profile();
        profile.ExclusionAccounts.Add(new ExclusionAccount { TenantId = TestData.TenantB, ObjectId = TestData.Emergency, Reason = "Emergency access" });
        Assert.Throws<ConfigurationException>(() => ProfileValidator.Validate(profile, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AdditionalExclusionsDoNotSatisfyEmergencyAccountRequirement()
    {
        var profile = TestData.Profile(emergency: false);
        profile.Parameters.AdditionalExclusionAccountIds.Add(TestData.Operator);
        Assert.Null(profile.Parameters.ToTemplateValues(profile.TenantId)["emergencyAccountIds"]);
        Assert.Null(profile.Parameters.ToTemplateValues(profile.TenantId)["emergencyAndGuestIds"]);
    }

    [Fact]
    public void SelectedExclusionRetainsReasonAndJoinsTemplateWithoutDuplicates()
    {
        var profile = TestData.Profile();
        profile.Parameters.AdditionalExclusionAccountIds.AddRange(new[] { TestData.Operator, TestData.Operator });
        profile.ExclusionAccounts.Add(new ExclusionAccount { TenantId = profile.TenantId, ObjectId = TestData.Operator, Purpose = "Approved exception", Reason = "Approved client exception", UserPrincipalName = "account@test.example" });
        var validated = ProfileValidator.Validate(profile, DateTimeOffset.UtcNow);
        Assert.Single(validated.ExclusionAccounts);
        Assert.Equal("Approved client exception", validated.ExclusionAccounts[0].Reason);
        Assert.Equal(2, validated.Parameters.ToTemplateValues(profile.TenantId)["emergencyAccountIds"]!.AsArray().Count);
    }
}
