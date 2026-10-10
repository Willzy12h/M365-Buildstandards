using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Exchange;

namespace BDIT.TenantToolkit.Engine.Scripts.Runner;

/// <summary>The child's <c>scriptReadResult</c> schema 1 envelope. Every member is required; nothing else is accepted.</summary>
public sealed class ScriptReadResult
{
    [JsonRequired] public int SchemaVersion { get; set; }
    [JsonRequired] public string Kind { get; set; } = "";
    [JsonRequired] public string RunId { get; set; } = "";
    [JsonRequired] public string ScriptId { get; set; } = "";
    [JsonRequired] public string ManifestSha256 { get; set; } = "";
    [JsonRequired] public string ScriptSha256 { get; set; } = "";
    [JsonRequired] public string RunnerTemplateSha256 { get; set; } = "";
    [JsonRequired] public string Adapter { get; set; } = "";
    [JsonRequired] public string ReportId { get; set; } = "";
    [JsonRequired] public int ReportSchemaVersion { get; set; }
    [JsonRequired] public string TenantId { get; set; } = "";
    [JsonRequired] public string? ObservedAccountUpn { get; set; }
    [JsonRequired] public string StartedAt { get; set; } = "";
    [JsonRequired] public string EndedAt { get; set; } = "";
    [JsonRequired] public string RuntimeVersion { get; set; } = "";
    [JsonRequired] public string ModuleVersion { get; set; } = "";
    [JsonRequired] public List<JsonObject> Parameters { get; set; } = [];
    [JsonRequired] public List<ReportSection> Sections { get; set; } = [];

    private static readonly JsonSerializerOptions Strict = new(ToolkitJson.Options)
    {
        PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, MaxDepth = 48
    };

    /// <summary>
    /// Reads the envelope and binds it to the exact request the parent issued: the same run, item, pins, adapter, report,
    /// tenant, account and parameters, with registered sections in order. Row content is checked again when the evidence is
    /// sealed. Anything else refuses, so no child output can choose its own identity, schema or parameters.
    /// </summary>
    public static ScriptReadResult Read(byte[] bytes, string runId, ScriptEntry entry, ReadRunContext context,
        IReadOnlyList<ReadRunParameter> parameters, DateTimeOffset launchedAt, DateTimeOffset finishedAt)
    {
        if (bytes.Length > ReadRunnerLimits.MaximumResultBytes) throw Bad("The result is larger than 32 MiB.");
        string json;
        try { json = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw Bad("The result is not valid UTF-8."); }
        ScriptReadResult r;
        try
        {
            using (var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 }))
                ExchangeCaptureSchema.RejectDuplicates(document.RootElement, StringComparer.Ordinal);
            r = JsonSerializer.Deserialize<ScriptReadResult>(json, Strict) ?? throw Bad("The result is empty.");
        }
        catch (JsonException) { throw Bad("The result is not a strict scriptReadResult envelope."); }

        var m = entry.Manifest;
        var report = ScriptCatalogue.ReportFor(m);
        var same = r.SchemaVersion == 1 && r.Kind == ReadRunnerLimits.OutputKind && r.RunId == runId && r.ScriptId == m.Id
            && r.ManifestSha256 == entry.ManifestSha256 && r.ScriptSha256 == m.ScriptSha256 && r.RunnerTemplateSha256 == ReadRunnerTemplate.Sha256
            && r.Adapter == m.Execution!.Adapter && r.ReportId == report.Id && r.ReportSchemaVersion == 1 && r.TenantId == context.TenantId;
        if (!same) throw Bad("The result names another run, item, pin, report or tenant.");
        if (r.ObservedAccountUpn is null || !string.Equals(r.ObservedAccountUpn.Trim(), context.AccountUpn, StringComparison.OrdinalIgnoreCase)
            || r.ObservedAccountUpn != r.ObservedAccountUpn.Trim())
            throw Bad("The result does not record the expected signed-in account.");
        // The child's clock is only trusted inside the window the parent itself observed, allowing for a small skew.
        var skew = TimeSpan.FromMinutes(2);
        if (!Utc(r.StartedAt, out var started) || !Utc(r.EndedAt, out var ended) || ended < started
            || started < launchedAt - skew || ended > finishedAt + skew)
            throw Bad("The result's collection interval is outside the run.");
        foreach (var version in new[] { r.RuntimeVersion, r.ModuleVersion })
            if (string.IsNullOrWhiteSpace(version) || version.Length > 128 || version.Any(char.IsControl)) throw Bad("The result lacks runtime or module provenance.");
        if (!Version.TryParse(r.ModuleVersion, out var module) || !Version.TryParse(m.Modules[0].MinimumVersion, out var minimum) || module < minimum)
            throw Bad("The result names a module older than the item requires.");
        var expected = new JsonArray(parameters.Select(ScriptReadRequest.Parameter).ToArray());
        if (!JsonNode.DeepEquals(new JsonArray(r.Parameters.Select(p => (JsonNode?)p.DeepClone()).ToArray()), expected))
            throw Bad("The result does not repeat the exact request parameters.");
        if (r.Sections.Count != report.Sections.Count || r.Sections.Any(s => s is null)
            || !r.Sections.Select(s => s.Id).SequenceEqual(report.Sections.Select(s => s.Id), StringComparer.Ordinal))
            throw Bad("The result's sections are not the registered sections.");
        var maximum = Math.Min(m.Limits.MaximumRows, ExchangeReportRegistry.MaximumRows);
        if (r.Sections.Any(s => s.Rows is null || s.Rows.Count > maximum)) throw Bad("The result has more rows than the item allows.");
        return r;
    }

    private static bool Utc(string value, out DateTimeOffset at) => Timestamps.TryParse(value, out at) && value.EndsWith('Z');
    private static ConfigurationException Bad(string message) => new("The runner's result was refused: " + message);
}
