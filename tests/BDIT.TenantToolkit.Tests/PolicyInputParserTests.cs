using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Engine.Planning;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// The input form replaces a hand-written JSON object, so parsing has to accept what an engineer would naturally type
/// and explain what is wrong at the field rather than failing a plan much later.
/// </summary>
public class PolicyInputParserTests
{
    private const string Id = "11111111-1111-1111-1111-111111111111";
    private const string Other = "22222222-2222-2222-2222-222222222222";

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111, 22222222-2222-2222-2222-222222222222")]
    [InlineData("11111111-1111-1111-1111-111111111111\n22222222-2222-2222-2222-222222222222")]
    [InlineData(" 11111111-1111-1111-1111-111111111111 ; 22222222-2222-2222-2222-222222222222 ")]
    public void Identifier_lists_accept_commas_newlines_and_spacing(string typed)
    {
        Assert.True(PolicyInputParser.TryRead("guidList", typed, out var node, out var problem));
        Assert.Equal(new[] { Id, Other }, ((JsonArray)node!).Select(n => n!.GetValue<string>()));
        Assert.Equal("", problem);
    }

    [Fact]
    public void A_bad_identifier_is_named_beside_the_field()
    {
        Assert.False(PolicyInputParser.TryRead("guidList", Id + ", not-an-id", out _, out var problem));
        Assert.Contains("not-an-id", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_field_supplies_nothing_rather_than_an_empty_value()
    {
        Assert.False(PolicyInputParser.TryRead("string", "   ", out var node, out var problem));
        Assert.Null(node);
        Assert.Equal("", problem);
    }

    [Fact]
    public void Invalid_json_for_a_json_array_is_explained_not_thrown()
    {
        Assert.False(PolicyInputParser.TryRead("jsonArray", "{ not json", out _, out var problem));
        Assert.NotEqual("", problem);
    }

    [Fact]
    public void A_json_object_is_refused_where_an_array_is_required()
    {
        Assert.False(PolicyInputParser.TryRead("jsonArray", "{\"a\":1}", out _, out var problem));
        Assert.Contains("JSON array", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_values_are_shown_the_way_they_are_typed()
    {
        Assert.Equal(Id + ", " + Other, PolicyInputParser.Render("guidList", new JsonArray(Id, Other)));
        Assert.Equal("14", PolicyInputParser.Render("string", JsonValue.Create("14")));
        Assert.Equal("", PolicyInputParser.Render("jsonArray", null));
    }

    /// <summary>What is rendered for display must parse back to the same value, or saving would corrupt an input.</summary>
    [Fact]
    public void Rendering_and_reading_a_list_round_trips()
    {
        var rendered = PolicyInputParser.Render("guidList", new JsonArray(Id, Other));

        Assert.True(PolicyInputParser.TryRead("guidList", rendered, out var node, out _));
        Assert.Equal(new[] { Id, Other }, ((JsonArray)node!).Select(n => n!.GetValue<string>()));
    }

    [Theory]
    [InlineData("guid", "not-a-guid", false)]
    [InlineData("guid", "11111111-1111-1111-1111-111111111111", true)]
    [InlineData("integer", "twelve", false)]
    [InlineData("integer", "12", true)]
    [InlineData("boolean", "yes", false)]
    [InlineData("boolean", "true", true)]
    public void Each_type_accepts_only_what_it_can_use(string type, string typed, bool accepted)
    {
        Assert.Equal(accepted, PolicyInputParser.TryRead(type, typed, out _, out var problem));
        Assert.Equal(!accepted, problem.Length > 0);
    }

    [Theory]
    [InlineData("jsonArray", "[\"alpha, beta\",\"spaces and quotes \\\"stay\\\"\"]")]
    [InlineData("jsonArray", "[]")]
    [InlineData("jsonArray", "[{\"nested\":[true,null,5]}]")]
    [InlineData("guidList", "[]")]
    [InlineData("integer", "14")]
    [InlineData("boolean", "false")]
    public void Rendering_uses_the_declared_type_and_preserves_the_saved_value(string type, string json)
    {
        var original = JsonNode.Parse(json);
        var rendered = PolicyInputParser.Render(type, original);
        Assert.True(PolicyInputParser.TryRead(type, rendered, out var restored, out var error), error);
        Assert.True(JsonNode.DeepEquals(original, restored));
    }

    [Fact]
    public void Saving_visible_fields_preserves_unknown_inputs_and_removes_only_cleared_fields()
    {
        var stored = new Dictionary<string, JsonNode?>
        {
            ["fromAnotherRelease"] = new JsonArray("keep", "this"),
            ["cleared"] = JsonValue.Create("old"),
            ["edited"] = JsonValue.Create(1)
        };
        var merged = PolicyInputParser.MergeInputs(stored, new Dictionary<string, JsonNode?>
        {
            ["cleared"] = null, ["edited"] = JsonValue.Create(2), ["explicitEmpty"] = new JsonArray()
        });
        Assert.True(JsonNode.DeepEquals(stored["fromAnotherRelease"], merged["fromAnotherRelease"]));
        Assert.False(merged.ContainsKey("cleared"));
        Assert.Equal(2, merged["edited"]!.GetValue<int>());
        Assert.Empty(Assert.IsType<JsonArray>(merged["explicitEmpty"]));
        Assert.Equal(1, stored["edited"]!.GetValue<int>());
        Assert.NotSame(stored["fromAnotherRelease"], merged["fromAnotherRelease"]);
    }
}
