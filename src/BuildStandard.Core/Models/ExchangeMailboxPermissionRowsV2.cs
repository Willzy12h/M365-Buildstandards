using System.Text.Json.Serialization;
namespace BuildStandard.Core.Models;

/// <summary>One selected source, not an effective access verdict. Unselected and failed reads are distinct.</summary>
public sealed class ExchangePermissionRead
{
    [JsonRequired] public string Kind { get; set; } = "";
    [JsonRequired] public bool Selected { get; set; }
    [JsonRequired] public string Status { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public string? Error { get; set; }
    [JsonRequired] public int? ReturnedEntries { get; set; }
    [JsonRequired] public int? ExcludedEntries { get; set; }
}

/// <summary>Exact mailbox lookup plus a separate diagnostic for each of the three permission sources.</summary>
public sealed class ExchangePermissionTargetRow : ExchangeReportRow
{
    [JsonRequired] public string RequestedExchangeGuid { get; set; } = "";
    [JsonRequired] public string? ObservedExchangeGuid { get; set; }
    [JsonRequired] public string? ExternalDirectoryObjectId { get; set; }
    [JsonRequired] public string? PrimarySmtpAddress { get; set; }
    [JsonRequired] public string LookupReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public List<ExchangePermissionRead> PermissionReads { get; set; } = [];
}

/// <summary>Original principal/rights/flags identify one observed edge; nothing here establishes effective access.</summary>
public sealed class MailboxPermissionObservationRow : ExchangeReportRow
{
    [JsonRequired] public string MailboxExchangeGuid { get; set; } = "";
    [JsonRequired] public string PermissionKind { get; set; } = "";
    [JsonRequired] public ExchangeReferenceValue Principal { get; set; } = new();
    [JsonRequired] public ExchangeObserved<List<string>> Rights { get; set; } = new();
    [JsonRequired] public ExchangeObserved<bool?> IsInherited { get; set; } = new();
    [JsonRequired] public ExchangeObserved<bool?> Deny { get; set; } = new();
}
