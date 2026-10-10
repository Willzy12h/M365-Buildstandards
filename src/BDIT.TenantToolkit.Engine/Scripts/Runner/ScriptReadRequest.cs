using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;

namespace BDIT.TenantToolkit.Engine.Scripts.Runner;

/// <summary>
/// The verified context a run needs before anything is created: the current tenant, the Graph-verified signed-in account
/// object ID and that account's confirmed sign-in name. A client profile or historical evidence is not a connection.
/// </summary>
public sealed record ReadRunContext(string TenantId, string AccountObjectId, string AccountUpn);

/// <summary>One registered parameter: its normalised evidence value and, when it is passed, the payload the wrapper decodes.</summary>
public sealed record ReadRunParameter(string Name, string Type, string Value, bool Bound, JsonNode? Payload);

/// <summary>
/// Builds the exact <c>scriptReadRequest</c> schema 1 bytes the parent pins. The parent is the only validator of what goes
/// in: typed values come from the manifest's own checks, text and dates travel as UTF-8 base64 so PowerShell cannot coerce
/// them, and the request carries no token, cache or credential.
/// </summary>
public static class ScriptReadRequest
{
    public const string Kind = "scriptReadRequest";

    /// <summary>Checks the verified context and binds the form, returning every registered parameter in order.</summary>
    public static IReadOnlyList<ReadRunParameter> Bind(ScriptEntry entry, ReadRunContext context, IReadOnlyDictionary<string, string?> inputs, DateTimeOffset now)
    {
        var report = ScriptCatalogue.ReportFor(entry.Manifest);
        if (!ReportValues.IsCanonicalGuid(context.TenantId) || !ReportValues.IsCanonicalGuid(context.AccountObjectId))
            throw new ConfigurationException("Run needs the current verified tenant and signed-in account. Reconnect first.");
        if (!ScriptCopy.IsAcceptableAccount(context.AccountUpn) || context.AccountUpn != context.AccountUpn.Trim())
            throw new ConfigurationException("Run needs the confirmed sign-in name of the connected account, such as engineer@example.com.");
        var binding = ScriptInputs.Bind(entry.Manifest, inputs, now);
        if (!binding.IsValid) throw new ConfigurationException(string.Join(" ", binding.Problems));

        var parameters = new List<ReadRunParameter>();
        foreach (var registered in report.Parameters)
        {
            var value = binding.Arguments.FirstOrDefault(a => a.Name == registered.Name)?.Value;
            parameters.Add(registered.Type switch
            {
                // An unticked box is left out of the binding; it is still a recorded, passed false.
                ExchangeReportParameterType.Boolean => value switch
                {
                    null => new(registered.Name, registered.Type, "false", true, JsonValue.Create(false)),
                    ScriptFlag flag => new(registered.Name, registered.Type, flag.Value ? "true" : "false", true, JsonValue.Create(flag.Value)),
                    _ => throw Mismatch(registered)
                },
                ExchangeReportParameterType.Integer => value is ScriptNumber number
                    ? new(registered.Name, registered.Type, number.Value.ToString(CultureInfo.InvariantCulture), true, JsonValue.Create(number.Value))
                    : throw Mismatch(registered),
                ExchangeReportParameterType.Date => value is ScriptText date
                    ? new(registered.Name, registered.Type, date.Value, true, JsonValue.Create(Base64(date.Value)))
                    : throw Mismatch(registered),
                // A blank optional text field is not passed, and is recorded as empty text.
                ExchangeReportParameterType.Text => value switch
                {
                    null => new(registered.Name, registered.Type, "", false, null),
                    ScriptText text => new(registered.Name, registered.Type, text.Value, true, JsonValue.Create(Base64(text.Value))),
                    _ => throw Mismatch(registered)
                },
                _ => throw Mismatch(registered)
            });
        }
        foreach (var p in parameters)
            if (!ExchangeReportEvidenceSchema.IsNormalised(p.Type, p.Value)) throw Mismatch(report.Parameters.Single(r => r.Name == p.Name));
        return parameters;
    }

    /// <summary>The request's exact bytes. The wrapper refuses them unless their SHA-256 equals the digest the parent passes.</summary>
    public static byte[] Create(string runId, ScriptEntry entry, ReadRunContext context, IReadOnlyList<ReadRunParameter> parameters)
    {
        var m = entry.Manifest;
        var report = ScriptCatalogue.ReportFor(m);
        var request = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = Kind,
            ["runId"] = runId,
            ["scriptId"] = m.Id,
            ["manifestSha256"] = entry.ManifestSha256,
            ["scriptSha256"] = m.ScriptSha256,
            ["runnerTemplateSha256"] = ReadRunnerTemplate.Sha256,
            ["adapter"] = m.Execution!.Adapter,
            ["reportId"] = report.Id,
            ["reportSchemaVersion"] = 1,
            ["tenantId"] = context.TenantId,
            ["expectedAccountUpn"] = context.AccountUpn,
            ["module"] = new JsonObject { ["name"] = m.Modules[0].Name, ["minimumVersion"] = m.Modules[0].MinimumVersion },
            ["sourceCommands"] = new JsonArray(report.SourceCommands.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()),
            ["sections"] = new JsonArray(report.Sections.Select(s => (JsonNode?)new JsonObject
            {
                ["id"] = s.Id,
                ["columns"] = new JsonArray(ExchangeReportEvidenceSchema.Columns(s.RowType).Select(c => (JsonNode?)JsonValue.Create(c)).ToArray())
            }).ToArray()),
            ["maximumRows"] = Math.Min(m.Limits.MaximumRows, ExchangeReportRegistry.MaximumRows),
            ["parameters"] = new JsonArray(parameters.Select(Parameter).ToArray())
        };
        var bytes = new UTF8Encoding(false).GetBytes(request.ToJsonString());
        if (bytes.Length > ReadRunnerLimits.MaximumRequestBytes) throw new ConfigurationException("The run request is too large.");
        return bytes;
    }

    /// <summary>The parameter exactly as the request carries it, which the result must repeat.</summary>
    public static JsonNode Parameter(ReadRunParameter p) => new JsonObject
    {
        ["name"] = p.Name, ["type"] = p.Type, ["bound"] = p.Bound, ["payload"] = p.Payload?.DeepClone()
    };

    private static string Base64(string value) => Convert.ToBase64String(new UTF8Encoding(false, true).GetBytes(value));
    private static ConfigurationException Mismatch(ExchangeReportRegistry.Parameter p) =>
        new($"The form value for {p.Name} is not a {p.Type} value the registered report accepts.");
}
