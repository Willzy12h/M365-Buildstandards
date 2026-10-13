namespace BuildStandard.App.Services.Reader;
public static class ReaderRuntimeConfiguration
{
    public static readonly IReadOnlyList<string> OverrideNames = Array.AsReadOnly(new[] {
        "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", "WEBVIEW2_USER_DATA_FOLDER", "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
        "WEBVIEW2_RELEASE_CHANNEL_PREFERENCE", "WEBVIEW2_RELEASE_CHANNELS", "WEBVIEW2_CHANNEL_SEARCH_KIND",
        "WEBVIEW2_WAIT_FOR_SCRIPT_DEBUGGER", "WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER" });
    public static void RequireCompatible(Func<string,string?> readEnvironment, Func<bool> registryOverride)
    {
        if (OverrideNames.Any(name => !string.IsNullOrEmpty(readEnvironment(name))) || registryOverride())
            throw new InvalidOperationException("Reader Runtime/profile/debugger overrides require separate review. Native catalogue and exports remain available.");
    }
}
