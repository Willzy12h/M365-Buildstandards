using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Core.Reporting;

/// <summary>Package-owned report routes and row schemas. Engineers cannot supply arbitrary Graph paths.</summary>
public static class GraphReportRegistry
{
    public const int MaximumRows = 5000;
    public const string AdapterVersion = "registered-graph-report/1";
    public sealed record Section(string Id, Type RowType, bool GuidIdentity);
    public sealed record Route(string Path, string Scope);
    public sealed record Definition(string Id, string Name, bool DateRange,
        IReadOnlyList<Section> Sections, IReadOnlyList<Route> Routes, string Reference, string Limitations);

    private static Definition Make(string id, string name, bool dates, Section[] sections, Route[] routes, string reference, string limitations)
        => new(id, name, dates, Array.AsReadOnly(sections), Array.AsReadOnly(routes), reference, limitations);

    public static IReadOnlyList<Definition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        Make("users-licences", "Users and assigned licences", false,
            [new("subscriptions", typeof(SubscriptionReportRow), false), new("users", typeof(UserLicenceReportRow), true)],
            [new("/subscribedSkus", "Organization.Read.All"), new("/users", "User.Read.All")],
            "https://learn.microsoft.com/en-us/graph/api/user-list-licensedetails?view=graph-rest-1.0",
            "Assignments use exact user/SKU/service-plan IDs and observed per-user licence details, never display-name joins. Subscription availability does not prove user entitlement. Sections are collected at different times within the recorded interval."),
        Make("intune-devices", "Intune managed devices", false, [new("devices", typeof(DeviceReportRow), true)],
            [new("/deviceManagement/managedDevices", "DeviceManagementManagedDevices.Read.All")],
            "https://learn.microsoft.com/en-us/graph/api/intune-devices-manageddevice-list?view=graph-rest-1.0",
            "Only Intune-returned managed devices are included. Enrolment type and management agent are reported separately; they do not identify physical form factor. Missing ownership, compliance or last sync stays unknown."),
        Make("mfa-registration", "Authentication and MFA registration", false, [new("registration", typeof(RegistrationReportRow), true)],
            [new("/reports/authenticationMethods/userRegistrationDetails", "AuditLog.Read.All")],
            "https://learn.microsoft.com/en-us/graph/api/authenticationmethodsroot-list-userregistrationdetails?view=graph-rest-1.0",
            "Registration/capability does not prove enforcement or successful use. Reported method labels do not identify a physical device. No phone/email method values, recovery codes or secrets are collected. New AuditLog.Read.All access is proposed, not granted by source development."),
        Make("sign-ins", "Sign-in logs", true, [new("sign-ins", typeof(SignInReportRow), false)],
            [new("/auditLogs/signIns", "AuditLog.Read.All")],
            "https://learn.microsoft.com/en-us/graph/api/signin-list?view=graph-rest-1.0",
            "Requested range is distinct from available retention; this adapter cannot prove full historical retention. Microsoft licence, access and role restrictions may prevent reads. Missing status/error codes never imply successful sign-in. New AuditLog.Read.All access is proposed, not granted by source development."),
        Make("directory-audit", "Directory audit logs", true, [new("audit", typeof(DirectoryAuditReportRow), false)],
            [new("/auditLogs/directoryAudits", "AuditLog.Read.All")],
            "https://learn.microsoft.com/en-us/graph/api/directoryaudit-list?view=graph-rest-1.0",
            "Requested range is distinct from available retention. Returned actor/target IDs are preserved; modified-property values are excluded because they can contain sensitive data. This is a directory audit, not Exchange/Purview unified audit. Existing Directory.Read.All is accepted on this documented route; otherwise AuditLog.Read.All is proposed, never granted by source development.")
    });

    public static Definition Find(string id) => Definitions.SingleOrDefault(d => d.Id == id)
        ?? throw new ConfigurationException("Unknown registered report: " + id + ".");

    // Microsoft's directory-audit route alone accepts Directory.Read.All as a higher permission.
    // Registration and sign-in endpoints still require AuditLog.Read.All.
    public static bool HasAccess(TenantSession session, Route route) => HasScope(session, route.Scope)
        || route.Path == "/auditLogs/directoryAudits" && session.Scopes.Contains("Directory.Read.All", StringComparer.OrdinalIgnoreCase);

    public static bool HasScope(TenantSession session, string scope) => session.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase)
        || scope is "User.Read.All" or "Organization.Read.All" && session.Scopes.Contains("Directory.Read.All", StringComparer.OrdinalIgnoreCase);
}
