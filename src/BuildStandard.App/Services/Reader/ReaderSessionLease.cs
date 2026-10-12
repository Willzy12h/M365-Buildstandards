using System.IO;
namespace BuildStandard.App.Services.Reader;

/// <summary>One transient session, outside evidence. Disposal alone never proves browser retirement.</summary>
public sealed class ReaderSessionLease : IDisposable
{
    private readonly string _owner = Guid.NewGuid().ToString("N");
    private FileStream? _lock;
    private bool _deleted;
    public string SessionDirectory { get; }
    public string ContentDirectory => Path.Combine(SessionDirectory, "content");
    public string ProfileDirectory => Path.Combine(SessionDirectory, "profile");
    public ReaderSessionLease(string root, Guid instance)
    {
        var fullRoot = Path.GetFullPath(root);
        AssertNoReparseAncestors(fullRoot);
        SessionDirectory = Path.Combine(fullRoot, instance.ToString("N"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(SessionDirectory);
        AssertNoReparseAncestors(SessionDirectory);
        _lock = new FileStream(Path.Combine(SessionDirectory, ".lease"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        File.WriteAllText(Path.Combine(SessionDirectory, ".owner"), _owner);
        Directory.CreateDirectory(ContentDirectory); Directory.CreateDirectory(ProfileDirectory);
    }
    public void ValidateBeforeUse()
    {
        AssertNoReparseAncestors(SessionDirectory);
        AssertNoReparseAncestors(ContentDirectory); AssertNoReparseAncestors(ProfileDirectory);
        AssertNoReparseAncestors(Path.Combine(SessionDirectory, ".owner"));
        if (_deleted || _lock is null || !Directory.Exists(ContentDirectory) || !Directory.Exists(ProfileDirectory)
            || File.ReadAllText(Path.Combine(SessionDirectory, ".owner")) != _owner)
            throw new IOException("Reader session ownership is no longer valid.");
    }
    public bool Retire(bool browserExited)
    {
        if (_deleted) return true;
        if (!browserExited) return false;
        try
        {
            AssertNoReparseAncestors(SessionDirectory);
            if (File.ReadAllText(Path.Combine(SessionDirectory, ".owner")) != _owner) return false;
            // Validate the whole tree before deleting any bytes; never follow a browser-created reparse point.
            ValidateTree(SessionDirectory);
            _lock?.Dispose(); _lock = null;
            DeleteTree(SessionDirectory); _deleted = true; return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    private static void AssertNoReparseAncestors(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Reader cache path contains a reparse point.");
    }
    private static void ValidateTree(string directory)
    {
        AssertNoReparseAncestors(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Reader cache contains a reparse point.");
            if ((attributes & FileAttributes.Directory) != 0) ValidateTree(entry);
        }
    }
    private static void DeleteTree(string directory)
    {
        AssertNoReparseAncestors(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Reader cache changed during retirement.");
            if ((attributes & FileAttributes.Directory) != 0) DeleteTree(entry);
            else File.Delete(entry);
        }
        Directory.Delete(directory, recursive: false);
    }
    public void Dispose() { _lock?.Dispose(); _lock = null; }
}
