using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BDIT.TenantToolkit.Core.Models;

/// <summary>
/// INT-088 Exchange Online / Purview report evidence: a separate record kind from Graph <see cref="ReportEvidence"/>
/// and from the configuration <c>ExchangeCapture</c>. It is never a configuration snapshot, never deployment
/// before-evidence and never proof that a read was authorised beyond what it records.
/// </summary>
public sealed class ExchangeReportEvidence
{
    [JsonRequired] public int SchemaVersion { get; set; } = 1;
    [JsonRequired] public string Kind { get; set; } = "exchangeReportEvidence";
    [JsonRequired] public string Id { get; set; } = "";
    /// <summary>The parent-issued run GUID the child's result envelope had to repeat.</summary>
    [JsonRequired] public string RunId { get; set; } = "";
    [JsonRequired] public string ReportId { get; set; } = "";
    [JsonRequired] public int ReportSchemaVersion { get; set; } = 1;
    [JsonRequired] public string TenantId { get; set; } = "";
    /// <summary>Entra object ID of the account the toolkit verified through Graph before launching the run.</summary>
    [JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? InitiatingAccountObjectId { get; set; }
    /// <summary>User principal name the module itself reported for its connection. Not an object ID.</summary>
    [JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? ObservedAccountUpn { get; set; }
    /// <summary><c>ExchangeOnline</c> or <c>Purview</c>, fixed by the registered adapter.</summary>
    [JsonRequired] public string Resource { get; set; } = "";
    /// <summary><c>live</c> for a run this toolkit made; <c>historical</c> for imported output, which keeps its original claims.</summary>
    [JsonRequired] public string SourceMode { get; set; } = "live";
    [JsonRequired] public string StartedAt { get; set; } = "";
    [JsonRequired] public string EndedAt { get; set; } = "";
    [JsonRequired] public string ToolkitVersion { get; set; } = "";
    [JsonRequired] public string AdapterVersion { get; set; } = "";
    [JsonRequired] public string ModuleVersion { get; set; } = "";
    [JsonRequired] public string RuntimeVersion { get; set; } = "";
    [JsonRequired] public string ScriptSha256 { get; set; } = "";
    [JsonRequired] public string ManifestSha256 { get; set; } = "";
    [JsonRequired] public string RunnerTemplateSha256 { get; set; } = "";
    [JsonRequired] public List<ExchangeReportParameter> Parameters { get; set; } = [];
    [JsonRequired] public List<string> SourceCommands { get; set; } = [];
    [JsonRequired] public string Status { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public List<string> Limitations { get; set; } = [];
    [JsonRequired] public List<ReportSection> Sections { get; set; } = [];
    [JsonRequired] public string IntegrityDigest { get; set; } = "";
}

/// <summary>A registered, typed, normalised parameter value. Never script source and never a secret.</summary>
public sealed class ExchangeReportParameter
{
    [JsonRequired] public string Name { get; set; } = "";
    /// <summary>One of <see cref="ExchangeReportParameterType"/>.</summary>
    [JsonRequired] public string Type { get; set; } = "";
    /// <summary>Invariant normalised text: <c>true</c>/<c>false</c>, decimal integer, <c>yyyy-MM-dd</c> or plain text.</summary>
    [JsonRequired] public string Value { get; set; } = "";
}

public static class ExchangeReportParameterType
{
    public const string Boolean = "Boolean";
    public const string Integer = "Integer";
    public const string Date = "Date";
    public const string Text = "Text";
}

/// <summary>Only whitelisted, registered fields are projected; raw module objects are never stored.</summary>
public abstract class ExchangeReportRow
{
    /// <summary>The row's exact identity, as registered for its section.</summary>
    [JsonRequired] public string? Id { get; set; }
    [JsonRequired] public string? Name { get; set; }
    [JsonRequired] public string ReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public string? Error { get; set; }
}

/// <summary>
/// One mailbox. <see cref="ExchangeReportRow.Id"/> is the exact Exchange mailbox GUID; shared, room and equipment mailboxes
/// stay distinct even when they have no joinable user. Size and quota values are kept exactly as Exchange returned them,
/// each group with its own read state: a failed read is never zero, and Unlimited stays Unlimited.
/// </summary>
public sealed class MailboxReportRow : ExchangeReportRow
{
    /// <summary>The Entra object ID Exchange reported, when it reported one. Never inferred from a name or address.</summary>
    [JsonRequired] public string? ExternalDirectoryObjectId { get; set; }
    [JsonRequired] public string? PrimarySmtpAddress { get; set; }
    /// <summary>RecipientTypeDetails as returned, for example UserMailbox or SharedMailbox.</summary>
    [JsonRequired] public string? MailboxType { get; set; }
    [JsonRequired] public string PrimarySizeReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public string? PrimarySizeRaw { get; set; }
    [JsonRequired] public string QuotaReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public string? IssueWarningQuotaRaw { get; set; }
    [JsonRequired] public string? ProhibitSendQuotaRaw { get; set; }
    [JsonRequired] public string? ProhibitSendReceiveQuotaRaw { get; set; }
    /// <summary>Whether an archive exists. False only from a successful archive-state read; otherwise null.</summary>
    [JsonRequired] public string ArchiveReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public bool? HasArchive { get; set; }
    [JsonRequired] public string ArchiveSizeReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public string? ArchiveSizeRaw { get; set; }
    [JsonRequired] public string? ArchiveQuotaRaw { get; set; }
}
