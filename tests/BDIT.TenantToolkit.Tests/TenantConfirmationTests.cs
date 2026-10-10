using BDIT.TenantToolkit.Core.Safety;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public class TenantConfirmationTests
{
    private const string Tenant = "11111111-aaaa-4111-8111-111111111111";

    [Theory]
    [InlineData(Tenant)]
    [InlineData("11111111-AAAA-4111-8111-111111111111")]
    [InlineData("  11111111-aaaa-4111-8111-111111111111  ")]
    public void The_full_tenant_ID_matches_whatever_its_case_or_surrounding_spaces(string typed) =>
        Assert.True(TenantConfirmation.Matches(typed, Tenant));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("11111111-aaaa-4111-8111-11111111111")]
    [InlineData("11111111-aaaa-4111-8111-1111111111110")]
    [InlineData("11111111-aaaa-4111-8111-111111111112")]
    [InlineData("11111111aaaa41118111111111111111")]
    [InlineData("Contoso")]
    public void Anything_else_does_not(string? typed) =>
        Assert.False(TenantConfirmation.Matches(typed, Tenant));

    [Fact]
    public void An_empty_expected_tenant_matches_nothing()
    {
        Assert.False(TenantConfirmation.Matches("", ""));
        Assert.False(TenantConfirmation.Matches(" ", null));
    }
}
