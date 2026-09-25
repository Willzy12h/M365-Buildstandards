using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Exchange;
using BDIT.TenantToolkit.Engine.Planning;
using BDIT.TenantToolkit.Engine.Reports;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ExchangeEvidenceTests
{
    private static AssessmentResult Assess(ExchangeCapture? capture)
    {
        var profile = TestData.Profile(); var standard = ExchangeTestData.Standard();
        var snapshot = capture is null ? TestData.Snapshot(standard) : ExchangeEvidenceImporter.Snapshot(capture, profile, standard);
        return new AssessmentEngine(new FixedClock { UtcNow = ExchangeTestData.Now }, "synthetic-test").Assess(snapshot, standard, profile,
            TestData.Mappings(), Array.Empty<Deviation>(), "synthetic offline reviewer");
    }
    private static ControlFinding Finding(ExchangeCapture capture, string id) => Assess(capture).Findings.Single(f => f.ControlId == id);

    [Fact]
    public void All_ten_controls_are_read_only_and_no_capture_means_unknown()
    {
        var standard = ExchangeTestData.Standard();
        Assert.Equal(10, standard.Controls.Count(c => c.Area is "Exchange" or "Purview"));
        Assert.All(ExchangeAssessment.ControlIds, id => { var c = standard.FindControl(id)!; Assert.False(c.HasRecipe); Assert.Null(c.Payload); Assert.Null(c.Collection); });
        Assert.All(Assess(null).Findings.Where(f => ExchangeAssessment.ControlIds.Contains(f.ControlId)), f => Assert.Equal(FindingStatus.UnableToAssess, f.Status));
        Assert.Equal(new[] { "EX-001", "EX-007", "EX-008" }, standard.Parameters.Single(p => p.Key == "exchangeDomain").RequiredForControls);
    }

    [Theory]
    [InlineData("tenant")][InlineData("exchange")][InlineData("purview")]
    public void Every_observed_tenant_identity_must_match_the_selected_client(string field)
    {
        var c = ExchangeTestData.Capture();
        if (field == "tenant") c.TenantId = TestData.TenantB;
        if (field == "exchange") c.ExchangeTenantId = TestData.TenantB;
        if (field == "purview") c.PurviewTenantId = TestData.TenantB;
        Assert.Throws<TenantMismatchException>(() => ExchangeCaptureSchema.Parse(ToolkitJson.Serialize(c), TestData.TenantA, ExchangeTestData.Now));
    }

    [Theory]
    [InlineData("source")][InlineData("schema")][InlineData("delegated")][InlineData("version")][InlineData("future")][InlineData("purview missing")]
    public void Unsupported_or_incomplete_provenance_is_refused(string field)
    {
        var c = ExchangeTestData.Capture();
        switch (field) {
            case "source": c.Source = "unknown"; break;
            case "schema": c.SchemaVersion = 9; break;
            case "delegated": c.Delegated = false; break;
            case "version": c.ModuleVersion = "1.0"; break;
            case "future": c.CapturedAt = Timestamps.Format(ExchangeTestData.Now.AddHours(1)); break;
            case "purview missing": c.PurviewTenantId = null; break;
        }
        Assert.Throws<ConfigurationException>(() => ExchangeCaptureSchema.Parse(ToolkitJson.Serialize(c), TestData.TenantA, ExchangeTestData.Now));
    }

    [Fact]
    public void Import_rejects_extra_fields_duplicate_fields_oversize_data_and_supplied_DNS()
    {
        var json = ToolkitJson.Serialize(ExchangeTestData.Capture());
        var node = JsonNode.Parse(json)!.AsObject(); node["accessToken"] = "synthetic sentinel - not a credential";
        Assert.Throws<ConfigurationException>(() => ExchangeCaptureSchema.Parse(node.ToJsonString(), TestData.TenantA, ExchangeTestData.Now));
        node = JsonNode.Parse(json)!.AsObject(); node["collections"]!["organisation"]!["items"]![0]!["unexpected"] = "value";
        Assert.Throws<ConfigurationException>(() => ExchangeCaptureSchema.Parse(node.ToJsonString(), TestData.TenantA, ExchangeTestData.Now));
        Assert.Throws<ConfigurationException>(() => ExchangeCaptureSchema.Parse(json.Replace("{", "{\"schemaVersion\":1,", StringComparison.Ordinal), TestData.TenantA, ExchangeTestData.Now));
        Assert.Throws<ConfigurationException>(() => ExchangeCaptureSchema.Parse(new string(' ', ExchangeCaptureSchema.MaximumBytes + 1), TestData.TenantA, ExchangeTestData.Now));
        var c = ExchangeTestData.Capture(); ExchangeTestData.AddDns(c);
        Assert.Throws<ConfigurationException>(() => ExchangeCaptureSchema.Parse(ToolkitJson.Serialize(c), TestData.TenantA, ExchangeTestData.Now));
    }

    [Theory]
    [InlineData("auditConfig","PUR-001")][InlineData("auditRetention","PUR-002")]
    [InlineData("transportRules","EX-001")][InlineData("presetEop","EX-002")][InlineData("presetAtp","EX-002")]
    [InlineData("outboundPolicies","EX-003")][InlineData("transportConfig","EX-004")][InlineData("externalTags","EX-005")]
    [InlineData("organisation","EX-006")][InlineData("dkim","EX-007")][InlineData("acceptedDomains","EX-008")]
    public void Failed_missing_and_partial_reads_never_become_missing_configuration(string key, string id)
    {
        var c = ExchangeTestData.Capture(); c.Collections.Remove(key);
        Assert.Equal(FindingStatus.UnableToAssess, Finding(c,id).Status);
        c = ExchangeTestData.Capture(); c.Collections[key].Status = CaptureStatus.Error; c.Collections[key].Items.Clear();
        Assert.Equal(FindingStatus.UnableToAssess, Finding(c,id).Status);
        c = ExchangeTestData.Capture();
        if (c.Collections[key].Items.Count == 0) c.Collections[key].Items.Add(new JsonObject());
        else c.Collections[key].Items[0].Remove(ExchangeCaptureSchema.Definitions[key].Fields.Keys.First());
        Assert.Equal(FindingStatus.UnableToAssess, Finding(c,id).Status);
    }

    [Fact]
    public void Imported_snapshots_never_satisfy_the_write_boundary_even_with_forged_complete_flag()
    {
        var standard = ExchangeTestData.Standard(); var profile = TestData.Profile();
        var imported = ExchangeEvidenceImporter.Import(ToolkitJson.Serialize(ExchangeTestData.Capture()), profile, standard, ExchangeTestData.Domain, ExchangeTestData.Now);
        Assert.False(imported.Complete); Assert.Empty(imported.Collections);
        var complete = new TenantSnapshot { Complete = true, StandardRelease = standard.Release };
        foreach (var (key, def) in standard.Collections) complete.Collections[key] = new CollectionCapture { Status = CaptureStatus.Collected, Api = def.Api, Path = def.Path };
        SnapshotRequirements.AssertComplete(complete, standard);
        complete.ExchangeCapture = imported.ExchangeCapture;
        Assert.Throws<PlanValidationException>(() => SnapshotRequirements.AssertComplete(complete, standard));
        Assert.False(ToolkitJson.ToNode(new TenantSnapshot())!.AsObject().ContainsKey("exchangeCapture"));
    }

    [Fact]
    public void Complete_boolean_observations_report_exact_values_without_claiming_live_verification()
    {
        var c = ExchangeTestData.Capture();
        foreach (var id in new[] { "PUR-001", "EX-003", "EX-004", "EX-005", "EX-006" })
        { var f = Finding(c,id); Assert.Equal(FindingStatus.RequiresManualReview, f.Status); Assert.NotEmpty(f.ObservedObjects); }
        c.Collections["organisation"].Items[0]["AuditDisabled"] = true;
        Assert.Equal(FindingStatus.PartialMatch, Finding(c,"EX-006").Status);
        c.CapturedAt = Timestamps.Format(ExchangeTestData.Now.AddDays(-2));
        Assert.Equal(FindingStatus.UnableToAssess, Finding(c,"PUR-001").Status);
    }

    [Fact]
    public void Default_forwarding_policy_and_one_preset_component_cannot_establish_all_recipients()
    {
        var c = ExchangeTestData.Capture();
        c.Collections["outboundPolicies"].Items.Add(new JsonObject { ["Identity"] = "Synthetic narrow policy", ["IsDefault"] = false, ["AutoForwardingMode"] = "On" });
        Assert.Equal(FindingStatus.PartialMatch, Finding(c,"EX-003").Status);
        c.Collections["presetAtp"].Items[0]["ExceptIfSentTo"] = new JsonArray("synthetic@example.invalid");
        Assert.Equal(FindingStatus.PartialMatch, Finding(c,"EX-002").Status);
        c.Collections["outboundPolicies"].Items[0]["IsDefault"] = false;
        Assert.Equal(FindingStatus.UnableToAssess, Finding(c,"EX-003").Status);
    }

    [Theory]
    [InlineData(0)][InlineData(1)]
    public void DKIM_requires_both_fresh_exact_CNAME_answers(int selector)
    {
        var c = ExchangeTestData.Capture(); ExchangeTestData.AddDns(c); var config = c.Collections["dkim"].Items[0];
        Assert.True(ExchangeAssessment.DkimDnsReady(c,config,ExchangeTestData.Now));
        c.Dns[selector].Records[0] = "wrong.example.invalid";
        Assert.False(ExchangeAssessment.DkimDnsReady(c,config,ExchangeTestData.Now));
        Assert.Equal(FindingStatus.UnableToAssess, Finding(c,"EX-007").Status);
        c = ExchangeTestData.Capture(); ExchangeTestData.AddDns(c); c.Dns[selector].QueriedAt = Timestamps.Format(ExchangeTestData.Now.AddHours(-2));
        Assert.False(ExchangeAssessment.DkimDnsReady(c,c.Collections["dkim"].Items[0],ExchangeTestData.Now));
    }

    [Theory]
    [InlineData("v=DMARC1; p=reject;",true)][InlineData("v=DMARC1; p=none",true)]
    [InlineData("v=DMARC1; p=quarantine",true)][InlineData("v=DMARC1",false)]
    [InlineData("v=DMARC1; p=reject; p=none",false)][InlineData("v=DMARC1; p=unknown",false)]
    [InlineData("p=reject; v=DMARC1",false)][InlineData("v=DMARC1; p=reject; broken",false)]
    [InlineData("v=DMARC1; p=reject; pct=101",false)][InlineData("v=DMARC1; p=reject; pct=-1",false)]
    [InlineData("v=DMARC1; p=reject; pct=100; sp=none; adkim=s; aspf=r",true)]
    [InlineData("v=DMARC1; p=reject; sp=invalid",false)][InlineData("v=DMARC1; p=reject; aspf=invalid",false)]
    public void DMARC_requires_an_unambiguous_version_and_policy(string record, bool valid) => Assert.Equal(valid, ExchangeAssessment.TryDmarcPolicy(record,out _));

    [Fact]
    public void Empty_present_DNS_and_incomplete_collection_status_are_not_successful_evidence()
    {
        var c = ExchangeTestData.Capture(); ExchangeTestData.AddDns(c);
        c.Dns[0].Records.Clear();
        Assert.Throws<ConfigurationException>(() => ExchangeCaptureSchema.Validate(c, TestData.TenantA, ExchangeTestData.Now));
        c = ExchangeTestData.Capture(); c.Collections["organisation"].Items[0].Remove("AuditDisabled");
        Assert.Equal("Incomplete fields; unable to assess", Assess(c).CollectionStatus["Get-OrganizationConfig"]);
    }

    [Fact]
    public void DNS_failure_is_unknown_and_explicit_no_record_is_distinct_from_ambiguity()
    {
        var c = ExchangeTestData.Capture(); ExchangeTestData.AddDns(c);
        c.Dns[2].Status = DnsResultStatus.Error; c.Dns[2].Records.Clear();
        Assert.Equal(FindingStatus.UnableToAssess, Finding(c,"EX-008").Status);
        c.Dns[2].Status = DnsResultStatus.NoRecords;
        Assert.Equal(FindingStatus.Missing, Finding(c,"EX-008").Status);
        c.Dns[2].Status = DnsResultStatus.Present; c.Dns[2].Records = new() { "v=DMARC1; p=reject", "v=DMARC1; p=none" };
        Assert.Equal(FindingStatus.UnableToAssess, Finding(c,"EX-008").Status);
    }

    [Fact]
    public async Task DNS_collection_uses_the_fake_interface_and_cannot_query_an_unestablished_domain()
    {
        var c = ExchangeTestData.Capture(); var standard = ExchangeTestData.Standard(); var profile = TestData.Profile(); var dns = new FakeDns();
        var updated = await ExchangeEvidenceImporter.CheckDnsAsync(ExchangeEvidenceImporter.Snapshot(c,profile,standard),profile,standard,dns,ExchangeTestData.Now,default);
        Assert.Equal(3,dns.Questions.Count); Assert.Equal(3,updated.ExchangeCapture!.Dns.Count); Assert.False(updated.Complete); Assert.Empty(updated.Collections);
        c.Collections["acceptedDomains"].Items.Clear(); dns.Questions.Clear();
        await Assert.ThrowsAsync<ConfigurationException>(() => ExchangeEvidenceImporter.CheckDnsAsync(ExchangeEvidenceImporter.Snapshot(c,profile,standard),profile,standard,dns,ExchangeTestData.Now,default));
        Assert.Empty(dns.Questions);
    }

    [Theory]
    [InlineData("")][InlineData("https://example.invalid")][InlineData("*.example.invalid")][InlineData("127.0.0.1")]
    [InlineData("example.invalid;whoami")][InlineData("example.invalid'")][InlineData("a..invalid")][InlineData("-a.invalid")]
    public void Domain_input_cannot_be_empty_or_become_script_text(string value)
    {
        Assert.Throws<ConfigurationException>(() => MailDomain.Validate(value));
        Assert.Throws<ConfigurationException>(() => ExchangeCaptureScripts.ReadOnlyCapture(TestData.TenantA,value));
    }

    [Fact]
    public async Task Windows_DNS_adapter_can_be_tested_without_starting_a_process()
    {
        var calls = 0;
        var resolver = new WindowsDnsLookup((script,_) => { calls++; Assert.Contains("-DnsOnly -NoHostsFile",script); Assert.Contains("9003, 9501",script);
            Assert.DoesNotContain("ExecutionPolicy",script); return Task.FromResult(ToolkitJson.Serialize(ExchangeTestData.Answer("_dmarc."+ExchangeTestData.Domain,DnsRecordKind.Txt,"v=DMARC1; p=none"))); });
        var result = await resolver.QueryAsync("_dmarc."+ExchangeTestData.Domain,DnsRecordKind.Txt,default);
        Assert.Equal(1,calls); Assert.Equal(DnsResultStatus.Present,result.Status);
        var failed = new WindowsDnsLookup((_,_) => throw new IOException("Synthetic lookup failure"));
        Assert.Equal(DnsResultStatus.Error,(await failed.QueryAsync("_dmarc."+ExchangeTestData.Domain,DnsRecordKind.Txt,default)).Status);
        Assert.Throws<ConfigurationException>(() => WindowsDnsLookup.QueryScript("x';whoami;'.invalid",DnsRecordKind.Txt));
    }

    [Fact]
    public void Capture_generation_is_read_only_and_reports_keep_DNS_evidence()
    {
        var script = ExchangeCaptureScripts.ReadOnlyCapture(TestData.TenantA,ExchangeTestData.Domain);
        foreach (var cmd in ExchangeCaptureSchema.Definitions.Values.Select(d=>d.Command)) Assert.Contains(cmd,script);
        Assert.Contains("-CommandName $readCommands",script); Assert.Contains("Assert-CaptureConnection -Purview $definition.purview",script);
        Assert.Contains("Use a fresh PowerShell process",script); Assert.DoesNotContain("__DEFINITIONS__",script);
        foreach (var forbidden in new[] { "Set-Transport", "Set-Dkim", "New-Transport", "Set-AdminAudit", "Install-Module", "-Certificate", "-Credential", "-AccessToken", "-ExecutionPolicy" }) Assert.DoesNotContain(forbidden,script);
        var c = ExchangeTestData.Capture(); ExchangeTestData.AddDns(c); var report = Assess(c);
        foreach (var target in new[] { "selector1-synthetic._domainkey.example.invalid", "selector2-synthetic._domainkey.example.invalid", "v=DMARC1; p=reject" })
        {
            Assert.Contains(target,HtmlReports.Engineer(report)); Assert.Contains(target,MarkdownReports.Engineer(report));
            Assert.Contains(TabularReports.AssessmentSheets(report).Single(s=>s.Name=="Findings").Rows.SelectMany(r=>r),cell=>cell.Contains(target,StringComparison.Ordinal));
        }
        var sheets = TabularReports.SnapshotSheets(ExchangeEvidenceImporter.Snapshot(c,TestData.Profile(),ExchangeTestData.Standard()),ExchangeTestData.Standard());
        Assert.Contains(sheets.Single(s=>s.Name=="Details").Rows.SelectMany(r=>r),cell=>cell.Contains("selector2-synthetic",StringComparison.Ordinal));
    }

    private sealed class FakeDns : IDnsLookup
    {
        public List<string> Questions { get; } = new();
        public Task<DnsObservation> QueryAsync(string name,DnsRecordKind kind,CancellationToken ct)
        { Questions.Add(name); return Task.FromResult(ExchangeTestData.Answer(name,kind)); }
    }
}
