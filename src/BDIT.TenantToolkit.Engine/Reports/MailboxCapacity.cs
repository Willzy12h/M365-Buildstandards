using System.Globalization;
using System.Text.RegularExpressions;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>INT-071 pure evaluation. Does not collect, change quotas or establish deployment authority.</summary>
public static partial class MailboxCapacity
{
    public const long HundredGiB = 100L * 1024 * 1024 * 1024;
    public const string Eligible = "Eligible";
    public const string NotEligible = "NotEligible";
    public const string UnableToCheck = "UnableToCheck";
    public const string Plan2Id = "efb87545-963c-4e0d-99df-69c6916d9eb0";
    public const string Plan1Id = "9aaf7827-d63c-4b61-89c3-182f06f82e5c";
    public const string ArchiveAddonId = "176a09a6-7ec5-4039-ac02-b2791c6ba793";
    public const string FoundationId = "113feb6c-3fe4-4440-bddc-54d774bf0318";
    public const string LimitsReference = "https://learn.microsoft.com/en-us/office365/servicedescriptions/exchange-online-service-description/exchange-online-limits";
    public const string PlansReference = "https://learn.microsoft.com/en-us/entra/identity/users/licensing-service-plan-reference";

    // Adapter input, not a persisted evidence schema. A future collector must verify its own tenant/account context.
    public sealed record Observation(string TenantId, string MailboxId, string? ExternalDirectoryObjectId,
        string? Name, string? PrimarySmtpAddress, string? MailboxType, string CapturedAt,
        string PrimaryReadStatus, string? PrimarySizeRaw, string? WarningQuotaRaw,
        string? SendQuotaRaw, string? SendReceiveQuotaRaw,
        string ArchiveReadStatus, string? ArchiveSizeRaw, string? ArchiveQuotaRaw,
        string? PrimarySizeReadStatus = null, string? ArchiveSizeReadStatus = null);
    public sealed record Measurement(string? Raw, long? Bytes, string State, string Reason);
    public sealed record Result(Observation Observed, Measurement PrimarySize, Measurement WarningQuota,
        Measurement SendQuota, Measurement SendReceiveQuota, Measurement ArchiveSize, Measurement ArchiveQuota,
        bool? Configured100GB, string Eligibility, string EligibilityReason, string Finding,
        string LicenceEvidenceId, string LicencesObservedAt);

    public static Result Review(Observation mailbox, ReportEvidence licences)
    {
        ReportEvidenceSchema.Validate(licences, mailbox.TenantId);
        if (licences.ReportId != "users-licences") throw new ConfigurationException("Mailbox eligibility requires the registered users and assigned licences report.");
        if (!ReportValues.IsCanonicalGuid(mailbox.MailboxId)
            || !Timestamps.TryParse(mailbox.CapturedAt, out _) || !mailbox.CapturedAt.EndsWith('Z'))
            throw new ConfigurationException("Mailbox observation needs an exact identity and UTC capture time.");
        if (!ReportReadState.All.Contains(mailbox.PrimaryReadStatus, StringComparer.Ordinal)
            || !ReportReadState.All.Contains(mailbox.ArchiveReadStatus, StringComparer.Ordinal)
            || mailbox.PrimarySizeReadStatus is { } ps && !ReportReadState.All.Contains(ps, StringComparer.Ordinal)
            || mailbox.ArchiveSizeReadStatus is { } ars && !ReportReadState.All.Contains(ars, StringComparer.Ordinal))
            throw new ConfigurationException("Unknown mailbox read status.");
        var size = Measure(mailbox.PrimarySizeRaw, mailbox.PrimarySizeReadStatus ?? mailbox.PrimaryReadStatus);
        var warning = Measure(mailbox.WarningQuotaRaw, mailbox.PrimaryReadStatus);
        var send = Measure(mailbox.SendQuotaRaw, mailbox.PrimaryReadStatus);
        var receive = Measure(mailbox.SendReceiveQuotaRaw, mailbox.PrimaryReadStatus);
        var archiveSize = Measure(mailbox.ArchiveSizeRaw, mailbox.ArchiveSizeReadStatus ?? mailbox.ArchiveReadStatus);
        var archiveQuota = Measure(mailbox.ArchiveQuotaRaw, mailbox.ArchiveReadStatus);
        bool? configured = receive.Bytes is { } bytes ? bytes == HundredGiB : null;
        var (eligibility, reason) = mailbox.MailboxType is "UserMailbox" or "SharedMailbox" or "RoomMailbox" or "EquipmentMailbox"
            ? Entitlement(mailbox.ExternalDirectoryObjectId, licences)
            : (UnableToCheck, "Mailbox type is missing or outside the reviewed primary-mailbox rules; archive and group capacity are separate.");
        // Creation time is not established by this observation. Microsoft documents a pre-July-2018
        // unlicensed shared-mailbox exception: an observed 100 GB quota alone cannot disprove entitlement.
        if (configured == true && eligibility == NotEligible && mailbox.MailboxType == "SharedMailbox")
            (eligibility, reason) = (UnableToCheck, "The observed 100 GB shared-mailbox quota may reflect the documented pre-July 2018 unlicensed exception; creation and legacy entitlement have not been verified.");
        var finding = (configured, eligibility) switch
        {
            (true, Eligible) => "Configured100GBAndEligible",
            (true, NotEligible) => "Configured100GBButNotEligible",
            (true, _) => "Configured100GBEntitlementUnconfirmed",
            (false, Eligible) when receive.Bytes < HundredGiB => "EligibleButLowerQuota",
            (false, Eligible) => "EligibleWithDifferentQuota",
            (_, NotEligible) => "NotEligible",
            _ => UnableToCheck
        };
        return new(mailbox, size, warning, send, receive, archiveSize, archiveQuota,
            configured, eligibility, reason, finding, licences.Id, licences.EndedAt);
    }

