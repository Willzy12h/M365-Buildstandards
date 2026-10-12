using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Engine.Evidence;
using Xunit;

namespace BuildStandard.Tests;

[CollectionDefinition("Root environment compatibility", DisableParallelization = true)]
public sealed class RootEnvironmentCompatibilityCollection { }

[Collection("Root environment compatibility")]
public sealed class IdentityCompatibilityTests
{
    [Fact]
    public void Neutral_root_alias_selects_the_existing_evidence_root()
    {
        using var root = new TempRoot();
        using var environment = new RootEnvironment(root.Root, null);
        Assert.Equal(root.Root, ToolkitPaths.Resolve().Root);
    }

    [Fact]
    public void Conflicting_root_aliases_refuse_instead_of_selecting_other_evidence()
    {
        using var first = new TempRoot();
        using var second = new TempRoot();
        using var environment = new RootEnvironment(first.Root, second.Root);
        var refusal = Assert.Throws<ConfigurationException>(() => ToolkitPaths.Resolve());
        Assert.Contains("BUILDSTANDARD_ROOT", refusal.Message);
        Assert.Contains("BDIT_TOOLKIT_ROOT", refusal.Message);
    }

    [Fact]
    public void Explicit_root_wins_even_when_aliases_conflict()
    {
        using var first = new TempRoot();
        using var second = new TempRoot();
        using var selected = new TempRoot();
        using var environment = new RootEnvironment(first.Root, second.Root);
        Assert.Equal(selected.Root, ToolkitPaths.Resolve(selected.Root).Root);
    }

    [Fact]
    public void Invalid_neutral_alias_cannot_fall_back_to_valid_legacy_evidence()
    {
        using var root = new TempRoot();
        using var environment = new RootEnvironment(Path.Combine(root.Root, "missing"), root.Root);
        Assert.Throws<ConfigurationException>(() => ToolkitPaths.Resolve());
    }

    [Fact]
    public void Equivalent_canonical_aliases_use_one_root()
    {
        using var root = new TempRoot();
        using var environment = new RootEnvironment(Path.Combine(root.Root, "."), root.Root + Path.DirectorySeparatorChar);
        Assert.Equal(root.Root, ToolkitPaths.Resolve().Root);
    }

    [Fact]
    public void Legacy_alias_remains_supported_without_neutral_alias()
    {
        using var root = new TempRoot();
        using var environment = new RootEnvironment(null, root.Root);
        Assert.Equal(root.Root, ToolkitPaths.Resolve().Root);
    }

    [Fact]
    public void Explicit_blank_root_refuses_instead_of_adopting_an_ancestor()
    {
        using var root = new TempRoot();
        using var environment = new RootEnvironment(root.Root, null);
        Assert.Throws<ConfigurationException>(() => ToolkitPaths.Resolve(" "));
    }

    [Fact]
    public void Canonical_alias_equality_respects_the_platform_path_case_rules()
    {
        using var root = new TempRoot();
        var legacy = OperatingSystem.IsWindows() ? root.Root.ToUpperInvariant() : Path.Combine(root.Root, ".");
        using var environment = new RootEnvironment(root.Root, legacy);
        Assert.Equal(root.Root, ToolkitPaths.Resolve().Root);
    }

    [Fact]
    public void Historical_settings_remain_readable_without_rewriting_the_file()
    {
        using var root = new TempRoot();
        var json = "{\"companyName\":\"Historical organisation\",\"defaultStandardRelease\":\"2026.09.30\",\"allowMicrosoftGraphPowerShellFallback\":false}";
        File.WriteAllText(root.Paths.SettingsFile, json);
        var bytes = File.ReadAllBytes(root.Paths.SettingsFile);
        var loaded = ToolkitSettings.Load(root.Paths.SettingsFile);
        Assert.Equal("Historical organisation", loaded.CompanyName);
        Assert.Equal("2026.09.30", loaded.DefaultStandardRelease);
        Assert.False(loaded.AllowMicrosoftGraphPowerShellFallback);
        Assert.Equal(bytes, File.ReadAllBytes(root.Paths.SettingsFile));
    }

    [Fact]
    public void Legacy_process_lock_blocks_the_neutral_engine_then_releases_normally()
    {
        using var root = new TempRoot();
        var tenant = root.Paths.TenantDirectory(TestData.TenantA);
        Directory.CreateDirectory(tenant);
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        using (var legacyLease = new FileStream(Path.Combine(tenant, "policy-write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var refusal = Assert.Throws<SafetyViolationException>(() => store.AcquireTenantWriteLease(TestData.TenantA));
            Assert.True(EvidenceStore.IsTenantWriteLeaseRefusal(refusal));
        }
        using var lease = store.AcquireTenantWriteLease(TestData.TenantA);
        Assert.Single(Directory.EnumerateFiles(tenant, "*.lock"));
    }

    private sealed class RootEnvironment : IDisposable
    {
        private readonly string? _neutral = Environment.GetEnvironmentVariable("BUILDSTANDARD_ROOT");
        private readonly string? _legacy = Environment.GetEnvironmentVariable("BDIT_TOOLKIT_ROOT");
        public RootEnvironment(string? neutral, string? legacy)
        {
            Environment.SetEnvironmentVariable("BUILDSTANDARD_ROOT", neutral);
            Environment.SetEnvironmentVariable("BDIT_TOOLKIT_ROOT", legacy);
        }
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("BUILDSTANDARD_ROOT", _neutral);
            Environment.SetEnvironmentVariable("BDIT_TOOLKIT_ROOT", _legacy);
        }
    }
}
