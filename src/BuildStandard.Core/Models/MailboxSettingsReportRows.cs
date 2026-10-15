using System.Text.Json.Serialization;

namespace BuildStandard.Core.Models;

/// <summary>INT-121 observed mailbox settings; Id is the exact ExchangeGuid, never a name or address.</summary>
public sealed class MailboxProtocolReportRow : ExchangeReportRow
{
    [JsonRequired] public string? PrimarySmtpAddress { get; set; }
    [JsonRequired] public string PopEnabledReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public bool? PopEnabled { get; set; }
    [JsonRequired] public string ImapEnabledReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public bool? ImapEnabled { get; set; }
    [JsonRequired] public string MapiEnabledReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public bool? MapiEnabled { get; set; }
    [JsonRequired] public string EwsEnabledReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public bool? EwsEnabled { get; set; }
    [JsonRequired] public string ActiveSyncEnabledReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public bool? ActiveSyncEnabled { get; set; }
    [JsonRequired] public string OwaEnabledReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public bool? OwaEnabled { get; set; }
    [JsonRequired] public string SmtpAuthOverrideReadStatus { get; set; } = ReportReadState.NotAttempted;
    /// <summary>Enabled, Disabled, Inherit or Unknown; inheritance is observed returned null, not a missing property.</summary>
    [JsonRequired] public string SmtpAuthOverride { get; set; } = "Unknown";
    [JsonRequired] public string OrganisationSmtpAuthDisabledReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public bool? OrganisationSmtpAuthDisabled { get; set; }
    [JsonRequired] public string EffectiveSmtpAuthDisabledReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public bool? EffectiveSmtpAuthDisabled { get; set; }
}

/// <summary>INT-121 observed mailbox settings; Id is the exact ExchangeGuid, never a name or address.</summary>
public sealed class MailboxForwardingReportRow : ExchangeReportRow
{
    [JsonRequired] public string? PrimarySmtpAddress { get; set; }
    /// <summary>Collected null means explicitly unset; an unsuccessful read remains unknown.</summary>
    [JsonRequired] public string ForwardingAddressReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public string? ForwardingAddress { get; set; }
    /// <summary>Collected null means explicitly unset; an unsuccessful read remains unknown.</summary>
    [JsonRequired] public string ForwardingSmtpAddressReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public string? ForwardingSmtpAddress { get; set; }
    [JsonRequired] public string DeliverToMailboxAndForwardReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public bool? DeliverToMailboxAndForward { get; set; }
}

/// <summary>INT-121 observed mailbox settings; Id is the exact ExchangeGuid, never a name or address.</summary>
public sealed class MailboxHoldReportRow : ExchangeReportRow
{
    [JsonRequired] public string? PrimarySmtpAddress { get; set; }
    [JsonRequired] public string LitigationHoldEnabledReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public bool? LitigationHoldEnabled { get; set; }
    [JsonRequired] public string RetentionHoldEnabledReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public bool? RetentionHoldEnabled { get; set; }
    [JsonRequired] public string ComplianceTagHoldAppliedReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public bool? ComplianceTagHoldApplied { get; set; }
    [JsonRequired] public string DelayHoldAppliedReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public bool? DelayHoldApplied { get; set; }
    [JsonRequired] public string InPlaceHoldIdsReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public List<string>? InPlaceHoldIds { get; set; }
}

