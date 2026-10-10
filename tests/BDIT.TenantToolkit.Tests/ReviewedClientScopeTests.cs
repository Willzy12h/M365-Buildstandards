using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>CLA-20261006-17 / INT-056: relabelling a client keeps reviewed work valid; changing what was reviewed does not.</summary>
public sealed class ReviewedClientScopeTests
{
    private static TenantProfile Copy(TenantProfile profile) => ToolkitJson.Deserialize<TenantProfile>(ToolkitJson.Serialize(profile));

    [Fact]
    public void A_label_or_note_edit_keeps_the_reviewed_scope()
    {
        var profile = TestData.Profile();
        var relabelled = Copy(profile);
        relabelled.Company = "Renamed Client Ltd"; relabelled.Notes = "Changed note"; relabelled.UpdatedAt = "2030-01-01T00:00:00Z";
        Assert.Equal(ReviewedClientScope.Digest(profile), ReviewedClientScope.Digest(relabelled));
    }

    [Fact]
    public void Resolving_the_same_exclusions_again_or_reordering_them_keeps_the_reviewed_scope()
    {
        var profile = WithExclusions(TestData.Profile());
        var again = Copy(profile);
        foreach (var account in again.ExclusionAccounts) { account.ResolvedAt = "2030-01-01T00:00:00Z"; account.SelectedBy = "other.engineer@test.example"; }
        again.ExclusionAccounts.Reverse();
        again.Parameters.EmergencyAccountIds.Reverse();
        again.Parameters.AdditionalExclusionAccountIds.Reverse();
        Assert.Equal(ReviewedClientScope.Digest(profile), ReviewedClientScope.Digest(again));
    }

    [Theory]
    [InlineData("objectId")]
    [InlineData("purpose")]
    [InlineData("reason")]
    [InlineData("displayName")]
    public void A_material_exclusion_change_changes_the_reviewed_scope(string change)
    {
        var profile = WithExclusions(TestData.Profile());
        var changed = Copy(profile);
        var account = changed.ExclusionAccounts[0];
        switch (change)
        {
            case "objectId": account.ObjectId = "aaaaaaaa-0000-4000-8000-0000000000fb"; break;
            case "purpose": account.Purpose = "Approved exception"; break;
            case "reason": account.Reason = "A different approved reason"; break;
            case "displayName": account.DisplayName = "Another account"; break;
        }
        Assert.NotEqual(ReviewedClientScope.Digest(profile), ReviewedClientScope.Digest(changed));
    }

    private static TenantProfile WithExclusions(TenantProfile profile)
    {
        var ids = new[] { "aaaaaaaa-0000-4000-8000-0000000000e1", "aaaaaaaa-0000-4000-8000-0000000000e2" };
        profile.Parameters.EmergencyAccountIds.AddRange(ids);
        profile.Parameters.AdditionalExclusionAccountIds.AddRange(new[] { "aaaaaaaa-0000-4000-8000-0000000000e3", "aaaaaaaa-0000-4000-8000-0000000000e4" });
        foreach (var id in ids)
            profile.ExclusionAccounts.Add(new ExclusionAccount { TenantId = profile.TenantId, ObjectId = id, DisplayName = "Break glass " + id[^1],
                UserPrincipalName = $"bg{id[^1]}@test.example", Purpose = "Emergency access", Reason = "Emergency access account",
                ResolvedAt = "2026-09-11T09:00:00Z", SelectedBy = "engineer@test.example" });
        return profile;
    }

    [Theory]
    [InlineData("domain")]
    [InlineData("exclusions")]
    [InlineData("pilot")]
    [InlineData("emergency")]
    [InlineData("application")]
    public void A_material_change_changes_the_reviewed_scope(string change)
    {
        var profile = TestData.Profile();
        var changed = Copy(profile);
        switch (change)
        {
            case "domain": changed.Domain = "changed.example"; break;
            case "exclusions": changed.ExclusionAccounts.Add(new ExclusionAccount { ObjectId = "aaaaaaaa-0000-4000-8000-0000000000ff" }); break;
            case "pilot": changed.Parameters.PilotGroupId = "aaaaaaaa-0000-4000-8000-0000000000fe"; break;
            case "emergency": changed.Parameters.EmergencyAccountIds.Add("aaaaaaaa-0000-4000-8000-0000000000fd"); break;
            case "application": changed.AssessmentClientId = "aaaaaaaa-0000-4000-8000-0000000000fc"; break;
        }
        Assert.NotEqual(ReviewedClientScope.Digest(profile), ReviewedClientScope.Digest(changed));
    }
}
