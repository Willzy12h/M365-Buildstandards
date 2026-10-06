using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Planning;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>Catalogue-only specification. No tenant profile, evidence or resolved customer identity is accepted.</summary>
public static class StandardSpecificationDocuments
{
    public sealed record Setting(string Path, string Template, string Value, string Origin);

    public const string Explanation = "This is the build standard definition, not observed tenant configuration or deployment evidence. "
        + "Fixed values, reviewable shipped defaults and unresolved client inputs are distinguished. "
        + "Intended production state is separate from the inert candidate recipe. Manual-only settings are documented as requirements; no API defaults are invented.";

    public static IReadOnlyList<Setting> Settings(ControlDefinition control, StandardCatalogue standard, DateTimeOffset now)
    {
        var result = new List<Setting>();
        if (control.Payload is null) return result;
        var supplied = PolicyInputDefaults.Apply(control.Payload, standard, new Dictionary<string, JsonNode?>(), now).Values;
        Visit(control.Payload, "");
        return result;

        void Visit(JsonNode? node, string path)
        {
            if (node is JsonObject obj && obj.Count > 0)
            {
                foreach (var (key, value) in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                    Visit(value, path + "/" + key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal));
                return;
            }
            if (node is JsonArray array && array.Count > 0)
            {
                for (var i = 0; i < array.Count; i++) Visit(array[i], path + "/" + i.ToString(CultureInfo.InvariantCulture));
                return;
            }
            var template = node?.ToJsonString() ?? "null";
            var keys = ParameterUsage.Keys(node).ToArray();
            var unresolved = keys.Any(k => !supplied.TryGetValue(k, out var value) || value is null);
            var origin = keys.Length == 0 ? "Fixed catalogue setting" : unresolved ? "Client input required; unresolved" : "Reviewable shipped default";
            if (!unresolved && keys.Any(k => standard.Parameters.Any(p => p.Key == k && p.IsStale(now)))) origin += "; review date needs checking";
            var singleSetting = new JsonObject { ["value"] = node?.DeepClone() };
            var valueText = unresolved || keys.Length == 0 ? template : PolicyInputDefaults.Resolve(singleSetting, standard, supplied)?["value"]?.ToJsonString() ?? "null";
            result.Add(new Setting(path.Length == 0 ? "/" : path, template, valueText, origin));
        }
    }

