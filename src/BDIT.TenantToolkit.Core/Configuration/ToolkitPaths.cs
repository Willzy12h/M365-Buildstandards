namespace BDIT.TenantToolkit.Core.Configuration;

/// <summary>
/// Resolves the portable folder layout. Everything the toolkit reads or writes lives under one root:
///   app/        the application (published output)
///   standards/  M365 Build Standard releases plus manifest.json
///   config/     toolkit.settings.json
///   data/       profiles and per-tenant evidence (never shared between tenants)
///   logs/       diagnostic logs
///   reports/    exported reports
///   transfers/  sensitive evidence backups and separate restores (created when first used)
/// </summary>
public sealed class ToolkitPaths
{
    public string Root { get; }
    public string AppDirectory { get; }
    public string StandardsDirectory => Path.Combine(Root, "standards");
    public string StandardsManifestFile => Path.Combine(StandardsDirectory, "manifest.json");
    public string ConfigDirectory => Path.Combine(Root, "config");
    public string SettingsFile => Path.Combine(ConfigDirectory, "toolkit.settings.json");
    public string DataDirectory => Path.Combine(Root, "data");
    public string ProfilesFile => Path.Combine(DataDirectory, "profiles.json");
    public string TenantsDirectory => Path.Combine(DataDirectory, "tenants");
    public string LogsDirectory => Path.Combine(Root, "logs");
    public string ReportsDirectory => Path.Combine(Root, "reports");
    public string DocsDirectory => Path.Combine(Root, "docs");
    /// <summary>Sensitive evidence backups and separate restores. Kept apart from shareable reports; created on demand.</summary>
    public string TransfersDirectory => Path.Combine(Root, "transfers");

    private ToolkitPaths(string root, string appDirectory)
    {
        Root = root;
        AppDirectory = appDirectory;
    }

    public string TenantDirectory(string tenantId)
    {
        if (!Models.ProfileValidator.IsGuid(tenantId)) throw new ConfigurationException("Tenant ID must be a GUID.");
        return Path.Combine(TenantsDirectory, tenantId.ToLowerInvariant());
    }

    /// <summary>
    /// Root resolution order: explicit override, BUILDSTANDARD_ROOT (with BDIT_TOOLKIT_ROOT as a legacy alias),
    /// then the nearest ancestor of the application directory containing standards (portable layout or source).
    /// Conflicting configured aliases are refused.
    /// </summary>
    public static ToolkitPaths Resolve(string? overrideRoot = null)
    {
        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? candidate;
        if (overrideRoot is not null)
        {
            if (string.IsNullOrWhiteSpace(overrideRoot))
                throw new ConfigurationException("The explicit toolkit root is empty. Select the extracted package root.");
            candidate = overrideRoot;
        }
        else
        {
            var neutral = Environment.GetEnvironmentVariable("BUILDSTANDARD_ROOT");
            var legacy = Environment.GetEnvironmentVariable("BDIT_TOOLKIT_ROOT");
            if (string.IsNullOrWhiteSpace(neutral)) neutral = null;
            if (string.IsNullOrWhiteSpace(legacy)) legacy = null;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (neutral is not null && legacy is not null
                && !string.Equals(CanonicalRoot(neutral), CanonicalRoot(legacy), comparison))
                throw new ConfigurationException("BUILDSTANDARD_ROOT and BDIT_TOOLKIT_ROOT select different roots. Use an explicit --root or correct the aliases before opening evidence.");
            candidate = neutral ?? legacy;
        }
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var full = CanonicalRoot(candidate);
            if (!Directory.Exists(Path.Combine(full, "standards")))
                throw new ConfigurationException($"The toolkit root '{full}' does not contain a standards folder.");
            return new ToolkitPaths(full, appDir);
        }

        var dir = new DirectoryInfo(appDir);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "standards")))
                return new ToolkitPaths(dir.FullName, appDir);
        }
        throw new ConfigurationException(
            "Could not locate the toolkit root (a folder containing 'standards'). Extract the complete portable package and start it with Start.cmd, or set BUILDSTANDARD_ROOT (BDIT_TOOLKIT_ROOT is a supported legacy alias).");
    }

    private static string CanonicalRoot(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public void EnsureWritableFolders()
    {
        foreach (var d in new[] { ConfigDirectory, DataDirectory, TenantsDirectory, LogsDirectory, ReportsDirectory })
            Directory.CreateDirectory(d);
    }
}
