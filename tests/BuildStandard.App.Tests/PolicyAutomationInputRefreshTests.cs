using System.Text.Json.Nodes;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using Xunit;

namespace BuildStandard.Tests;
public sealed class PolicyAutomationInputRefreshTests
{
    [Fact]
    public void Second_edit_of_the_bound_input_after_save_is_saved_instead_of_lost()
    {
        using var root = new TempRoot();
        var standard = JsonNode.Parse(TestData.StandardJson)!.AsObject();
        standard["parameters"]!.AsArray().Add(new JsonObject { ["key"] = "testLabel", ["type"] = "string", ["label"] = "Synthetic label" });
        root.WriteStandard("test.json", standard.ToJsonString()); root.WriteManifest();
        using var log = new ToolkitLogger(root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(root.Paths, new ToolkitSettings(), log, true); workspace.Initialise();
        var profile = workspace.SaveProfile(TestData.Profile()); workspace.ApplyProfileToSession(profile, save: false);
        var shell = new ShellViewModel(workspace); var page = shell.Page<AutomationViewModel>(); page.ShowAllInputs = true;
        // Model WPF ItemsSource: refresh only when its bound property raises a change notification.
        var bound = page.VisibleInputFields;
        page.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(page.VisibleInputFields)) bound = page.VisibleInputFields; };
        bound.Single(f => f.Key == "testLabel").Value = "First value";
        page.SaveInputsCommand.Execute(null);
        Assert.Empty(shell.ErrorMessage); Assert.Equal("First value", workspace.Profile!.Parameters.PolicyInputs!["testLabel"]!.GetValue<string>());
        bound.Single(f => f.Key == "testLabel").Value = "Second value";
        page.SaveInputsCommand.Execute(null);
        Assert.Empty(shell.ErrorMessage); Assert.Equal("Second value", workspace.Profile!.Parameters.PolicyInputs!["testLabel"]!.GetValue<string>());
        Assert.Null(workspace.Connection);
    }
}