    public static string Html(StandardCatalogue standard, DateTimeOffset now)
    {
        var text = new StringBuilder("<section id=\"defaults-settings\" aria-labelledby=\"specification-heading\"><h2 id=\"specification-heading\">Default values and settings specification</h2><p>");
        text.Append(H(Explanation)).Append("</p><p>Default review checked as of ").Append(H(now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append(". The exported JSON retains the original templates and exact source bytes.</p>");
        text.Append("<h3>Catalogue input defaults</h3>");
        Table(text, new[] { "Input / type", "Default", "Review date", "Requirement / scope" }, standard.Parameters.Select(p => new[]
        {
            p.Key + " · " + p.Type,
            p.HasDefault ? p.Default!.ToJsonString() + (p.IsStale(now) ? " · review needed" : "") : "No default — client input",
            p.ReviewedOn,
            p.Label + ": " + p.Description + (p.Required ? " · required" : " · when applicable") + (p.RequiredForControls is { Count: > 0 } ? " · " + string.Join(", ", p.RequiredForControls) : "")
        }));
        text.Append("<h3>Declared collection interfaces and scopes</h3><p>These are catalogue collection permissions. Connection also verifies the operator/organisation; setup and separately reviewed actions have their own documented access requirements.</p>");
        Table(text, new[] { "Collection", "Interface", "Read scope", "Candidate write scope" }, standard.Collections.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new[]
        { p.Key, p.Value.Api + " " + p.Value.Path, p.Value.Scope, p.Value.Write ?? "No write declared" }));
        text.Append("<h3>Settings by control</h3>");
        foreach (var control in standard.Controls)
        {
            text.Append("<section id=\"settings-").Append(H(control.Id)).Append("\"><h4>").Append(H(control.Id + " — " + control.Name)).Append("</h4>");
            var settings = Settings(control, standard, now);
            if (settings.Count == 0) text.Append("<p>Manual or observed-only requirement: ").Append(H(control.DesiredState)).Append(". See its control reference below for procedure and verification.</p>");
            else Table(text, new[] { "JSON pointer", "Catalogue template", "Default / value", "Classification" }, settings.Select(s => new[] { s.Path, s.Template, s.Value, s.Origin }));
            text.Append("<p><a href=\"#").Append(H(control.Id)).Append("\">Purpose, intended state, licences and prerequisites</a></p></section>");
        }
        text.Append("</section>");
        var document = EngineerStandardDocuments.Html(standard, EngineerDocumentKind.BuildStandard);
        var anchor = "<main>";
        document = document.Replace(anchor, anchor + text, StringComparison.Ordinal);
        return document.Replace("</head>", "<style>table{width:100%;border-collapse:collapse;table-layout:fixed;font-size:13px;margin:12px 0}th,td{text-align:left;vertical-align:top;padding:8px;border:1px solid #bdcbd7;overflow-wrap:anywhere;white-space:pre-wrap}th{background:#edf4fa}section section{margin-bottom:24px}@media print{thead{display:table-header-group}tr{break-inside:avoid}}</style></head>", StringComparison.Ordinal);
    }

    public static string Markdown(StandardCatalogue standard, DateTimeOffset now)
    {
        var text = new StringBuilder("# Default values and settings specification\n\n");
        text.Append("Release **").Append(M(standard.Release)).Append("** · catalogue SHA-256 `").Append(standard.IntegrityDigest).Append("`\n\n")
            .Append(Explanation).Append("\n\nDefault review checked as of ").Append(now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(". JSON preserves the original templates.\n\n## Catalogue input defaults\n\n");
        foreach (var p in standard.Parameters)
        {
            text.Append("### ").Append(M(p.Key + " — " + p.Label)).Append("\n\nType: ").Append(M(p.Type)).Append(p.Required ? " · required" : " · when applicable").Append("\n\n")
                .Append(M(p.Description)).Append("\n\nDefault: ").Append(p.HasDefault ? M(p.Default!.ToJsonString()) : "No default — client input required").Append("\n\n")
                .Append("Reviewed: ").Append(M(p.ReviewedOn.Length == 0 ? "not recorded" : p.ReviewedOn)).Append(p.HasDefault && p.IsStale(now) ? " · review needed" : "").Append("\n\n");
            if (p.RequiredForControls is { Count: > 0 }) text.Append("Applies to: ").Append(M(string.Join(", ", p.RequiredForControls))).Append("\n\n");
        }
        text.Append("## Declared collection interfaces and scopes\n\nThese are collection permissions, not a complete setup or administrator-role matrix.\n\n");
        foreach (var (key, def) in standard.Collections.OrderBy(p => p.Key, StringComparer.Ordinal))
            text.Append("- **").Append(M(key)).Append("**: ").Append(M(def.Api + " " + def.Path)).Append("; read ").Append(M(def.Scope)).Append("; candidate write ").Append(M(def.Write ?? "not declared")).Append("\n");
        text.Append("\n## Settings by control\n\n");
        foreach (var control in standard.Controls)
        {
            text.Append("### ").Append(M(control.Id + " — " + control.Name)).Append("\n\n");
            var settings = Settings(control, standard, now);
            if (settings.Count == 0) text.Append("Manual or observed-only requirement: ").Append(M(control.DesiredState)).Append("\n\n");
            foreach (var s in settings)
                text.Append("- **").Append(M(s.Path)).Append("**: ").Append(M(s.Value)).Append(" · ").Append(M(s.Origin))
                    .Append(s.Template == s.Value ? "" : " · template " + M(s.Template)).Append("\n");
            text.Append('\n');
        }
        text.Append("---\n\n").Append(EngineerStandardDocuments.Markdown(standard, EngineerDocumentKind.BuildStandard));
        return text.ToString();
    }

    private static void Table(StringBuilder text, IReadOnlyList<string> headings, IEnumerable<string[]> rows)
    {
        text.Append("<table><thead><tr>");
        foreach (var h in headings) text.Append("<th scope=\"col\">").Append(H(h)).Append("</th>");
        text.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            text.Append("<tr>");
            foreach (var cell in row) text.Append("<td>").Append(H(cell)).Append("</td>");
            text.Append("</tr>");
        }
        text.Append("</tbody></table>");
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string M(string? value) => (value ?? "").Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\\", "\\\\", StringComparison.Ordinal).Replace("`", "\\`", StringComparison.Ordinal)
        .Replace("*", "\\*", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal);
}