    /// <summary>Only Exchange's explicit integer byte count is interpreted. Raw text is always retained.</summary>
    public static Measurement Measure(string? raw, string readStatus)
    {
        if (readStatus != ReportReadState.Collected)
            return new(raw, null, UnableToCheck, "The size or quota read was incomplete, failed or not attempted.");
        if (string.IsNullOrWhiteSpace(raw)) return new(raw, null, UnableToCheck, "No size or quota value was returned.");
        if (raw.Length > 4096) return new(raw, null, UnableToCheck, "The returned size or quota text exceeds the supported interpretation bound.");
        if (raw.Trim().Equals("Unlimited", StringComparison.OrdinalIgnoreCase))
            return new(raw, null, "Unlimited", "Unlimited is not an observed 100 GB primary quota.");
        // Do not guess units from a rounded GB display, decimal commas, localised text or an overflow.
        var match = ByteCount().Match(raw);
        if (!match.Success || !long.TryParse(match.Groups[1].Value.Replace(",", "", StringComparison.Ordinal),
                NumberStyles.None, CultureInfo.InvariantCulture, out var bytes))
            return new(raw, null, UnableToCheck, "No unambiguous supported byte count was returned.");
        return new(raw, bytes, "Observed", "Explicit Exchange byte count; original text retained.");
    }

    private static (string State, string Reason) Entitlement(string? objectId, ReportEvidence report)
    {
        if (!ReportValues.IsCanonicalGuid(objectId))
            return (UnableToCheck, "No exact ExternalDirectoryObjectId was returned; names and SMTP addresses are not licence joins.");
        var users = report.Sections.Single(s => s.Id == "users");
        var matches = users.Rows.Select(r => ToolkitJson.Deserialize<UserLicenceReportRow>(r.ToJsonString()))
            .Where(r => string.Equals(r.Id, objectId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1) return (UnableToCheck, "No unique matching user identity exists in the assigned-licence evidence.");
        var user = matches[0];
        if (user.ReadStatus != ReportReadState.Collected || user.ProductsReadStatus != ReportReadState.Collected)
            return (UnableToCheck, "Assigned products or service-plan reads for this exact user are incomplete.");
        var plans = user.Products.SelectMany(p => p.ServicePlans).ToList();
        var exchange = plans.Where(p => p.ServicePlanName?.StartsWith("EXCHANGE_", StringComparison.Ordinal) == true
            || p.ServicePlanId is Plan1Id or Plan2Id or ArchiveAddonId or FoundationId).ToList();
        if (exchange.Any(p => p.ServicePlanId == Plan2Id && p.ServicePlanName == "EXCHANGE_S_ENTERPRISE"
            && p.ProvisioningStatus == "Success" && p.AppliesTo == "User"))
            return (Eligible, "The exact user has a successfully provisioned commercial Exchange Online Plan 2 service plan. This does not prove its configured quota or current Microsoft behaviour.");
        // Exact pairs reviewed against Microsoft's licensing CSV on 8 October 2026.
        // Archive add-on/Foundation do not confer primary capacity. Unknown variants remain unknown.
        if (exchange.Any(p => !ReviewedCommercialPlan(p.ServicePlanId, p.ServicePlanName)
            || p.AppliesTo != "User" || p.ProvisioningStatus is not ("Success" or "Disabled")))
            return (UnableToCheck, "An Exchange plan identity, applicability or provisioning state is outside the reviewed commercial rules.");
        return (NotEligible, "Complete assigned-plan evidence does not contain an enabled reviewed Exchange Online Plan 2 plan. Plan 1, archive add-ons and product display names do not confer 100 GB primary capacity.");
    }

    private static bool ReviewedCommercialPlan(string? id, string? name) => (id, name) is
        (Plan1Id, "EXCHANGE_S_STANDARD") or (Plan2Id, "EXCHANGE_S_ENTERPRISE")
        or (ArchiveAddonId, "EXCHANGE_S_ARCHIVE_ADDON")
        or (FoundationId, "EXCHANGE_S_FOUNDATION");

    [GeneratedRegex(@"\A[0-9]+(?:\.[0-9]+)? (?:B|KB|MB|GB|TB) \(([0-9]+|[1-9][0-9]{0,2}(?:,[0-9]{3})+) bytes\)\z", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ByteCount();
}
