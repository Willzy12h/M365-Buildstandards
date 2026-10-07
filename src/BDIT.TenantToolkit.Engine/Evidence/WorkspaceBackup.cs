using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;

namespace BDIT.TenantToolkit.Engine.Evidence;

/// <summary>Transfers existing evidence bytes, without migrating, deserialising or reauthorising records.</summary>
public sealed class WorkspaceBackup(ToolkitPaths paths, long maximumBytes = 1024L * 1024 * 1024)
{
    private const int MaxFiles = 20000;
    private const long MaxFileBytes = 32L * 1024 * 1024;
    private const long MaxTotalBytes = 1024L * 1024 * 1024;
    private const int MaxChecksumListBytes = 4 * 1024 * 1024;
    private readonly long _maximumBytes = maximumBytes > 0 && maximumBytes <= MaxTotalBytes ? maximumBytes : throw new ArgumentOutOfRangeException(nameof(maximumBytes), "Transfer limit must be positive and at most 1 GiB.");
    public const string Guide = "Sensitive local evidence backup. Close other copies of the tool and disconnect before backing up. Includes every JSON/JSONL/NDJSON record under data/, including historical and unresolved writes, profiles, imported catalogues and application setup evidence. Authentication caches and policy-write locks are excluded. Unknown file types and links cause refusal rather than silent loss. Logs, reports, config and binaries are excluded; retain the original approved package separately. Restore checks the archive against the trusted SHA-256 received through the approved handoff channel, verifies every file checksum and extracts into a new separate folder only. Adoption extracts the archive, checked against the same trusted SHA-256, into an empty evidence folder and re-verifies every file in place. Neither overwrites existing evidence or makes old captures, plans or approvals live. Checksums detect modification against a trusted reference; they are not a publisher signature. Store and share this archive under the organisation's client-evidence policy.";

