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
