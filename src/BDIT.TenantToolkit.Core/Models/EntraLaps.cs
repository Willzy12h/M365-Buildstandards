using System.Text.Json.Nodes;

namespace BDIT.TenantToolkit.Core.Models;

public sealed class EntraLapsPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = "";
    public string OperatorId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string StandardDigest { get; set; } = "";
    public string SnapshotId { get; set; } = "";
    public string SnapshotDigest { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public JsonObject Before { get; set; } = new();
    public JsonObject Payload { get; set; } = new();
    public bool AlreadyEnabled { get; set; }
    public string Consequence { get; set; } = "Enable tenant-wide Windows LAPS password backup to Entra ID. Existing device LAPS policies can start backing up passwords. No device policy is assigned and no local account is created. Other registration settings are preserved. Disabling this later is a separate tenant-wide change, not automatic rollback.";
    public string IntegrityDigest { get; set; } = "";
}

public sealed class EntraLapsRun
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string PlanDigest { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string Status { get; set; } = RunStatus.Running;
    public string WriteAcceptance { get; set; } = Models.WriteAcceptance.NotAttempted;
    public string Verification { get; set; } = ConfigurationVerification.NotRun;
    public JsonObject? After { get; set; }
    public string? Error { get; set; }
    public string IntegrityDigest { get; set; } = "";
}

public sealed class EntraLapsVerification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = "";
    public string RunId { get; set; } = "";
    public string RunDigest { get; set; } = "";
    public string OperatorId { get; set; } = "";
    public DateTimeOffset CheckedAt { get; set; }
    public string Verification { get; set; } = ConfigurationVerification.Unknown;
    public JsonObject? After { get; set; }
    public string? Error { get; set; }
    public string IntegrityDigest { get; set; } = "";
}
