using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using System.IO.Compression;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using BuildStandard.Engine.Scripts;
using Xunit;

namespace BuildStandard.Tests;

public sealed class MailboxSettingsEvidenceTests
{
    [Theory]
    [InlineData("exo-mailbox-protocols", "exo.mailbox-protocol-settings")]
    [InlineData("exo-mailbox-forwarding", "exo.mailbox-forwarding-settings")]
    [InlineData("exo-mailbox-holds", "exo.mailbox-hold-settings")]
    public void Approved_reports_have_typed_registered_rows_and_separate_experimental_run_entries(string reportId, string itemId)
    {
        var report = ExchangeReportRegistry.Find(reportId);
        var section = Assert.Single(report.Sections);
        Assert.NotEqual("MailboxReportRow", section.RowType.Name);
        Assert.Contains("id", ExchangeReportEvidenceSchema.Columns(section.RowType));
        var entry = ScriptCatalogue.Shipped.Find(itemId);
        Assert.Equal(2, entry.Manifest.SchemaVersion);
        Assert.Equal(reportId, entry.Manifest.Execution!.ReportId);
        Assert.Equal(ScriptLiveStatus.Unverified, entry.Manifest.LiveStatus);
        Assert.Empty(entry.Manifest.Scopes);
    }

    private static JsonObject Row(string id) => id switch
    {
        "exo-mailbox-protocols" => ExchangeReportEvidenceSchema.Row(new MailboxProtocolReportRow
        {
            Id = TestData.Operator, Name = "Synthetic mailbox", ReadStatus = ReportReadState.Collected,
            PopEnabledReadStatus = "Collected", PopEnabled = false, ImapEnabledReadStatus = "Collected", ImapEnabled = false,
            MapiEnabledReadStatus = "Collected", MapiEnabled = true, EwsEnabledReadStatus = "Collected", EwsEnabled = false,
            ActiveSyncEnabledReadStatus = "Collected", ActiveSyncEnabled = false, OwaEnabledReadStatus = "Collected", OwaEnabled = true,
            SmtpAuthOverrideReadStatus = "Collected", SmtpAuthOverride = "Inherit",
            OrganisationSmtpAuthDisabledReadStatus = "Collected", OrganisationSmtpAuthDisabled = true,
            EffectiveSmtpAuthDisabledReadStatus = "Collected", EffectiveSmtpAuthDisabled = true
        }),
        "exo-mailbox-forwarding" => ExchangeReportEvidenceSchema.Row(new MailboxForwardingReportRow
        {
            Id = TestData.Operator, Name = "Synthetic mailbox", ReadStatus = ReportReadState.Collected,
            ForwardingAddressReadStatus = "Collected", ForwardingAddress = null,
            ForwardingSmtpAddressReadStatus = "Collected", ForwardingSmtpAddress = "smtp:target@synthetic.example",
            DeliverToMailboxAndForwardReadStatus = "Collected", DeliverToMailboxAndForward = false
        }),
        _ => ExchangeReportEvidenceSchema.Row(new MailboxHoldReportRow
        {
            Id = TestData.Operator, Name = "Synthetic mailbox", ReadStatus = ReportReadState.Collected,
            LitigationHoldEnabledReadStatus = "Collected", LitigationHoldEnabled = false,
            RetentionHoldEnabledReadStatus = "Collected", RetentionHoldEnabled = false,
            ComplianceTagHoldAppliedReadStatus = "Collected", ComplianceTagHoldApplied = false,
            DelayHoldAppliedReadStatus = "Collected", DelayHoldApplied = false,
            InPlaceHoldIdsReadStatus = "Collected", InPlaceHoldIds = []
        })
    };

