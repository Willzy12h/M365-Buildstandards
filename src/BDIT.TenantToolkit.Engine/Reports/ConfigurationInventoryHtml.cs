using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Exchange;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>All returned configuration, not an assessment or an intended standard. Rendering performs no reads or writes.</summary>
public static class ConfigurationInventoryHtml
{
    public static string Render(TenantSnapshot snapshot, StandardCatalogue? standard = null)
    {
        if (snapshot.ExchangeCapture is { } supplemental)
        {
            if (!Timestamps.TryParse(supplemental.CapturedAt, out var captured)) throw new ConfigurationException("Supplemental capture time is invalid.");
            // Historical schema/identity validation, not a claim that the capture is fresh now.
            ExchangeCaptureSchema.Validate(supplemental, snapshot.TenantId, captured);
        }
        var catalogue = standard?.Release == snapshot.StandardRelease ? standard : null;
        var keys = snapshot.Collections.Keys.Concat(catalogue?.Collections.Keys ?? Enumerable.Empty<string>())
            .Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var intact = EvidenceIntegrity.Verify(snapshot, snapshot.IntegrityDigest);
        var incomplete = !snapshot.Complete || keys.Count == 0 || keys.Any(k => !snapshot.Collections.TryGetValue(k, out var c) || !Usable(c));
        var sb = new StringBuilder("<!doctype html><html lang=\"en-GB\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\"><title>Tenant configuration inventory</title>");
        sb.Append("<style>body{font:14px/1.5 'Segoe UI',sans-serif;max-width:1180px;margin:auto;padding:28px;color:#1a2028}h1{margin-bottom:4px}h2{margin-top:28px}table{width:100%;border-collapse:collapse}th,td{padding:8px;text-align:left;vertical-align:top;border-bottom:1px solid #d9dee5;overflow-wrap:anywhere}th{background:#f7f9fc}dl{display:grid;grid-template-columns:180px 1fr;gap:4px 12px}dd{margin:0;overflow-wrap:anywhere}pre{white-space:pre-wrap;overflow-wrap:anywhere;margin:0;font:12px/1.5 Consolas,monospace}summary{cursor:pointer;padding:10px;background:#f7f9fc;margin-top:8px}.notice{background:#fff5de;padding:12px;border-left:4px solid #946100}@media print{body{max-width:none;padding:0}details{break-inside:avoid}}</style></head><body>");
        sb.Append("<p>M365 BuildStandard Tool · Captured configuration</p><h1>").Append(H(snapshot.TenantName)).Append("</h1>");
        sb.Append("<p>This inventory shows all data in the captured collections, not every setting in every Microsoft 365 service. It is separate from assessment findings, the intended build standard, drift and upgrade impact.</p>");
        if (!intact) sb.Append("<p class=\"notice\"><b>Evidence integrity is unverified.</b> The capture digest is missing or does not match. Do not treat these values as verified evidence.</p>");
        if (incomplete) sb.Append("<p class=\"notice\"><b>INCOMPLETE — review collection status.</b> Failed, missing, cancelled or partially collected data is unknown and cannot establish absence.</p>");
        sb.Append("<dl>");
        Meta(sb, "Tenant ID", snapshot.TenantId); Meta(sb, "Primary domain", snapshot.PrimaryDomain); Meta(sb, "Client label", snapshot.ClientLabel);
        Meta(sb, "All verified domains", "Not recorded in this capture; the primary domain is shown separately.");
        Meta(sb, "Captured at", snapshot.CapturedAt); Meta(sb, "Captured by", snapshot.CapturedBy); Meta(sb, "Identity source", snapshot.IdentitySource);
        Meta(sb, "Permission mode at capture", snapshot.SessionMode); Meta(sb, "Capture ID", snapshot.Id); Meta(sb, "Recorded standard release", snapshot.StandardRelease);
        Meta(sb, "Toolkit version at capture", snapshot.ToolkitVersion); Meta(sb, "Recorded capture digest", snapshot.IntegrityDigest);
        sb.Append("</dl><p>Values are shown exactly as returned. An omitted property was not returned; <code>null</code> is an explicit returned null. Neither means compliant. Arrays include only the captured members; incomplete assignment/detail reads remain unknown.</p><h2>Collection status</h2><table><thead><tr><th>Collection</th><th>Read status</th><th>Returned objects</th><th>Source / limitation</th></tr></thead><tbody>");
        foreach (var key in keys)
        {
            snapshot.Collections.TryGetValue(key, out var capture);
            sb.Append("<tr><td>").Append(H(catalogue?.FindCollection(key)?.Label ?? key)).Append("<br><code>").Append(H(key)).Append("</code></td><td>")
                .Append(H(Status(capture))).Append("</td><td>").Append(capture is null ? "Unknown" : capture.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append("</td><td>").Append(H(capture is null ? "No collection evidence recorded." : $"{capture.Api} {capture.Path}; recorded count: {capture.Count}"));
            if (capture?.Error is { Length: > 0 } error) sb.Append("<br>").Append(H(error));
            sb.Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
        if (keys.Count == 0) sb.Append("<p>No collection evidence was recorded. No absence can be established.</p>");
        foreach (var key in keys)
        {
            sb.Append("<section><h2>").Append(H(catalogue?.FindCollection(key)?.Label ?? key)).Append("</h2>");
            if (!snapshot.Collections.TryGetValue(key, out var capture)) { sb.Append("<p>Not recorded — unable to check.</p></section>"); continue; }
            sb.Append("<p>").Append(H(Status(capture))).Append(". ");
            if (capture.Items.Count == 0)
                sb.Append(Usable(capture) ? "Read completed successfully; no objects were returned." : "No objects are available from this incomplete read; absence is unknown.");
            else if (!Usable(capture)) sb.Append("Only the returned data below is available; missing settings or objects remain unknown.");
            sb.Append("</p>");
            Objects(sb, capture.Items);
            sb.Append("</section>");
        }
        if (snapshot.ExchangeCapture is { } exchange)
        {
            sb.Append("<section><h2>Separate Exchange / Purview observations</h2><p>Separate service authentication and capture time; these observations do not establish Graph snapshot completeness or deployment authority.</p><dl>");
            Meta(sb, "Exchange capture ID", exchange.Id); Meta(sb, "Captured at", exchange.CapturedAt); Meta(sb, "Module version", exchange.ModuleVersion);
            Meta(sb, "Source", exchange.Source); Meta(sb, "Domain", exchange.Domain); Meta(sb, "Exchange tenant", exchange.ExchangeTenantId); Meta(sb, "Purview tenant", exchange.PurviewTenantId);
            sb.Append("</dl>");
            foreach (var (key, collection) in exchange.Collections.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                sb.Append("<h3>").Append(H(key)).Append("</h3><p>").Append(H(collection.Status)).Append(" · ").Append(H(collection.Command)).Append("</p>");
                if (collection.Status != CaptureStatus.Collected) sb.Append("<p class=\"notice\">Unable to check; absence is unknown.</p>");
                else if (collection.Items.Count == 0) sb.Append("<p>Read completed successfully; no objects were returned.</p>");
                if (collection.Error is { Length: > 0 } error) sb.Append("<p>").Append(H(error)).Append("</p>");
                Objects(sb, collection.Items);
            }
            if (exchange.Dns.Count > 0)
            {
                sb.Append("<h3>Stored DNS observations — no lookup performed</h3><table><thead><tr><th>Name / kind</th><th>Status / time</th><th>Returned records / error</th></tr></thead><tbody>");
                foreach (var dns in exchange.Dns)
                    sb.Append("<tr><td>").Append(H(dns.Name)).Append(" · ").Append(H(dns.Kind.ToString())).Append("</td><td>").Append(H(dns.Status.ToString())).Append(" · ").Append(H(dns.QueriedAt))
                        .Append("</td><td><pre>").Append(H(string.Join("\n", dns.Records))).Append("</pre>").Append(H(dns.Error)).Append("</td></tr>");
                sb.Append("</tbody></table>");
            }
            sb.Append("</section>");
        }
        sb.Append("<footer><p>Local configuration inventory. No tenant configuration was read or changed while exporting. Capture digests detect modification; they are not signatures. Keep this client evidence in approved storage.</p></footer></body></html>");
        return sb.ToString();
    }

    private static bool Usable(CollectionCapture c) => c.Usable && c.Count == c.Items.Count;
    private static string Status(CollectionCapture? c) => c is null ? "Not recorded — unable to check"
        : c.Count != c.Items.Count ? $"{c.Status} — recorded count differs from returned objects; verify collection"
        : c.Status == CaptureStatus.Collected && c.DetailIncomplete ? "Partially collected — detail or assignments incomplete" : c.Status;
    private static void Objects(StringBuilder sb, IEnumerable<JsonObject> items)
    {
        foreach (var item in items)
        {
            sb.Append("<details open><summary><b>").Append(H(Name(item))).Append("</b> · Object ID: ").Append(H(Text(item, "id") ?? Text(item, "ExternalDirectoryObjectId") ?? Text(item, "Guid") ?? "Not returned"))
                .Append("</summary><table><thead><tr><th>Property</th><th>Returned value</th></tr></thead><tbody>");
            foreach (var property in item.OrderBy(p => p.Key, StringComparer.Ordinal))
                sb.Append("<tr><td>").Append(H(property.Key)).Append("</td><td><pre>").Append(H(property.Value?.ToJsonString() ?? "null")).Append("</pre></td></tr>");
            sb.Append("</tbody></table></details>");
        }
    }
    private static string Name(JsonObject item) => Text(item, "displayName") ?? Text(item, "deviceName") ?? Text(item, "name") ?? Text(item, "userPrincipalName") ?? Text(item, "DisplayName") ?? Text(item, "Name") ?? Text(item, "Identity") ?? "Name not returned";
    private static string? Text(JsonObject item, string key) => item[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static void Meta(StringBuilder sb, string label, string? value) => sb.Append("<dt>").Append(H(label)).Append("</dt><dd>").Append(H(string.IsNullOrEmpty(value) ? "Not recorded" : value)).Append("</dd>");
}
