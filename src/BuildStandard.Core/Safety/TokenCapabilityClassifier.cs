namespace BuildStandard.Core.Safety;

public enum TokenCapability { ReadOnlyVerified, BroaderPresent, Unclassified }

/// <summary>Observed scopes only: never proves role sufficiency, resource access or permission to execute.</summary>
public sealed record TokenCapabilityResult(TokenCapability Classification,
    IReadOnlyList<string> ClassifiedScopes, IReadOnlyList<string> UnclassifiedScopes)
{
    public string Description => Classification switch
    {
        TokenCapability.ReadOnlyVerified => "Token: read-only capabilities verified",
        TokenCapability.BroaderPresent => "Token: broader permissions present" + (UnclassifiedScopes.Count > 0 ? "; some capabilities not classified" : ""),
        _ => "Token: not fully classified"
    };
}

/// <summary>Conservative package-owned classification, independent of Graph's write guards and scope requests.</summary>
public static class TokenCapabilityClassifier
{
    private const string GraphPrefix = "https://graph.microsoft.com/";
    // Existing catalogue, diagnostic and registered-report read capabilities. Recognition does not request consent.
    private static readonly HashSet<string> Reads = new(StringComparer.OrdinalIgnoreCase)
    {
        "User.Read", "User.Read.All", "Organization.Read.All", "RoleManagement.Read.Directory",
        "Application.Read.All", "Group.Read.All", "Directory.Read.All", "AuditLog.Read.All",
        "DeviceManagementApps.Read.All", "DeviceManagementConfiguration.Read.All",
        "DeviceManagementManagedDevices.Read.All", "DeviceManagementServiceConfig.Read.All",
        "Policy.Read.All", "Policy.Read.AuthenticationMethod", "Policy.Read.DeviceConfiguration"
    };
    // Existing deployment/setup capabilities plus delegated examples that defeat the old spelling proxy.
    private static readonly HashSet<string> Broader = new(StringComparer.OrdinalIgnoreCase)
    {
        "Directory.AccessAsUser.All", "User.Invite.All", "Mail.Send", "Directory.Write.All",
        "Directory.ReadWrite.All", "Application.ReadWrite.All", "Application.ReadWrite.OwnedBy",
        "AppRoleAssignment.ReadWrite.All", "DelegatedPermissionGrant.ReadWrite.All", "Group.ReadWrite.All",
        "WindowsUpdates.ReadWrite.All",
        "DeviceManagementApps.ReadWrite.All", "DeviceManagementConfiguration.ReadWrite.All",
        "DeviceManagementManagedDevices.ReadWrite.All", "DeviceManagementServiceConfig.ReadWrite.All",
        "Policy.ReadWrite.AuthenticationMethod", "Policy.ReadWrite.Authorization",
        "Policy.ReadWrite.ConditionalAccess", "Policy.ReadWrite.ConsentRequest",
        "Policy.ReadWrite.DeviceConfiguration", "Policy.ReadWrite.MobilityManagement"
    };
    private static readonly HashSet<string> Protocol = new(StringComparer.OrdinalIgnoreCase)
        { "openid", "profile", "offline_access", "email" };

    public static TokenCapabilityResult Classify(IEnumerable<string>? returnedScopes)
    {
        var classified = new List<string>(); var unknown = new List<string>();
        var read = false; var broader = false;
        foreach (var raw in returnedScopes ?? Array.Empty<string>())
        {
            var scope = raw ?? "";
            // Preserve unknown/blank input. Do not trim, parse arbitrary URLs, or infer access from a suffix.
            var graphScope = scope.StartsWith(GraphPrefix, StringComparison.OrdinalIgnoreCase) ? scope[GraphPrefix.Length..] : scope;
            if (Broader.Contains(graphScope)) { broader = true; classified.Add(scope); }
            else if (Reads.Contains(graphScope)) { read = true; classified.Add(scope); }
            else if (Protocol.Contains(scope)) classified.Add(scope);
            else unknown.Add(scope);
        }
        var kind = broader ? TokenCapability.BroaderPresent
            : read && unknown.Count == 0 ? TokenCapability.ReadOnlyVerified : TokenCapability.Unclassified;
        return new(kind, Array.AsReadOnly(classified.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()),
            Array.AsReadOnly(unknown.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
    }
}