    private static ExchangeReportEvidence Evidence(string id, JsonObject row)
    {
        var definition = ExchangeReportRegistry.Find(id);
        var state = row["readStatus"]!.GetValue<string>();
        return new ExchangeReportEvidence
        {
            Id = Guid.NewGuid().ToString("D"), RunId = Guid.NewGuid().ToString("D"), ReportId = id,
            TenantId = TestData.TenantA, InitiatingAccountObjectId = TestData.Operator, ObservedAccountUpn = "operator@synthetic.example",
            Resource = definition.Resource, StartedAt = "2026-10-15T01:00:00Z", EndedAt = "2026-10-15T01:01:00Z",
            ToolkitVersion = "synthetic", AdapterVersion = ExchangeReportRegistry.AdapterVersion, ModuleVersion = "99.0.0", RuntimeVersion = "7.6.7",
            ScriptSha256 = new string('a',64), ManifestSha256 = new string('b',64), RunnerTemplateSha256 = new string('c',64),
            SourceCommands = [.. definition.SourceCommands], Status = state,
            Sections = [new() { Id = definition.Sections[0].Id, Status = state, Error = state == "Collected" ? null : "Synthetic partial read", Rows = [row] }]
        };
    }

    [Theory]
    [InlineData("exo-mailbox-protocols")]
    [InlineData("exo-mailbox-forwarding")]
    [InlineData("exo-mailbox-holds")]
    public void False_explicit_unset_and_empty_collections_round_trip_without_becoming_unknown(string id)
    {
        var row = Row(id); var evidence = Evidence(id,row); ExchangeReportEvidenceSchema.Seal(evidence);
        var read = ExchangeReportEvidenceSchema.Read(ExchangeReportEvidenceSchema.Serialize(evidence), TestData.TenantA);
        Assert.Equal(row.ToJsonString(), read.Sections[0].Rows[0].ToJsonString());
    }

    [Theory]
    [InlineData("exo-mailbox-protocols", "popEnabled", "popEnabledReadStatus")]
    [InlineData("exo-mailbox-forwarding", "deliverToMailboxAndForward", "deliverToMailboxAndForwardReadStatus")]
    [InlineData("exo-mailbox-holds", "litigationHoldEnabled", "litigationHoldEnabledReadStatus")]
    public void Unread_boolean_cannot_become_false(string id,string field,string state)
    {
        var row = Row(id); row["readStatus"] = "Partial"; row["error"] = "Not returned"; row[state] = "Failed"; row[field] = false;
        Assert.Throws<ConfigurationException>(() => ExchangeReportEvidenceSchema.Seal(Evidence(id,row)));
    }

    [Theory]
    [InlineData("exo-mailbox-protocols", "popEnabled")]
    [InlineData("exo-mailbox-forwarding", "deliverToMailboxAndForward")]
    [InlineData("exo-mailbox-holds", "litigationHoldEnabled")]
    public void Collected_boolean_requires_an_observed_value(string id,string field)
    {
        var row = Row(id); row[field] = null;
        Assert.Throws<ConfigurationException>(() => ExchangeReportEvidenceSchema.Seal(Evidence(id,row)));
    }

    [Fact]
    public void Smtp_inheritance_cannot_invent_a_different_effective_setting()
    {
        var row = Row("exo-mailbox-protocols"); row["effectiveSmtpAuthDisabled"] = false;
        Assert.Throws<ConfigurationException>(() => ExchangeReportEvidenceSchema.Seal(Evidence("exo-mailbox-protocols",row)));
    }

