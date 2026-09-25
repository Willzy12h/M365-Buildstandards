using System.Net;
using System.Text.RegularExpressions;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class EngineerStandardDocumentsTests
{
    [Fact]
    public void Every_shipped_control_has_all_four_manual_sections_and_specific_verification()
    {
        var standard = ExchangeTestData.Standard();
        Assert.Equal(96, standard.Controls.Count);
        Assert.All(standard.Controls, c =>
        {
            Assert.True(EngineerStandardDocuments.HasCompleteManual(c), c.Id + " is missing manual instructions.");
            Assert.Contains(c.Implementation!.After, step => step.Contains("Pass", StringComparison.OrdinalIgnoreCase) || step.Contains("Confirm", StringComparison.OrdinalIgnoreCase));
            Assert.NotEmpty(c.Implementation.PortalSteps);
            Assert.Contains("https://learn.microsoft.com/", c.References.Microsoft + " " + c.DocumentationNotes);
        });
        Assert.Contains("LowercaseLetters=1", standard.FindControl("CFG-WIN-003")!.Implementation!.After[0]);
        Assert.Contains("Digits=0", standard.FindControl("CFG-WIN-003")!.Implementation!.After[0]);
        Assert.Contains("5 days", standard.FindControl("CMP-WIN-001")!.Implementation!.PortalSteps[2]);
        Assert.Contains("ESET", standard.FindControl("APP-WIN-008")!.Implementation!.After[0]);
        Assert.DoesNotContain("deviceCompliancePolicySettings", standard.FindControl("CMP-001")!.Implementation!.PowerShell);
        Assert.Contains("settings=$settings", standard.FindControl("CMP-001")!.Implementation!.PowerShell);
        Assert.DoesNotContain("Optional", standard.FindControl("CFG-WIN-007")!.Payload!.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manual_identity_changes_require_exact_policy_identity_and_preserve_unrelated_properties()
    {
        var standard = ExchangeTestData.Standard();
        var mdm = standard.FindControl("ENR-001")!.Implementation!.PowerShell;
        Assert.Contains("[guid]::TryParse($policyId", mdm);
        Assert.Contains("$before.discoveryUrl -ine 'https://enrollment.manage.microsoft.com/enrollmentserver/discovery.svc'", mdm);
        Assert.Contains("$before.isValid -ne $true", mdm);
        Assert.True(mdm.IndexOf("Assert-ManualGraphTenant", StringComparison.Ordinal) < mdm.IndexOf("-Method PATCH", StringComparison.Ordinal));
        var methods = standard.FindControl("ID-002")!.Implementation!.PowerShell;
        Assert.Contains("state=$(if ($method -eq 'MicrosoftAuthenticator')", methods);
        Assert.Contains("includeTargets = $tap.includeTargets; excludeTargets = $tap.excludeTargets", methods);
        Assert.Contains("@($tap.includeTargets).Count -eq 0", methods);
        Assert.Equal(2, Regex.Matches(methods, "Assert-ManualGraphTenant").Count);
    }

    [Theory]
    [InlineData(EngineerDocumentKind.BuildStandard)][InlineData(EngineerDocumentKind.ManualGuide)]
    public void Both_formats_include_every_control_once_and_preserve_exact_engineer_settings(EngineerDocumentKind kind)
    {
        var standard = ExchangeTestData.Standard();
        var html = EngineerStandardDocuments.Html(standard, kind);
        var markdown = EngineerStandardDocuments.Markdown(standard, kind);
        Assert.Equal(96, Regex.Matches(html, "<article id=").Count);
        foreach (var c in standard.Controls)
        {
            Assert.Contains("<article id=\"" + c.Id + "\">", html);
            Assert.Contains("### " + c.Id + " - ", markdown);
            if (kind == EngineerDocumentKind.ManualGuide)
            {
                Assert.Contains(WebUtility.HtmlEncode(c.Implementation!.Before[0]), html);
                Assert.Contains(WebUtility.HtmlEncode(c.Implementation.After[0]), html);
                Assert.Contains(c.Implementation.PowerShell, markdown);
            }
        }
        Assert.Contains("PassportForWork", html); Assert.Contains("LowercaseLetters", markdown);
        Assert.Contains("gracePeriodHours", html); Assert.Contains("120", html);
        Assert.Contains("isTrusted", html); Assert.Contains("Office list: stable unique key", markdown);
        Assert.Contains("Client mail domain", markdown); Assert.Contains("Admin consent reviewer", markdown);
        Assert.Contains("unverified", html); Assert.Contains("Optional branding", html);
        Assert.True(markdown.IndexOf("## Entra", StringComparison.Ordinal) < markdown.IndexOf("## Intune", StringComparison.Ordinal));
        Assert.True(markdown.IndexOf("## Exchange", StringComparison.Ordinal) < markdown.IndexOf("## Purview", StringComparison.Ordinal));
        Assert.DoesNotContain(TestData.TenantA, html); Assert.DoesNotContain(TestData.TenantB, markdown);
    }

    [Fact]
    public void Manual_sections_are_in_the_required_order_for_every_control()
    {
        var text = EngineerStandardDocuments.Markdown(ExchangeTestData.Standard(), EngineerDocumentKind.ManualGuide);
        var controls = Regex.Split(text, "^### ", RegexOptions.Multiline).Where(s => Regex.IsMatch(s, "^(PRE|ID|CA|ENR|CMP|CFG|SEC|APP|MAM|UPD|EX|PUR)-")).ToArray();
        Assert.Equal(96, controls.Length);
        foreach (var section in controls)
        {
            var before = section.IndexOf("#### Before running the automation", StringComparison.Ordinal);
            var portal = section.IndexOf("#### By hand - portal", StringComparison.Ordinal);
            var ps = section.IndexOf("#### By hand - PowerShell", StringComparison.Ordinal);
            var after = section.IndexOf("#### After the automation", StringComparison.Ordinal);
            Assert.True(before >= 0 && portal > before && ps > portal && after > ps, section.Split('\n')[0]);
        }
    }

    [Theory]
    [InlineData("all")][InlineData("before")][InlineData("portal")][InlineData("PowerShell")][InlineData("after")][InlineData("blank")]
    public void Incomplete_guide_is_refused_with_the_control_ID(string missing)
    {
        var standard = ExchangeTestData.Standard(); var control = standard.FindControl("ID-004")!;
        switch (missing)
        {
            case "all": control.Implementation = null; break;
            case "before": control.Implementation!.Before.Clear(); break;
            case "portal": control.Implementation!.PortalSteps.Clear(); break;
            case "PowerShell": control.Implementation!.PowerShell = " "; break;
            case "after": control.Implementation!.After.Clear(); break;
            case "blank": control.Implementation!.After.Add(" "); break;
        }
        Assert.Contains("ID-004", Assert.Throws<ConfigurationException>(() => EngineerStandardDocuments.Html(standard, EngineerDocumentKind.ManualGuide)).Message);
        Assert.Throws<ConfigurationException>(() => EngineerStandardDocuments.Markdown(standard, EngineerDocumentKind.ManualGuide));
    }

    [Fact]
    public void HTML_encodes_catalogue_content_and_Markdown_fences_cannot_be_closed_by_embedded_text()
    {
        var standard = ExchangeTestData.Standard(); var c = standard.Controls[0];
        c.Name = "<script>alert('synthetic')</script>";
        c.References.Microsoft = "javascript:alert('synthetic')";
        c.Implementation!.PowerShell = "# reference\n   ```\n<script>synthetic</script>";
        var html = EngineerStandardDocuments.Html(standard, EngineerDocumentKind.ManualGuide);
        Assert.DoesNotContain("<script>", html); Assert.DoesNotContain("href=\"javascript:", html);
        Assert.Contains("&lt;script&gt;", html);
        var md = EngineerStandardDocuments.Markdown(standard, EngineerDocumentKind.ManualGuide);
        Assert.Contains("````powershell\n# reference\n   ```\n<script>synthetic</script>\n````", md);
    }

    [Fact]
    public void Historical_catalogue_still_exports_the_standard_but_does_not_invent_manual_sections()
    {
        var file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.09.11.json"));
        var standard = StandardsLoader.Parse(File.ReadAllText(file), "2026.09.11.json");
        Assert.Contains("2026.09.11", EngineerStandardDocuments.Html(standard, EngineerDocumentKind.BuildStandard));
        Assert.Throws<ConfigurationException>(() => EngineerStandardDocuments.Html(standard, EngineerDocumentKind.ManualGuide));
    }

    [Fact]
    public void Four_distinct_local_exports_use_only_the_catalogue_and_refuse_other_formats()
    {
        using var root = new TempRoot();
        var exporter = new ReportExporter(root.Paths, "SYNTHETIC CLIENT SENTINEL"); var standard = ExchangeTestData.Standard();
        var files = new List<string>();
        foreach (var kind in Enum.GetValues<EngineerDocumentKind>())
            foreach (var format in new[] { ExportFormat.Html, ExportFormat.Markdown })
            {
                var file = exporter.ExportEngineerStandard(standard, kind, format); files.Add(file);
                Assert.True(File.Exists(file)); Assert.DoesNotContain("SYNTHETIC CLIENT SENTINEL", File.ReadAllText(file));
            }
        Assert.Equal(4, files.Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => exporter.ExportEngineerStandard(standard, EngineerDocumentKind.BuildStandard, ExportFormat.Json));
    }
}
