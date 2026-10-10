using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Engine.Exchange;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>Bounded strict serialisation/parsing shared by new Graph envelopes and their explicit dispatcher.</summary>
internal static class RegisteredReportJson
{
    internal static readonly JsonSerializerOptions Strict = Options();
    private static JsonSerializerOptions Options()
    {
        var o = new JsonSerializerOptions(ToolkitJson.Options)
        {
            PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never, NumberHandling = JsonNumberHandling.Strict, MaxDepth = 48
        };
        o.Converters.Clear(); o.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        o.MakeReadOnly(populateMissingResolver: true); return o;
    }
    internal static JsonDocument Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > ReportEvidenceSchema.MaximumBytes)
            throw new ConfigurationException("Report evidence exceeds the 32 MiB reader limit.");
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
            ExchangeCaptureSchema.RejectDuplicates(document.RootElement, StringComparer.Ordinal);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ConfigurationException("Report envelope must be an object.");
            return document;
        }
        catch (JsonException) { document?.Dispose(); throw new ConfigurationException("Invalid report envelope JSON."); }
        catch { document?.Dispose(); throw; }
    }
    internal static (int Schema, string Id) Inspect(JsonElement root)
    {
        if (!root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "reportEvidence"
            || !root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version)
            || !root.TryGetProperty("reportId", out var id) || id.ValueKind != JsonValueKind.String)
            throw new ConfigurationException("Unsupported Graph report envelope kind, schema or report ID.");
        return (version, id.GetString()!);
    }
}
