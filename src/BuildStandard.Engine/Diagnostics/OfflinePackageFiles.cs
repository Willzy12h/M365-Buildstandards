using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Standards;

namespace BuildStandard.Engine.Diagnostics;

/// <summary>Resource bounds for a local, unsigned package inventory; these are not tenant collection limits.</summary>
public sealed record OfflineDiagnosticLimits
{
    public int ManifestBytes { get; init; } = 1024 * 1024;
    public int ManifestEntries { get; init; } = 10_000;
    public long FileBytes { get; init; } = 256L * 1024 * 1024;
    public long TotalHashBytes { get; init; } = 2L * 1024 * 1024 * 1024;
    public int StructuredFileBytes { get; init; } = 4 * 1024 * 1024;
    public int MetadataBytes { get; init; } = 64 * 1024;
    public int InventoryEntries { get; init; } = 20_000;

    internal void Validate()
    {
        if (ManifestBytes <= 0 || ManifestEntries <= 0 || FileBytes <= 0 || TotalHashBytes <= 0
            || StructuredFileBytes <= 0 || MetadataBytes <= 0 || InventoryEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(OfflineDiagnosticLimits));
    }
}

// Only file admission, bounded reads and owned probes have fault seams. Production never substitutes these operations.
internal sealed class OfflineDiagnosticIo
{
    public Func<string, FileAttributes> Attributes { get; init; } = File.GetAttributes;
    public Func<string, Stream> Read { get; init; } = path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    public Func<string, Stream> CreateProbe { get; init; } = path => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    public Action<string> DeleteProbe { get; init; } = File.Delete;
    public Func<string, IEnumerable<string>> Entries { get; init; } = Directory.EnumerateFileSystemEntries;
    public Func<string> ProbeName { get; init; } = () => ".buildstandard-doctor-" + Guid.NewGuid().ToString("N") + ".tmp";
}

internal sealed class UnsafeOfflinePathException : Exception;
internal sealed class OfflinePackageScopeException : Exception;
internal sealed class OfflineBoundException : Exception;
internal sealed class OfflineDocumentException : Exception;

/// <summary>Never admits private working folders as package contents, even if an adjacent inventory lists them.</summary>
internal sealed partial class OfflinePackageFiles(string root, OfflineDiagnosticIo io, OfflineDiagnosticLimits limits)
{
    internal static readonly string[] RootFiles = ["VERSION.json", "DEPENDENCIES.json", "README.md", "CHANGELOG.md",
        "Start.cmd", "Start-Diagnostics.cmd", "Open-Evidence.cmd", "Open-Reports.cmd", "buildstandard.cmd", "bdit.cmd"];
    private static readonly HashSet<string> PrivateSegments = new(StringComparer.OrdinalIgnoreCase)
        { "data", "logs", "reports", "transfers", "profiles", "evidence", "cache", "caches", "credentials", "secrets", "tokens", "token", "connections", ".git" };
    private long _hashedBytes;

    internal static bool SafeRelative(string path)
    {
        if (path.Length is 0 or > 240 || !RelativePattern().IsMatch(path)) return false;
        return path.Split('/').All(part => part.Length is > 0 and <= 128 && part is not ("." or "..")
            && part == part.Trim() && !part.EndsWith('.') && part.Split('.')[0] == part.Split('.')[0].Trim()
            && !DeviceNamePattern().IsMatch(part));
    }

    internal static bool PackageContent(string path)
    {
        if (!SafeRelative(path) || PrivatePath(path)) return false;
        var parts = path.Split('/');
        if (parts.Length == 1) return RootFiles.Contains(path, StringComparer.OrdinalIgnoreCase);
        return parts[0].ToLowerInvariant() switch
        {
            "app" => true,
            "standards" => parts.Length == 2 && CatalogueName(parts[1])
                || parts.Length == 3 && parts[1].Equals(ReleaseLineage.Folder, StringComparison.OrdinalIgnoreCase)
                && (parts[2].Equals(ReleaseLineage.ManifestFile, StringComparison.OrdinalIgnoreCase)
                    || parts[2].StartsWith("lineage-", StringComparison.OrdinalIgnoreCase) && CatalogueName(parts[2]["lineage-".Length..])),
            "config" => parts.Length == 2 && parts[1].Equals("toolkit.settings.json", StringComparison.OrdinalIgnoreCase),
            "docs" => parts.Length == 2 && parts[1].EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || path.Equals("docs/examples/external-check-results.example.json", StringComparison.OrdinalIgnoreCase),
            "licenses" => true,
            _ => false
        };
    }

    internal static bool PrivatePath(string path) => path.Split('/').Any(part => PrivateSegments.Contains(part.Split('.')[0]) || PrivateSegments.Contains(part));

    internal static bool CatalogueName(string name) => CataloguePattern().IsMatch(name) && SafeRelative(name);

    internal string Admit(string relative, bool directory = false)
    {
        if (!SafeRelative(relative)) throw new UnsafeOfflinePathException();
        var current = root;
        CheckDirectory(current);
        var parts = relative.Split('/');
        for (var i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);
            var attributes = io.Attributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new UnsafeOfflinePathException();
            var expectDirectory = directory || i < parts.Length - 1;
            if (((attributes & FileAttributes.Directory) != 0) != expectDirectory) throw new UnsafeOfflinePathException();
        }
        return current;
    }

    internal void CheckDirectory(string path)
    {
        // Recheck root ancestors as well as descendants before each read, enumeration and probe.
        foreach (var ancestor in Ancestors(path))
        {
            var attributes = io.Attributes(ancestor);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                throw new UnsafeOfflinePathException();
        }
    }

    internal static IEnumerable<string> Ancestors(string path)
    {
        var names = new Stack<string>();
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent) names.Push(directory.FullName);
        return names;
    }

    internal byte[] ReadBytes(string relative, int maximum, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = Admit(relative);
        using var stream = io.Read(path);
        Admit(relative); // No content is read if admission changed while opening.
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, (long)maximum - bytes.Length + 1));
            if (read == 0) break;
            if (bytes.Length + read > maximum) throw new OfflineBoundException();
            bytes.Write(buffer, 0, read);
        }
        return bytes.ToArray();
    }

    internal string ReadText(string relative, int maximum, CancellationToken ct)
    {
        var bytes = ReadBytes(relative, maximum, ct);
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { throw new OfflineDocumentException(); }
    }

    internal string Hash(string relative, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = Admit(relative);
        using var stream = io.Read(path);
        Admit(relative);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long fileBytes = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            // Read one byte beyond the remaining bound at most; never silently certify a truncated file.
            var remaining = Math.Min(limits.FileBytes - fileBytes, limits.TotalHashBytes - _hashedBytes);
            var read = stream.Read(buffer, 0, remaining >= buffer.Length ? buffer.Length : (int)remaining + 1);
            if (read == 0) break;
            if (read > remaining) throw new OfflineBoundException();
            fileBytes += read;
            _hashedBytes += read;
            hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal string Digest(byte[] bytes)
    {
        if (bytes.LongLength > limits.FileBytes || bytes.LongLength > limits.TotalHashBytes - _hashedBytes)
            throw new OfflineBoundException();
        _hashedBytes += bytes.LongLength;
        return CanonicalJson.Sha256Hex(bytes);
    }

    [GeneratedRegex("^[A-Za-z0-9_./+ -]+$")]
    private static partial Regex RelativePattern();
    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])($|\\.)", RegexOptions.IgnoreCase)]
    private static partial Regex DeviceNamePattern();
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,60}\\.json$")]
    private static partial Regex CataloguePattern();
}
