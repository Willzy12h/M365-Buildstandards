using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class MailboxCapacityTests
{
    private const string At = "2026-10-08T10:00:00Z";

    [Theory]
    [InlineData("100 GB (107,374,182,400 bytes)", 107374182400L, "Observed")]
    [InlineData("0 B (0 bytes)", 0L, "Observed")]
    [InlineData("Unlimited", null, "Unlimited")]
    [InlineData(null, null, MailboxCapacity.UnableToCheck)]
    [InlineData("100 GB", null, MailboxCapacity.UnableToCheck)]
    [InlineData("100 GB (107,37,418,2400 bytes)", null, MailboxCapacity.UnableToCheck)]
    [InlineData("100 GB (107.374.182.400 bytes)", null, MailboxCapacity.UnableToCheck)]
    [InlineData("999 GB (999999999999999999999999999 bytes)", null, MailboxCapacity.UnableToCheck)]
    [InlineData("abc (5 bytes)", null, MailboxCapacity.UnableToCheck)]
    [InlineData("5 B (5 bytes)\n", null, MailboxCapacity.UnableToCheck)]
    public void Quotas_preserve_raw_values_without_unit_guesses_or_unknown_zero(string? raw, long? expected, string state)
    {
        var value = MailboxCapacity.Measure(raw, ReportReadState.Collected);
        Assert.Equal(raw, value.Raw); Assert.Equal(expected, value.Bytes); Assert.Equal(state, value.State);
        var failure = MailboxCapacity.Measure(raw, ReportReadState.Failed);
        Assert.Equal(raw, failure.Raw); Assert.Null(failure.Bytes); Assert.Equal(MailboxCapacity.UnableToCheck, failure.State);
    }

    [Theory]
    [InlineData(MailboxCapacity.Plan2Id, "EXCHANGE_S_ENTERPRISE", "Success", MailboxCapacity.Eligible)]
    [InlineData(MailboxCapacity.Plan2Id, "EXCHANGE_S_ENTERPRISE", "Disabled", MailboxCapacity.NotEligible)]
    [InlineData(MailboxCapacity.Plan2Id, "EXCHANGE_S_ENTERPRISE", "PendingActivation", MailboxCapacity.UnableToCheck)]
    [InlineData(MailboxCapacity.Plan1Id, "EXCHANGE_S_STANDARD", "Success", MailboxCapacity.NotEligible)]
    [InlineData(MailboxCapacity.Plan2Id, "OTHER_SERVICE", "Success", MailboxCapacity.UnableToCheck)]
    [InlineData(MailboxCapacity.Plan1Id, "OTHER_SERVICE", "Success", MailboxCapacity.UnableToCheck)]
    [InlineData(MailboxCapacity.ArchiveAddonId, "OTHER_SERVICE", "Success", MailboxCapacity.UnableToCheck)]
    [InlineData(MailboxCapacity.FoundationId, "OTHER_SERVICE", "Success", MailboxCapacity.UnableToCheck)]
    [InlineData("11111111-1111-4111-8111-111111111111", "EXCHANGE_S_ENTERPRISE", "Success", MailboxCapacity.UnableToCheck)]
    public void Eligibility_uses_actual_assigned_service_plan_identity_name_and_state(string id, string name, string provisioning, string expected)
    {
        var result = MailboxCapacity.Review(Mailbox(), Evidence(Plan(id, name, provisioning)));
        Assert.True(result.Configured100GB); Assert.Equal(expected, result.Eligibility);
        Assert.Equal(At, result.LicencesObservedAt);
    }

    [Fact]
    public void Plan1_product_display_name_and_archive_capacity_do_not_prove_primary_100GB()
    {
        var mailbox = Mailbox() with { SendReceiveQuotaRaw = "50 GB (53,687,091,200 bytes)", ArchiveQuotaRaw = "100 GB (107,374,182,400 bytes)" };
        var result = MailboxCapacity.Review(mailbox, Evidence(Plan(MailboxCapacity.Plan1Id, "EXCHANGE_S_STANDARD", "Success")));
        Assert.False(result.Configured100GB); Assert.Equal(MailboxCapacity.NotEligible, result.Eligibility);
        Assert.Equal(MailboxCapacity.HundredGiB, result.ArchiveQuota.Bytes);
        Assert.Equal("NotEligible", result.Finding);
    }

    [Fact]
    public void Eligible_but_lower_quota_is_separate_from_actual_size_and_all_raw_thresholds()
    {
        var mailbox = Mailbox() with { SendReceiveQuotaRaw = "50 GB (53,687,091,200 bytes)" };
        var result = MailboxCapacity.Review(mailbox, Evidence(Plan(MailboxCapacity.Plan2Id, "EXCHANGE_S_ENTERPRISE", "Success")));
        Assert.Equal("EligibleButLowerQuota", result.Finding);
        Assert.Equal(0L, result.PrimarySize.Bytes); Assert.Equal(mailbox.WarningQuotaRaw, result.WarningQuota.Raw);
        Assert.Equal(mailbox.SendQuotaRaw, result.SendQuota.Raw); Assert.Equal(mailbox.SendReceiveQuotaRaw, result.SendReceiveQuota.Raw);
        Assert.Equal(mailbox, result.Observed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("22222222-2222-4222-8222-222222222222")]
    [InlineData("not-an-object-id")]
    public void Missing_exact_user_join_does_not_fall_back_to_matching_name_or_SMTP(string? objectId)
    {
        var result = MailboxCapacity.Review(Mailbox() with { ExternalDirectoryObjectId = objectId }, Evidence(Plan(MailboxCapacity.Plan2Id, "EXCHANGE_S_ENTERPRISE", "Success")));
        Assert.True(result.Configured100GB); Assert.Equal(MailboxCapacity.UnableToCheck, result.Eligibility);
        Assert.Equal("Configured100GBEntitlementUnconfirmed", result.Finding);
    }

    [Theory]
    [InlineData("Partial")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public void Incomplete_user_licence_reads_never_mean_not_eligible(string state)
    {
        var report = Evidence(null);
        var user = ToolkitJson.Deserialize<UserLicenceReportRow>(report.Sections[1].Rows[0].ToJsonString());
        user.ReadStatus = state; user.ProductsReadStatus = state; user.Error = "Synthetic unavailable read";
        report.Sections[1].Rows[0] = ReportEvidenceSchema.Row(user);
        report.Sections[1].Status = state == ReportReadState.Failed ? ReportReadState.Partial : state;
        report.Sections[1].Error = user.Error;
        report.Status = ReportEvidenceSchema.Overall(report.Sections.Select(s => s.Status)); ReportEvidenceSchema.Seal(report);
        Assert.Equal(MailboxCapacity.UnableToCheck, MailboxCapacity.Review(Mailbox(), report).Eligibility);
    }

    [Fact]
    public void Wrong_tenant_modified_or_duplicate_evidence_refuses_instead_of_guessing()
    {
        var report = Evidence(null);
        Assert.Throws<TenantMismatchException>(() => MailboxCapacity.Review(Mailbox() with { TenantId = TestData.TenantB }, report));
        report.Sections[1].Rows[0]["name"] = "Modified";
        Assert.Throws<ConfigurationException>(() => MailboxCapacity.Review(Mailbox(), report));
        report = Evidence(null); report.Sections[1].Rows.Add((System.Text.Json.Nodes.JsonObject)report.Sections[1].Rows[0].DeepClone());
        Assert.Throws<ConfigurationException>(() => ReportEvidenceSchema.Seal(report));
    }

    [Fact]
    public void Unlimited_or_failed_primary_quota_does_not_become_100GB_or_zero()
    {
        var report = Evidence(Plan(MailboxCapacity.Plan2Id, "EXCHANGE_S_ENTERPRISE", "Success"));
        foreach (var mailbox in new[] { Mailbox() with { SendReceiveQuotaRaw = "Unlimited" }, Mailbox() with { PrimaryReadStatus = ReportReadState.Failed } })
        {
            var result = MailboxCapacity.Review(mailbox, report);
            Assert.Null(result.Configured100GB); Assert.Null(result.SendReceiveQuota.Bytes);
            Assert.Equal(MailboxCapacity.Eligible, result.Eligibility); Assert.Equal(MailboxCapacity.UnableToCheck, result.Finding);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("GroupMailbox")]
    [InlineData("ArchiveMailbox")]
    public void Missing_or_unreviewed_mailbox_type_cannot_confer_primary_entitlement(string? type)
    {
        var result = MailboxCapacity.Review(Mailbox() with { MailboxType = type }, Evidence(Plan(MailboxCapacity.Plan2Id, "EXCHANGE_S_ENTERPRISE", "Success")));
        Assert.Equal(MailboxCapacity.UnableToCheck, result.Eligibility);
        Assert.Equal("Configured100GBEntitlementUnconfirmed", result.Finding);
    }

    [Fact]
    public void Overlong_quota_text_keeps_raw_value_without_unbounded_interpretation()
    {
        var raw = new string('x', 5000) + " (107374182400 bytes)";
        var result = MailboxCapacity.Measure(raw, ReportReadState.Collected);
        Assert.Equal(raw, result.Raw); Assert.Null(result.Bytes); Assert.Equal(MailboxCapacity.UnableToCheck, result.State);
    }

    [Fact]
    public void Unlicensed_100GB_shared_mailbox_cannot_be_labelled_ineligible_without_legacy_evidence()
    {
        var result = MailboxCapacity.Review(Mailbox() with { MailboxType = "SharedMailbox" }, Evidence(null));
        Assert.True(result.Configured100GB);
        Assert.Equal(MailboxCapacity.UnableToCheck, result.Eligibility);
        Assert.Equal("Configured100GBEntitlementUnconfirmed", result.Finding);
        Assert.Contains("July 2018", result.EligibilityReason);
    }

    [Fact]
    public void Actual_Business_Premium_plan_set_does_not_confer_100GB_primary_capacity()
    {
        var report = Evidence(Plan(MailboxCapacity.Plan1Id, "EXCHANGE_S_STANDARD", "Success"));
        var user = ToolkitJson.Deserialize<UserLicenceReportRow>(report.Sections[1].Rows[0].ToJsonString());
        user.Products[0].ServicePlans.Add(Plan("176a09a6-7ec5-4039-ac02-b2791c6ba793", "EXCHANGE_S_ARCHIVE_ADDON", "Success"));
        user.Products[0].ServicePlans.Add(Plan("113feb6c-3fe4-4440-bddc-54d774bf0318", "EXCHANGE_S_FOUNDATION", "Success"));
        report.Sections[1].Rows[0] = ReportEvidenceSchema.Row(user); ReportEvidenceSchema.Seal(report);
        Assert.Equal(MailboxCapacity.NotEligible, MailboxCapacity.Review(Mailbox(), report).Eligibility);
    }

    [Fact]
    public void Failed_statistics_do_not_hide_observed_quotas_or_claim_a_zero_size()
    {
        var result = MailboxCapacity.Review(Mailbox() with {
            PrimarySizeReadStatus = ReportReadState.Failed, ArchiveSizeReadStatus = ReportReadState.Cancelled
        }, Evidence(Plan(MailboxCapacity.Plan2Id, "EXCHANGE_S_ENTERPRISE", "Success")));
        Assert.True(result.Configured100GB); Assert.Equal(MailboxCapacity.HundredGiB, result.SendReceiveQuota.Bytes);
        Assert.Equal(MailboxCapacity.UnableToCheck, result.PrimarySize.State); Assert.Null(result.PrimarySize.Bytes);
        Assert.Equal(MailboxCapacity.UnableToCheck, result.ArchiveSize.State); Assert.Null(result.ArchiveSize.Bytes);
        Assert.Equal("Unlimited", result.ArchiveQuota.State);
    }

    private static MailboxCapacity.Observation Mailbox() => new(TestData.TenantA, TestData.Office, TestData.Operator,
        "Same synthetic name", "engineer@example.invalid", "UserMailbox", At, ReportReadState.Collected,
        "0 B (0 bytes)", "98 GB (105,226,698,752 bytes)", "99 GB (106,300,440,576 bytes)",
        "100 GB (107,374,182,400 bytes)", ReportReadState.Collected, "0 B (0 bytes)", "Unlimited");

    private static AssignedPlanReport Plan(string id, string name, string status) => new()
    { ServicePlanId = id, ServicePlanName = name, ProvisioningStatus = status, AppliesTo = "User" };

    private static ReportEvidence Evidence(AssignedPlanReport? plan)
    {
        var definition = GraphReportRegistry.Find("users-licences");
        var user = new UserLicenceReportRow { Id = TestData.Operator, Name = "Same synthetic name", UserPrincipalName = "engineer@example.invalid",
            AccountEnabled = true, UserType = "Member", ReadStatus = ReportReadState.Collected, ProductsReadStatus = ReportReadState.Collected,
            Products = plan is null ? [] : [new() { SkuId = TestData.ClientId, SkuPartNumber = "Business Premium", ServicePlans = [plan] }] };
        var report = new ReportEvidence { Id = TestData.Mam, TenantId = TestData.TenantA, ReportId = definition.Id,
            SourceMode = "historical", StartedAt = At, EndedAt = At, ToolkitVersion = "synthetic-test", ModuleVersion = GraphReportRegistry.AdapterVersion,
            Status = ReportReadState.Collected,
            Sources = definition.Routes.Select(r => new ReportSource { Api = "v1.0", RegisteredRoute = r.Path, Reference = definition.Reference }).ToList(),
            Sections = [new() { Id = "subscriptions", Status = ReportReadState.Collected }, new() { Id = "users", Status = ReportReadState.Collected, Rows = [ReportEvidenceSchema.Row(user)] }] };
        ReportEvidenceSchema.Seal(report); return report;
    }
}
