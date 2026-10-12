using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Standards;
using BuildStandard.Engine.Reports;
using Xunit;
using Xunit.Abstractions;

namespace BuildStandard.Tests;

public sealed class CatalogueDocumentTests(ITestOutputHelper output)
{
    [Fact]
    public void Catalogue_html_has_a_static_default_deny_content_policy_before_any_style()
    {
        var html = EngineerStandardDocuments.Html(TestData.Standard(), EngineerDocumentKind.BuildStandard);
        Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
        Assert.Contains("script-src 'none'", html, StringComparison.Ordinal);
        Assert.Contains("connect-src 'none'", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("Content-Security-Policy", StringComparison.Ordinal) < html.IndexOf("<style", StringComparison.Ordinal));
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Catalogue_html_export_refuses_unverified_source_instead_of_creating_a_reader_reference()
    {
        using var root = new TempRoot();
        var exporter = new ReportExporter(root.Paths, "");
        Assert.Throws<IntegrityException>(() => exporter.ExportEngineerStandard(TestData.Standard(), EngineerDocumentKind.BuildStandard, ExportFormat.Html));
        Assert.Empty(Directory.EnumerateFiles(root.Paths.ReportsDirectory));
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 11, 0, 0, TimeSpan.Zero);
    private static StandardCatalogue Load(TempRoot root, string? json = null)
    {
        root.WriteStandard("test.json", json ?? TestData.StandardJson); root.WriteManifest();
        return new StandardsLoader(root.Paths, NullLog.Instance).Load("test.json");
    }

    [Fact]
    public void Descriptor_owns_exact_UTF8_bytes_and_old_document_saves_after_source_replacement()
    {
        using var root = new TempRoot(); var standard = Load(root);
        var document = new CatalogueDocumentService(root.Paths, () => Now).Create(standard);
        var original = document.CopyHtmlBytes(); var copy = document.CopyHtmlBytes(); copy[0] ^= 1;
        Assert.Equal(original, document.CopyHtmlBytes());
        Assert.Equal(CanonicalJson.Sha256Hex(original), document.HtmlSha256);
        output.WriteLine($"Synthetic catalogue SHA256={document.CatalogueDigest}; HTML SHA256={document.HtmlSha256}; generated={document.GeneratedAt:O}; presentation={document.PresentationVersion}");
        Assert.NotEqual(document.CatalogueDigest, document.HtmlSha256);
        Assert.Equal(standard.Release, document.CatalogueRelease);
        Assert.Equal(standard.IntegrityDigest, document.CatalogueDigest);
        Assert.Equal("BuildStandard", document.Kind);
        Assert.Equal(Now, document.GeneratedAt);
        Assert.Equal(EngineerStandardDocuments.PresentationVersion, document.PresentationVersion);
        Assert.False(original.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        var html = new UTF8Encoding(false, true).GetString(original);
        Assert.Contains(Now.ToString("O"), html, StringComparison.Ordinal);
        Assert.Contains(document.CatalogueDigest, html, StringComparison.Ordinal);
        Assert.Contains(document.PresentationVersion, html, StringComparison.Ordinal);
        foreach (var control in standard.Controls) Assert.Equal("#" + control.Id, document.RequireAnchor(control.Id));
        Assert.Throws<ConfigurationException>(() => document.RequireAnchor("missing-control"));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)document.Anchors).Add("invented"));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)document.RendererReferences).Add("https://evil.example"));
        // Save uses admitted bytes, not a freshly regenerated mutable/current catalogue.
        standard.Controls[0].Name = "Changed in memory";
        root.WriteStandard("test.json", "{}\n");
        var saved = new ReportExporter(root.Paths, "SYNTHETIC CLIENT").SaveHtml(document);
        Assert.Equal(original, File.ReadAllBytes(saved)); Assert.Equal(document.HtmlSha256, CanonicalJson.Sha256Hex(File.ReadAllBytes(saved)));
        Assert.DoesNotContain("SYNTHETIC CLIENT", File.ReadAllText(saved), StringComparison.Ordinal);
        Assert.ThrowsAny<ToolkitException>(() => new CatalogueDocumentService(root.Paths).Create(standard));
    }

    [Theory]
    [InlineData("model")]
    [InlineData("file")]
    [InlineData("manifest")]
    [InlineData("unverified")]
    public void Creation_refuses_invalid_or_changed_catalogue_source(string change)
    {
        using var root = new TempRoot(); var standard = Load(root);
        switch (change)
        {
            case "model": standard.Controls[0].Name = "Changed"; break;
            case "file": File.AppendAllText(Path.Combine(root.Paths.StandardsDirectory, "test.json"), " "); break;
            case "manifest": File.Delete(Path.Combine(root.Paths.StandardsDirectory, "manifest.json")); break;
            case "unverified": standard.IntegrityDigest = ""; break;
        }
        Assert.ThrowsAny<ToolkitException>(() => new CatalogueDocumentService(root.Paths).Create(standard));
        Assert.Empty(Directory.EnumerateFiles(root.Paths.ReportsDirectory));
    }

    [Fact]
    public void Generated_identity_distinguishes_generation_time_and_retains_encoded_content_and_emitted_references()
    {
        using var root = new TempRoot(); var json = ToolkitJson.ParseObject(TestData.StandardJson);
        json["controls"]![0]!["name"] = "<script>alert('no')</script> & synthetic";
        json["controls"]![0]!["references"] = new System.Text.Json.Nodes.JsonObject { ["microsoft"] = "https://learn.microsoft.com/en-us/graph/overview?x=1&y=2" };
        var standard = Load(root, json.ToJsonString());
        var first = new CatalogueDocumentService(root.Paths, () => Now).Create(standard);
        var later = new CatalogueDocumentService(root.Paths, () => Now.AddSeconds(1)).Create(standard);
        Assert.Equal(first.CatalogueDigest, later.CatalogueDigest); Assert.NotEqual(first.HtmlSha256, later.HtmlSha256);
        var html = Encoding.UTF8.GetString(first.CopyHtmlBytes());
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal); Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://learn.microsoft.com/en-us/graph/overview?x=1&y=2", first.RendererReferences);
        Assert.Contains("href=\"https://learn.microsoft.com/en-us/graph/overview?x=1&amp;y=2\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_save_is_not_reported_as_matched_or_successful_and_does_not_modify_document()
    {
        using var root = new TempRoot(); var document = new CatalogueDocumentService(root.Paths, () => Now).Create(Load(root));
        var bytes = document.CopyHtmlBytes();
        Directory.Delete(root.Paths.ReportsDirectory); File.WriteAllText(root.Paths.ReportsDirectory, "synthetic blocking file");
        Assert.ThrowsAny<IOException>(() => new ReportExporter(root.Paths, "").SaveHtml(document));
        Assert.Equal(bytes, document.CopyHtmlBytes());
    }

    [Fact]
    public void Rendering_uses_the_admitted_source_even_if_the_caller_changes_after_verification()
    {
        using var root = new TempRoot(); var standard = Load(root);
        var originalName = standard.Controls[0].Name;
        var document = new CatalogueDocumentService(root.Paths, () =>
        { standard.Controls[0].Name = "Changed after verification"; return Now; }).Create(standard);
        var html = Encoding.UTF8.GetString(document.CopyHtmlBytes());
        Assert.Contains(System.Net.WebUtility.HtmlEncode(originalName), html, StringComparison.Ordinal);
        Assert.DoesNotContain("Changed after verification", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("script-src 'none'", "script-src 'unsafe-inline'")]
    [InlineData("#172d43", "#152C38")]
    [InlineData("BUILDSTANDARD", "OTHER PRODUCT")]
    public void Security_style_or_brand_changes_cannot_borrow_the_generated_document_digest(string before, string after)
    {
        using var root = new TempRoot(); var document = new CatalogueDocumentService(root.Paths, () => Now).Create(Load(root));
        var bytes = document.CopyHtmlBytes(); var html = Encoding.UTF8.GetString(bytes);
        Assert.Contains(before, html, StringComparison.Ordinal);
        Assert.NotEqual(document.HtmlSha256, CanonicalJson.Sha256Hex(Encoding.UTF8.GetBytes(html.Replace(before, after, StringComparison.Ordinal))));
        Assert.Equal(document.HtmlSha256, CanonicalJson.Sha256Hex(document.CopyHtmlBytes()));
    }
}
