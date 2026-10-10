using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// INT-071 over INT-088: every mailbox in a sealed inventory record is reviewed against one sealed assigned-licence report.
/// All records are synthetic; nothing is read from a tenant.
/// </summary>
public sealed class MailboxCapacityInventoryTests
{
    private const string UserMailbox = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private const string SharedMailbox = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
    private const string UserObject = "cccccccc-cccc-4ccc-8ccc-cccccccccccc";
    private const string Hundred = "100 GB (107,374,182,400 bytes)";

    private static MailboxReportRow User(bool? hasArchive = true) => new()
    {
        Id = UserMailbox, Name = "Synthetic user", ReadStatus = ReportReadState.Collected, Error = null,
        ExternalDirectoryObjectId = UserObject, PrimarySmtpAddress = "user@synthetic.example", MailboxType = "UserMailbox",
        PrimarySizeReadStatus = ReportReadState.Collected, PrimarySizeRaw = "1.2 GB (1,288,490,189 bytes)",
        QuotaReadStatus = ReportReadState.Collected, IssueWarningQuotaRaw = "98 GB (105,226,698,752 bytes)",
        ProhibitSendQuotaRaw = "99 GB (106,300,440,576 bytes)", ProhibitSendReceiveQuotaRaw = Hundred,
        ArchiveReadStatus = hasArchive is null ? ReportReadState.NotAttempted : ReportReadState.Collected, HasArchive = hasArchive,
        ArchiveSizeReadStatus = hasArchive == true ? ReportReadState.Collected : ReportReadState.NotAttempted,
        ArchiveSizeRaw = hasArchive == true ? "300 MB (314,572,800 bytes)" : null,
        ArchiveQuotaRaw = hasArchive == true ? Hundred : null
    };

    private static MailboxReportRow Shared() => new()
    {
        Id = SharedMailbox, Name = "Synthetic shared", ReadStatus = ReportReadState.Collected, Error = null,
        ExternalDirectoryObjectId = null, PrimarySmtpAddress = "shared@synthetic.example", MailboxType = "SharedMailbox",
        PrimarySizeReadStatus = ReportReadState.Collected, PrimarySizeRaw = "0 B (0 bytes)",
        QuotaReadStatus = ReportReadState.Collected, IssueWarningQuotaRaw = "Unlimited", ProhibitSendQuotaRaw = "Unlimited",
        ProhibitSendReceiveQuotaRaw = "Unlimited", ArchiveReadStatus = ReportReadState.Collected, HasArchive = false,
        ArchiveSizeReadStatus = ReportReadState.NotAttempted, ArchiveSizeRaw = null, ArchiveQuotaRaw = null
    };

    private static ExchangeReportEvidence Inventory(string sectionStatus, string? sectionError, params MailboxReportRow[] rows)
    {
        var definition = ExchangeReportRegistry.Find(MailboxCapacityInventory.ReportId);
        var report = new ExchangeReportEvidence
        {
            Id = Guid.NewGuid().ToString("D"), RunId = Guid.NewGuid().ToString("D"), ReportId = definition.Id,
            TenantId = TestData.TenantA, InitiatingAccountObjectId = TestData.Operator, ObservedAccountUpn = "operator@synthetic.example",
            Resource = definition.Resource, SourceMode = "live", StartedAt = "2026-10-10T01:00:00Z", EndedAt = "2026-10-10T01:05:00Z",
            ToolkitVersion = "0.0.0-test", AdapterVersion = ExchangeReportRegistry.AdapterVersion, ModuleVersion = "3.9.0",
            RuntimeVersion = "7.4.6", ScriptSha256 = new string('a', 64), ManifestSha256 = new string('b', 64),
            RunnerTemplateSha256 = new string('c', 64),
            Parameters = [new() { Name = "IncludeArchive", Type = ExchangeReportParameterType.Boolean, Value = "true" }],
            SourceCommands = [.. definition.SourceCommands], Limitations = [definition.Limitations],
            Sections = [new ReportSection { Id = "mailboxes", Status = sectionStatus, Limitations = [], Error = sectionError,
                Rows = [.. rows.Select(ExchangeReportEvidenceSchema.Row)] }],
            Status = sectionStatus
        };
        ExchangeReportEvidenceSchema.Seal(report);
        return report;
    }

