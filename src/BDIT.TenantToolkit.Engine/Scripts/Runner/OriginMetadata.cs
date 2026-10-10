using BDIT.TenantToolkit.Core;

namespace BDIT.TenantToolkit.Engine.Scripts.Runner;

/// <summary>
/// Carries the application's own download-origin marking (the Windows Zone.Identifier stream) onto the files it materialises
/// for a run, so script policy judges them as it would the distribution. It never removes, lowers or invents a marking:
/// when the source has none, nothing is written. If a marking exists but cannot be carried, the run is refused rather
/// than launched without it.
/// </summary>
internal static class OriginMetadata
{
    private const string Stream = ":Zone.Identifier";

    public static void Inherit(string? source, params string[] targets)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(source) || !File.Exists(source)) return;
        byte[] marking;
        try { marking = File.ReadAllBytes(source + Stream); }
        catch (FileNotFoundException) { return; }
        catch (IOException) { throw new ConfigurationException("The application's download marking could not be read, so the run was not started."); }
        catch (UnauthorizedAccessException) { throw new ConfigurationException("The application's download marking could not be read, so the run was not started."); }
        foreach (var target in targets)
        {
            try { File.WriteAllBytes(target + Stream, marking); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            { throw new ConfigurationException("The application's download marking could not be kept on the runner files, so the run was not started."); }
        }
    }
}
