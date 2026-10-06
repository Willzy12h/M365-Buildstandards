using System.IO.Compression;
using System.Text;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Standards;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>Exports verified catalogue assets and documents only; never reads a tenant or connection record.</summary>
public sealed class StandardDefinitionExporter(ToolkitPaths paths)
{
    public string Export(StandardCatalogue standard, ExportFormat format, DateTimeOffset now)
    {
        if (format is not (ExportFormat.Html or ExportFormat.Markdown or ExportFormat.Json))
            throw new ArgumentOutOfRangeException(nameof(format), "Standard definitions export as HTML, Markdown or exact catalogue JSON.");
        var bytes = VerifiedSource(standard);
        Directory.CreateDirectory(paths.ReportsDirectory);
        var extension = format switch { ExportFormat.Html => "html", ExportFormat.Markdown => "md", _ => "json" };
        var file = Path.Combine(paths.ReportsDirectory, BaseName(standard, now) + "." + extension);
        if (format == ExportFormat.Json) File.WriteAllBytes(file, bytes);
        else File.WriteAllText(file, format == ExportFormat.Html ? StandardSpecificationDocuments.Html(standard, now) : StandardSpecificationDocuments.Markdown(standard, now), new UTF8Encoding(false));
        // Every file carries its own digest, so a single reused JSON keeps its link to the verified source (CLA-20261006-16).
        WriteDigest(file);
        return file;
    }

    public string ExportSet(StandardCatalogue standard, DateTimeOffset now)
    {
        var bytes = VerifiedSource(standard);
        Directory.CreateDirectory(paths.ReportsDirectory);
        var name = BaseName(standard, now);
        var folder = Path.Combine(paths.ReportsDirectory, name);
        var staging = folder + ".staging";
        var zip = folder + ".zip";
        Directory.CreateDirectory(staging);
        try
        {
            var catalogues = Path.Combine(staging, "standards");
            Directory.CreateDirectory(catalogues);
            File.WriteAllBytes(Path.Combine(catalogues, standard.SourceFileName), bytes);
            StandardsManifest.Write(catalogues, StandardsManifest.Generate(catalogues, "M365 BuildStandard definition export " + ToolkitVersion.Current, now));
            Write("defaults-and-settings.html", StandardSpecificationDocuments.Html(standard, now));
            Write("defaults-and-settings.md", StandardSpecificationDocuments.Markdown(standard, now));
            Write("capability-and-access-matrix.md", CapabilityDocuments.Markdown(standard));
            if (standard.Controls.All(EngineerStandardDocuments.HasCompleteManual))
            {
                Write("manual-guide.html", EngineerStandardDocuments.Html(standard, EngineerDocumentKind.ManualGuide));
                Write("manual-guide.md", EngineerStandardDocuments.Markdown(standard, EngineerDocumentKind.ManualGuide));
            }
            Write("README.md", "# M365 Build Standard definition export\n\nRelease: " + standard.Release + "\n\nSource SHA-256: " + standard.IntegrityDigest
                + "\n\n" + StandardSpecificationDocuments.Explanation
                + "\n\nOpen defaults-and-settings.html in a browser, print it to PDF, or use defaults-and-settings.md as a document source. "
                + "standards/ contains the exact verified catalogue JSON and a loader-compatible integrity manifest. JSON retains placeholders; it is a reusable definition, not a deployment plan. "
                + "Review the source/version and manifest before reusing JSON in another tool or a reviewed catalogue release. This export does not install a catalogue; Load local candidate is for separately saved policy-import candidates. Do not overwrite a published standard or an installation's manifest. "
                + "The manifest detects changes but is not a publisher signature. Trust the source/release through your approved channel. "
                + "Manual guides are included only when this release contains complete procedures. No tenant profile, credentials, captured configuration or connection settings are exported.\n");
            var checksums = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => CanonicalJson.Sha256Hex(File.ReadAllBytes(f)) + "  " + Path.GetRelativePath(staging, f).Replace('\\', '/'));
            Write("SHA256SUMS.txt", string.Join('\n', checksums) + "\n");
            Directory.Move(staging, folder);
            ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
            WriteDigest(zip);
            return zip;
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            if (File.Exists(zip)) File.Delete(zip);
            if (File.Exists(zip + ".sha256")) File.Delete(zip + ".sha256");
            throw;
        }

        void Write(string file, string content) => File.WriteAllText(Path.Combine(staging, file), content, new UTF8Encoding(false));
    }

    /// <summary>Release and UTC export time, so exports sort chronologically; a short suffix keeps same-second exports distinct.</summary>
    private static string BaseName(StandardCatalogue standard, DateTimeOffset now) =>
        $"standard-definition-{ReportExporter.SafeName(standard.Release)}-{now.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}-{Guid.NewGuid().ToString("N")[..8]}";

    private static void WriteDigest(string file) =>
        File.WriteAllText(file + ".sha256", CanonicalJson.Sha256Hex(File.ReadAllBytes(file)) + "  " + Path.GetFileName(file) + "\n", new UTF8Encoding(false));

    private byte[] VerifiedSource(StandardCatalogue standard)
    {
        if (string.IsNullOrWhiteSpace(standard.IntegrityDigest))
            throw new IntegrityException("A default/settings export requires a manifest-verified catalogue. Load a verified release first.");
        var reloaded = new StandardsLoader(paths, NullLog.Instance).Load(standard.SourceFileName);
        if (!string.Equals(reloaded.IntegrityDigest, standard.IntegrityDigest, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(CanonicalJson.Sha256Value(reloaded), CanonicalJson.Sha256Value(standard), StringComparison.Ordinal))
            throw new IntegrityException("The catalogue changed after loading. Reload it before exporting its definition.");
        var bytes = File.ReadAllBytes(Path.Combine(paths.StandardsDirectory, standard.SourceFileName));
        if (!string.Equals(CanonicalJson.Sha256Hex(bytes), standard.IntegrityDigest, StringComparison.OrdinalIgnoreCase))
            throw new IntegrityException("The catalogue changed during export. No definition was exported.");
        return bytes;
    }
}
