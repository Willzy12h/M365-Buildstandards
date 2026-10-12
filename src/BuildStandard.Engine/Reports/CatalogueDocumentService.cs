using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Standards;

namespace BuildStandard.Engine.Reports;

/// <summary>Generates the existing whole-catalogue reference from an admitted private snapshot, never a client.</summary>
public sealed class CatalogueDocumentService(ToolkitPaths paths, Func<DateTimeOffset>? now = null)
{
    public CatalogueDocument Create(StandardCatalogue standard)
    {
        var bytes = VerifiedCatalogueSource.Read(paths, standard);
        // Render these exact verified bytes, not the caller's mutable model after admission.
        var snapshot = StandardsLoader.Parse(new UTF8Encoding(false, true).GetString(bytes), standard.SourceFileName);
        snapshot.IntegrityDigest = CanonicalJson.Sha256Hex(bytes);
        var generated = (now?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var html = EngineerStandardDocuments.Html(snapshot, EngineerDocumentKind.BuildStandard, generated);
        // These lists are derived from what this renderer actually emitted; they are not a launch allowlist.
        var anchors = Regex.Matches(html, " id=\"(?<value>[^\"]*)\"").Select(m => WebUtility.HtmlDecode(m.Groups["value"].Value)).ToArray();
        var references = Regex.Matches(html, " href=\"(?<value>[^\"]*)\"").Select(m => WebUtility.HtmlDecode(m.Groups["value"].Value))
            .Where(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps).ToArray();
        return new CatalogueDocument(snapshot.Release, snapshot.IntegrityDigest, EngineerStandardDocuments.PresentationVersion,
            generated, new UTF8Encoding(false).GetBytes(html), anchors, references);
    }
}
