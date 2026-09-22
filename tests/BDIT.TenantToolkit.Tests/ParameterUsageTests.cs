using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// Which parameters a payload uses decides whether an input is demanded from the client, whether a dated default is
/// applied, and what the client document asks for. Five places used to answer it by searching serialised text, which
/// asked a different question from the one the resolver answers. These pin the two together.
/// </summary>
public class ParameterUsageTests
{
    private static JsonObject Payload(string json) => (JsonObject)ToolkitJson.ParseNode(json)!;

    [Fact]
    public void A_parameter_used_only_inside_a_nested_array_is_found()
    {
        var payload = Payload("""
            { "conditions": { "users": { "excludeUsers": [ "{{emergencyAccountIds}}" ] },
                              "locations": { "excludeLocations": [ "a", [ "{{officeLocationId}}" ] ] } } }
            """);

        var keys = ParameterUsage.Keys(payload);

        Assert.Contains("emergencyAccountIds", keys);
        Assert.Contains("officeLocationId", keys);
    }

    [Fact]
    public void A_placeholder_embedded_in_a_longer_string_is_found()
    {
        var keys = ParameterUsage.Keys(Payload("""{ "displayName": "BDIT - {{clientName}} - MFA" }"""));

        Assert.Equal(new[] { "clientName" }, keys);
    }

    /// <summary>
    /// The false positive the tree walk removes. <see cref="CanonicalJson.Resolve"/> substitutes only inside string
    /// values, so a property whose *name* looks like a placeholder is never resolved. Serialised-text searching
    /// reported it as used, which would demand an input from the client that nothing would ever consume.
    /// </summary>
    [Fact]
    public void A_property_name_that_looks_like_a_placeholder_is_not_a_used_parameter()
    {
        var payload = Payload("""{ "settings": { "{{officeIpRanges}}": "literal value" } }""");

        Assert.DoesNotContain("officeIpRanges", ParameterUsage.Keys(payload));
        Assert.False(ParameterUsage.Uses(payload, "officeIpRanges"));

        // And the resolver agrees: it leaves the property name alone, so nothing is missing.
        var resolved = (JsonObject)CanonicalJson.Resolve(payload, new Dictionary<string, JsonNode?>())!;
        Assert.True(((JsonObject)resolved["settings"]!).ContainsKey("{{officeIpRanges}}"));
    }

    [Fact]
    public void A_placeholder_that_is_not_an_identifier_is_not_reported()
    {
        Assert.Empty(ParameterUsage.Keys(Payload("""{ "note": "use {{not-an-identifier}} here" }""")));
    }

    [Fact]
    public void Keys_across_several_payloads_walks_each_once_and_unions_them()
    {
        var a = Payload("""{ "a": "{{one}}" }""");
        var b = Payload("""{ "b": "{{two}}" }""");

        Assert.Equal(new[] { "one", "two" }, ParameterUsage.KeysAcross(new JsonNode?[] { a, b, null }).OrderBy(k => k));
    }

    /// <summary>
    /// The promise that matters: everything reported as used is something the resolver demands, and nothing else is.
    /// Supplying exactly the reported keys must resolve, and withholding any one of them must fail.
    /// </summary>
    [Fact]
    public void What_is_reported_as_used_is_exactly_what_the_resolver_demands()
    {
        var payload = Payload("""
            { "displayName": "BDIT - {{clientName}} - MFA",
              "conditions": { "users": { "excludeUsers": [ "{{emergencyAccountIds}}" ] },
                              "{{notAProperty}}": "literal" },
              "state": "disabled" }
            """);
        var keys = ParameterUsage.Keys(payload);
        var values = keys.ToDictionary(k => k, k => (JsonNode?)JsonValue.Create("x"), StringComparer.Ordinal);

        CanonicalJson.Resolve(payload, values);

        foreach (var key in keys)
        {
            var withheld = values.Where(p => p.Key != key).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            Assert.Throws<MissingParameterException>(() => CanonicalJson.Resolve(payload, withheld));
        }
    }
}
