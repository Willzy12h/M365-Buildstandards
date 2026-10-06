using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class StandardDefinitionExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static StandardCatalogue Load(TempRoot root, string? json = null)
    {
        root.WriteStandard("test.json", json ?? TestData.StandardJson);
        root.WriteManifest();
        return new StandardsLoader(root.Paths, NullLog.Instance).Load("test.json");
    }

    [Fact]
    public void Complete_set_preserves_exact_catalogue_bytes_including_unknown_metadata_and_loads_with_its_manifest()
    {
        using var root = new TempRoot();
        var node = ToolkitJson.ParseObject(TestData.StandardJson);
        node["syntheticFutureMetadata"] = "SYNTHETIC-CATALOGUE-EXTENSION";
        var text = node.ToJsonString(ToolkitJson.Options) + "\n";
        var standard = Load(root, text);
        var zip = new StandardDefinitionExporter(root.Paths).ExportSet(standard, Now);
        using var archive = ZipFile.OpenRead(zip);
        using var input = archive.GetEntry("standards/test.json")!.Open();
        using var copy = new MemoryStream();
        input.CopyTo(copy);
        Assert.Equal(File.ReadAllBytes(Path.Combine(root.Paths.StandardsDirectory, "test.json")), copy.ToArray());
        using var restored = new TempRoot();
        ZipFile.ExtractToDirectory(zip, restored.Root, overwriteFiles: true);
        var roundTrip = new StandardsLoader(restored.Paths, NullLog.Instance).Load("test.json");
        Assert.Equal(standard.IntegrityDigest, roundTrip.IntegrityDigest);
        Assert.Equal(standard.Controls.Select(c => c.Id), roundTrip.Controls.Select(c => c.Id));
        foreach (var line in File.ReadAllLines(Path.Combine(restored.Root, "SHA256SUMS.txt")))
        {
            var parts = line.Split("  ", 2);
            Assert.Equal(parts[0], CanonicalJson.Sha256Hex(File.ReadAllBytes(Path.Combine(restored.Root, parts[1]))));
        }
        Assert.StartsWith(CanonicalJson.Sha256Hex(File.ReadAllBytes(zip)), File.ReadAllText(zip + ".sha256"), StringComparison.Ordinal);
        Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("data/", StringComparison.Ordinal) || e.FullName.StartsWith("config/", StringComparison.Ordinal));
    }

    [Fact]
    public void Defaults_and_settings_distinguish_fixed_values_defaults_identity_inputs_and_explicit_null_or_empty()
    {
        var standard = TestData.Standard();
        standard.Parameters.Add(new ParameterDefinition { Key = "window", Label = "Window", Type = "integer", Default = JsonValue.Create(5), ReviewedOn = "2026-09-30" });
        var control = standard.Controls[0];
        control.Payload = ToolkitJson.ParseObject("""{"state":"disabled","window":"{{window}}","operator":"{{tenantId}}","nullValue":null,"empty":[],"enabled":false,"a/b~c":0}""");
        var before = CanonicalJson.Sha256Value(standard);
        var settings = StandardSpecificationDocuments.Settings(control, standard, Now);
        Assert.Contains(settings, s => s.Path == "/window" && s.Value == "5" && s.Template == "\"{{window}}\"" && s.Origin == "Reviewable shipped default");
        Assert.Contains(settings, s => s.Path == "/operator" && s.Value == "\"{{tenantId}}\"" && s.Origin.Contains("unresolved", StringComparison.Ordinal));
        Assert.Contains(settings, s => s.Path == "/state" && s.Value == "\"disabled\"" && s.Origin == "Fixed catalogue setting");
        Assert.Contains(settings, s => s.Path == "/nullValue" && s.Value == "null");
        Assert.Contains(settings, s => s.Path == "/empty" && s.Value == "[]");
        Assert.Contains(settings, s => s.Path == "/enabled" && s.Value == "false");
        Assert.Contains(settings, s => s.Path == "/a~1b~0c" && s.Value == "0");
        Assert.Equal(before, CanonicalJson.Sha256Value(standard));
    }

    [Fact]
    public void Current_specification_covers_all_controls_parameters_interfaces_and_inert_candidate_state()
    {
        var file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.09.30.json"));
        var standard = StandardsLoader.Parse(File.ReadAllText(file), "2026.09.30.json");
        var html = StandardSpecificationDocuments.Html(standard, Now);
        var markdown = StandardSpecificationDocuments.Markdown(standard, Now);
        var matrix = CapabilityDocuments.Markdown(standard);
        Assert.Equal(93, Regex.Matches(html, "<article id=").Count);
        Assert.Equal(93, Regex.Matches(html, "<section id=\"settings-").Count);
        foreach (var control in standard.Controls)
        {
            Assert.Contains("id=\"settings-" + control.Id + "\"", html);
            Assert.Contains("|" + control.Id + " — ", matrix);
        }
        Assert.Equal(93, Regex.Matches(matrix, @"\|Not run live\|").Count);
        // CLA-20261006-13: CA settings show the planner's safety additions; non-CA settings do not.
        var caSection = markdown[markdown.IndexOf("### CA-001 — ", StringComparison.Ordinal)..];
        Assert.Contains("Plan-time safety additions", caSection[..caSection.IndexOf("\n### ", 5, StringComparison.Ordinal)]);
        var intuneSection = markdown[markdown.IndexOf("### CMP-WIN-001 — ", StringComparison.Ordinal)..];
        Assert.DoesNotContain("Plan-time safety additions", intuneSection[..intuneSection.IndexOf("\n### ", 5, StringComparison.Ordinal)]);
        Assert.Contains("Default staleness evaluated on", markdown);
        Assert.Contains("Application.ReadWrite.All", matrix);
        foreach (var parameter in standard.Parameters) { Assert.Contains(parameter.Key, html); Assert.Contains(parameter.Key, markdown); }
        foreach (var collection in standard.Collections) Assert.Contains(collection.Value.Scope, html);
        Assert.Contains("Reviewable shipped default", html);
        Assert.Contains("Client input required; unresolved", html);
        Assert.Contains("Intended production state", html);
        Assert.Contains("disabled", html);
        Assert.Contains("unassigned", markdown);
        Assert.Contains("Manual or observed-only requirement", html);
    }

    [Fact]
    public void Capability_matrix_reports_the_engine_route_rather_than_the_catalogue_mode()
    {
        // CLA-20261006-02: controls assessed from equivalence signals or a separate Exchange/Purview capture were
        // published as "Manual". The negative cases keep genuinely manual controls labelled as such.
        var file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.09.30.json"));
        var standard = StandardsLoader.Parse(File.ReadAllText(file), "2026.09.30.json");
        var rows = CapabilityDocuments.Markdown(standard).Split('\n').Where(l => l.StartsWith("|", StringComparison.Ordinal) && l.Contains(" — ", StringComparison.Ordinal))
            .ToDictionary(l => l[1..l.IndexOf(" — ", StringComparison.Ordinal)], l => l.Split('|'));
        Assert.Equal(93, rows.Count);
        foreach (var id in new[] { "ID-002", "ID-003", "ENR-001", "CMP-001" }) Assert.Equal("Equivalence evidence; engineer confirms", rows[id][2]);
        foreach (var id in new[] { "EX-001", "EX-008", "PUR-001", "PUR-002" }) Assert.Equal("Exchange/Purview read capture (separate connection)", rows[id][2]);
        Assert.Equal("Settings comparison", rows["CA-001"][2]);
        Assert.Equal("Manual only", rows["ID-001"][2]);
        Assert.Equal(standard.Controls.Count(c => CapabilityDocuments.ReadRoute(standard, c) == "Manual only"), rows.Values.Count(r => r[2] == "Manual only"));
        foreach (var control in standard.Controls.Where(c => c.Licence.ServicePlans.Count > 0))
            Assert.Equal(string.Join(", ", control.Licence.ServicePlans), rows[control.Id][4]);
    }

    [Theory]
    [InlineData("2026.09.3")][InlineData("2026.09.4")][InlineData("2026.09.5")][InlineData("2026.09.6")]
    [InlineData("2026.09.7")][InlineData("2026.09.8")][InlineData("2026.09.9")][InlineData("2026.09.10")]
    [InlineData("2026.09.11")][InlineData("2026.09.12")][InlineData("2026.09.30")]
    public void Every_published_release_exports_without_changing_valid_optional_empty_defaults(string release)
    {
        using var root = new TempRoot();
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/" + release + ".json"));
        root.WriteStandard(release + ".json", File.ReadAllText(source)); root.WriteManifest();
        var standard = new StandardsLoader(root.Paths, NullLog.Instance).Load(release + ".json");
        var zip = new StandardDefinitionExporter(root.Paths).ExportSet(standard, Now);
        Assert.True(File.Exists(zip));
        if (standard.Parameters.Any(p => p.Key == "espBlockingAppIds" && p.Default is JsonArray { Count: 0 })
            && standard.Controls.Any(c => c.Payload?.ToJsonString().Contains("{{espBlockingAppIds}}", StringComparison.Ordinal) == true))
        {
            var settings = standard.Controls.SelectMany(c => StandardSpecificationDocuments.Settings(c, standard, Now));
            Assert.Contains(settings, s => s.Template == "\"{{espBlockingAppIds}}\"" && s.Value == "[]" && s.Origin.StartsWith("Reviewable shipped default", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Rendering_encodes_settings_and_names_and_marks_stale_defaults_without_inventing_manual_values()
    {
        var standard = TestData.Standard();
        standard.Parameters.Add(new ParameterDefinition { Key = "stale", Label = "<script>synthetic</script>", Type = "string", Default = JsonValue.Create("old"), ReviewedOn = "2020-01-01" });
        standard.Controls[0].Payload!["description"] = "<script>synthetic</script>";
        var html = StandardSpecificationDocuments.Html(standard, Now);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("review needed", html);
        var manual = standard.Controls.First(c => c.Payload is null);
        Assert.Empty(StandardSpecificationDocuments.Settings(manual, standard, Now));
    }

    [Theory]
    [InlineData("memory")][InlineData("source")][InlineData("unverified")]
    public void Changed_or_unverified_definition_is_refused_before_outputs_are_created(string change)
    {
        using var root = new TempRoot();
        var standard = Load(root);
        if (change == "memory") standard.Controls[0].Name = "Changed in memory";
        else if (change == "source") File.AppendAllText(Path.Combine(root.Paths.StandardsDirectory, "test.json"), " ");
        else standard.IntegrityDigest = "";
        Assert.Throws<IntegrityException>(() => new StandardDefinitionExporter(root.Paths).ExportSet(standard, Now));
        Assert.Empty(Directory.EnumerateFiles(root.Paths.ReportsDirectory, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(ExportFormat.Html)][InlineData(ExportFormat.Markdown)][InlineData(ExportFormat.Json)]
    public void Individual_exports_are_supported_without_tenant_context(ExportFormat format)
    {
        using var root = new TempRoot();
        var standard = Load(root);
        var file = new StandardDefinitionExporter(root.Paths).Export(standard, format, Now);
        Assert.True(File.Exists(file));
        Assert.Matches(@"standard-definition-test\.1-\d{8}T\d{6}Z-[0-9a-f]{8}\.(html|md|json)$", Path.GetFileName(file));
        Assert.Equal(CanonicalJson.Sha256Hex(File.ReadAllBytes(file)) + "  " + Path.GetFileName(file) + "\n", File.ReadAllText(file + ".sha256"));
        if (format == ExportFormat.Json) Assert.Equal(File.ReadAllBytes(Path.Combine(root.Paths.StandardsDirectory, "test.json")), File.ReadAllBytes(file));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StandardDefinitionExporter(root.Paths).Export(standard, ExportFormat.Xlsx, Now));
    }
}
