using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Standards;
namespace BuildStandard.Engine.Reports;

/// <summary>Engineer-entered context only; never proof of remediation, assessment or ownership.</summary>
public sealed record IncidentControlReference(string SourceSha256, string IncidentId, string StandardRelease, string StandardSha256, string ControlId);
public static class IncidentControlReferences
{
    public static IncidentControlReference Create(ToolkitPaths paths, OperationalContextFile source, string incidentId, StandardCatalogue standard, string controlId)
    {
        var bytes = source.CopyOriginal(); RequireIncident(source, incidentId);
        var catalogueBytes = VerifiedCatalogueSource.Read(paths, standard);
        var catalogue = StandardsLoader.Parse(VerifiedCatalogueSource.Decode(catalogueBytes), standard.SourceFileName);
        if (!catalogue.Controls.Any(c => c.Id == controlId)) throw new ConfigurationException("Choose an exact control in the verified catalogue.");
        return new(CanonicalJson.Sha256Hex(bytes), incidentId, catalogue.Release, CanonicalJson.Sha256Hex(catalogueBytes), controlId);
    }
    public static ExternalControlReferenceResolution Resolve(ToolkitPaths paths, IncidentControlReference link, OperationalContextFile source, StandardCatalogue? standard)
    {
        try
        {
            RequireIncident(source, link.IncidentId);
            if (link.SourceSha256 != CanonicalJson.Sha256Hex(source.CopyOriginal())) return Missing("The original incident evidence changed; no reference was rebound.");
            if (standard is null || link.StandardRelease != standard.Release || link.StandardSha256 != standard.IntegrityDigest)
                return Missing("Load the exact referenced catalogue release and digest; current names cannot substitute.");
            var bytes = VerifiedCatalogueSource.Read(paths, standard);
            if (CanonicalJson.Sha256Hex(bytes) != link.StandardSha256) return Missing("The referenced catalogue bytes changed.");
            var control = StandardsLoader.Parse(VerifiedCatalogueSource.Decode(bytes), standard.SourceFileName).Controls.SingleOrDefault(c => c.Id == link.ControlId);
            return control is null ? Missing("The exact referenced control is unavailable.") : new(true,
                "Engineer-entered context only. An incident status does not establish remediation, compliance or ownership.", control.Name);
        }
        catch (Exception error) when (error is ToolkitException or IOException or UnauthorizedAccessException)
        { return Missing("The original incident or catalogue could not be verified. The reference remains unresolved."); }
    }
    private static void RequireIncident(OperationalContextFile source, string incidentId)
    {
        // Re-admit immutable bytes rather than trusting the mutable presentation model.
        var report = OperationalContextFile.Read(source.CopyOriginal(), source.Report.TenantId).Report;
        if (report.ReportId != "security-incidents" || report.Sections.Count != 1
            || report.Sections[0].Rows.Count(row => row["id"]?.GetValue<string>() == incidentId) != 1)
            throw new ConfigurationException("Choose one unambiguous incident ID from the exact original incident report.");
    }
    private static ExternalControlReferenceResolution Missing(string reason) => new(false, reason, "");
}
