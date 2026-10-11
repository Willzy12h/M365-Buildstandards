using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Core.Reporting;

/// <summary>Schema/ID pairs and metadata-only application routes. Does not change schema 1 registrations or scopes.</summary>
public static class ApplicationReportRegistry
{
    public const int SchemaVersion = 2;
    public const string AdapterVersion = "registered-graph-application-report/1";
    public const string ExpiryAdapterVersion = "registered-graph-application-expiry/2";
    public const int MaximumRows = GraphReportRegistry.MaximumRows;
    public const int MaximumCredentials = 256;
    public const int MaximumOwners = 100;
    public const int MaximumAppRoles = 256;
    public const int MaximumAssignmentsPerPrincipal = 256;
    public const int MaximumChildObjects = 256;
    public const string ApplicationScope = "Application.Read.All";
    public const string DirectoryScope = "Directory.Read.All";
    public const string ApplicationsReference = "https://learn.microsoft.com/en-us/graph/api/application-list?view=graph-rest-1.0";
    public const string ConsentReference = "https://learn.microsoft.com/en-us/graph/api/serviceprincipal-list-approleassignments?view=graph-rest-1.0";
    public sealed record Route(string Path, string Scope);
    public sealed record Section(string Id, Type RowType, bool GuidIdentity);
    public sealed record Definition(string Id, string Name, Type EnvelopeType, IReadOnlyList<Section> Sections,
        IReadOnlyList<Route> Routes, string Reference, string Limitations)
    {
        public int ReportSchemaVersion => Id == "application-expiry" ? 2 : 1;
        public string RegisteredAdapter => Id == "application-expiry" ? ExpiryAdapterVersion : AdapterVersion;
    }
    private static Definition Make(string id, string name, Type type, Section[] sections, Route[] routes, string reference, string limitations)
        => new(id, name, type, Array.AsReadOnly(sections), Array.AsReadOnly(routes), reference, limitations);
    public static IReadOnlyList<Definition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        Make("application-expiry", "Application credential expiry metadata", typeof(ApplicationExpiryEvidence),
            [new("applications", typeof(ApplicationExpiryRow), true), new("service-principals", typeof(ApplicationExpiryRow), true)],
            [new("/applications", ApplicationScope), new("/servicePrincipals", ApplicationScope),
                new("/applications/{id}/owners", ApplicationScope), new("/servicePrincipals/{id}/owners", ApplicationScope)], ApplicationsReference,
            "Application registrations and enterprise applications are distinct exact objects. Metadata and owner names do not establish credential possession, trust or authority to rotate. No secret values or certificate/key bytes are retained. Classification uses the fixed collection end and visible horizon."),
        Make("application-consent", "Observed application consent", typeof(ApplicationConsentEvidence),
            [new("principals", typeof(ApplicationPrincipalReportRow), true), new("delegated-grants", typeof(DelegatedGrantReportRow), false),
                new("app-role-assignments", typeof(ApplicationRoleAssignmentReportRow), false)],
            [new("/servicePrincipals", ApplicationScope), new("/oauth2PermissionGrants", DirectoryScope),
                new("/servicePrincipals/{id}/appRoleAssignments", ApplicationScope)], ConsentReference,
            "Observed delegated grants and app-role assignments are separate from requested permissions and do not prove token contents, permission use or effective access. Exact resource and role IDs alone join roles. Grant/assignment changes have replication delays; a later deliberate read may be needed. No consent, revocation, deletion or automatic write/read replay is offered.")
    });
    public static Definition Find(int schemaVersion, string id) => schemaVersion == SchemaVersion
        ? Definitions.SingleOrDefault(d => d.Id == id) ?? throw new ConfigurationException("Unknown schema 2 application report.")
        : throw new ConfigurationException("Application reports require envelope schema 2.");
    public static bool HasAccess(TenantSession session, Route route) => session.Scopes.Contains(route.Scope, StringComparer.OrdinalIgnoreCase)
        || route.Scope == ApplicationScope && session.Scopes.Contains(DirectoryScope, StringComparer.OrdinalIgnoreCase);
}

/// <summary>The same fixed-end classification rule is used by projection and strict imports.</summary>
public static class ApplicationCredentialClassification
{
    public static (string State, string? Reason) Classify(ApplicationCredentialMetadata credential, DateTimeOffset endedAt, int horizonDays)
    {
        if (!ReportValues.IsCanonicalGuid(credential.KeyId)) return ("Unknown", "Credential key ID is missing, zero or malformed.");
        if (credential.Kind == "key" && (!ReportValues.HasText(credential.Type) || !ReportValues.HasText(credential.Usage)))
            return ("Unknown", "Key credential type or usage is not reported.");
        if (!Date(credential.Start, out var start) || !Date(credential.End, out var end) || end <= start)
            return ("Unknown", "Credential dates are missing, malformed or do not define a valid interval.");
        if (end <= endedAt) return ("Expired", null);
        if (start > endedAt) return ("NotYetValid", null);
        DateTimeOffset horizon;
        try { horizon = endedAt.AddDays(horizonDays); }
        catch (ArgumentOutOfRangeException) { return ("Unknown", "The selected horizon exceeds the supported time range."); }
        return end <= horizon ? ("ExpiringWithinHorizon", null) : ("ValidBeyondHorizon", null);
    }
    public static bool Date(string? text, out DateTimeOffset at)
    {
        at = default;
        return text is { Length: > 0 and <= 128 }
            && System.Text.RegularExpressions.Regex.IsMatch(text,
                @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
            && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out at);
    }
}
