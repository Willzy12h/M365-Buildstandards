using System.Text.Json.Nodes;

namespace BDIT.TenantToolkit.Core.Models;

/// <summary>Delegated permission resolved from the tenant's Microsoft Graph service principal.</summary>
public sealed class SetupPermission
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool AdminConsentRequired { get; set; }
}

public sealed class ApplicationSetupRow
{
    public SessionMode Mode { get; set; }
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "Create";
    public string Reason { get; set; } = "";
    public List<SetupPermission> Permissions { get; set; } = new();
    public JsonObject ApplicationPayload { get; set; } = new();
    public List<JsonObject> ExistingMatches { get; set; } = new();
    public string ClientId { get; set; } = "";
    public string ApplicationObjectId { get; set; } = "";
    public string ServicePrincipalId { get; set; } = "";
    public JsonObject? ExistingApplication { get; set; }
    public JsonObject? ExistingPrincipal { get; set; }
}

public sealed class ApplicationSetupPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = "";
    public string TenantName { get; set; } = "";
    public string OperatorId { get; set; } = "";
    public string OperatorName { get; set; } = "";
    public string StandardHash { get; set; } = "";
    public string StandardRelease { get; set; } = "";
    public string GraphServicePrincipalId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public List<ApplicationSetupRow> Rows { get; set; } = new();
    public JsonObject Before { get; set; } = new();
    public string PlanHash { get; set; } = "";
    public bool AssignOperator { get; set; }
    public string LogoDigest { get; set; } = "";
}

public sealed class ApplicationSetupItemResult
{
    public SessionMode Mode { get; set; }
    public string DisplayName { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ApplicationObjectId { get; set; } = "";
    public string ServicePrincipalId { get; set; } = "";
    public string Status { get; set; } = "Not run";
    public string ApplicationWrite { get; set; } = "Not attempted";
    public string ServicePrincipalWrite { get; set; } = "Not attempted";
    public string ConfigurationVerification { get; set; } = "Not checked";
    public string Reason { get; set; } = "";
    public List<SetupWriteRecord> AdditionalWrites { get; set; } = new();
}

public sealed class SetupWriteRecord
{
    public string Action { get; set; } = "";
    public string PayloadDigest { get; set; } = "";
    public string Acceptance { get; set; } = "Not attempted";
}

public sealed class ApplicationSetupResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = "";
    public string OperatorId { get; set; } = "";
    public string PlanId { get; set; } = "";
    public string Status { get; set; } = "Running";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string EvidenceDirectory { get; set; } = "";
    public bool AfterComplete { get; set; }
    public string AfterError { get; set; } = "";
    public List<ApplicationSetupItemResult> Rows { get; set; } = new();
    public string NextSteps { get; set; } = "Approve each application permission list, then validate configuration, actual grants and engineer assignment. Continue directly into assessment or deployment. Application setup has no automatic rollback; directory roles and secrets are never created.";
}

public sealed class ApplicationPermissionValidation
{
    public SessionMode Mode { get; set; }
    public string ClientId { get; set; } = "";
    public string ApplicationObjectId { get; set; } = "";
    public string ServicePrincipalId { get; set; } = "";
    public bool ConfigurationValid { get; set; }
    public bool ConsentComplete { get; set; }
    public bool? AssignmentRequired { get; set; }
    public bool EngineerAssignmentConfirmed { get; set; }
    public string EngineerAssignmentStatus { get; set; } = "Not checked";
    public List<string> RequiredScopes { get; set; } = new();
    public List<string> ConfiguredScopes { get; set; } = new();
    public List<string> GrantedScopes { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> MissingScopes => RequiredScopes.Except(GrantedScopes, StringComparer.OrdinalIgnoreCase).ToList();
    public string PermissionSummary => $"Required {RequiredScopes.Count} · configured {ConfiguredScopes.Count} · tenant-wide granted {GrantedScopes.Count} · missing {MissingScopes.Count}";
    public string AccessStatus { get; set; } = "Not tested. Reconnect using this application to check effective access; grants do not prove roles, licensing or successful writes.";
}

public sealed class ApplicationSetupValidation
{
    public string TenantId { get; set; } = "";
    public DateTimeOffset CheckedAt { get; set; }
    public List<ApplicationPermissionValidation> Rows { get; set; } = new();
    public bool Ready => Rows.Count == 2 && Rows.All(r => r.ConfigurationValid && r.ConsentComplete && r.EngineerAssignmentConfirmed);
}
