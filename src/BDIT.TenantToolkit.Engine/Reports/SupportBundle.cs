using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>Optional support-bundle sections, each chosen by the engineer and shown in the preview before export. All off by default.</summary>
public sealed record SupportSections(bool Environment = false, bool RecentErrors = false, bool CollectionStatus = false, bool Timeouts = false)
{
    public static SupportSections None { get; } = new();
    public bool Any => Environment || RecentErrors || CollectionStatus || Timeouts;
}

/// <summary>
/// The values the optional sections are drawn from, captured once so the exported text is exactly the text previewed.
/// The snapshot and settings are read only for the constrained fields named in <see cref="SupportBundle"/>.
/// </summary>
public sealed record SupportContext(double? DisplayScalePercent, TenantSnapshot? Snapshot, ToolkitSettings? Settings, IReadOnlyList<GraphErrorRecord> Errors)
{
    public static SupportContext Empty { get; } = new(null, null, null, Array.Empty<GraphErrorRecord>());
}

/// <summary>
/// Generated allowlisted metadata only. Never reads logs, profiles, plans or authentication caches. The optional sections
/// (CLA-20261006-10) add OS build, display scale, recent Graph error classifications with Microsoft correlation IDs,
/// collection read status counts and timeout settings, and nothing that names a tenant, account or object.
/// </summary>
public static class SupportBundle
{
    public const string Guide = "Local technical metadata only. No logs, paths, account/tenant identifiers, settings, profiles, snapshots, plans or authentication caches are included. Nothing is uploaded. This small bundle cannot diagnose a tenant-specific failure by itself. Review additional evidence separately before sharing it.";

    public const string OptionalGuide = "Optional sections you selected are in diagnostics.txt. They contain no tenant, account, object or client names: only the Windows version, display scale, Graph error status codes with Microsoft request IDs, collection read status counts and timeout settings. Microsoft support can use a request ID to find the failed call.";

    private static readonly Regex CollectionKey = new(@"\A[A-Za-z0-9_.-]{1,80}\z", RegexOptions.CultureInvariant);

    public static string Preview(StandardCatalogue? standard) => Preview(standard, SupportSections.None, SupportContext.Empty);

    public static string Preview(StandardCatalogue? standard, SupportSections sections, SupportContext context)
    {
        var files = Files(standard, sections, context);
        var names = string.Join(", ", files.Keys.Append("SHA256SUMS.txt"));
        var text = new StringBuilder("Files: " + names + "\n\n" + files["README.txt"].TrimEnd('\n') + "\n\n" + files["technical-metadata.txt"]);
        if (files.TryGetValue("diagnostics.txt", out var diagnostics)) text.Append('\n').Append(diagnostics);
        return text.ToString();
    }

    public static string Export(ToolkitPaths paths, StandardCatalogue? standard) => Export(paths, standard, SupportSections.None, SupportContext.Empty);