    private static ExchangeReportEvidence Collected() => Inventory(ReportReadState.Collected, null, User(), Shared());

    private static ReportEvidence Licences(string tenant = TestData.TenantA, string planId = MailboxCapacity.Plan2Id, string planName = "EXCHANGE_S_ENTERPRISE")
    {
        var definition = GraphReportRegistry.Find("users-licences");
        var plan = new AssignedPlanReport { ServicePlanId = planId, ServicePlanName = planName, ProvisioningStatus = "Success", AppliesTo = "User" };
        var user = new UserLicenceReportRow { Id = UserObject, Name = "Synthetic user", UserPrincipalName = "user@synthetic.example",
            AccountEnabled = true, UserType = "Member", ReadStatus = ReportReadState.Collected, ProductsReadStatus = ReportReadState.Collected,
            Products = [new() { SkuId = TestData.ClientId, SkuPartNumber = "Business Premium", ServicePlans = [plan] }] };
        var report = new ReportEvidence { Id = TestData.Mam, TenantId = tenant, ReportId = definition.Id,
            SourceMode = "historical", StartedAt = "2026-10-10T00:50:00Z", EndedAt = "2026-10-10T00:55:00Z", ToolkitVersion = "synthetic-test",
            ModuleVersion = GraphReportRegistry.AdapterVersion, Status = ReportReadState.Collected,
            Sources = definition.Routes.Select(r => new ReportSource { Api = "v1.0", RegisteredRoute = r.Path, Reference = definition.Reference }).ToList(),
            Sections = [new() { Id = "subscriptions", Status = ReportReadState.Collected }, new() { Id = "users", Status = ReportReadState.Collected, Rows = [ReportEvidenceSchema.Row(user)] }] };
        ReportEvidenceSchema.Seal(report);
        return report;
    }

    private static MailboxCapacityInventory.Mailbox Reviewed(MailboxCapacityInventory.Review review, string id) =>
        review.Mailboxes.Single(m => m.Capacity.Observed.MailboxId == id);

    [Fact]
    public void Every_mailbox_is_reviewed_by_exact_identity_and_both_records_are_cited()
    {
        var inventory = Collected();
        var review = MailboxCapacityInventory.Evaluate(inventory, Licences());

        Assert.Equal(2, review.Mailboxes.Count);
        Assert.Equal(inventory.Id, review.MailboxEvidenceId);
        Assert.Equal(inventory.EndedAt, review.MailboxesObservedAt);
        Assert.Equal(TestData.Mam, review.LicenceEvidenceId);
        Assert.Equal(ReportReadState.Collected, review.SectionStatus);
        var user = Reviewed(review, UserMailbox);
        Assert.Equal("Configured100GBAndEligible", user.Capacity.Finding);
        Assert.Equal(MailboxCapacity.HundredGiB, user.Capacity.SendReceiveQuota.Bytes);
        Assert.Equal(inventory.EndedAt, user.Capacity.Observed.CapturedAt);
        // A shared mailbox has no joinable user: it stays listed, unable to check, and Unlimited stays Unlimited.
        var shared = Reviewed(review, SharedMailbox);
        Assert.Equal(MailboxCapacity.UnableToCheck, shared.Capacity.Eligibility);
        Assert.Equal("Unlimited", shared.Capacity.SendReceiveQuota.State);
        Assert.Null(shared.Capacity.Configured100GB);
    }

    [Fact]
    public void A_plan_1_user_with_a_100GB_quota_is_configured_but_not_eligible()
    {
        var review = MailboxCapacityInventory.Evaluate(Collected(), Licences(planId: MailboxCapacity.Plan1Id, planName: "EXCHANGE_S_STANDARD"));
        Assert.Equal("Configured100GBButNotEligible", Reviewed(review, UserMailbox).Capacity.Finding);
    }

