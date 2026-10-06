using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>Generated allowlisted metadata only. Does not read logs, settings, profiles, captures or caches.</summary>
public static class SupportBundle
{
    public const string Guide = "Local technical metadata only. No logs, paths, account/tenant identifiers, settings, profiles, snapshots, plans or authentication caches are included. Nothing is uploaded. This small bundle cannot diagnose a tenant-specific failure by itself. Review additional evidence separately before sharing it.";

    public static string Preview(StandardCatalogue? standard) =>
        "Files: README.txt, technical-metadata.txt, SHA256SUMS.txt\n\n" + Guide + "\n\n" + Metadata(standard);

    public static string Export(ToolkitPaths paths, StandardCatalogue? standard)
    {
        Directory.CreateDirectory(paths.ReportsDirectory);
        var file = Path.Combine(paths.ReportsDirectory, "support-metadata-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var archive = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                var files = new Dictionary<string, string> { ["README.txt"] = Guide + "\n", ["technical-metadata.txt"] = Metadata(standard) };
                foreach (var (name, value) in files) Write(archive, name, value);
                Write(archive, "SHA256SUMS.txt", string.Join('\n', files.OrderBy(f => f.Key, StringComparer.Ordinal)
                    .Select(f => CanonicalJson.Sha256Hex(Encoding.UTF8.GetBytes(f.Value)) + "  " + f.Key)) + "\n");
            }
            File.WriteAllText(file + ".sha256", CanonicalJson.Sha256Hex(File.ReadAllBytes(file)) + "  " + Path.GetFileName(file) + "\n", new UTF8Encoding(false));
            return file;
        }
        catch { File.Delete(file); File.Delete(file + ".sha256"); throw; }
    }

    private static string Metadata(StandardCatalogue? standard)
    {
        // Labels in imported catalogues may contain client names. Export only constrained version/digest fields.
        var release = standard is not null && Regex.IsMatch(standard.Release, @"\A[0-9]+(?:\.[0-9]+){2,3}\z") ? standard.Release : "not a published version identifier";
        var digest = standard is not null && Regex.IsMatch(standard.IntegrityDigest, @"\A[0-9a-fA-F]{64}\z") ? standard.IntegrityDigest : "not manifest verified";
        return $"Product version: {ToolkitVersion.Current}\nFramework: {RuntimeInformation.FrameworkDescription}\nProcess architecture: {RuntimeInformation.ProcessArchitecture}\nWindows: {OperatingSystem.IsWindows()}\nStandard release: {release}\nCatalogue SHA-256: {digest}\nControls: {standard?.Controls.Count ?? 0}\n";
    }

    private static void Write(ZipArchive archive, string name, string value)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(value);
    }
}