    public static string Export(ToolkitPaths paths, StandardCatalogue? standard, SupportSections sections, SupportContext context)
    {
        Directory.CreateDirectory(paths.ReportsDirectory);
        var file = Path.Combine(paths.ReportsDirectory, "support-metadata-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var archive = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                var files = Files(standard, sections, context);
                foreach (var (name, value) in files) Write(archive, name, value);
                Write(archive, "SHA256SUMS.txt", string.Join('\n', files.OrderBy(f => f.Key, StringComparer.Ordinal)
                    .Select(f => CanonicalJson.Sha256Hex(Encoding.UTF8.GetBytes(f.Value)) + "  " + f.Key)) + "\n");
            }
            File.WriteAllText(file + ".sha256", CanonicalJson.Sha256Hex(File.ReadAllBytes(file)) + "  " + Path.GetFileName(file) + "\n", new UTF8Encoding(false));
            return file;
        }
        catch { File.Delete(file); File.Delete(file + ".sha256"); throw; }
    }

    private static Dictionary<string, string> Files(StandardCatalogue? standard, SupportSections sections, SupportContext context)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["README.txt"] = Guide + "\n" + (sections.Any ? OptionalGuide + "\n" : ""),
            ["technical-metadata.txt"] = Metadata(standard)
        };
        if (sections.Any) files["diagnostics.txt"] = Diagnostics(sections, context);
        return files;
    }

    private static string Metadata(StandardCatalogue? standard)
    {
        // Labels in imported catalogues may contain client names. Export only constrained version/digest fields.
        var release = standard is not null && Regex.IsMatch(standard.Release, @"\A[0-9]+(?:\.[0-9]+){2,3}\z") ? standard.Release : "not a published version identifier";
        var digest = standard is not null && Regex.IsMatch(standard.IntegrityDigest, @"\A[0-9a-fA-F]{64}\z") ? standard.IntegrityDigest : "not manifest verified";
        return $"Product version: {ToolkitVersion.Current}\nFramework: {RuntimeInformation.FrameworkDescription}\nProcess architecture: {RuntimeInformation.ProcessArchitecture}\nWindows: {OperatingSystem.IsWindows()}\nStandard release: {release}\nCatalogue SHA-256: {digest}\nControls: {standard?.Controls.Count ?? 0}\n";
    }

    private static string Diagnostics(SupportSections sections, SupportContext context)
    {
        var text = new StringBuilder();
        if (sections.Environment)
        {
            text.Append("[Environment]\n");
            text.Append("OS version: ").Append(Environment.OSVersion.Version.ToString()).Append('\n');
            text.Append("Display scale: ").Append(context.DisplayScalePercent is { } scale && scale is > 0 and < 1000
                ? scale.ToString("0", CultureInfo.InvariantCulture) + "%" : "not available").Append("\n\n");
        }
        if (sections.RecentErrors)
        {
            text.Append("[Recent Graph errors, oldest first]\n");
            if (context.Errors.Count == 0) text.Append("None recorded since the application started.\n");
            foreach (var e in context.Errors)
                text.Append(e.OccurredAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(e.Method).Append(' ').Append(e.Route).Append(" -> ").Append(e.Status.ToString(CultureInfo.InvariantCulture))
                    .Append(e.ErrorCode is null ? "" : " " + e.ErrorCode)
                    .Append(" request-id=").Append(e.RequestId ?? "none")
                    .Append(" client-request-id=").Append(e.ClientRequestId ?? "none").Append('\n');
            text.Append('\n');
        }
        if (sections.CollectionStatus)
        {
            text.Append("[Last capture: collection read status]\n");
            var collections = context.Snapshot?.Collections;
            if (collections is null || collections.Count == 0) text.Append("No capture is loaded.\n");
            else
            {
                foreach (var group in collections.Values.GroupBy(c => c.Status).OrderBy(g => g.Key, StringComparer.Ordinal))
                    text.Append(StatusLabel(group.Key)).Append(": ").Append(group.Count().ToString(CultureInfo.InvariantCulture)).Append('\n');
                text.Append("Detail incomplete: ").Append(collections.Values.Count(c => c.DetailIncomplete).ToString(CultureInfo.InvariantCulture)).Append('\n');
                // Collection keys are fixed catalogue identifiers, not tenant data; anything else is withheld.
                var unread = collections.Where(c => !c.Value.Usable).Select(c => CollectionKey.IsMatch(c.Key) ? c.Key : "(withheld)").Order(StringComparer.Ordinal).ToList();
                text.Append("Not fully read: ").Append(unread.Count == 0 ? "none" : string.Join(", ", unread)).Append('\n');
            }
            text.Append('\n');
        }
        if (sections.Timeouts)
        {
            var s = context.Settings ?? new ToolkitSettings();
            text.Append("[Timeouts and limits]\n")
                .Append("Sign-in timeout (minutes): ").Append(s.SignInTimeoutMinutes.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append("Graph read timeout (seconds): ").Append(s.GraphReadTimeoutSeconds.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append("Graph write timeout (seconds): ").Append(s.GraphWriteTimeoutSeconds.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append("Max Retry-After honoured (seconds): ").Append(s.MaxRetryAfterSeconds.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append("Snapshot max age (minutes): ").Append(s.SnapshotMaxAgeMinutes.ToString(CultureInfo.InvariantCulture)).Append('\n')
                .Append("Plan max age (minutes): ").Append(s.PlanMaxAgeMinutes.ToString(CultureInfo.InvariantCulture)).Append("\n\n");
        }
        return text.ToString().TrimEnd('\n') + "\n";
    }

    private static string StatusLabel(string status) =>
        status is CaptureStatus.Collected or CaptureStatus.Error or CaptureStatus.NotAttempted ? status : "Other";

    private static void Write(ZipArchive archive, string name, string value)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(value);
    }
}
