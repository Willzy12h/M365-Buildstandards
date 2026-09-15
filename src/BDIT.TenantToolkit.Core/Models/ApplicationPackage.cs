using System.Text.Json.Nodes;
namespace BDIT.TenantToolkit.Core.Models;

public enum Win32ContentAction { CreateVersion, CreateFile, CommitFile, PublishVersion }
public sealed class PackagePublishPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = "";
    public string OperatorId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ControlId { get; set; } = "";
    public string ObjectId { get; set; } = "";
    public string StandardDigest { get; set; } = "";
    public string MappingsDigest { get; set; } = "";
    public string SnapshotId { get; set; } = "";
    public string SnapshotDigest { get; set; } = "";
    public string PackageSha256 { get; set; } = "";
    public string InstallerName { get; set; } = "";
    public long EncryptedBytes { get; set; }
    public long UnencryptedBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public JsonObject Before { get; set; } = new();
    public string Consequence { get; set; } = "Upload and publish this package into the recorded, unassigned Win32 app. No assignment is created. Installer commands, detection and uninstall rules remain those of the reviewed app. Vendor console registration and device execution need independent verification.";
    public string IntegrityDigest { get; set; } = "";
}
public sealed class PackagePublishStep
{
    public string Action { get; set; } = "";
    public string Acceptance { get; set; } = WriteAcceptance.Unknown;
    public string? ObjectId { get; set; }
}
public sealed class PackagePublishRun
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ControlId { get; set; } = "";
    public string ObjectId { get; set; } = "";
    public string PlanDigest { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string Status { get; set; } = RunStatus.Running;
    public string Verification { get; set; } = ConfigurationVerification.NotRun;
    public string? VersionId { get; set; }
    public string? FileId { get; set; }
    public List<PackagePublishStep> Steps { get; set; } = new();
    public JsonObject? After { get; set; }
    public string? Error { get; set; }
    public string IntegrityDigest { get; set; } = "";
}
public sealed class PackageVerification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = "";
    public string RunId { get; set; } = "";
    public string RunDigest { get; set; } = "";
    public string OperatorId { get; set; } = "";
    public bool Verified { get; set; }
    public string? VersionId { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public JsonObject After { get; set; } = new();
    public string IntegrityDigest { get; set; } = "";
}
