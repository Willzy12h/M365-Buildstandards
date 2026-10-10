using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using BDIT.TenantToolkit.Core;

namespace BDIT.TenantToolkit.Engine.Standards;

/// <summary>Reads a Microsoft Content Prep Tool package without extraction or executing its installer.</summary>
public sealed class IntuneWinPackage : IDisposable
{
    private readonly FileStream _file;
    private readonly ZipArchive _archive;
    private readonly ZipArchiveEntry _content;
    private readonly JsonObject _encryption;
    public string Sha256 { get; }
    public string InstallerName { get; }
    public string ContentName => _content.Name;
    public long EncryptedBytes => _content.Length;
    public long UnencryptedBytes { get; }
    public IntuneWinPackage(string fileName)
    {
        _file = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (_file.Length is <= 0 or > 8L * 1024 * 1024 * 1024) throw new ConfigurationException("Select an .intunewin package up to 8 GiB.");
            Sha256 = Convert.ToHexString(SHA256.HashData(_file)).ToLowerInvariant(); _file.Position = 0;
            _archive = new ZipArchive(_file, ZipArchiveMode.Read, leaveOpen: true);
            var metadata = _archive.Entries.SingleOrDefault(e => e.FullName.Equals("IntuneWinPackage/Metadata/Detection.xml", StringComparison.OrdinalIgnoreCase))
                ?? throw new ConfigurationException("Package Detection.xml is missing.");
            if (metadata.Length > 1024 * 1024) throw new ConfigurationException("Package metadata exceeds 1 MiB.");
            using var stream = metadata.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
            var root = XDocument.Load(reader).Root ?? throw new ConfigurationException("Invalid package metadata.");
            string Value(string name) => root.Descendants().SingleOrDefault(e => e.Name.LocalName == name)?.Value ?? throw new ConfigurationException("Package metadata is missing " + name + ".");
            InstallerName = Value("SetupFile");
            if (InstallerName != Path.GetFileName(InstallerName) || InstallerName.Contains('/') || InstallerName.Contains('\\')) throw new ConfigurationException("Installer name must not contain a path.");
            var contentName = Value("FileName");
            _content = _archive.Entries.SingleOrDefault(e => e.FullName.Equals("IntuneWinPackage/Contents/" + contentName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ConfigurationException("Encrypted package content is missing.");
            if (_content.Length is <= 0 or > 8L * 1024 * 1024 * 1024) throw new ConfigurationException("Encrypted content size is unsupported.");
            if (!long.TryParse(Value("UnencryptedContentSize"), out var plain) || plain <= 0) throw new ConfigurationException("Invalid package size.");
            UnencryptedBytes = plain;
            _encryption = new JsonObject();
            foreach (var name in new[] { "EncryptionKey", "MacKey", "InitializationVector", "Mac", "ProfileIdentifier", "FileDigest", "FileDigestAlgorithm" })
                _encryption[char.ToLowerInvariant(name[0]) + name[1..]] = Value(name);
            foreach (var (name, size) in new[] { ("encryptionKey", 32), ("macKey", 32), ("initializationVector", 16), ("mac", 32), ("fileDigest", 32) })
                if (Convert.FromBase64String(_encryption[name]!.ToString()).Length != size) throw new ConfigurationException("Unsupported package encryption metadata.");
            if (_encryption["profileIdentifier"]?.ToString() != "ProfileVersion1" || _encryption["fileDigestAlgorithm"]?.ToString() != "SHA256")
                throw new ConfigurationException("Unsupported package encryption profile.");
        }
        catch { _file.Dispose(); throw; }
    }
    public Stream OpenEncryptedContent() => _content.Open();
    public JsonObject CommitPayload() => new() { ["fileEncryptionInfo"] = _encryption.DeepClone() };
    public void Dispose() { _encryption.Clear(); _archive.Dispose(); _file.Dispose(); }
}
