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
