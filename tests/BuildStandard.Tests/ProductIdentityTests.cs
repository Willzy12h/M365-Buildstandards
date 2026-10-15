using BuildStandard.Core.Configuration;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ProductIdentityTests
{
    [Fact]
    public void New_settings_and_blank_title_have_neutral_defaults()
    {
        var settings = ToolkitSettings.Validate(new ToolkitSettings { ProductName = " " });
        Assert.Equal("BuildStandard", settings.ProductName);
        Assert.Equal("", settings.CompanyName);
    }

    [Fact]
    public void Historical_configured_report_organisation_is_preserved_without_rewriting()
    {
        using var root = new TempRoot();
        var file = Path.Combine(root.Root, "settings.json");
        const string original = "{\"productName\":\"M365 BuildStandard Tool\",\"companyName\":\"BDIT client service\"}";
        File.WriteAllText(file, original);
        var settings = ToolkitSettings.Load(file);
        Assert.Equal("BuildStandard", settings.ProductName);
        Assert.Equal("BDIT client service", settings.CompanyName);
        Assert.Equal(original, File.ReadAllText(file));
    }

    [Fact]
    public void Neutral_new_report_retains_observed_tenant_and_raw_evidence_identity()
    {
        var assessment = new AssessmentResult { TenantName = "BDIT historical client", TenantId = "synthetic", PrimaryDomain = "example.invalid" };
        var raw = ToolkitJson.Serialize(assessment);
        var html = HtmlReports.Engineer(assessment);
        Assert.Contains("BuildStandard · Engineer report", html);
        Assert.DoesNotContain("M365 BuildStandard Tool", html);
        Assert.Contains("BDIT historical client", html);
        Assert.Equal(raw, ToolkitJson.Serialize(assessment));
    }

    [Fact]
    public void Blank_report_organisation_does_not_restore_a_fixed_company_or_invent_an_author()
    {
        using var root = new TempRoot();
        var exporter = new ReportExporter(ToolkitPaths.Resolve(root.Root), "");
        var file = exporter.ExportAssessment(new AssessmentResult { TenantName = "Synthetic client" }, ExportFormat.ClientHtml);
        var html = File.ReadAllText(file);
        Assert.DoesNotContain("M365 BuildStandard", html);
        Assert.DoesNotContain("Prepared by", html);
        Assert.Contains("Agree the priority items", html);
    }
    [Fact]
    public void Catalogue_presentation_has_its_own_neutral_version_without_rebinding_catalogue()
    {
        var standard = ExchangeTestData.Standard();
        var original = ToolkitJson.Serialize(standard);
        var html = EngineerStandardDocuments.Html(standard, EngineerDocumentKind.BuildStandard);
        Assert.Contains("Presentation " + EngineerStandardDocuments.PresentationVersion, html);
        Assert.StartsWith("buildstandard-native-reader-v", EngineerStandardDocuments.PresentationVersion, StringComparison.Ordinal);
        Assert.Contains("BUILDSTANDARD · ENGINEER REFERENCE", html);
        Assert.DoesNotContain("M365 BUILDSTANDARD · ENGINEER REFERENCE", html);
        Assert.Equal(original, ToolkitJson.Serialize(standard));
    }

    [Fact]
    public void Offline_probe_uses_new_prefix_and_never_deletes_a_legacy_named_file()
    {
        using var fixture = new OfflinePackageFixture();
        const string legacy = ".bdit-doctor-existing.tmp";
        fixture.Write(legacy, "Preserve existing file");
        Assert.StartsWith(".buildstandard-doctor-", new BuildStandard.Engine.Diagnostics.OfflineDiagnosticIo().ProbeName());
        _ = fixture.Service().Check(fixture.Root);
        Assert.Equal("Preserve existing file", File.ReadAllText(fixture.FilePath(legacy)));
        Assert.Empty(Directory.EnumerateFiles(fixture.Root, ".buildstandard-doctor-*", SearchOption.AllDirectories));
    }
}
