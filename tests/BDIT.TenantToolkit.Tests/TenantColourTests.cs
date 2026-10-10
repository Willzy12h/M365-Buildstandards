using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Scripts;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// The tenant colour band on the Scripts &amp; Reports page and its copy confirmation. It must be the same for a tenant
/// every time and on every machine, readable, and visible against the page; and it never stands in for the tenant ID.
/// </summary>
public sealed class TenantColourTests
{
    private const string Tenant = "11111111-1111-4111-8111-111111111111";
    private static readonly string[] Pages = { "#FFFFFF", "#F3F6FA", "#F7F9FC" };

    [Fact]
    public void Every_band_carries_text_at_wcag_aa_and_stands_out_from_the_page()
    {
        Assert.True(TenantColours.Palette.Count >= 6);
        Assert.Equal(TenantColours.Palette.Count, TenantColours.Palette.Select(c => c.Background).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var colour in TenantColours.Palette.Append(TenantColours.Neutral))
            Assert.True(TenantColours.Contrast(colour.Foreground, colour.Background) >= 4.5, $"{colour.Name} text is below 4.5:1.");
        // A band only identifies a tenant if it can be seen; 3:1 is WCAG's non-text contrast minimum.
        foreach (var colour in TenantColours.Palette)
            foreach (var page in Pages)
                Assert.True(TenantColours.Contrast(colour.Background, page) >= 3.0, $"{colour.Name} is below 3:1 on {page}.");
    }

    [Fact]
    public void The_contrast_measure_matches_the_wcag_reference_values()
    {
        Assert.Equal(21.0, TenantColours.Contrast("#000000", "#FFFFFF"), 3);
        Assert.Equal(1.0, TenantColours.Contrast("#1F5FA8", "#1F5FA8"), 3);
        Assert.Equal(TenantColours.Contrast("#1F5FA8", "#FFFFFF"), TenantColours.Contrast("#FFFFFF", "#1F5FA8"), 6);
        Assert.Throws<ArgumentException>(() => TenantColours.Contrast("blue", "#FFFFFF"));
    }

    [Fact]
    public void A_tenant_always_gets_the_same_colour_however_its_id_is_written()
    {
        var colour = TenantColours.For(Tenant);
        Assert.Contains(colour, TenantColours.Palette);
        Assert.Equal(colour, TenantColours.For(Tenant.ToUpperInvariant()));
        Assert.Equal(colour, TenantColours.For("{" + Tenant + "}"));
        Assert.Equal(colour, TenantColours.For("  " + Tenant + " "));
        // Pinned, so a change to the derivation - which would recolour every client an engineer has learned - is deliberate.
        Assert.Equal("Brown", colour.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("contoso.onmicrosoft.com")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Anything_that_is_not_a_tenant_id_is_neutral(string? input) =>
        Assert.Equal(TenantColours.Neutral, TenantColours.For(input));

    [Fact]
    public void Tenant_ids_spread_across_the_palette()
    {
        var used = Enumerable.Range(1, 64).Select(i => TenantColours.For($"aaaaaaaa-aaaa-4aaa-8aaa-{i:D12}").Name).Distinct().Count();
        Assert.Equal(TenantColours.Palette.Count, used);
    }

    [Theory]
    [InlineData("engineer@example.com", true)]
    [InlineData("engineer@contoso.onmicrosoft.com", true)]
    [InlineData("", false)]
    [InlineData("not an account", false)]
    [InlineData("engineer@example.com'; Remove-Item", false)]
    public void Only_a_sign_in_name_is_offered_as_the_copied_scripts_account(string account, bool accepted) =>
        Assert.Equal(accepted, ScriptCopy.IsAcceptableAccount(account));

    [Fact]
    public void The_desktop_preview_writes_values_exactly_as_the_copied_script_does()
    {
        var entry = ScriptCatalogue.Shipped.Find("exo.message-trace");
        var binding = ScriptInputs.Bind(entry.Manifest, new Dictionary<string, string?>
        {
            ["StartDate"] = "2026-10-06", ["EndDate"] = "2026-10-08", ["RecipientAddress"] = "alex@example.com; sam@example.com",
            ["Status"] = "Delivered\nFailed"
        }, new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero));
        Assert.True(binding.IsValid, string.Join(" ", binding.Problems));
        var script = ScriptCopy.Generate(entry, binding, new ScriptCopyTarget(Tenant, "Test client"), DateTimeOffset.UtcNow);
        foreach (var argument in binding.Arguments)
            Assert.Contains("        " + argument.Name + " = " + ScriptCopy.Literal(argument.Value) + "\n", script, StringComparison.Ordinal);
        Assert.Equal("@('Delivered', 'Failed')", ScriptCopy.Literal(binding.Arguments.Single(a => a.Name == "Status").Value));
    }
}
