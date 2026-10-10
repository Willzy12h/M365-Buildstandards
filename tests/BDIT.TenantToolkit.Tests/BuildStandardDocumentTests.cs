using BDIT.TenantToolkit.Core.Models;
using System.Text.Json.Nodes;
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
        Assert.True(Available, "The shipped 2026.09.9 catalogue must be available to verify document generation.");
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
        Assert.True(Available, "The shipped 2026.09.9 catalogue must be available to verify document generation.");

        var html = BuildStandardDocument.Html(Shipped(), "Example Client", "Blue Diamond IT", "15 September 2026");

        Assert.DoesNotContain("@odata.type", html, StringComparison.Ordinal);
        Assert.DoesNotContain("deviceManagement/", html, StringComparison.Ordinal);
        Assert.DoesNotContain("graph.microsoft.com", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{{", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Delivery_wording_follows_how_the_control_is_actually_applied()
    {
        Assert.True(Available, "The shipped 2026.09.9 catalogue must be available to verify document generation.");
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
        Assert.True(Available, "The shipped 2026.09.9 catalogue must be available to verify document generation.");
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
        Assert.True(Available, "The shipped 2026.09.9 catalogue must be available to verify document generation.");
        var standard = Shipped();

        var markdown = BuildStandardDocument.Markdown(standard, "Example Client", "Blue Diamond IT", "15 September 2026");

        foreach (var control in standard.Controls) Assert.Contains("(" + control.Id + ")", markdown, StringComparison.Ordinal);
        Assert.StartsWith("# Microsoft 365 Build Standard — Example Client", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalogue_document_does_not_claim_tenant_rollout_or_force_supplier_branding()
    {
        var standard = new StandardCatalogue
        {
            Controls = new() { new ControlDefinition { Id = "EXAMPLE", Name = "Candidate", Payload = new JsonObject(), Assessment = new() { Mode = AssessmentMode.Settings } } }
        };
        var html = BuildStandardDocument.Html(standard, "Example", "Example Engineer", "18 September 2026");
        var markdown = BuildStandardDocument.Markdown(standard, "Example", "Example Engineer", "18 September 2026");
        Assert.Contains("candidate creation recipes", html);
        foreach (var document in new[] { html, markdown })
        {
            Assert.Contains("not evidence", document);
            Assert.DoesNotContain("Blue Diamond", document);
            Assert.DoesNotContain("created and activated by the toolkit", document);
            Assert.DoesNotContain("access can always be recovered", document);
        }
    }

    [Fact]
    public void Structured_defaults_and_technical_narrative_never_become_client_details()
    {
        const string identifier = "11111111-1111-1111-1111-111111111111";
        var standard = new StandardCatalogue
        {
            Description = "Configuration /deviceManagement/deviceConfigurations",
            Parameters = new() { new ParameterDefinition { Key = "settings", Label = "Approved settings", Type = "jsonArray",
                Default = new JsonArray(new JsonObject { ["@odata.type"] = "#microsoft.graph.test", ["id"] = identifier }) } },
            Controls = new() { new ControlDefinition { Id = "TEST-001", Name = "Review the candidate", Purpose = identifier,
                Payload = new JsonObject { ["settings"] = "{{settings}}" }, Assessment = new() { Mode = AssessmentMode.Settings } } }
        };
        foreach (var document in new[] { BuildStandardDocument.Html(standard, "Example", "Engineer", "Today"), BuildStandardDocument.Markdown(standard, "Example", "Engineer", "Today") })
        {
            Assert.Contains("reviewed configuration; confirm with your engineer", document);
            Assert.DoesNotContain(identifier, document);
            Assert.DoesNotContain("@odata.type", document);
            Assert.DoesNotContain("deviceManagement/", document);
            Assert.DoesNotContain("{{", document);
        }
    }

    [Fact]
    public void Directory_recipe_delivery_does_not_promise_inactive_policies_or_automatic_removal()
    {
        var group = new ControlDefinition { Collection = "groups", Payload = new JsonObject(), Assessment = new() { Mode = AssessmentMode.Settings } };
        Assert.Contains("empty security group", BuildStandardDocument.Delivery(group));
        Assert.Contains("later removal require a separate engineer review", BuildStandardDocument.Delivery(group));
        group.Collection = "namedLocations";
        Assert.Contains("untrusted IP location", BuildStandardDocument.Delivery(group));
        Assert.DoesNotContain("inactive candidate", BuildStandardDocument.Delivery(group));
    }
}
