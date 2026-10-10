using System.Text.Json.Nodes;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Planning;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// The policy automation form turns what an engineer types into the value a policy is created with, and its field is
/// a thin wrapper by design: the parsing rules live once, in <see cref="PolicyInputParser"/>, where the engine tests
/// cover them. The risk is drift - a field that grows its own parsing and starts accepting what the parser refuses,
/// or saying something different about why a value is wrong. The headless runner drifted in exactly that way before
/// it was made to delegate. Until this project existed the field could not be tested at all, because it lives in the
/// WPF assembly.
///
/// These compare the field against the parser rather than restating the parser's rules, so they fail on drift and not
/// on a legitimate change to what the parser accepts.
/// </summary>
public class PolicyInputFieldTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
    private const string Id = "11111111-1111-4111-8111-111111111111";
    private const string OtherId = "22222222-2222-4222-8222-222222222222";

    private static PolicyInputField Field(string type, bool required = false, JsonNode? defaultValue = null, JsonNode? current = null) =>
        new(new ParameterDefinition { Key = "k", Label = "Label", Type = type, Required = required, Default = defaultValue, ReviewedOn = "2026-09-15" }, current, Now);

    public static TheoryData<string, string> Inputs => new()
    {
        { "guid", Id },
        { "guid", "not-an-id" },
        { "guid", "   " },
        { "guidList", Id + ", " + OtherId },
        { "guidList", Id + "\n" + OtherId },
        { "guidList", "[]" },
        { "guidList", Id + ", nope" },
        { "integer", "14" },
        { "integer", "fourteen" },
        { "boolean", "true" },
        { "boolean", "yes" },
        { "jsonArray", "[\"a\", 1]" },
        { "jsonArray", "{\"a\": 1}" },
        { "jsonArray", "[unterminated" },
        { "string", "any text at all" },
        { "string", "" }
    };

    /// <summary>For every type and every shape of input, the field says exactly what the parser says.</summary>
    [Theory]
    [MemberData(nameof(Inputs))]
    public void The_field_reads_exactly_as_the_parser_does(string type, string text)
    {
        var expected = PolicyInputParser.TryRead(type, text, out var expectedNode, out var expectedProblem);

        var field = Field(type);
        field.Value = text;
        var actual = field.TryRead(out var actualNode);

        Assert.Equal(expected, actual);
        Assert.Equal(expectedNode?.ToJsonString(), actualNode?.ToJsonString());
        Assert.Equal(expectedProblem, field.Problem);
        Assert.Equal(expectedProblem.Length > 0, field.HasProblem);
    }

    /// <summary>
    /// A saved value is shown the way it is typed and reads back as the same value, so opening the form and saving it
    /// unchanged cannot alter a client's inputs. A saved list must not come back as JSON punctuation.
    /// </summary>
    [Theory]
    [InlineData("guid", "\"" + Id + "\"")]
    [InlineData("guidList", "[\"" + Id + "\", \"" + OtherId + "\"]")]
    [InlineData("guidList", "[]")]
    [InlineData("integer", "14")]
    [InlineData("boolean", "false")]
    [InlineData("jsonArray", "[{\"a\":1}]")]
    public void A_saved_value_reads_back_unchanged(string type, string savedJson)
    {
        var saved = JsonNode.Parse(savedJson);
        var field = Field(type, current: saved);

        Assert.Equal(PolicyInputParser.Render(type, saved), field.Value);
        Assert.True(field.TryRead(out var read));
        Assert.Equal(saved!.ToJsonString(), read!.ToJsonString());
    }

    /// <summary>An error belongs to the value that caused it. Editing the field clears it rather than leaving it stale.</summary>
    [Fact]
    public void Editing_the_value_clears_the_previous_problem()
    {
        var field = Field("guid");
        field.Value = "not-an-id";
        Assert.False(field.TryRead(out _));
        Assert.True(field.HasProblem);

        field.Value = Id;

        Assert.False(field.HasProblem);
        Assert.Equal("", field.Problem);
    }

    /// <summary>
    /// The status line is how an engineer tells a blocking input from one that will fall back to a dated default, so
    /// each state must read differently. Blank is "not supplied", never an empty value.
    /// </summary>
    [Fact]
    public void Status_distinguishes_supplied_required_defaulted_and_optional()
    {
        var supplied = Field("guid", required: true);
        supplied.Value = Id;
        Assert.Equal("Supplied", supplied.Status);

        Assert.Equal("Needed before this policy can be created", Field("guid", required: true).Status);
        Assert.StartsWith("Default ", Field("integer", defaultValue: JsonValue.Create(14)).Status, StringComparison.Ordinal);
        Assert.Equal("Not supplied", Field("string").Status);

        var blank = Field("guid", required: true);
        blank.Value = "   ";
        Assert.False(blank.TryRead(out var node));
        Assert.Null(node);
        Assert.False(blank.HasProblem);
    }
}
