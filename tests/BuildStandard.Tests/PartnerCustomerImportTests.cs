using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Identity;
using Xunit;

namespace BuildStandard.Tests;

public sealed class PartnerCustomerImportTests
{
    private const string Tenant = "11111111-1111-4111-8111-111111111111";
    private const string Other = "22222222-2222-4222-8222-222222222222";
    private const string Header = "Microsoft ID,Company name,Primary domain name,Relationship,Tags\r\n";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-18T12:00:00Z");
    private static byte[] Csv(string rows) => Encoding.UTF8.GetBytes(Header + rows);
    private static PartnerCustomerPreview Preview(string rows, params TenantProfile[] profiles) => PartnerCustomerImport.Preview(Csv(rows), profiles);

    [Fact]
    public void Quoted_fields_newlines_and_utf8_bom_are_previewed_as_text()
    {
        var csv = Encoding.UTF8.GetPreamble().Concat(Csv($"{Tenant},\"Example, Ltd\",example.test,CSP,\"Line one\r\nLine \"\"two\"\"\"\r\n")).ToArray();
        var preview = PartnerCustomerImport.Preview(csv, []);
        var row = Assert.Single(preview.Rows);
        Assert.Equal(CustomerImportState.Ready, row.State);
        Assert.Equal("Example, Ltd", row.Company);
        Assert.Equal("Line one\r\nLine \"two\"", row.Tags);
        Assert.Equal(Tenant, row.TenantId);
        Assert.Equal(1, row.Occurrences);
    }

    [Fact]
    public void Required_columns_can_be_reordered_and_domain_is_optional()
    {
        var preview = PartnerCustomerImport.Preview(Encoding.UTF8.GetBytes($"Company name,Microsoft ID\nExample,{Tenant}\n"), []);
        var row = Assert.Single(preview.Rows);
        Assert.Equal(CustomerImportState.Ready, row.State);
        Assert.Equal("", row.Domain);
    }

    [Fact]
    public void Empty_valid_customer_list_is_explicitly_empty()
    {
        var preview = PartnerCustomerImport.Preview(Csv(""), []);
        Assert.Empty(preview.Rows);
        Assert.Equal(0, preview.DataRows);
    }

    [Theory]
    [InlineData("Microsoft ID,Microsoft ID,Company name\n")]
    [InlineData("Microsoft ID,Company name,company NAME\n")]
    [InlineData("Tenant,Company name\n")]
    [InlineData("Microsoft ID,Name\n")]
    [InlineData("")]
    public void Missing_or_duplicate_headers_refuse_the_entire_import(string csv) =>
        Assert.Throws<ConfigurationException>(() => PartnerCustomerImport.Preview(Encoding.UTF8.GetBytes(csv), []));

    [Theory]
    [InlineData("\"unfinished")]
    [InlineData("\"Company\"tail")]
    public void Malformed_quoting_is_not_an_empty_success(string company) =>
        Assert.Throws<ConfigurationException>(() => Preview($"{Tenant},{company},example.test,,\n"));

    [Fact]
    public void Unsupported_encoding_is_not_replaced_with_plausible_text() =>
        Assert.Throws<ConfigurationException>(() => PartnerCustomerImport.Preview([0xff, 0xfe, 0x4d, 0x00], []));

    [Theory]
    [InlineData("not-a-guid", "Example", "example.test")]
    [InlineData("00000000-0000-0000-0000-000000000000", "Example", "example.test")]
    [InlineData(Tenant, "", "example.test")]
    [InlineData(Tenant, "Bad\tname", "example.test")]
    [InlineData(Tenant, "Example", "bad\tdomain")]
    public void Invalid_rows_are_visible_and_not_selectable(string tenant, string company, string domain)
    {
        var row = Assert.Single(Preview($"{tenant},{company},{domain},,\n").Rows);
        Assert.Equal(CustomerImportState.Invalid, row.State);
        Assert.NotEmpty(row.Detail);
    }

    [Fact]
    public void Oversized_profile_values_are_invalid_not_silently_truncated()
    {
        Assert.Equal(CustomerImportState.Invalid, Assert.Single(Preview($"{Tenant},{new string('a', 121)},example.test,,").Rows).State);
        Assert.Equal(CustomerImportState.Invalid, Assert.Single(Preview($"{Tenant},Example,{new string('a', 201)},,").Rows).State);
    }

    [Fact]
    public void Wrong_column_count_refuses_import_instead_of_shifting_identity_fields() =>
        Assert.Throws<ConfigurationException>(() => Preview($"{Tenant},Example,example.test,extra,tag,surprise\n"));

    [Fact]
    public void Exact_duplicate_tenants_collapse_with_visible_count()
    {
        var preview = Preview($"{Tenant},Example,example.test,CSP,tag\n{Tenant},Example,example.test,CSP,tag\n");
        var row = Assert.Single(preview.Rows);
        Assert.Equal(CustomerImportState.Ready, row.State);
        Assert.Equal(2, row.Occurrences);
        Assert.Equal(2, preview.DataRows);
    }

    [Theory]
    [InlineData("Different", "example.test")]
    [InlineData("Example", "different.test")]
    public void Conflicting_tenant_rows_are_blocked(string company, string domain)
    {
        var row = Assert.Single(Preview($"{Tenant},Example,example.test,,\n{Tenant},{company},{domain},,\n").Rows);
        Assert.Equal(CustomerImportState.Conflict, row.State);
        Assert.Equal(2, row.Occurrences);
    }

