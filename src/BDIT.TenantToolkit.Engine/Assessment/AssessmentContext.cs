using System.Text;
using System.Text.Json;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Exchange;
using BDIT.TenantToolkit.Engine.Standards;

namespace BDIT.TenantToolkit.Engine.Assessment;

/// <summary>Shared read-only record loading for desktop and headless assessment; creates no execution authority.</summary>
public static class AssessmentContext
{
    public static AssessmentResult Assess(AssessmentEngine engine, EvidenceStore store, TenantSnapshot snapshot,
        StandardCatalogue standard, TenantProfile profile, string actor, ExchangeCapture? supplementalExchange = null, DateTimeOffset? evidenceTime = null)
    {
        var mappings = store.LoadMappings(profile.TenantId);
        var deviations = store.LoadDeviations(profile.TenantId);
        var result = engine.Assess(snapshot, standard, profile, mappings, deviations, actor, supplementalExchange, evidenceTime);
        LineageReview.Annotate(result, LineageReview.Review(mappings, standard, LoadLineage(store.Paths.StandardsDirectory, standard, result)));
        return result;
    }

    /// <summary>
    /// The shipped lineage into the assessed release, or null. Lineage only explains; a lineage file that fails its
    /// integrity check is reported and ignored rather than stopping the assessment, so every earlier-release record is
    /// then flagged for review.
    /// </summary>
    private static ReleaseLineage? LoadLineage(string standardsDirectory, StandardCatalogue standard, AssessmentResult result)
    {
        try
        {
            if (!File.Exists(Path.Combine(standardsDirectory, StandardsManifest.FileName))) return null;
            var lineage = ReleaseLineage.Load(standardsDirectory, StandardsManifest.Load(standardsDirectory), standard.Release);
            // A catalogue imported under a published release name is not the published bytes the lineage describes.
            return lineage is not null && string.Equals(lineage.Target.Sha256, standard.IntegrityDigest, StringComparison.OrdinalIgnoreCase) ? lineage : null;
        }
        catch (IntegrityException ex)
        {
            result.Limitations.Add("Release lineage could not be verified and was not used: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Reads a primary Graph snapshot for offline reporting. A snapshot that no longer matches its recorded integrity
    /// digest is refused, as supplemental Exchange evidence is: a report must not present modified evidence as a
    /// normal assessment. A snapshot with no digest (older exports) is assessed and labelled as unverified.
    /// </summary>
    public static TenantSnapshot ReadPrimary(string file)
    {
        var snapshot = ToolkitJson.Deserialize<TenantSnapshot>(File.ReadAllText(file))
            ?? throw new ConfigurationException("The snapshot file did not contain a capture.");
        if (AssessmentEngine.IntegrityOf(snapshot) == SnapshotIntegrityState.Modified)
            throw new IntegrityException("The snapshot no longer matches its recorded integrity digest; it was modified after capture. Capture fresh evidence rather than reporting on it.");
        return snapshot;
    }

    /// <summary>Accepts the existing raw capture or exported snapshot format; no new persisted schema is introduced.</summary>
    public static ExchangeCapture ReadSupplement(string file, string expectedTenant, DateTimeOffset now)
    {
        const int maximumFileBytes = ExchangeCaptureSchema.MaximumBytes * 2;
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumFileBytes) throw new ConfigurationException("Supplemental Exchange evidence exceeds the 4 MiB file limit.");
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = stream.Read(bytes, 0, bytes.Length)) > 0)
        {
            if (buffer.Length + count > maximumFileBytes) throw new ConfigurationException("Supplemental Exchange evidence exceeds the 4 MiB file limit.");
            buffer.Write(bytes, 0, count);
        }
        var json = new UTF8Encoding(false, true).GetString(buffer.ToArray()).TrimStart('\uFEFF');
        var node = ToolkitJson.ParseObject(json);
        if (node.ContainsKey("exchangeCapture"))
        {
            using var document = JsonDocument.Parse(json);
            ExchangeCaptureSchema.RejectDuplicates(document.RootElement, StringComparer.OrdinalIgnoreCase);
            var snapshot = ToolkitJson.Deserialize<TenantSnapshot>(json);
            if (snapshot.IntegrityDigest is null || snapshot.ExchangeCapture is null)
                throw new ConfigurationException("Supplemental snapshot has no supported Exchange capture or integrity metadata.");
            if (!string.Equals(snapshot.TenantId, expectedTenant, StringComparison.OrdinalIgnoreCase))
                throw new TenantMismatchException("Supplemental Exchange snapshot belongs to a different tenant.");
            if (snapshot.IntegrityDigest.Length > 0 && !EvidenceIntegrity.Verify(snapshot, snapshot.IntegrityDigest))
                throw new IntegrityException("Supplemental Exchange snapshot failed its recorded integrity check.");
            var embedded = document.RootElement.GetProperty("exchangeCapture").GetRawText();
            var capture = snapshot.IntegrityDigest.Length > 0
                ? ExchangeCaptureSchema.ParseStored(embedded, expectedTenant, now)
                : ExchangeCaptureSchema.Parse(embedded, expectedTenant, now);
            if (!string.Equals(snapshot.CapturedAt, capture.CapturedAt, StringComparison.Ordinal))
                throw new ConfigurationException("Supplemental snapshot and capture times do not match.");
            return capture;
        }
        return ExchangeCaptureSchema.Parse(json, expectedTenant, now);
    }
}