    [Fact]
    public void A_value_that_was_not_read_stays_unable_to_check_and_the_section_keeps_its_limitation()
    {
        var failedQuota = User();
        failedQuota.QuotaReadStatus = ReportReadState.Failed; failedQuota.IssueWarningQuotaRaw = null;
        failedQuota.ProhibitSendQuotaRaw = null; failedQuota.ProhibitSendReceiveQuotaRaw = null;
        failedQuota.ReadStatus = ReportReadState.Partial; failedQuota.Error = "The mailbox quotas were not all returned.";
        const string limitation = "More than 10,000 mailboxes were found; only the first 10,000 are listed.";
        var review = MailboxCapacityInventory.Evaluate(Inventory(ReportReadState.Partial, limitation, failedQuota, Shared()), Licences());

        Assert.Equal(ReportReadState.Partial, review.SectionStatus);
        Assert.Equal(limitation, review.SectionError);
        var user = Reviewed(review, UserMailbox);
        Assert.Equal(ReportReadState.Partial, user.RowReadStatus);
        Assert.Equal("The mailbox quotas were not all returned.", user.RowError);
        Assert.Null(user.Capacity.Configured100GB);
        Assert.Null(user.Capacity.SendReceiveQuota.Bytes);
        Assert.Equal(MailboxCapacity.UnableToCheck, user.Capacity.SendReceiveQuota.State);
        // Eligibility is still known; only the configured quota is not.
        Assert.Equal(MailboxCapacity.Eligible, user.Capacity.Eligibility);
        Assert.Equal(MailboxCapacity.UnableToCheck, user.Capacity.Finding);
        // The primary size was read separately and is still observed.
        Assert.Equal("Observed", user.Capacity.PrimarySize.State);
    }

    [Fact]
    public void Archive_values_apply_only_to_an_archive_read_as_present()
    {
        var withArchive = MailboxCapacityInventory.Evaluate(Collected(), Licences());
        var user = Reviewed(withArchive, UserMailbox);
        Assert.True(user.HasArchive);
        Assert.Equal(MailboxCapacity.HundredGiB, user.Capacity.ArchiveQuota.Bytes);
        Assert.Equal(314572800L, user.Capacity.ArchiveSize.Bytes);

        var shared = Reviewed(withArchive, SharedMailbox);
        Assert.False(shared.HasArchive);
        Assert.Null(shared.Capacity.ArchiveQuota.Bytes);

        var failedArchive = User(hasArchive: null);
        failedArchive.ArchiveReadStatus = ReportReadState.Failed; failedArchive.ReadStatus = ReportReadState.Partial;
        failedArchive.Error = "Whether the mailbox has an archive was not returned.";
        var notRead = MailboxCapacityInventory.Evaluate(
            Inventory(ReportReadState.Partial, "Some mailbox values could not be read; each row says which.", failedArchive), Licences());
        var unknown = Reviewed(notRead, UserMailbox);
        Assert.Null(unknown.HasArchive);
        Assert.Equal(MailboxCapacity.UnableToCheck, unknown.Capacity.ArchiveQuota.State);
    }

    [Fact]
    public void A_section_that_was_not_read_lists_no_mailboxes_and_says_why()
    {
        var review = MailboxCapacityInventory.Evaluate(Inventory(ReportReadState.Failed, "The module refused the read."), Licences());
        Assert.Empty(review.Mailboxes);
        Assert.Equal(ReportReadState.Failed, review.SectionStatus);
        Assert.Equal("The module refused the read.", review.SectionError);
    }

    [Fact]
    public void Records_from_another_tenant_or_report_or_changed_after_sealing_are_refused()
    {
        Assert.Throws<TenantMismatchException>(() => MailboxCapacityInventory.Evaluate(Collected(), Licences(tenant: TestData.TenantB)));

        var notLicences = Licences();
        notLicences.ReportId = "users-sign-in-activity";
        Assert.ThrowsAny<ToolkitException>(() => MailboxCapacityInventory.Evaluate(Collected(), notLicences));

        var changed = Collected();
        changed.Sections[0].Rows[0]["prohibitSendReceiveQuotaRaw"] = "Unlimited";
        Assert.ThrowsAny<ToolkitException>(() => MailboxCapacityInventory.Evaluate(changed, Licences()));
    }
}
