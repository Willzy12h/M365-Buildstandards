using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Planning;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// A reviewable setting the client has not supplied falls back to the standard's default so the candidate is still
/// created, with a warning naming the value. Identity inputs declare no default and must keep blocking: a plausible
/// but wrong exclusion is how a tenant locks itself out.
/// </summary>
public class PolicyInputDefaultsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    private static StandardCatalogue Standard() => new()
    {
        Parameters =
        {
            new ParameterDefinition
            {
                Key = "androidMinimumVersion", Label = "Minimum supported Android version", Type = "string",
                Default = JsonValue.Create("14"), ReviewedOn = "2026-09-15"
            },
            new ParameterDefinition { Key = "emergencyAccountIds", Label = "Emergency access account object IDs", Type = "guidList", Required = true }
        }
    };

    private static JsonObject Payload(string template) => new() { ["osMinimumVersion"] = template };

    [Fact]
    public void A_missing_reviewable_input_uses_the_default_and_warns()
    {
        var applied = PolicyInputDefaults.Apply(Payload("{{androidMinimumVersion}}"), Standard(), new Dictionary<string, JsonNode?>(), Now);

        Assert.Equal("14", applied.Values["androidMinimumVersion"]!.GetValue<string>());
        var warning = Assert.Single(applied.Warnings);
        Assert.Contains("Minimum supported Android version", warning, StringComparison.Ordinal);
        Assert.Contains("\"14\"", warning, StringComparison.Ordinal);
        Assert.Contains("before assigning", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("90 days", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void A_supplied_value_is_never_replaced_and_raises_no_warning()
    {
        var supplied = new Dictionary<string, JsonNode?> { ["androidMinimumVersion"] = JsonValue.Create("16") };

        var applied = PolicyInputDefaults.Apply(Payload("{{androidMinimumVersion}}"), Standard(), supplied, Now);

        Assert.Equal("16", applied.Values["androidMinimumVersion"]!.GetValue<string>());
        Assert.Empty(applied.Warnings);
    }

    [Fact]
    public void An_input_the_payload_does_not_use_is_left_alone()
    {
        var applied = PolicyInputDefaults.Apply(Payload("10.0.26200.0"), Standard(), new Dictionary<string, JsonNode?>(), Now);

        Assert.Empty(applied.Warnings);
        Assert.False(applied.Values.ContainsKey("androidMinimumVersion"));
    }

    /// <summary>No default means the control still blocks, which is the whole protection for identity inputs.</summary>
    [Fact]
    public void An_identity_input_is_never_defaulted()
    {
        var applied = PolicyInputDefaults.Apply(new JsonObject { ["exclude"] = "{{emergencyAccountIds}}" }, Standard(), new Dictionary<string, JsonNode?>(), Now);

        Assert.Empty(applied.Warnings);
        Assert.False(applied.Values.ContainsKey("emergencyAccountIds"));
    }

    [Fact]
    public void A_default_reviewed_more_than_ninety_days_ago_says_so()
    {
        var standard = Standard();
        standard.Parameters[0].ReviewedOn = "2026-01-01";

        var applied = PolicyInputDefaults.Apply(Payload("{{androidMinimumVersion}}"), standard, new Dictionary<string, JsonNode?>(), Now);

        Assert.Contains("more than 90 days ago", Assert.Single(applied.Warnings), StringComparison.Ordinal);
        Assert.True(standard.Parameters[0].IsStale(Now));
        Assert.False(standard.Parameters[1].IsStale(Now));
    }
}
