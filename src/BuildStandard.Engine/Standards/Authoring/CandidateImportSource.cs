using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Exchange;
using BuildStandard.Engine.Reports;
using System.Text;
using System.Text.Json;
namespace BuildStandard.Engine.Standards.Authoring;

/// <summary>One bounded original file image. Its digest identifies bytes, not trusted settings or a tenant.</summary>
internal sealed record CandidateImportSource(string Json, string Sha256)
{
    internal const int MaximumBytes = 1024 * 1024;
    internal static CandidateImportSource ReadFile(string file)
    {
        ReportEnvelopeDispatcher.RefuseLinks(file);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw new ConfigurationException("Policy import exceeds 1 MiB.");
        using var reader = new BinaryReader(stream);
        var bytes = reader.ReadBytes(MaximumBytes + 1);
        if (bytes.Length > MaximumBytes) throw new ConfigurationException("Policy import exceeds 1 MiB.");
        try
        {
            var json = new UTF8Encoding(false, true).GetString(bytes);
            if (json.StartsWith('\uFEFF')) json = json[1..];
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ConfigurationException("Import one policy JSON object, not a list or envelope.");
            try { ExchangeCaptureSchema.RejectDuplicates(document.RootElement, StringComparer.OrdinalIgnoreCase); }
            catch (ConfigurationException ex) { throw new ConfigurationException("Policy import contains duplicate JSON properties.", ex); }
            return new(json, CanonicalJson.Sha256Hex(bytes));
        }
        catch (DecoderFallbackException ex) { throw new ConfigurationException("Policy import must be valid UTF-8.", ex); }
        catch (JsonException ex) { throw new ConfigurationException("Policy import must be valid JSON within depth 48.", ex); }
    }
}
