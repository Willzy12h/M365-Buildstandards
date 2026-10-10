using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;

namespace BDIT.TenantToolkit.Tests;

/// <summary>Strict, immutable offline UI fixtures. Never invokes a collector, transport or token provider.</summary>
internal static class ApplicationReportUiFixtures
{
    internal const string At = "2026-09-10T10:00:00Z";
    internal static string Id(int value) => new Guid(value, 0, 0, new byte[8]).ToString("D");
    internal static ApplicationExpiryEvidence Expiry(string tenant, string account, string version, int count = 2)
    {
        var report = new ApplicationExpiryEvidence { Parameters = new() { HorizonDays = 30 } };
        Initialise(report, tenant, account, version);
        for (var section = 0; section < 2; section++)
        {
            for (var i = 0; i < count; i++)
            {
                var credential = new ApplicationCredentialMetadata { Kind = "password", KeyId = i == 0 ? null : Id(9300 + i),
                    DisplayName = "Synthetic credential " + i, Start = "2026-01-01T00:00:00Z", End = "2026-09-20T00:00:00Z" };
                (credential.Classification, credential.Reason) = ApplicationCredentialClassification.Classify(credential, DateTimeOffset.Parse(At), 30);
                var row = new ApplicationExpiryRow { Id = Id(9000 + section * 500 + i), Name = "Same synthetic application " + i,
                    IdentityStatus = "Exact", ObjectKind = section == 0 ? "application" : "servicePrincipal", ApplicationId = Id(9900),
                    Credentials = [credential], CredentialsReadStatus = i == 0 ? ReportReadState.Partial : ReportReadState.Collected,
                    CredentialReadReason = i == 0 ? "Synthetic unknown key identity." : null,
                    Owners = [new() { Id = Id(9800), Name = "Synthetic owner", ReadStatus = ReportReadState.Collected },
                        new() { Id = Id(9801), Name = null, ReadStatus = ReportReadState.Partial, Reason = "Synthetic limited owner information." }],
                    OwnersReadStatus = ReportReadState.Partial, OwnerReadReason = "Synthetic limited owner information.",
                    ReadStatus = ReportReadState.Partial, Error = "Synthetic metadata limitations; no live checks occurred." };
                report.Sections[section].Rows.Add(ApplicationReportEvidenceSchema.Row(row));
            }
            report.Sections[section].Status = ReportReadState.Partial; report.Sections[section].Error = "Synthetic incomplete metadata.";
            report.Sources[section].Rows = count;
            var owners = report.Sources[section + 2]; owners.Status = ReportReadState.Partial; owners.Error = "Synthetic limited owner information.";
            owners.Rows = count * 2; owners.ObjectsAttempted = count;
        }
        report.Status = ReportReadState.Partial;
        ApplicationReportEvidenceSchema.Seal(report); return report;
    }
    internal static ApplicationConsentEvidence Consent(string tenant, string account, string version)
    {
        var report = new ApplicationConsentEvidence(); Initialise(report, tenant, account, version);
        for (var i = 0; i < 2; i++)
            report.Sections[0].Rows.Add(ApplicationReportEvidenceSchema.Row(new ApplicationPrincipalReportRow
            {
                Id = Id(9000 + i), Name = "Same synthetic application", IdentityStatus = "Exact", ApplicationId = Id(9900),
                ReadStatus = ReportReadState.Collected, AppRolesReadStatus = ReportReadState.Collected,
                AppRoles = [new() { Id = Id(9700), DisplayName = "Synthetic read role", Value = "Synthetic.Read", IsEnabled = true },
                    new() { Id = Id(9701), DisplayName = "Synthetic write role", Value = "Synthetic.Write", IsEnabled = true }]
            }));
        report.Sources[0].Rows = 2;
        report.Sources[1].Status = report.Sections[1].Status = ReportReadState.NotAttempted;
        report.Sources[1].Error = report.Sections[1].Error = "Directory.Read.All absent from this synthetic source; delegated grants were not attempted.";
        for (var i = 0; i < 2; i++)
            report.Sections[2].Rows.Add(ApplicationReportEvidenceSchema.Row(new ApplicationRoleAssignmentReportRow
            {
                Id = "synthetic-assignment-" + i, QueriedPrincipalId = Id(9000 + i), PrincipalId = Id(9000 + i), ResourceId = Id(9001),
                AppRoleId = i == 0 ? Id(9700) : Id(9799), PrincipalName = "Same synthetic application", ResourceName = "Same synthetic application",
                RoleName = i == 0 ? "Synthetic read role" : null, RoleValue = i == 0 ? "Synthetic.Read" : null,
                ResolutionStatus = i == 0 ? "Resolved" : "Unresolved", ResolutionReason = i == 0 ? null : "Exact role metadata was not returned.",
                ReadStatus = i == 0 ? ReportReadState.Collected : ReportReadState.Partial, Error = i == 0 ? null : "Synthetic unresolved role."
            }));
        report.Sources[2].Rows = 2; report.Sources[2].ObjectsAttempted = 2;
        report.Sections[2].Status = ReportReadState.Partial; report.Sections[2].Error = "Synthetic unresolved role.";
        report.Status = ReportReadState.Partial; ApplicationReportEvidenceSchema.Seal(report); return report;
    }
    private static void Initialise(ApplicationReportEvidence report, string tenant, string account, string version)
    {
        var definition = ApplicationReportRegistry.Find(2, report.ReportId);
        report.Id = Guid.NewGuid().ToString("D"); report.TenantId = tenant; report.AccountObjectId = account;
        report.SourceMode = "historical"; report.StartedAt = At; report.EndedAt = At; report.ToolkitVersion = version;
        report.ModuleVersion = ApplicationReportRegistry.AdapterVersion;
        report.Limitations = ["OFFLINE SYNTHETIC UI FIXTURE. No authentication or tenant calls.", definition.Limitations];
        report.Sections = definition.Sections.Select(s => new ReportSection { Id = s.Id, Status = ReportReadState.Collected }).ToList();
        report.Sources = definition.Routes.Select(r => new ApplicationReportSource { RegisteredRoute = r.Path, Reference = definition.Reference,
            StartedAt = At, EndedAt = At, Status = ReportReadState.Collected }).ToList();
    }
}
