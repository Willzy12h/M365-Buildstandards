using BuildStandard.App.Services.Reader;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ReaderRuntimeConfigurationTests
{
    [Theory]
    [InlineData("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER")]
    [InlineData("WEBVIEW2_USER_DATA_FOLDER")]
    [InlineData("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS")]
    [InlineData("WEBVIEW2_RELEASE_CHANNELS")]
    [InlineData("WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER")]
    public void Reader_refuses_runtime_profile_or_debugger_overrides_without_echoing_values(string name)
    {
        var error=Assert.Throws<InvalidOperationException>(()=>ReaderRuntimeConfiguration.RequireCompatible(n=>n==name?"synthetic private override value":null,()=>false));
        Assert.DoesNotContain("synthetic private override value",error.Message);
    }
    [Fact] public void Applicable_registry_override_also_refuses_before_browser_creation()
        =>Assert.Throws<InvalidOperationException>(()=>ReaderRuntimeConfiguration.RequireCompatible(_=>null,()=>true));
    [Fact] public void Ordinary_defaults_remain_available() => ReaderRuntimeConfiguration.RequireCompatible(_=>null,()=>false);
}
