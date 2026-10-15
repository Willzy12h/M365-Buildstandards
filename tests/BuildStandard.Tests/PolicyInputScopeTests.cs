using System.Text.Json.Nodes;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Planning;
using Xunit;
namespace BuildStandard.Tests;
public sealed class PolicyInputScopeTests
{
    private static readonly ParameterDefinition[] Parameters = [new() { Key = "used" }, new() { Key = "required", RequiredForControls = ["CA-001"] }, new() { Key = "unrelated" }];
    [Fact]
    public void Focus_shows_only_used_or_explicitly_required_fields_without_changing_definitions()
    {
        var control = new ControlDefinition { Id = "CA-001", Payload = new JsonObject { ["syntheticSetting"] = "{{used}}" } };
        Assert.True(PolicyInputScope.Keys(Parameters, control, false).SetEquals(["used", "required"]));
        Assert.Equal(3, Parameters.Length);
    }
    [Fact]
    public void No_control_is_not_guessed_and_explicit_all_preserves_other_inputs()
    {
        Assert.Empty(PolicyInputScope.Keys(Parameters, null, false));
        Assert.True(PolicyInputScope.Keys(Parameters, null, true).SetEquals(Parameters.Select(x => x.Key)));
    }
}