    [Fact]
    public void Tenant_guid_comparison_is_canonical_not_case_sensitive()
    {
        const string tenant = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
        var row = Assert.Single(Preview($"{tenant.ToUpperInvariant()},Example,example.test,,\n{tenant},Example,example.test,,\n").Rows);
        Assert.Equal(tenant, row.TenantId);
        Assert.Equal(2, row.Occurrences);
    }

    [Fact]
    public void Existing_clients_are_skipped_and_all_their_fields_are_preserved()
    {
        var existing = ProfileValidator.Validate(new TenantProfile { TenantId = Tenant, Company = "Original", AssessmentClientId = Other, Notes = "Keep this", Parameters = new() { PilotGroupId = Other } }, Now);
        var before = ToolkitJson.Serialize(existing);
        var preview = Preview($"{Tenant},Changed,changed.test,,\n{Other},New,new.test,,", existing);
        Assert.Equal(CustomerImportState.Existing, preview.Rows[0].State);
        var result = PartnerCustomerImport.PrepareAdditions(preview, [Other], [existing], Now);
        Assert.Equal(2, result.Count);
        Assert.Equal(before, ToolkitJson.Serialize(result[0]));
        Assert.Equal(before, ToolkitJson.Serialize(existing));
        Assert.Equal("New", result[1].Company);
        Assert.Empty(result[1].AssessmentClientId);
        Assert.Empty(result[1].DeploymentClientId);
        Assert.Empty(result[1].ExclusionAccounts);
    }

    [Fact]
    public void Changed_profile_list_invalidates_preview_before_preparing_additions()
    {
        var existing = ProfileValidator.Validate(new TenantProfile { TenantId = Tenant, Company = "Original" }, Now);
        var preview = Preview($"{Other},New,new.test,,", existing);
        existing.Notes = "Another engineer changed this";
        Assert.Throws<ConfigurationException>(() => PartnerCustomerImport.PrepareAdditions(preview, [Other], [existing], Now));
        Assert.Equal("Another engineer changed this", existing.Notes);
    }

    [Theory]
    [InlineData(Tenant)]
    [InlineData("not-present")]
    public void Invalid_or_absent_selection_is_refused(string selection)
    {
        var preview = Preview($"{Tenant},,example.test,,");
        Assert.Throws<ConfigurationException>(() => PartnerCustomerImport.PrepareAdditions(preview, [selection], [], Now));
    }

    [Fact]
    public void Repeated_selection_adds_one_profile_and_empty_selection_adds_nothing()
    {
        var preview = Preview($"{Tenant},Example,example.test,,");
        Assert.Single(PartnerCustomerImport.PrepareAdditions(preview, [Tenant, Tenant], [], Now));
        Assert.Empty(PartnerCustomerImport.PrepareAdditions(preview, [], [], Now));
    }

    [Fact]
    public void Byte_and_row_limits_refuse_instead_of_truncating()
    {
        Assert.Throws<ConfigurationException>(() => PartnerCustomerImport.Preview(new byte[5 * 1024 * 1024 + 1], []));
        Assert.Equal(5000, Preview(string.Concat(Enumerable.Repeat($"{Tenant},Example,example.test,,\n", 5000))).DataRows);
        Assert.Throws<ConfigurationException>(() => Preview(string.Concat(Enumerable.Repeat($"{Tenant},Example,example.test,,\n", 5001))));
    }

    [Fact]
    public void Hostile_display_text_is_inert_and_does_not_become_access_or_an_application_id()
    {
        var preview = Preview($"{Tenant},=HYPERLINK(1),example.test,<script>bad</script>,Global administrator");
        var result = Assert.Single(PartnerCustomerImport.PrepareAdditions(preview, [Tenant], [], Now));
        Assert.Equal("=HYPERLINK(1)", result.Company);
        Assert.Empty(result.AssessmentClientId);
        Assert.Empty(result.DeploymentClientId);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void Quote_inside_unquoted_company_refuses_the_whole_preview() =>
        Assert.Throws<ConfigurationException>(() => Preview($"{Tenant},Acme\"broken,example.test,,"));

    [Fact]
    public void Parser_must_not_strip_a_control_character_before_quoted_identity() =>
        Assert.Throws<ConfigurationException>(() => Preview($"\t\"{Tenant}\",Example,example.test,,"));

    [Fact]
    public void Saved_identity_with_trailing_line_feed_cannot_allow_a_duplicate_tenant()
    {
        var existing = new TenantProfile { TenantId = Tenant + "\n", Company = "Original" };
        Assert.Throws<ConfigurationException>(() => Preview($"{Tenant},New,example.test,,", existing));
        Assert.Equal(Tenant + "\n", existing.TenantId);
    }

    [Fact]
    public void Preparing_additions_preserves_existing_explicit_null_values()
    {
        var existing = new TenantProfile { TenantId = Tenant, Company = "Original", Notes = null!, Parameters = null! };
        var before = ToolkitJson.Serialize(existing);
        var preview = Preview($"{Other},New,new.test,,", existing);
        var result = PartnerCustomerImport.PrepareAdditions(preview, [Other], [existing], Now);
        Assert.Null(result[0].Parameters);
        Assert.Null(result[0].Notes);
        Assert.Equal(before, ToolkitJson.Serialize(result[0]));
    }

}
