using System.Text;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Checks;

/// <summary>Immutable separate records. This store has no ordinary snapshot, plan, run or tenant-write entry point.</summary>
public sealed class ScopedCheckStore(ToolkitPaths paths)
{
    public string Save(ScopedCheckEvidence evidence, StandardCatalogue catalogue, TenantProfile profile)
    {
        ScopedCheckSchema.Validate(evidence, catalogue, profile);
        var json = ToolkitJson.Serialize(evidence);
        // Exercise the actual strict reader before committing bytes, including the size limit.
        ScopedCheckSchema.Read(json, catalogue, profile);
        var folder = Path.Combine(paths.TenantDirectory(evidence.TenantId), "scoped-checks");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, Guid.Parse(evidence.Id).ToString("D") + ".json");
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                writer.Write(json);
                writer.Flush(); stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, file, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return file;
    }

    public ScopedCheckEvidence Read(string file, StandardCatalogue catalogue, TenantProfile profile)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > ScopedCheckSchema.MaximumBytes)
            throw new BDIT.TenantToolkit.Core.ConfigurationException("The scoped evidence exceeds the reader limit.");
        using var reader = new BinaryReader(stream);
        var bytes = reader.ReadBytes(ScopedCheckSchema.MaximumBytes + 1);
        if (bytes.Length > ScopedCheckSchema.MaximumBytes)
            throw new BDIT.TenantToolkit.Core.ConfigurationException("The scoped evidence exceeds the reader limit.");
        return ScopedCheckSchema.Read(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'), catalogue, profile);
    }
}