    [Fact]
    public void Smtp_override_must_be_a_known_registered_value()
    {
        var row = Row("exo-mailbox-protocols"); row["smtpAuthOverride"] = "Maybe";
        Assert.Throws<ConfigurationException>(() => ExchangeReportEvidenceSchema.Seal(Evidence("exo-mailbox-protocols",row)));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Hold_ids_cannot_turn_unread_null_into_known_empty(bool collected)
    {
        var row = Row("exo-mailbox-holds");
        row["inPlaceHoldIdsReadStatus"] = collected ? "Collected" : "Failed";
        row["inPlaceHoldIds"] = collected ? null : new JsonArray();
        if (!collected) { row["readStatus"]="Partial";row["error"]="Not returned"; }
        Assert.Throws<ConfigurationException>(() => ExchangeReportEvidenceSchema.Seal(Evidence("exo-mailbox-holds",row)));
    }

    [Fact]
    public void New_setting_rows_without_identity_are_refused_even_when_partial()
    {
        var row = Row("exo-mailbox-holds"); row["id"] = null; row["readStatus"]="Partial";row["error"]="Missing identity";
        Assert.Throws<ConfigurationException>(() => ExchangeReportEvidenceSchema.Seal(Evidence("exo-mailbox-holds",row)));
    }

    [Fact]
    public void Native_report_projection_distinguishes_explicit_unset_false_and_unread_values()
    {
        var row=Row("exo-mailbox-forwarding");row["readStatus"]="Partial";row["error"]="Delivery setting not returned";
        row["deliverToMailboxAndForwardReadStatus"]="Failed";row["deliverToMailboxAndForward"]=null;
        var evidence=Evidence("exo-mailbox-forwarding",row);ExchangeReportEvidenceSchema.Seal(evidence);
        var table=RegisteredReportDocuments.Sheets(evidence).Single(s=>s.Name=="forwarding");
        Assert.Contains("Not set (observed)",table.Rows[1]);Assert.Contains("Unknown / not returned",table.Rows[1]);
        Assert.Equal("Exchange mailbox GUID",table.Rows[0][0]);
    }

    [Fact]
    public void Complete_empty_report_projection_preserves_read_state_and_provenance()
    {
        var evidence=Evidence("exo-mailbox-holds",Row("exo-mailbox-holds"));evidence.Sections[0].Rows.Clear();ExchangeReportEvidenceSchema.Seal(evidence);
        var tables=RegisteredReportDocuments.Sheets(evidence);
        Assert.Single(tables.Single(s=>s.Name=="holds").Rows);
        Assert.Contains(tables[0].Rows,r=>r.Contains(TestData.TenantA));
        Assert.Contains(tables[0].Rows,r=>r.Any(c=>c.Contains("Collected")));
    }

    [Fact]
    public void Successful_row_does_not_display_an_unknown_error()
    {
        var report=Evidence("exo-mailbox-forwarding",Row("exo-mailbox-forwarding"));ExchangeReportEvidenceSchema.Seal(report);
        var table=RegisteredReportDocuments.Sheets(report).Single(s=>s.Name=="forwarding");
        Assert.Equal("No read error",table.Rows[1][3]);
    }

    [Theory]
    [InlineData(ExportFormat.Html)] [InlineData(ExportFormat.Json)] [InlineData(ExportFormat.Csv)] [InlineData(ExportFormat.Xlsx)]
    public void Registered_exchange_exports_preserve_original_tenant_status_and_record(ExportFormat format)
    {
        using var root=new TempRoot();var row=Row("exo-mailbox-forwarding");row["name"]="<script>synthetic</script>";
        var report=Evidence("exo-mailbox-forwarding",row);ExchangeReportEvidenceSchema.Seal(report);var digest=report.IntegrityDigest;
        var file=new ReportExporter(root.Paths,"Synthetic").ExportExchangeReport(report,format);
        Assert.True(File.Exists(file));Assert.Equal(digest,report.IntegrityDigest);
        if (format==ExportFormat.Html)
        {
            var text=File.ReadAllText(file);Assert.Contains(TestData.TenantA,text);Assert.Contains("Collected",text);Assert.Contains("Not set (observed)",text);
            Assert.DoesNotContain("<script>",text);Assert.Contains("&lt;script&gt;",text);Assert.Contains("default-src 'none'",text);
        }
        else if (format==ExportFormat.Json) Assert.Equal(report.Id,ExchangeReportEvidenceSchema.Read(File.ReadAllText(file),TestData.TenantA).Id);
        else
        {
            using var zip=ZipFile.OpenRead(file);Assert.NotEmpty(zip.Entries);
            if(format==ExportFormat.Csv)Assert.Contains(zip.Entries,e=>e.FullName.Contains("provenance",StringComparison.OrdinalIgnoreCase));
        }
    }
}
