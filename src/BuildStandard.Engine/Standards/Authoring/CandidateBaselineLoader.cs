using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
namespace BuildStandard.Engine.Standards.Authoring;

/// <summary>Reloads the exact manifest-verified base; never selects a newer release or trusts draft metadata alone.</summary>
public sealed class CandidateBaselineLoader(ToolkitPaths paths, IToolkitLog log)
{
    public StandardCandidateDraft Create(string fileName, string title)
    {
        var catalogue = ReadExact(fileName);
        var draft = new StandardCandidateDraft
        {
            Kind = "standard-candidate-draft", SchemaVersion = 1,
            Baseline = new(catalogue.Release, catalogue.IntegrityDigest, catalogue.SchemaVersion), Title = title,
            Changes = [], Imports = [], Unresolved = [], AssignmentObservations = [], ContentDigest = ""
        };
        CandidateDraftCodec.Seal(draft);
        return draft;
    }
    public StandardCatalogue Load(StandardCandidateDraft draft)
    {
        // Snapshot all inputs before inspecting files; the editor cannot change the identity part-way through.
        var snapshot = CandidateDraftCodec.Read(CandidateDraftCodec.Serialize(draft));
        var manifest = StandardsManifest.Load(paths.StandardsDirectory);
        var name = manifest.Files.Where(p => string.Equals(p.Value, snapshot.Baseline.FileSha256, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Key).Order(StringComparer.Ordinal).FirstOrDefault()
            ?? throw new IntegrityException("The exact candidate baseline is unavailable. Restore its original verified catalogue; no automatic rebase is permitted.");
        try
        {
            var catalogue = ReadExact(name);
            if (catalogue.IntegrityDigest != snapshot.Baseline.FileSha256 || catalogue.Release != snapshot.Baseline.Release
                || catalogue.SchemaVersion != snapshot.Baseline.CatalogueSchemaVersion)
                throw new IntegrityException("Candidate baseline release, schema or exact bytes do not match.");
            return catalogue;
        }
        catch (IOException ex) { throw new IntegrityException("The candidate baseline could not be read: " + ex.Message); }
    }
    private StandardCatalogue ReadExact(string name)
    {
        var verified = new StandardsLoader(paths, log).Load(name);
        // The ordinary loader verifies before parsing. Re-read and hash one bounded byte image here so a
        // concurrent replacement cannot give the candidate the old digest and new contents.
        using var input = new FileStream(Path.Combine(paths.StandardsDirectory, name), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > CandidateDraftCodec.MaximumBytes) throw new ConfigurationException("Candidate baseline exceeds the 8 MiB authoring limit.");
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            if (output.Length + count > CandidateDraftCodec.MaximumBytes) throw new ConfigurationException("Candidate baseline exceeds the 8 MiB authoring limit.");
            output.Write(buffer, 0, count);
        }
        var bytes = output.ToArray();
        var digest = CanonicalJson.Sha256Hex(bytes);
        if (digest != verified.IntegrityDigest) throw new IntegrityException("Candidate baseline changed while being loaded; retry against its original verified bytes.");
        var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        var catalogue = StandardsLoader.Parse(text, name);
        catalogue.IntegrityDigest = digest;
        return catalogue;
    }
}
