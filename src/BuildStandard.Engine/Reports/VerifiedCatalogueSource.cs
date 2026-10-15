using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Standards;

namespace BuildStandard.Engine.Reports;

/// <summary>Shared existing manifest, loaded-model and exact-byte admission; no tenant context.</summary>
internal static class VerifiedCatalogueSource
{
    internal static byte[] Read(ToolkitPaths paths, StandardCatalogue standard)
    {
        // Capture caller identity before serialising its model; never reread mutable identity after admission.
        var fileName = standard.SourceFileName;
        var expectedDigest = standard.IntegrityDigest;
        if (string.IsNullOrWhiteSpace(expectedDigest))
            throw new IntegrityException("A catalogue document export requires a manifest-verified catalogue. Load a verified release first.");
        var expectedModel = CanonicalJson.Sha256Value(standard);
        var reloaded = new StandardsLoader(paths, NullLog.Instance).Load(fileName);
        var manifestDigest = reloaded.IntegrityDigest;
        if (!string.Equals(manifestDigest, expectedDigest, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(CanonicalJson.Sha256Value(reloaded), expectedModel, StringComparison.Ordinal))
            throw new IntegrityException("The catalogue changed after loading. Reload it before exporting its definition.");
        var bytes = File.ReadAllBytes(Path.Combine(paths.StandardsDirectory, fileName));
        if (!string.Equals(CanonicalJson.Sha256Hex(bytes), manifestDigest, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(CanonicalJson.Sha256Value(StandardsLoader.Parse(Decode(bytes), fileName)), expectedModel, StringComparison.Ordinal))
            throw new IntegrityException("The catalogue changed during export. No definition was exported.");
        return bytes;
    }
    // Match File.ReadAllText's loader semantics, but decode this captured buffer without reopening its path.
    internal static string Decode(byte[] bytes)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(input, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
