using System.Text.Json.Nodes;

namespace BDIT.TenantToolkit.Core.Models;

public enum RecoveryAction { DeleteCreatedObject, RestoreUpdate, DisableConditionalAccess }

/// <summary>A single reviewed recovery action. This is not a general-purpose Graph request.</summary>
public sealed class RecoveryPlan
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string OperatorId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string StandardDigest { get; set; } = "";
    public string BeforeSnapshotId { get; set; } = "";
    public string BeforeSnapshotDigest { get; set; } = "";
    public string SourceRunId { get; set; } = "";
    public string SourceRunDigest { get; set; } = "";
    public string ControlId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Collection { get; set; } = "";
    public string ObjectId { get; set; } = "";
    public RecoveryAction Action { get; set; }
    public string CreatedAt { get; set; } = "";
    public JsonObject CurrentObject { get; set; } = new();
    public JsonObject? Payload { get; set; }
    public string MappingsDigest { get; set; } = "";
    public bool DriftDetected { get; set; }
    public string Consequence { get; set; } = "";
    public string IntegrityDigest { get; set; } = "";
}

public sealed class RecoveryRun
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string SourceRunId { get; set; } = "";
    public string ControlId { get; set; } = "";
    public string ObjectId { get; set; } = "";
    public RecoveryAction Action { get; set; }
    public RunActor Actor { get; set; } = new();
    public string StartedAt { get; set; } = "";
    public string? EndedAt { get; set; }
    public string Status { get; set; } = RunStatus.Running;
    public string WriteAcceptance { get; set; } = global::BDIT.TenantToolkit.Core.Models.WriteAcceptance.NotAttempted;
    public string Verification { get; set; } = ConfigurationVerification.NotRun;
    public string? Reason { get; set; }
    public JsonObject BeforeObject { get; set; } = new();
    public JsonObject? AfterObject { get; set; }
    public bool ObjectAbsent { get; set; }
    public string PlanDigest { get; set; } = "";
    public string IntegrityDigest { get; set; } = "";
}

public sealed record ChangeRegisterRow(string RunId, string ControlId, string Name, string Collection,
    string Action, string ObjectId, string At, string Acceptance, string Verification, string RecoveryStatus);
