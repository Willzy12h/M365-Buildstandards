using System.Text.RegularExpressions;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Exchange;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ExchangeProposalTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly EvidenceStore _evidence;
    private readonly StandardCatalogue _standard = ExchangeTestData.Standard();
    private readonly TenantProfile _profile = TestData.Profile();
    private TenantSnapshot _snapshot;

    public ExchangeProposalTests()
    {
        _evidence = new EvidenceStore(_root.Paths, NullLog.Instance);
        _snapshot = ExchangeEvidenceImporter.Snapshot(ExchangeTestData.Capture(), _profile, _standard);
        _evidence.SaveSnapshot(_snapshot);
    }
    private string Proposal(string id = "PUR-001", string typed = TestData.TenantA, string domain = ExchangeTestData.Domain) =>
        ExchangeProposal.Create(_standard, _profile, _snapshot, _evidence, id, typed, domain, ExchangeTestData.Now);

    [Theory]
    [InlineData("EX-001")][InlineData("EX-002")][InlineData("EX-003")][InlineData("EX-004")][InlineData("EX-005")]
    [InlineData("EX-006")][InlineData("EX-007")][InlineData("EX-008")][InlineData("PUR-001")][InlineData("PUR-002")]
    public void Every_selected_proposal_is_inert_and_contains_only_its_own_control(string id)
    {
        var text = Proposal(id);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        Assert.Equal(ExchangeProposal.Refusal, lines[0]);
        Assert.All(lines.Skip(1).Where(l => l.Length > 0), line => Assert.StartsWith("# ", line));
        Assert.Contains("REVIEW PROPOSAL: " + id, text);
        Assert.All(ExchangeAssessment.ControlIds.Where(other => other != id), other => Assert.DoesNotContain("REVIEW PROPOSAL: " + other, text));
        Assert.Contains(_snapshot.IntegrityDigest, text); Assert.Contains("NOT YET OBSERVED", text);
        Assert.Contains("never repeat a write", text); Assert.Contains("Record intent durably", text);
        Assert.False(_snapshot.Complete); Assert.Empty(_snapshot.Collections);
        Assert.DoesNotContain("Install-Module", text); Assert.DoesNotContain("-ExecutionPolicy", text);
        var implementation = _standard.FindControl(id)!.Implementation!;
        Assert.NotEmpty(implementation.Before); Assert.NotEmpty(implementation.PortalSteps); Assert.NotEmpty(implementation.After);
        var commands = implementation.PowerShell.Split('\n');
        for (var i = 0; i < commands.Length; i++)
            if (Regex.IsMatch(commands[i], "^(Set|New|Enable)-")) Assert.Equal("Assert-ManualTenant", commands[i - 1]);
    }

    [Theory]
    [InlineData("")][InlineData("   ")][InlineData(TestData.TenantB)]
    public void Confirmation_is_required_even_for_the_inert_client_bound_export(string typed) =>
        Assert.Throws<PlanValidationException>(() => Proposal(typed: typed));

    [Theory]
    [InlineData("")][InlineData("https://example.invalid")][InlineData("wrong.invalid")]
    public void Empty_invalid_or_different_domain_is_refused(string domain) => Assert.Throws<ConfigurationException>(() => Proposal("EX-001", domain: domain));

    [Fact]
    public void Unsaved_modified_and_cross_tenant_evidence_is_refused()
    {
        _snapshot = ExchangeEvidenceImporter.Snapshot(ExchangeTestData.Capture(), _profile, _standard);
        Assert.Throws<PlanValidationException>(() => Proposal());
        _evidence.SaveSnapshot(_snapshot);
        _snapshot.ExchangeCapture!.Collections["auditConfig"].Items[0]["UnifiedAuditLogIngestionEnabled"] = false;
        Assert.Throws<PlanValidationException>(() => Proposal());
        _evidence.SaveSnapshot(_snapshot);
        _snapshot.TenantId = TestData.TenantB;
        Assert.Throws<TenantMismatchException>(() => Proposal());
    }

    [Fact]
    public void Changed_saved_before_evidence_cannot_be_replaced_by_the_in_memory_copy()
    {
        var other = _evidence.LoadSnapshot(TestData.TenantA, _snapshot.Id)!;
        other.ExchangeCapture!.Collections["auditConfig"].Items[0]["UnifiedAuditLogIngestionEnabled"] = false;
        _evidence.SaveSnapshot(other);
        Assert.Throws<PlanValidationException>(() => Proposal());
    }

    [Theory]
    [InlineData("EX-001","transportRules")][InlineData("EX-002","presetEop")][InlineData("EX-002","presetAtp")]
    [InlineData("EX-003","outboundPolicies")][InlineData("EX-004","transportConfig")][InlineData("EX-005","externalTags")]
    [InlineData("EX-006","organisation")][InlineData("EX-007","dkim")][InlineData("EX-008","acceptedDomains")]
    [InlineData("PUR-001","auditConfig")][InlineData("PUR-002","auditRetention")]
    public void Unknown_before_observations_refuse_proposals(string id, string collection)
    {
        _snapshot.ExchangeCapture!.Collections.Remove(collection); _evidence.SaveSnapshot(_snapshot);
        Assert.Throws<PlanValidationException>(() => Proposal(id));
    }

    [Fact]
    public void Old_evidence_and_unaccepted_domain_cannot_authorise_proposals()
    {
        _snapshot.ExchangeCapture!.CapturedAt = Timestamps.Format(ExchangeTestData.Now.AddDays(-2)); _evidence.SaveSnapshot(_snapshot);
        Assert.Throws<PlanValidationException>(() => Proposal());
        _snapshot.ExchangeCapture.CapturedAt = Timestamps.Format(ExchangeTestData.Now);
        _snapshot.ExchangeCapture.Collections["acceptedDomains"].Items.Clear(); _evidence.SaveSnapshot(_snapshot);
        Assert.Throws<PlanValidationException>(() => Proposal("EX-001"));
    }

    [Fact]
    public void DKIM_enable_instructions_are_omitted_until_both_fresh_actual_CNAMEs_match()
    {
        Assert.DoesNotContain("Set-DkimSigningConfig", Proposal("EX-007"));
        ExchangeTestData.AddDns(_snapshot.ExchangeCapture!); _evidence.SaveSnapshot(_snapshot);
        var enabled = Proposal("EX-007");
        Assert.Contains("Set-DkimSigningConfig -Identity $domain -Enabled $true", enabled);
        Assert.Contains("Resolve-DnsName", enabled); Assert.Contains("$answer.Count -ne 1", enabled);
        foreach (var index in new[] { 0, 1 })
        {
            _snapshot.ExchangeCapture!.Dns.Clear();
            ExchangeTestData.AddDns(_snapshot.ExchangeCapture!);
            _snapshot.ExchangeCapture!.Dns[index].Records[0] = "different.example.invalid";
            _evidence.SaveSnapshot(_snapshot);
            Assert.DoesNotContain("Set-DkimSigningConfig", Proposal("EX-007"));
        }
        _snapshot.ExchangeCapture!.Dns.Clear(); ExchangeTestData.AddDns(_snapshot.ExchangeCapture);
        _snapshot.ExchangeCapture.Dns[0].QueriedAt = Timestamps.Format(ExchangeTestData.Now.AddHours(-2)); _evidence.SaveSnapshot(_snapshot);
        Assert.DoesNotContain("Set-DkimSigningConfig", Proposal("EX-007"));
    }

    [Fact]
    public void Missing_DKIM_config_has_disabled_creation_only_and_bypass_has_no_activation()
    {
        _snapshot.ExchangeCapture!.Collections["dkim"].Items.Clear(); _evidence.SaveSnapshot(_snapshot);
        var dkim = Proposal("EX-007");
        Assert.Contains("New-DkimSigningConfig -DomainName $domain -Enabled $false", dkim);
        Assert.DoesNotContain("Set-DkimSigningConfig", dkim);
        var bypass = Proposal("EX-001");
        Assert.Contains("-Enabled $false -Mode Audit", bypass); Assert.Contains("-SenderAddressLocation Header", bypass);
        Assert.Contains("-FromAddressMatchesPatterns $fromPattern", bypass); Assert.Contains("-HeaderMatchesPatterns '\\bspf=pass\\b' -SetSCL -1", bypass);
        Assert.Contains("do not adopt or overwrite by name", bypass); Assert.DoesNotContain("Enable-TransportRule", bypass);
        foreach (var id in new[] { "EX-008", "PUR-002" })
            Assert.DoesNotMatch("(?m)^# (Set|New|Enable)-", Proposal(id));
    }

    public void Dispose() => _root.Dispose();
}
