using System.Text;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Exchange;

namespace BDIT.TenantToolkit.Engine.Assessment;

/// <summary>Shared read-only record loading for desktop and headless assessment; creates no execution authority.</summary>
public static class AssessmentContext
{
    public static AssessmentResult Assess(AssessmentEngine engine, EvidenceStore store, TenantSnapshot snapshot,
        StandardCatalogue standard, TenantProfile profile, string actor, ExchangeCapture? supplementalExchange = null)
    {
        var mappings = store.LoadMappings(profile.TenantId);
        var deviations = store.LoadDeviations(profile.TenantId);
        return engine.Assess(snapshot, standard, profile, mappings, deviations, actor, supplementalExchange);
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
            var snapshot = ToolkitJson.Deserialize<TenantSnapshot>(json);
            if (!string.Equals(snapshot.TenantId, expectedTenant, StringComparison.OrdinalIgnoreCase))
                throw new TenantMismatchException("Supplemental Exchange snapshot belongs to a different tenant.");
            if (snapshot.IntegrityDigest.Length > 0 && !EvidenceIntegrity.Verify(snapshot, snapshot.IntegrityDigest))
                throw new IntegrityException("Supplemental Exchange snapshot failed its recorded integrity check.");
            var capture = snapshot.ExchangeCapture ?? throw new ConfigurationException("Supplemental snapshot has no Exchange capture.");
            if (!string.Equals(snapshot.CapturedAt, capture.CapturedAt, StringComparison.Ordinal))
                throw new ConfigurationException("Supplemental snapshot and capture times do not match.");
            if (Encoding.UTF8.GetByteCount(ToolkitJson.Serialize(capture)) > ExchangeCaptureSchema.MaximumBytes)
                throw new ConfigurationException("Exchange capture exceeds the 2 MiB limit.");
            ExchangeCaptureSchema.Validate(capture, expectedTenant, now);
            return capture;
        }
        return ExchangeCaptureSchema.Parse(json, expectedTenant, now);
    }
}
