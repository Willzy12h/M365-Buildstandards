using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Models;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ReleaseInputTests
{
    [Theory][InlineData("string", "")][InlineData("guidList", "[]")][InlineData("guidList", " ")]
    public void A_selected_controls_mandatory_field_names_what_is_needed(string type, string value)
    {
        var field = new PolicyInputField(new ParameterDefinition { Key = "reviewers", Label = "Client reviewers", Type = type, Required = true }, null, DateTimeOffset.UtcNow) { Value = value };
        field.TryRead(out _, requireNow: true);
        Assert.True(field.HasProblem); Assert.Contains("Client reviewers", field.Problem); Assert.Contains("before saving", field.Problem);
    }
}
