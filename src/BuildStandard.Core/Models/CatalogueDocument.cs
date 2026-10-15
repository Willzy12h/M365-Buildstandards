using BuildStandard.Core.Json;

namespace BuildStandard.Core.Models;

/// <summary>Immutable generated reference, not tenant evidence or execution authority.</summary>
public sealed class CatalogueDocument
{
    private readonly byte[] _html;
    public string Kind => "BuildStandard";
    public string CatalogueRelease { get; }
    public string CatalogueDigest { get; }
    public string PresentationVersion { get; }
    public DateTimeOffset GeneratedAt { get; }
    public string HtmlSha256 { get; }
    public IReadOnlyList<string> Anchors { get; }
    public IReadOnlyList<string> RendererReferences { get; }

    internal CatalogueDocument(string release, string catalogueDigest, string presentationVersion,
        DateTimeOffset generatedAt, byte[] html, IEnumerable<string> anchors, IEnumerable<string> references)
    {
        CatalogueRelease = release; CatalogueDigest = catalogueDigest; PresentationVersion = presentationVersion;
        GeneratedAt = generatedAt.ToUniversalTime(); _html = (byte[])html.Clone(); HtmlSha256 = CanonicalJson.Sha256Hex(_html);
        Anchors = Array.AsReadOnly(anchors.Distinct(StringComparer.Ordinal).ToArray());
        RendererReferences = Array.AsReadOnly(references.Distinct(StringComparer.Ordinal).ToArray());
    }

    // A ReadOnlyMemory wrapping our actual array could still disclose its backing store. Return a copy.
    public byte[] CopyHtmlBytes() => (byte[])_html.Clone();

    public string RequireAnchor(string id) => Anchors.Contains(id, StringComparer.Ordinal)
        ? "#" + Uri.EscapeDataString(id)
        : throw new ConfigurationException("This generated catalogue does not contain the selected anchor. Reload and select a control from the same catalogue.");
}
