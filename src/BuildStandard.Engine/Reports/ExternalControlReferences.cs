using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Standards;
namespace BuildStandard.Engine.Reports;

/// <summary>An engineer's reference only, never a verdict mapping or evidence of control ownership.</summary>
public sealed record ExternalControlReference(string SourceSha256, string SourceTestId, string StandardRelease, string StandardSha256, string ControlId);
public sealed record ExternalControlReferenceResolution(bool Resolved, string Reason, string ControlName);

/// <summary>Exact source/catalogue reference admission. No assessment, deployment or job state is changed.</summary>
public static class ExternalControlReferences
{
    public static ExternalControlReference Create(ToolkitPaths paths, ExternalCheckImport source, string testId,
        StandardCatalogue standard, string controlId)
    {
        RequireSourceTest(source, testId);
        var fileName = standard.SourceFileName;
        var bytes = VerifiedCatalogueSource.Read(paths, standard);
        var snapshot = StandardsLoader.Parse(VerifiedCatalogueSource.Decode(bytes), fileName);
        if (!snapshot.Controls.Any(c => c.Id == controlId)) throw new ConfigurationException("Choose an exact existing control in this verified catalogue.");
        return new(source.OriginalSha256, testId, snapshot.Release, CanonicalJson.Sha256Hex(bytes), controlId);
    }

    public static ExternalControlReferenceResolution Resolve(ToolkitPaths paths, ExternalControlReference reference,
        ExternalCheckImport source, StandardCatalogue? standard)
    {
        if (reference.SourceSha256 != source.OriginalSha256 || !source.Evidence.Rows.Any(r => r.TestId == reference.SourceTestId))
            return Missing("The exact original external result is unavailable or changed. This reference was not rebound.");
        if (standard is null || reference.StandardRelease != standard.Release || reference.StandardSha256 != standard.IntegrityDigest)
            return Missing("Load the exact referenced standard release and digest; a newer release is not a substitute.");
        try
        {
            var fileName = standard.SourceFileName;
            var bytes = VerifiedCatalogueSource.Read(paths, standard);
            if (reference.StandardSha256 != CanonicalJson.Sha256Hex(bytes)) return Missing("The referenced catalogue bytes changed.");
            var snapshot = StandardsLoader.Parse(VerifiedCatalogueSource.Decode(bytes), fileName);
            var control = snapshot.Controls.SingleOrDefault(c => c.Id == reference.ControlId);
            return control is null ? Missing("The referenced control is unavailable; it was not matched by name.")
                : new(true, "Engineer-entered reference only. External claims do not establish native compliance, ownership or completion.", control.Name);
        }
        catch (Exception ex) when (ex is ToolkitException or IOException or UnauthorizedAccessException)
        { return Missing("The referenced catalogue could not be verified. No control or compliance result was inferred."); }
    }
    private static void RequireSourceTest(ExternalCheckImport source, string testId)
    {
        if (!source.Evidence.Rows.Any(r => r.TestId == testId)) throw new ConfigurationException("Choose an exact source test ID from the admitted external results.");
    }
    private static ExternalControlReferenceResolution Missing(string reason) => new(false, reason, "");
}
