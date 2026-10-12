using Xunit;
namespace BuildStandard.Tests;
public sealed class ReaderBoundaryStructureTests
{
    [Fact] public void Production_host_has_no_script_or_command_bridge_and_explicit_deny_mapping()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../src/BuildStandard.App/Services/Reader"));
        var file=Path.Combine(root,"CatalogueReaderHost.cs"); Assert.True(File.Exists(file),"Production contained host is not implemented.");
        var source=File.ReadAllText(file);
        foreach(var forbidden in new[]{"ExecuteScript", "AddScriptToExecuteOnDocumentCreated", "CallDevToolsProtocol", "AddHostObjectToScript", "WebMessageReceived"}) Assert.DoesNotContain(forbidden,source,StringComparison.Ordinal);
        Assert.Contains("CoreWebView2HostResourceAccessKind.Deny",source,StringComparison.Ordinal);
        foreach(var setting in new[]{"IsScriptEnabled = false","AreHostObjectsAllowed = false","IsWebMessageEnabled = false","AreDevToolsEnabled = false"}) Assert.Contains(setting,source,StringComparison.Ordinal);
    }
}
