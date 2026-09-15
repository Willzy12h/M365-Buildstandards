using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// The client document is generated from the catalogue so it cannot describe a tenant nobody has. These tests hold it
/// to that: every control appears, the words come from the standard, and the technical detail a client should not be
/// handed stays out.
/// </summary>
public class BuildStandardDocumentTests
{
    private static StandardCatalogue Shipped()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards"));
        var file = Path.Combine(directory, "2026.09.9.json");
        return StandardsLoader.Parse(File.ReadAllText(file), Path.GetFileName(file));
    }

    private static bool Available =>
        File.Exists(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards", "2026.09.9.json")));

    [Fact]
    public void Every_control_reaches_the_document()
    {
        if (!Available) return;
        var standard = Shipped();

        var html = BuildStandardDocument.Html(standard, "Example Client", "Blue Diamond IT", "15 September 2026");

        foreach (var control in standard.Controls)
        {
            Assert.Contains(control.Id, html, StringComparison.Ordinal);
            Assert.Contains(System.Net.WebUtility.HtmlEncode(control.Name), html, StringComparison.Ordinal);
            Assert.Contains(System.Net.WebUtility.HtmlEncode(control.Purpose), html, StringComparison.Ordinal);
        }
        Assert.Contains("Example Client", html, StringComparison.Ordinal);
        Assert.Contains(standard.Release, html, StringComparison.Ordinal);
    }

    /// <summary>A client document that leaked a payload or a Graph path would be the wrong document for its reader.</summary>
    [Fact]
    public void Technical_detail_stays_out_of_the_client_document()
    {
        if (!Available) return;

        var html = BuildStandardDocument.Html(Shipped(), "Example Client", "Blue Diamond IT", "15 September 2026");

        Assert.DoesNotContain("@odata.type", html, StringComparison.Ordinal);
        Assert.DoesNotContain("deviceManagement/", html, StringComparison.Ordinal);
        Assert.DoesNotContain("graph.microsoft.com", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{{", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Delivery_wording_follows_how_the_control_is_actually_applied()
    {
        if (!Available) return;
        var standard = Shipped();

        Assert.Contains("Created by the toolkit", BuildStandardDocument.Delivery(standard.FindControl("CA-001")!), StringComparison.Ordinal);
        Assert.Contains("Read from the tenant", BuildStandardDocument.Delivery(standard.FindControl("ID-002")!), StringComparison.Ordinal);
        Assert.Contains("Completed by an engineer", BuildStandardDocument.Delivery(standard.FindControl("ENR-005")!), StringComparison.Ordinal);
    }

    /// <summary>
    /// The client needs to see what is waiting on them, and to know which assumptions were made on their behalf.
    /// </summary>
    [Fact]
    public void Required_and_defaulted_inputs_are_separated()
    {
        if (!Available) return;
        var standard = Shipped();

        var mfa = BuildStandardDocument.Inputs(standard.FindControl("CA-001")!, standard);
        Assert.NotEmpty(mfa.Required);
        Assert.Empty(mfa.Defaulted);

        var android = BuildStandardDocument.Inputs(standard.FindControl("CMP-AND-001")!, standard);
        Assert.Empty(android.Required);
        Assert.Contains(android.Defaulted, d => d.Contains("14", StringComparison.Ordinal));

        // The tenant's own identifier is resolved from the session and is not something to ask a client for.
        foreach (var control in standard.Controls)
        {
            var inputs = BuildStandardDocument.Inputs(control, standard);
            Assert.DoesNotContain(inputs.Required.Concat(inputs.Defaulted), i => i.Contains("tenantId", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void The_markdown_document_carries_the_same_controls()
    {
        if (!Available) return;
        var standard = Shipped();

        var markdown = BuildStandardDocument.Markdown(standard, "Example Client", "Blue Diamond IT", "15 September 2026");

        foreach (var control in standard.Controls) Assert.Contains("(" + control.Id + ")", markdown, StringComparison.Ordinal);
        Assert.StartsWith("# Microsoft 365 Build Standard — Example Client", markdown, StringComparison.Ordinal);
    }
}
