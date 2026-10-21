using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Diagnostics;
using BuildStandard.Engine.Standards;
using Xunit;

namespace BuildStandard.Tests;

internal sealed class OfflinePackageFixture : IDisposable
{
    internal const string Private = "private-user private-machine token-private profile@example.invalid";
    private readonly TempRoot _root = new();
    internal string Root => _root.Root;
    internal string FilePath(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
    internal OfflinePackageFixture()
    {
        _root.WriteStandard("test.1.json", TestData.StandardJson);
        _root.WriteManifest();
        Write("config/toolkit.settings.json", ToolkitJson.Serialize(new ToolkitSettings { DefaultStandardRelease = "test.1" }));
        foreach (var name in new[] { "Start.cmd", "buildstandard.cmd", "bdit.cmd", "app/BuildStandard.App.exe", "app/BuildStandard.App.dll",
                     "app/BuildStandard.App.deps.json", "app/BuildStandard.App.runtimeconfig.json", "app/System.Private.CoreLib.dll" })
            Write(name, "Synthetic fixture only: " + name);
        Write("VERSION.json", ToolkitJson.Serialize(new
        {
            product = "M365 BuildStandard Tool", version = OfflinePrerequisiteService.ToolkitVersion, runtime = "win-x64",
            selfContained = true, dotnetRuntime = "10.0.1", dotnetSdk = "10.0.401", sourceCommit = new string('a', 40),
            sourceTreeDirty = false, defaultStandard = "test.1", builtAt = "2026-10-10T00:00:00Z", builtOn = Private,
            standards = new[] { "test.1.json" }, documents = Array.Empty<string>()
        }));
        RewriteInventory();
    }
    internal void Write(string relative, string text)
    {
        var path = FilePath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
    internal void RewriteInventory()
    {
        var lines = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(path => (Path: path, Relative: Path.GetRelativePath(Root, path).Replace('\\', '/')))
            // Build-Portable inventories every staged file, independently of the doctor's admission policy.
            .Where(file => file.Relative != "SHA256SUMS.txt")
            .OrderBy(file => file.Relative, StringComparer.Ordinal)
            .Select(file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file.Path))) + "  " + file.Relative);
        Write("SHA256SUMS.txt", string.Join('\n', lines) + "\n");
    }
    internal void UseShippedStandards()
    {
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards"));
        var destination = FilePath("standards");
        Directory.Delete(destination, recursive: true);
        foreach (var file in Directory.EnumerateFiles(source, "*.json", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        Write("config/toolkit.settings.json", ToolkitJson.Serialize(new ToolkitSettings { DefaultStandardRelease = "2026.09.30" }));
        var metadata = ToolkitJson.ParseObject(File.ReadAllText(FilePath("VERSION.json")));
        metadata["defaultStandard"] = "2026.09.30";
        metadata["standards"] = new JsonArray(Directory.EnumerateFiles(destination, "*.json")
            .Select(Path.GetFileName).Where(name => name != "manifest.json")
            .Select(name => (JsonNode?)JsonValue.Create(name)).ToArray());
        Write("VERSION.json", ToolkitJson.Serialize(metadata));
        RewriteInventory();
    }
    internal void RefreshStandardManifest() { _root.WriteManifest(); RewriteInventory(); }
    internal OfflinePrerequisiteService Service(OfflineDiagnosticIo? io = null, OfflineDiagnosticLimits? limits = null,
        IClock? clock = null, OfflineDiagnosticHost? host = null) =>
        new(limits ?? new(), clock ?? new FixedClock(), io ?? new(), host ?? new(true, _ => FilePath("absent-host.exe")));
    internal void NoProbes() => Assert.Empty(Directory.EnumerateFiles(Root, ".buildstandard-doctor-*", SearchOption.AllDirectories));
    public void Dispose() => _root.Dispose();
}

public sealed class OfflinePrerequisiteTests
{
    internal static OfflinePrerequisiteFinding Finding(OfflinePrerequisiteResult result, string id) => Assert.Single(result.Findings, f => f.Id == id);

    [Fact]
    public void Valid_package_uses_existing_validators_and_optional_limits_do_not_fail_Graph_readiness()
    {
        using var fixture = new OfflinePackageFixture();
        var result = fixture.Service().Check(fixture.Root);
        Assert.Equal(OfflinePrerequisiteStatus.Ready, result.Status);
        Assert.All(result.Findings.Where(f => f.Applicability == OfflineFindingApplicability.Required), f => Assert.Equal(OfflineFindingDisposition.Pass, f.Disposition));
        Assert.Equal(OfflineFindingDisposition.Unknown, Finding(result, "powershell.7").Disposition);
        Assert.Equal(OfflineFindingDisposition.Unknown, Finding(result, "exchange.access").Disposition);
        Assert.Equal(OfflineFindingDisposition.Unknown, Finding(result, "package.trust").Disposition);
        Assert.Contains("SDK is not required", Finding(result, "package.metadata").Explanation);
        fixture.NoProbes();
    }

    [Fact]
    public void Recursive_builder_inventory_with_shipped_catalogues_and_lineage_is_ready()
    {
        using var fixture = new OfflinePackageFixture();
        fixture.UseShippedStandards();
        var paths = ToolkitPaths.Resolve(fixture.Root);
        var catalogue = new StandardsLoader(paths, NullLog.Instance).Load("2026.09.30.json");
        var lineage = ReleaseLineage.Load(paths.StandardsDirectory, StandardsManifest.Load(paths.StandardsDirectory), catalogue.Release);
        Assert.NotNull(lineage);
        Assert.Equal(catalogue.IntegrityDigest, lineage.Target.Sha256);
        var inventory = File.ReadAllLines(fixture.FilePath("SHA256SUMS.txt")).Select(line => line[66..]).ToArray();
        Assert.Equal(Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(fixture.Root, path).Replace('\\', '/'))
            .Where(path => path != "SHA256SUMS.txt").Order(StringComparer.Ordinal), inventory);
        Assert.Contains("standards/lineage/manifest.json", inventory);
        Assert.Contains("standards/lineage/lineage-2026.09.30.json", inventory);

        var reads = new List<string>();
        var io = new OfflineDiagnosticIo { Read = path => { reads.Add(Path.GetRelativePath(fixture.Root, path).Replace('\\', '/')); return File.OpenRead(path); } };
        var result = fixture.Service(io).Check(fixture.Root);
        Assert.Equal(OfflineFindingDisposition.Pass, Finding(result, "package.integrity").Disposition);
        Assert.Contains("standards/lineage/manifest.json", reads);
        Assert.Contains("standards/lineage/lineage-2026.09.30.json", reads);
        Assert.Equal(OfflineFindingDisposition.Pass, Finding(result, "standard").Disposition);
        Assert.Equal(OfflinePrerequisiteStatus.Ready, result.Status);
        fixture.NoProbes();
    }

    [Theory]
    [InlineData("standards/lineage/manifest.json")]
    [InlineData("standards/lineage/lineage-2026.09.30.json")]
    public void Changed_lineage_bytes_fail_the_immutable_inventory_check(string relative)
    {
        using var fixture = new OfflinePackageFixture();
        fixture.UseShippedStandards();
        fixture.Write(relative, "Synthetic changed lineage bytes");
        var result = fixture.Service().Check(fixture.Root);
        Assert.Equal(OfflineFindingDisposition.Fail, Finding(result, "package.integrity").Disposition);
        Assert.Contains("differ from SHA256SUMS.txt", Finding(result, "package.integrity").Explanation);
        Assert.Equal(OfflinePrerequisiteStatus.NeedsAttention, result.Status);
        fixture.NoProbes();
    }

    [Fact]
    public void Shipped_neutral_example_is_in_scope_and_hashed_without_admitting_arbitrary_JSON()
    {
        using var fixture=new OfflinePackageFixture();
        const string relative="docs/examples/external-check-results.example.json";
        fixture.Write(relative,"{\"synthetic\":true}");fixture.RewriteInventory();
        Assert.Equal(OfflinePrerequisiteStatus.Ready,fixture.Service().Check(fixture.Root).Status);
        fixture.Write(relative,"{\"synthetic\":false}");
        Assert.Equal(OfflineFindingDisposition.Fail,Finding(fixture.Service().Check(fixture.Root),"package.integrity").Disposition);
        fixture.NoProbes();
    }

    [Theory]
    [InlineData("standards/lineage/unrelated.json")]
    [InlineData("standards/lineage/nested/lineage-2026.09.30.json")]
    [InlineData("standards/lineage/lineage-2026.09.30.txt")]
    [InlineData("extra/public.txt")]
    [InlineData("docs/examples/tenant-export.json")]
    [InlineData("docs/examples/external-check-results.other.json")]
    public void Safe_out_of_scope_inventory_has_a_distinct_reason_and_never_opens_its_content(string relative)
    {
        using var fixture = new OfflinePackageFixture();
        fixture.Write(relative, "Synthetic out-of-scope component");
        fixture.RewriteInventory();
        var opened = false;
        var io = new OfflineDiagnosticIo { Read = path =>
        {
            if (path == fixture.FilePath(relative)) { opened = true; throw new InvalidOperationException("Out-of-scope sentinel was opened."); }
            return File.OpenRead(path);
        } };
        var finding = Finding(fixture.Service(io).Check(fixture.Root), "package.integrity");
        Assert.False(opened);
        Assert.Equal(OfflineFindingDisposition.Fail, finding.Disposition);
        Assert.Contains("outside the supported package inspection scope", finding.Explanation);
        Assert.DoesNotContain("unsafe path", finding.Explanation);
        Assert.DoesNotContain(relative, finding.Explanation);
        fixture.NoProbes();
    }

    [Fact]
    public void Complete_inventory_cannot_admit_or_open_a_sensitive_app_file()
    {
        using var fixture = new OfflinePackageFixture();
        const string relative = "app/secrets.json";
        // Inventory is otherwise complete: omission checks cannot mask removal of the private-path guard.
        fixture.Write(relative, OfflinePackageFixture.Private);
        fixture.RewriteInventory();
        var opened = false;
        var io = new OfflineDiagnosticIo { Read = path =>
        {
            if (path == fixture.FilePath(relative)) { opened = true; throw new InvalidOperationException("PRIVATE_FILE_OPENED_SENTINEL"); }
            return File.OpenRead(path);
        } };
        OfflinePrerequisiteResult? result = null;
        var error = Record.Exception(() => result = fixture.Service(io).Check(fixture.Root));
        Assert.False(opened);
        Assert.Null(error);
        var finding = Finding(Assert.IsType<OfflinePrerequisiteResult>(result), "package.integrity");
        Assert.Equal(OfflineFindingDisposition.Fail, finding.Disposition);
        Assert.Contains("unsafe path", finding.Explanation);
        Assert.DoesNotContain(OfflinePackageFixture.Private, ToolkitJson.Serialize(result));
        fixture.NoProbes();
    }

    [Theory]
    [InlineData("standards/lineage/manifest.json")]
    [InlineData("standards/lineage/lineage-2026.09.30.json")]
    public void Admitted_lineage_files_still_refuse_links_before_opening(string relative)
    {
        using var fixture = new OfflinePackageFixture();
        fixture.UseShippedStandards();
        var opened = false;
        var io = new OfflineDiagnosticIo
        {
            Attributes = path => path == fixture.FilePath(relative) ? FileAttributes.ReparsePoint : File.GetAttributes(path),
            Read = path =>
            {
                if (path == fixture.FilePath(relative)) { opened = true; throw new InvalidOperationException("Linked lineage sentinel was opened."); }
                return File.OpenRead(path);
            }
        };
        var finding = Finding(fixture.Service(io).Check(fixture.Root), "package.integrity");
        Assert.False(opened);
        Assert.Equal(OfflineFindingDisposition.Fail, finding.Disposition);
        Assert.Contains("unsafe path, link or reparse point", finding.Explanation);
        fixture.NoProbes();
    }

    [Fact]
    public void Admitted_lineage_inventory_still_refuses_case_duplicates()
    {
        using var fixture = new OfflinePackageFixture();
        fixture.UseShippedStandards();
        var inventory = File.ReadAllText(fixture.FilePath("SHA256SUMS.txt"));
        var entry = inventory.Split('\n').Single(line => line.EndsWith("  standards/lineage/lineage-2026.09.30.json", StringComparison.Ordinal));
        fixture.Write("SHA256SUMS.txt", inventory + entry.ToUpperInvariant() + "\n");
        var finding = Finding(fixture.Service().Check(fixture.Root), "package.integrity");
        Assert.Equal(OfflineFindingDisposition.Fail, finding.Disposition);
        Assert.Contains("unsafe path", finding.Explanation);
        fixture.NoProbes();
    }

    [Theory]
    [InlineData("VERSION.json", "package.metadata")]
    [InlineData("SHA256SUMS.txt", "package.integrity")]
    [InlineData("app/BuildStandard.App.dll", "package.layout")]
    [InlineData("config/toolkit.settings.json", "settings")]
    [InlineData("standards/manifest.json", "standard")]
    public void Missing_required_components_are_unknown_not_pass(string path, string id)
    {
        using var fixture = new OfflinePackageFixture();
        File.Delete(fixture.FilePath(path));
        var result = fixture.Service().Check(fixture.Root);
        Assert.Equal(OfflinePrerequisiteStatus.NeedsAttention, result.Status);
        Assert.Equal(OfflineFindingDisposition.Unknown, Finding(result, id).Disposition);
    }

    [Fact]
    public void Missing_published_metadata_never_claims_source_checkout_is_package_ready()
    {
        using var fixture = new OfflinePackageFixture();
        File.Delete(fixture.FilePath("VERSION.json"));
        File.Delete(fixture.FilePath("SHA256SUMS.txt"));
        var result = fixture.Service().Check(fixture.Root);
        Assert.Equal(OfflinePrerequisiteStatus.NeedsAttention, result.Status);
        Assert.Equal(OfflineFindingDisposition.Unknown, Finding(result, "package.metadata").Disposition);
        Assert.Contains("source checkout", Finding(result, "package.metadata").NextStep);
    }

    [Fact]
    public void Mutable_settings_change_is_distinct_from_immutable_binary_mismatch()
    {
        using var fixture = new OfflinePackageFixture();
        fixture.Write("config/toolkit.settings.json", ToolkitJson.Serialize(new ToolkitSettings { CompanyName = OfflinePackageFixture.Private, DefaultStandardRelease = "test.1" }));
        var changedSettings = fixture.Service().Check(fixture.Root);
        Assert.Equal(OfflinePrerequisiteStatus.Ready, changedSettings.Status);
        Assert.Contains("differ from the packaged default", Finding(changedSettings, "settings").Explanation);
        Assert.Equal(OfflineFindingDisposition.Pass, Finding(changedSettings, "package.integrity").Disposition);
        fixture.Write("app/BuildStandard.App.dll", "changed binary");
        Assert.Equal(OfflineFindingDisposition.Fail, Finding(fixture.Service().Check(fixture.Root), "package.integrity").Disposition);
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("catalogue")]
    [InlineData("standardDigest")]
    [InlineData("standardDuplicate")]
    [InlineData("metadata")]
    public void Existing_document_and_integrity_contracts_refuse_invalid_synthetic_documents(string kind)
    {
        using var fixture = new OfflinePackageFixture();
        var id = "standard";
        switch (kind)
        {
            case "settings": fixture.Write("config/toolkit.settings.json", "{\"assessmentClientId\":\"" + OfflinePackageFixture.Private + "\"}"); id = "settings"; break;
            case "catalogue": fixture.Write("standards/test.1.json", TestData.StandardJson.Replace("\"schemaVersion\": 3", "\"schemaVersion\": 999", StringComparison.Ordinal)); fixture.RefreshStandardManifest(); break;
            case "standardDigest": fixture.Write("standards/test.1.json", "{}"); fixture.RewriteInventory(); break;
            case "standardDuplicate": fixture.Write("standards/manifest.json", "{\"files\":{\"test.1.json\":\"" + new string('a', 64) + "\",\"TEST.1.JSON\":\"" + new string('b', 64) + "\"}}"); fixture.RewriteInventory(); break;
            case "metadata": fixture.Write("VERSION.json", "{\"runtime\":\"" + OfflinePackageFixture.Private + "\"}"); id = "package.metadata"; break;
        }
        var result = fixture.Service().Check(fixture.Root);
        Assert.Equal(OfflinePrerequisiteStatus.NeedsAttention, result.Status);
        Assert.Equal(OfflineFindingDisposition.Fail, Finding(result, id).Disposition);
        Assert.DoesNotContain(OfflinePackageFixture.Private, ToolkitJson.Serialize(result));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/outside.txt")]
    [InlineData("C:/private.txt")]
    [InlineData("app/part.dll:private")]
    [InlineData("app/../outside.txt")]
    [InlineData("app//part.dll")]
    [InlineData("app\\part.dll")]
    [InlineData("app/part.dll ")]
    [InlineData("app/NUL.dll")]
    [InlineData("data/profiles.json")]
    [InlineData("logs/existing.log")]
    [InlineData("app/cache/token.json")]
    [InlineData("app/secrets.json")]
    [InlineData("app/token.json")]
    public void Hostile_inventory_is_refused_before_any_listed_content_is_hashed(string path)
    {
        using var fixture = new OfflinePackageFixture();
        fixture.Write("SHA256SUMS.txt", new string('a', 64) + "  " + path + "\n");
        var reads = new List<string>();
        var io = new OfflineDiagnosticIo { Read = file => { reads.Add(Path.GetRelativePath(fixture.Root, file).Replace('\\', '/')); return File.OpenRead(file); } };
        var result = fixture.Service(io).Check(fixture.Root);
        Assert.Equal(OfflineFindingDisposition.Fail, Finding(result, "package.integrity").Disposition);
        Assert.All(reads, read => Assert.Contains(read, new[] { "VERSION.json", "SHA256SUMS.txt", "config/toolkit.settings.json", "standards/manifest.json", "standards/test.1.json" }));
        Assert.DoesNotContain(path, ToolkitJson.Serialize(result));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("caseDuplicate")]
    [InlineData("malformed")]
    [InlineData("omission")]
    public void Inventory_rejects_ambiguous_malformed_or_incomplete_lists(string kind)
    {
        using var fixture = new OfflinePackageFixture();
        var text = File.ReadAllText(fixture.FilePath("SHA256SUMS.txt"));
        var first = text.Split('\n')[0];
        fixture.Write("SHA256SUMS.txt", kind switch
        {
            "duplicate" => text + first + "\n",
            "caseDuplicate" => text + first.ToUpperInvariant() + "\n",
            "malformed" => "not a checksum " + OfflinePackageFixture.Private,
            _ => first + "\n"
        });
        Assert.Equal(OfflineFindingDisposition.Fail, Finding(fixture.Service().Check(fixture.Root), "package.integrity").Disposition);
    }

    [Theory]
    [InlineData("manifestBytes")]
    [InlineData("entries")]
    [InlineData("file")]
    [InlineData("total")]
    [InlineData("metadata")]
    [InlineData("structured")]
    [InlineData("enumeration")]
    public void Every_resource_bound_reports_unknown_rather_than_truncated_success(string kind)
    {
        using var fixture = new OfflinePackageFixture();
        var limits = kind switch
        {
            "manifestBytes" => new OfflineDiagnosticLimits { ManifestBytes = 12 },
            "entries" => new OfflineDiagnosticLimits { ManifestEntries = 1 },
            "file" => new OfflineDiagnosticLimits { FileBytes = 12 },
            "total" => new OfflineDiagnosticLimits { TotalHashBytes = 12 },
            "metadata" => new OfflineDiagnosticLimits { MetadataBytes = 12 },
            "structured" => new OfflineDiagnosticLimits { StructuredFileBytes = 12 },
            _ => new OfflineDiagnosticLimits { InventoryEntries = 1 }
        };
        var id = kind switch { "metadata" => "package.metadata", "structured" => "settings", "enumeration" => "package.additional", _ => "package.integrity" };
        Assert.Equal(OfflineFindingDisposition.Unknown, Finding(fixture.Service(limits: limits).Check(fixture.Root), id).Disposition);
    }

    [Fact]
    public void Private_working_contents_and_unknown_files_are_never_read_or_recursively_enumerated()
    {
        using var fixture = new OfflinePackageFixture();
        foreach (var path in new[] { "data/profiles.json", "data/tenants/private/evidence.json", "logs/existing.log", "reports/private.json", "transfers/private.zip", "app/cache/token.json", "config/private.json", "private-folder/secret.txt" })
            fixture.Write(path, OfflinePackageFixture.Private);
        var reads = new List<string>(); var enumerated = new List<string>();
        var io = new OfflineDiagnosticIo
        {
            Read = path => { reads.Add(Path.GetRelativePath(fixture.Root, path).Replace('\\', '/')); return File.OpenRead(path); },
            Entries = path => { enumerated.Add(Path.GetRelativePath(fixture.Root, path).Replace('\\', '/')); return Directory.EnumerateFileSystemEntries(path); }
        };
        var result = fixture.Service(io).Check(fixture.Root);
        Assert.Equal(OfflinePrerequisiteStatus.Ready, result.Status);
        Assert.Equal(OfflineFindingDisposition.Unknown, Finding(result, "package.additional").Disposition);
        Assert.DoesNotContain(reads, path => path.StartsWith("data/", StringComparison.Ordinal) || path.Contains("private", StringComparison.Ordinal) || path.Contains("cache", StringComparison.Ordinal));
        Assert.DoesNotContain(enumerated, path => path.StartsWith("data", StringComparison.Ordinal) || path.StartsWith("logs", StringComparison.Ordinal) || path.StartsWith("reports", StringComparison.Ordinal) || path.Contains("cache", StringComparison.Ordinal) || path.Contains("private-folder", StringComparison.Ordinal));
        Assert.DoesNotContain(OfflinePackageFixture.Private, ToolkitJson.Serialize(result));
        fixture.NoProbes();
    }

    [Theory]
    [InlineData("file")]
    [InlineData("ancestor")]
    [InlineData("manifest")]
    [InlineData("probe")]
    public void Links_refuse_content_and_probe_admission(string kind)
    {
        using var fixture = new OfflinePackageFixture();
        using var outside = new TempRoot();
        var outsideFile = Path.Combine(outside.Root, "private.txt"); File.WriteAllText(outsideFile, OfflinePackageFixture.Private);
        var linkRelative = kind switch { "ancestor" => "app", "probe" => "logs", "manifest" => "SHA256SUMS.txt", _ => "app/BuildStandard.App.dll" };
        // Actual Unix links; a reparse attribute seam on Windows avoids requiring Developer Mode/admin rights in tests.
        if (!OperatingSystem.IsWindows())
        {
            if (kind is "ancestor" or "probe") { Directory.Delete(fixture.FilePath(linkRelative), true); Directory.CreateSymbolicLink(fixture.FilePath(linkRelative), outside.Root); }
            else { File.Delete(fixture.FilePath(linkRelative)); File.CreateSymbolicLink(fixture.FilePath(linkRelative), outsideFile); }
        }
        var reads = new List<string>(); var probes = new List<string>();
        var io = new OfflineDiagnosticIo
        {
            Attributes = path => File.GetAttributes(path) | (OperatingSystem.IsWindows() && path == fixture.FilePath(linkRelative) ? FileAttributes.ReparsePoint : 0),
            Read = path => { reads.Add(path); return File.OpenRead(path); },
            CreateProbe = path => { probes.Add(path); return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
        };
        var result = fixture.Service(io).Check(fixture.Root);
        Assert.Equal(OfflinePrerequisiteStatus.NeedsAttention, result.Status);
        Assert.Equal(OfflineFindingDisposition.Fail, Finding(result, kind == "probe" ? "writable.logs" : kind == "ancestor" ? "package.layout" : "package.integrity").Disposition);
        Assert.DoesNotContain(reads, path => path.StartsWith(outside.Root, StringComparison.Ordinal) || (kind == "manifest" && path == fixture.FilePath("SHA256SUMS.txt")));
        Assert.DoesNotContain(probes, path => kind == "probe" && path.StartsWith(fixture.FilePath("logs") + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.Equal(OfflinePackageFixture.Private, File.ReadAllText(outsideFile));
    }

    [Fact]
    public void Root_or_root_ancestor_link_is_refused_before_reads_or_probes()
    {
        using var fixture = new OfflinePackageFixture();
        using var container = new TempRoot();
        var alias = Path.Combine(container.Root, "linked-root");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(Path.Combine(alias, "config"));
        else Directory.CreateSymbolicLink(alias, fixture.Root);
        var io = new OfflineDiagnosticIo { Attributes = path => File.GetAttributes(path) | (path == alias ? FileAttributes.ReparsePoint : 0), Read = _ => throw new Exception("should not read"), CreateProbe = _ => throw new Exception("should not probe") };
        Assert.Throws<OfflineDiagnosticInputException>(() => fixture.Service(io).Check(alias));
        var child = Path.Combine(alias, "config");
        Assert.Throws<OfflineDiagnosticInputException>(() => fixture.Service(io).Check(child));
    }

    [Fact]
    public void Existing_probe_collision_is_neither_overwritten_nor_deleted()
    {
        using var fixture = new OfflinePackageFixture();
        const string name = ".buildstandard-doctor-collision.tmp";
        foreach (var component in new[] { "config", "data", "logs", "reports" }) fixture.Write(component + "/" + name, OfflinePackageFixture.Private);
        var result = fixture.Service(new() { ProbeName = () => name }).Check(fixture.Root);
        Assert.Equal(OfflinePrerequisiteStatus.NeedsAttention, result.Status);
        foreach (var component in new[] { "config", "data", "logs", "reports" })
        {
            Assert.Equal(OfflineFindingDisposition.Unknown, Finding(result, "writable." + component).Disposition);
            Assert.Equal(OfflinePackageFixture.Private, File.ReadAllText(fixture.FilePath(component + "/" + name)));
        }
    }

    [Fact]
    public void Missing_writable_folders_probe_only_existing_parent_and_do_not_create_destinations()
    {
        using var fixture = new OfflinePackageFixture();
        foreach (var component in new[] { "data", "logs", "reports" }) Directory.Delete(fixture.FilePath(component), true);
        var result = fixture.Service().Check(fixture.Root);
        Assert.Equal(OfflinePrerequisiteStatus.Ready, result.Status);
        foreach (var component in new[] { "data", "logs", "reports" })
        {
            Assert.False(Directory.Exists(fixture.FilePath(component)));
            Assert.Contains("parent-create capability only", Finding(result, "writable." + component).Explanation);
        }
        fixture.NoProbes();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cleanup_failure_is_honest_even_when_delete_returns_without_removing_owned_file(bool noOp)
    {
        using var fixture = new OfflinePackageFixture();
        var io = new OfflineDiagnosticIo { DeleteProbe = _ => { if (!noOp) throw new UnauthorizedAccessException(OfflinePackageFixture.Private); } };
        var result = fixture.Service(io).Check(fixture.Root);
        Assert.Equal(OfflinePrerequisiteStatus.NeedsAttention, result.Status);
        Assert.Equal(OfflineFindingDisposition.Fail, Finding(result, "cleanup.config").Disposition);
        Assert.NotEmpty(Directory.EnumerateFiles(fixture.Root, ".buildstandard-doctor-*", SearchOption.AllDirectories));
        Assert.DoesNotContain(OfflinePackageFixture.Private, ToolkitJson.Serialize(result));
    }

    [Theory]
    [InlineData("before")]
    [InlineData("hash")]
    [InlineData("probe")]
    public void Cancellation_retains_completed_checks_marks_rest_unknown_and_cleans_owned_probe(string stage)
    {
        using var fixture = new OfflinePackageFixture(); using var cts = new CancellationTokenSource();
        if (stage == "before") cts.Cancel();
        var io = new OfflineDiagnosticIo
        {
            Read = path => { var stream = File.OpenRead(path); if (stage == "hash" && path.EndsWith("App.dll", StringComparison.Ordinal)) cts.Cancel(); return stream; },
            CreateProbe = path => { var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); if (stage == "probe") cts.Cancel(); return stream; }
        };
        var result = fixture.Service(io).Check(fixture.Root, cts.Token);
        Assert.Equal(OfflinePrerequisiteStatus.Cancelled, result.Status);
        Assert.Equal(OfflineFindingDisposition.Unknown, Finding(result, "writable.reports").Disposition);
        if (stage != "before") Assert.Equal(OfflineFindingDisposition.Pass, Finding(result, "package.metadata").Disposition);
        if (stage == "probe") Assert.Equal(OfflineFindingDisposition.Pass, Finding(result, "cleanup.config").Disposition);
        fixture.NoProbes();
    }

    [Fact]
    public void Aggregate_hash_budget_includes_repeated_catalogue_and_mutable_settings_digests()
    {
        using var fixture = new OfflinePackageFixture();
        var immutableBytes = File.ReadAllLines(fixture.FilePath("SHA256SUMS.txt"))
            .Select(line => line[66..]).Where(path => path != "config/toolkit.settings.json")
            .Sum(path => new FileInfo(fixture.FilePath(path)).Length);
        var result = fixture.Service(limits: new() { TotalHashBytes = immutableBytes }).Check(fixture.Root);
        Assert.Equal(OfflineFindingDisposition.Pass, Finding(result, "package.integrity").Disposition);
        Assert.Equal(OfflineFindingDisposition.Unknown, Finding(result, "settings").Disposition);
        Assert.Equal(OfflineFindingDisposition.Unknown, Finding(result, "standard").Disposition);
        Assert.Equal(OfflinePrerequisiteStatus.NeedsAttention, result.Status);
    }
}