    public string Create()
    {
        var source = Sources();
        // Sensitive evidence stays out of reports/, which holds documents engineers routinely share.
        Directory.CreateDirectory(paths.TransfersDirectory);
        var file = Path.Combine(paths.TransfersDirectory, "workspace-backup-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            using (var archive = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                WriteText(archive, "README.txt", Guide + "\n", hashes);
                long total = Encoding.UTF8.GetByteCount(Guide + "\n");
                foreach (var item in source)
                {
                    CheckLink(item);
                    var name = "data/" + Path.GetRelativePath(paths.DataDirectory, item).Replace('\\', '/');
                    ValidatePath(name);
                    using var input = new FileStream(item, FileMode.Open, FileAccess.Read, FileShare.Read);
                    total = checked(total + input.Length);
                    if (input.Length > MaxFileBytes || total > _maximumBytes) throw new ConfigurationException("Backup exceeds the bounded evidence transfer size. Keep the source intact and contact the workspace custodian.");
                    using var output = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
                    hashes[name] = CopyHash(input, output, MaxFileBytes);
                }
                var manifest = string.Join('\n', hashes.Select(h => h.Value + "  " + h.Key)) + "\n";
                var manifestBytes = Encoding.UTF8.GetByteCount(manifest);
                if (manifestBytes > MaxChecksumListBytes || total + manifestBytes > _maximumBytes) throw new ConfigurationException("Backup including its metadata exceeds the bounded transfer size.");
                WriteText(archive, "SHA256SUMS.txt", manifest, null);
            }
            if (!source.SequenceEqual(Sources(), StringComparer.Ordinal)) throw new IntegrityException("Evidence files changed during backup. Close other tool copies and try again.");
            foreach (var item in source)
            {
                CheckLink(item);
                using var input = new FileStream(item, FileMode.Open, FileAccess.Read, FileShare.Read);
                var name = "data/" + Path.GetRelativePath(paths.DataDirectory, item).Replace('\\', '/');
                if (CopyHash(input, Stream.Null, MaxFileBytes) != hashes[name]) throw new IntegrityException("Evidence changed during backup. No backup was retained.");
            }
            File.WriteAllText(file + ".sha256", ArchiveDigest(file) + "  " + Path.GetFileName(file) + "\n", new UTF8Encoding(false));
            return file;
        }
        catch { File.Delete(file); File.Delete(file + ".sha256"); throw; }
    }

    /// <summary>SHA-256 of a backup archive, for comparison with the digest received through the approved handoff channel.</summary>
    public static string ArchiveDigest(string archiveFile)
    {
        using var input = File.OpenRead(archiveFile);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }

    /// <summary>The single format rule for a trusted archive digest: 64 hexadecimal characters, surrounding spaces ignored.</summary>
    public static bool IsValidDigest(string? digest) => digest?.Trim() is { Length: 64 } value && value.All(Uri.IsHexDigit);

    /// <summary>
    /// The SHA-256 in what an engineer pastes or loads: a bare digest or a checksum line such as "&lt;digest&gt;  backup.zip".
    /// Text holding no single digest is returned trimmed, so validation still refuses it.
    /// </summary>
    public static string NormaliseDigest(string? text)
    {
        var found = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim('*', '(', ')', '=', ',', ';')).Where(IsValidDigest)
            .Select(token => token.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
        return found.Count == 1 ? found[0] : (text ?? "").Trim();
    }

    /// <param name="trustedArchiveSha256">
    /// The archive digest received separately from the archive. When supplied, a mismatch refuses the restore before
    /// anything is staged: the archive's own checksum list cannot detect deliberate tampering, because whoever changed
    /// the archive could regenerate it (CLA-20261006-05). Null restores against internal checksums only; the interface
    /// requires the engineer to acknowledge that explicitly.
    /// </param>
    public string RestoreSeparate(string archiveFile, string destination, string? trustedArchiveSha256 = null)
    {
        var expected = TrustedDigest(trustedArchiveSha256);
        destination = Path.GetFullPath(destination);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new ConfigurationException("Restore requires a new separate folder. Existing folders are never overwritten.");
        var parent = Path.GetDirectoryName(destination) ?? throw new ConfigurationException("Restore needs a parent folder.");
        // The workspace's own transfers folder is created on first use; any other parent must already exist.
        if (string.Equals(parent, paths.TransfersDirectory, StringComparison.OrdinalIgnoreCase)) { CheckParents(paths.Root); Directory.CreateDirectory(parent); }
        CheckParents(parent);
        if (!Directory.Exists(parent)) throw new ConfigurationException("Create the restore parent folder first.");
        if (Inside(destination, paths.DataDirectory)) throw new ConfigurationException("Do not restore inside the active data folder.");
        var staging = Path.Combine(parent, ".restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            var hashes = ExtractVerified(archiveFile, expected, staging);
            File.WriteAllText(Path.Combine(staging, "SHA256SUMS.txt"), string.Join('\n', hashes.OrderBy(h => h.Key, StringComparer.Ordinal).Select(h => h.Value + "  " + h.Key)) + "\n", new UTF8Encoding(false));
            CheckParents(parent);
            Directory.Move(staging, destination);
            return destination;
        }
        catch { if (Directory.Exists(staging)) Directory.Delete(staging, true); throw; }
    }

    private static string? TrustedDigest(string? digest) => digest is null ? null
        : IsValidDigest(digest) ? digest.Trim() : throw new ConfigurationException("The trusted archive SHA-256 must be 64 hexadecimal characters.");

    /// <summary>
    /// Hashes and extracts one read-locked archive stream into a new staging folder and returns the checksum list it
    /// verified every extracted file against. The bytes checked against the trusted digest are exactly the bytes
    /// extracted, and callers act on this in-memory list, never on a copy of it read back from disk.
    /// </summary>
    private Dictionary<string, string> ExtractVerified(string archiveFile, string? expected, string staging)
    {
        using var stream = new FileStream(archiveFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > _maximumBytes + MaxFileBytes) throw new ConfigurationException("Backup archive exceeds the bounded transfer size.");
        if (expected is not null)
        {
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), expected, StringComparison.OrdinalIgnoreCase))
                throw new IntegrityException("The backup archive does not match the trusted SHA-256 received for it. Nothing was restored. Obtain the archive again through the approved handoff channel.");
            stream.Position = 0;
        }
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MaxFiles + 2) throw new ConfigurationException("Backup contains too many entries.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            ValidatePath(entry.FullName);
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType is not (0 or 0x8000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new IntegrityException("Backup links or non-file entries are not supported.");
            if (!entries.TryAdd(entry.FullName, entry)) throw new IntegrityException("Backup contains duplicate paths.");
            total = checked(total + entry.Length);
            if (entry.Length > MaxFileBytes || total > _maximumBytes) throw new ConfigurationException("Backup exceeds the bounded restore size.");
        }
        if (!entries.TryGetValue("README.txt", out _) || !entries.TryGetValue("SHA256SUMS.txt", out var manifest)) throw new IntegrityException("Backup is missing its documentation or checksums.");
        var hashes = ReadHashes(manifest);
        if (hashes.Count != entries.Count - 1 || hashes.Keys.Any(k => !entries.ContainsKey(k))) throw new IntegrityException("Backup checksum list does not cover the exact entries.");
        Directory.CreateDirectory(staging);
        long actualTotal = manifest.Length;
        foreach (var (name, entry) in entries)
        {
            if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) continue;
            var file = Path.Combine(staging, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using var input = entry.Open();
            using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            if (!string.Equals(CopyHash(input, output, Math.Min(MaxFileBytes, _maximumBytes - actualTotal)), hashes[name], StringComparison.OrdinalIgnoreCase) || output.Length != entry.Length) throw new IntegrityException("Backup entry failed its size or SHA-256 check. Source evidence is unchanged.");
            actualTotal = checked(actualTotal + output.Length);
        }
        Directory.CreateDirectory(Path.Combine(staging, "data"));
        return hashes;
    }

    /// <summary>
    /// Re-checks a restored folder against the checksum list written at restore: every listed file present with its
    /// recorded SHA-256, and nothing unlisted under data/. Read-only. Returns the number of evidence files verified.
    /// </summary>
    public static int VerifyRestored(string restoredFolder)
    {
        restoredFolder = Path.GetFullPath(restoredFolder);
        CheckParents(restoredFolder);
        var listFile = Path.Combine(restoredFolder, "SHA256SUMS.txt");
        if (!File.Exists(listFile)) throw new IntegrityException("The restored folder has no SHA256SUMS.txt. Restore the backup again with Verify and restore.");
        if (new FileInfo(listFile).Length > MaxChecksumListBytes) throw new IntegrityException("Restored checksum list is too large.");
        var hashes = ParseHashes(File.ReadAllBytes(listFile));
        var data = Path.Combine(restoredFolder, "data");
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(data)) CollectFiles(data, data, present);
        var listedData = hashes.Keys.Where(k => k.StartsWith("data/", StringComparison.Ordinal)).ToList();
        if (present.Count != listedData.Count || present.Any(f => !hashes.ContainsKey(f)))
            throw new IntegrityException("The restored data folder contains files that are not in its checksum list, or is missing listed files. Restore the backup again into a new folder.");
        foreach (var (name, digest) in hashes)
        {
            var file = Path.Combine(restoredFolder, name.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file)) throw new IntegrityException($"Restored file is missing: {name}.");
            CheckLink(file);
            using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!string.Equals(CopyHash(input, Stream.Null, MaxFileBytes), digest, StringComparison.OrdinalIgnoreCase))
                throw new IntegrityException($"Restored file failed its SHA-256 check: {name}.");
        }
        return listedData.Count;
    }

    /// <summary>
    /// Adopts a backup archive into this workspace's evidence folder, which must be empty, re-verifying every file in
    /// place (CLA-20261006-04). It replaces the manual folder copy that could silently drop run journals and with them
    /// the blockers on unresolved writes. Adoption extracts straight from the archive stream checked against the
    /// trusted digest and compares against that verified list in memory: a restored folder only agrees with the
    /// checksum list stored beside it, which whoever changed the folder could rewrite. No sign-in, acknowledgement,
    /// plan approval or session is restored; historical captures stay historical and the next plan needs fresh evidence.
    /// </summary>
    /// <param name="trustedArchiveSha256">As for <see cref="RestoreSeparate"/>; null only with the engineer's explicit acknowledgement.</param>
    public string AdoptFromArchive(string archiveFile, string? trustedArchiveSha256)
    {
        var expected = TrustedDigest(trustedArchiveSha256);
        CheckParents(paths.Root);
        AssertEmptyEvidenceFolder();
        var staging = Path.Combine(paths.Root, ".adopt-" + Guid.NewGuid().ToString("N"));
        List<KeyValuePair<string, string>> dataEntries;
        try
        {
            dataEntries = ExtractVerified(archiveFile, expected, staging).Where(h => h.Key.StartsWith("data/", StringComparison.Ordinal)).ToList();
            // Only empty folders remain in the evidence folder (checked above). They are removed one by one without
            // recursion, so a file that appears meanwhile makes the removal fail rather than being deleted.
            if (Directory.Exists(paths.DataDirectory))
            {
                try { DeleteEmptyTree(paths.DataDirectory); }
                catch (IOException) { throw new ConfigurationException("Evidence appeared in this workspace during adoption. Nothing was adopted."); }
            }
            Directory.Move(Path.Combine(staging, "data"), paths.DataDirectory);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }

        // Verify in place against the list verified from the archive. On failure the adopted files are moved, intact,
        // out of the live evidence folder: nothing is deleted, and the workspace cannot use or overwrite them.
        foreach (var (name, digest) in dataEntries)
        {
            bool intact;
            using (var input = new FileStream(Path.Combine(paths.DataDirectory, name["data/".Length..].Replace('/', Path.DirectorySeparatorChar)), FileMode.Open, FileAccess.Read, FileShare.Read))
                intact = string.Equals(CopyHash(input, Stream.Null, MaxFileBytes), digest, StringComparison.OrdinalIgnoreCase);
            if (intact) continue;
            var quarantine = Path.Combine(paths.TransfersDirectory, "adoption-failed-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(paths.TransfersDirectory);
            Directory.Move(paths.DataDirectory, quarantine);
            Directory.CreateDirectory(paths.DataDirectory);
            throw new IntegrityException($"Adopted file failed its in-place SHA-256 check: {name}. The adopted evidence was moved, unchanged, to {quarantine} and this workspace's evidence folder is empty again. Contact the evidence custodian.");
        }
        return $"Adopted {dataEntries.Count} verified evidence file(s) into {paths.DataDirectory}. Sign in and capture fresh evidence before any new plan; no approval or session was carried over. Keep the backup archive for the custodian's records.";
    }

    private void AssertEmptyEvidenceFolder()
    {
        if (!Directory.Exists(paths.DataDirectory)) return;
        CheckLink(paths.DataDirectory);
        if (Directory.EnumerateFiles(paths.DataDirectory, "*", SearchOption.AllDirectories).Any())
            throw new ConfigurationException("Adoption needs an empty evidence folder, so no existing record can be overwritten or mixed with another workspace. Use a newly extracted package.");
    }

    private static void DeleteEmptyTree(string directory)
    {
        CheckLink(directory);
        foreach (var child in Directory.EnumerateDirectories(directory)) DeleteEmptyTree(child);
        Directory.Delete(directory, recursive: false);
    }

    private static void CollectFiles(string directory, string dataRoot, ISet<string> names)
    {
        CheckLink(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            CheckLink(entry);
            if (Directory.Exists(entry)) { CollectFiles(entry, dataRoot, names); continue; }
            var name = "data/" + Path.GetRelativePath(dataRoot, entry).Replace('\\', '/');
            ValidatePath(name);
            names.Add(name);
        }
    }

    /// <summary>
    /// Refuses a backup while another process holds a tenant's write lease (CLA-20261006-15). A backup taken mid-write
    /// would carry intent without an outcome: still blocked safely after restore, but avoidable reconciliation.
    /// </summary>
    private static void AssertNoWriteInProgress(string lockFile)
    {
        // Read access is enough: any handle opened with FileShare.None by a lease holder refuses this open.
        try { using var probe = new FileStream(lockFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
        catch (IOException) { throw new ConfigurationException("A deployment, recovery or other tenant write is in progress in this workspace. Let it finish, close other copies of the tool and try the backup again."); }
        catch (UnauthorizedAccessException) { throw new ConfigurationException("A tenant write lock in this workspace cannot be read. Ask the evidence custodian to check the folder permissions before backing up."); }
    }

    private string[] Sources()
    {
        CheckParents(paths.DataDirectory);
        if (!Directory.Exists(paths.DataDirectory)) return [];
        var files = new List<string>();
        Visit(paths.DataDirectory);
        return files.OrderBy(f => f, StringComparer.Ordinal).ToArray();
        void Visit(string directory)
        {
            CheckLink(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                CheckLink(entry);
                if (Directory.Exists(entry)) { Visit(entry); continue; }
                var name = Path.GetFileName(entry);
                if (name.StartsWith("msal-", StringComparison.OrdinalIgnoreCase) && (name.EndsWith(".cache", StringComparison.OrdinalIgnoreCase) || name.Contains(".cache.", StringComparison.OrdinalIgnoreCase))) continue;
                if (name == "policy-write.lock") { AssertNoWriteInProgress(entry); continue; }
                if (!Record(entry)) throw new ConfigurationException("An unsupported file is present in data/. Backup stopped to avoid incomplete transfer. Preserve it separately and ask the workspace custodian to review it.");
                ValidatePath("data/" + Path.GetRelativePath(paths.DataDirectory, entry).Replace('\\', '/'));
                files.Add(entry);
                if (files.Count > MaxFiles) throw new ConfigurationException("Too many evidence files for one backup.");
            }
        }
    }

    private static Dictionary<string, string> ReadHashes(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxChecksumListBytes) throw new IntegrityException("Backup checksum list is too large.");
        using var content = new MemoryStream();
        using (var input = entry.Open()) CopyHash(input, content, MaxChecksumListBytes);
        if (content.Length != entry.Length) throw new IntegrityException("Backup checksum list size is inconsistent.");
        return ParseHashes(content.ToArray());
    }

    private static Dictionary<string, string> ParseHashes(byte[] list)
    {
        using var reader = new StreamReader(new MemoryStream(list), new UTF8Encoding(false, true));
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length < 67 || line[64..66] != "  " || line[..64].Any(c => !Uri.IsHexDigit(c))) throw new IntegrityException("Malformed backup checksum list.");
            var name = line[66..]; ValidatePath(name);
            if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase) || !hashes.TryAdd(name, line[..64])) throw new IntegrityException("Duplicate or recursive backup checksum entry.");
        }
        return hashes;
    }

    private static bool Record(string file) => Path.GetExtension(file).ToLowerInvariant() is ".json" or ".jsonl" or ".ndjson";
    private static void ValidatePath(string name)
    {
        if (name == "README.txt" || name == "SHA256SUMS.txt") return;
        if (!name.StartsWith("data/", StringComparison.Ordinal) || !Record(name)) throw new IntegrityException("Backup contains an unsupported path or file type.");
        foreach (var part in name.Split('/'))
        {
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.Any(c => c < 32 || "\\:*?\"<>|".Contains(c))
                || stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(stem[3])))
                throw new IntegrityException("Backup path is unsafe on Windows.");
        }
    }
    private static void CheckLink(string file) { if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IntegrityException("Evidence transfer refuses symbolic links and reparse points."); }
    private static void CheckParents(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            if (current.Exists) CheckLink(current.FullName);
    }
    private static bool Inside(string child, string parent) => child.StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || child.Equals(Path.GetFullPath(parent), StringComparison.OrdinalIgnoreCase);
    private static string CopyHash(Stream input, Stream output, long limit)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long count = 0;
        while (true)
        {
            var read = input.Read(buffer); if (read == 0) break;
            count += read; if (count > limit) throw new IntegrityException("Backup entry exceeded its transfer limit.");
            output.Write(buffer, 0, read); hash.AppendData(buffer, 0, read);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static void WriteText(ZipArchive archive, string name, string value, IDictionary<string, string>? hashes)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        using var output = archive.CreateEntry(name).Open(); output.Write(bytes);
        if (hashes is not null) hashes[name] = Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
