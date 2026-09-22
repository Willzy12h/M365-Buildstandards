using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Json;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>
/// Describes the proposed catalogue, intended rollout and client decisions. It does not report tenant state or prove
/// successful deployment. Keep observed results in the separate assessment and execution reports.
///
/// It is deliberately not a technical specification. Graph paths, payloads, API versions and object identifiers are
/// left to the engineer reports; a client needs to know what is protected, what it costs them in inconvenience, and
/// what is still waiting on them.
/// </summary>
public static class BuildStandardDocument
{
    private static readonly Regex TechnicalDetail = new(
        @"[{}\[\]]|@odata|graph\.microsoft\.com|/(?:deviceManagement|deviceAppManagement|identity|policies|groups|users)(?:/|\?|\s|$)|\./(?:Device|User)/Vendor/MSFT|\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static string ClientText(string? text) => TechnicalDetail.IsMatch(text ?? "")
        ? "Configuration details are available in the engineer report." : text ?? "";
    private static string H(string? s) => WebUtility.HtmlEncode(ClientText(s));
    private static string M(string? s) => ClientText(s).Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("|", "\\|", StringComparison.Ordinal);

    /// <summary>How a control reaches the tenant, in words a client can act on.</summary>
    public static string Delivery(ControlDefinition control) =>
        control.HasRecipe && control.Collection == "groups" ? "Created by the toolkit as an empty security group. Membership and any later removal require a separate engineer review."
        : control.HasRecipe && control.Collection == "namedLocations" ? "Created by the toolkit as an untrusted IP location. Referencing it in a sign-in policy and any later removal require a separate engineer review."
        : control.HasRecipe ? "Created by the toolkit as an inactive candidate. Supported activation or assignment requires a separate reviewed action; other rollout steps are completed by an engineer."
        : control.Equivalence is not null ? "Read from the tenant and reported. Changed only through a separate reviewed action."
        : "Completed by an engineer, or in a vendor portal. Nothing is created automatically.";

    /// <summary>Inputs this control cannot be created without, and the ones that fall back to a reviewed default.</summary>
    public static (IReadOnlyList<string> Required, IReadOnlyList<string> Defaulted) Inputs(ControlDefinition control, StandardCatalogue standard)
    {
        var required = new List<string>();
        var defaulted = new List<string>();
        if (control.Payload is null) return (required, defaulted);

        var used = ParameterUsage.Keys(control.Payload);
        foreach (var parameter in standard.Parameters)
        {
            if (!used.Contains(parameter.Key)) continue;
            if (string.Equals(parameter.Key, "tenantId", StringComparison.Ordinal)) continue;
            if (parameter.HasDefault) defaulted.Add(ClientText(parameter.Label) + " (default " + DefaultDescription(parameter.Default) + ")");
            else required.Add(parameter.Label);
        }
        return (required, defaulted);
    }

    public static string Html(StandardCatalogue standard, string clientName, string preparedBy, string preparedOn)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en-GB\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>")
          .Append(H("Microsoft 365 Build Standard - " + clientName)).Append("</title>").Append(Css).Append("</head><body>");

        sb.Append("<div class=\"brand\">Microsoft 365 Build Standard</div>");
        sb.Append("<h1>").Append(H(clientName)).Append("</h1>");
        sb.Append("<p class=\"lede\">").Append(H(standard.Description)).Append("</p>");

        sb.Append("<dl class=\"meta\">");
        Meta(sb, "Prepared for", clientName);
        Meta(sb, "Prepared by", preparedBy);
        Meta(sb, "Date", preparedOn);
        Meta(sb, "Standard release", standard.Release);
        Meta(sb, "Status", standard.Status);
        sb.Append("</dl>");

        sb.Append("<h2>What this document is</h2>");
        sb.Append("<p>This catalogue describes the proposed configuration for your Microsoft 365 tenant. Each control below says what it protects, ")
          .Append("what it means in practice for the people using it, and what happens without it. It is generated from the same file the ")
          .Append("deployment tool reads. It is not evidence that any change has been deployed, verified or activated in your tenant.</p>");

        sb.Append("<h2>How changes are made</h2>");
        sb.Append("<p>Nothing is switched on without review. A policy is created inactive: Conditional Access policies are created disabled, ")
          .Append("and device policies are created with nothing assigned to them. An engineer then checks the settings against this document, ")
          .Append("tests where appropriate, and activates each one as a separate deliberate step. Every change is recorded with evidence of the ")
          .Append("before and after state where available; incomplete verification is reported separately. Toolkit-created sign-in candidates retain the reviewed emergency-account and operator exclusions. Recovery access must still be tested and maintained.</p>");

        Summary(sb, standard);

        foreach (var category in standard.Controls.Select(c => c.Category).Distinct(StringComparer.Ordinal))
        {
            sb.Append("<h2>").Append(H(category)).Append("</h2>");
            foreach (var control in standard.Controls.Where(c => string.Equals(c.Category, category, StringComparison.Ordinal)))
                Control(sb, control, standard);
        }

        Outstanding(sb, standard);

        sb.Append("<footer>Generated from Build Standard ").Append(H(standard.Release))
          .Append(". This document describes configuration only. It does not certify compliance with any external standard, ")
          .Append("and it does not record the operational checks an engineer performs during deployment.</footer>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void Summary(StringBuilder sb, StandardCatalogue standard)
    {
        var created = standard.Controls.Count(c => c.HasRecipe);
        var reported = standard.Controls.Count(c => !c.HasRecipe && c.Equivalence is not null);
        var manual = standard.Controls.Count - created - reported;

        sb.Append("<h2>At a glance</h2><div class=\"tiles\">");
        Tile(sb, standard.Controls.Count, "controls in this standard");
        Tile(sb, created, "candidate creation recipes");
        Tile(sb, reported, "read from your tenant and reported");
        Tile(sb, manual, "completed by an engineer or in a vendor portal");
        sb.Append("</div>");
    }

    private static void Control(StringBuilder sb, ControlDefinition control, StandardCatalogue standard)
    {
        var severity = control.Severity.ToLowerInvariant();
        sb.Append("<div class=\"control sev-").Append(H(severity)).Append("\">");
        sb.Append("<h3>").Append(H(control.Name)).Append("</h3>");
        sb.Append("<div><span class=\"badge\">").Append(H(control.Id)).Append("</span>")
          .Append("<span class=\"badge sev\">").Append(H(control.Severity)).Append("</span></div>");

        Field(sb, "Why it matters", control.Purpose);
        Field(sb, "What good looks like", control.DesiredState);
        Field(sb, "Without it", control.BusinessImpact);
        Field(sb, "How it is applied", Delivery(control));
        if (control.ExpectedProduction.Assignment.Length > 0) Field(sb, "Intended scope after rollout", control.ExpectedProduction.Assignment);

        var (required, defaulted) = Inputs(control, standard);
        if (required.Count > 0) Field(sb, "We need from you", string.Join("; ", required));
        if (defaulted.Count > 0) Field(sb, "Assumed unless you say otherwise", string.Join("; ", defaulted));

        sb.Append("</div>");
    }

    private static void Outstanding(StringBuilder sb, StandardCatalogue standard)
    {
        // Walk each control's payload once rather than re-serialising every payload for every parameter.
        var referenced = ParameterUsage.KeysAcross(standard.Controls.Select(c => (JsonNode?)c.Payload));
        var inputs = standard.Parameters
            .Where(p => !p.HasDefault && !string.Equals(p.Key, "tenantId", StringComparison.Ordinal))
            .Where(p => referenced.Contains(p.Key))
            .ToList();
        if (inputs.Count == 0) return;

        sb.Append("<h2>What we still need from you</h2>");
        sb.Append("<p>Each of these decides how a policy behaves in your tenant. Until we have it, the controls that depend on it are not created.</p>");
        sb.Append("<table><thead><tr><th>Information</th><th>Why we need it</th></tr></thead><tbody>");
        foreach (var input in inputs)
            sb.Append("<tr><td>").Append(H(input.Label)).Append("</td><td>").Append(H(input.Description)).Append("</td></tr>");
        sb.Append("</tbody></table>");
    }

    public static string Markdown(StandardCatalogue standard, string clientName, string preparedBy, string preparedOn)
    {
        var sb = new StringBuilder();
        sb.Append("# Microsoft 365 Build Standard — ").Append(M(clientName)).Append("\n\n");
        sb.Append(M(standard.Description)).Append("\n\n");
        sb.Append("This catalogue describes proposed configuration. It is not evidence that a change has been deployed, verified or activated in your tenant.\n\n");
        sb.Append("| | |\n|---|---|\n");
        sb.Append("| Prepared for | ").Append(M(clientName)).Append(" |\n");
        sb.Append("| Prepared by | ").Append(M(preparedBy)).Append(" |\n");
        sb.Append("| Date | ").Append(M(preparedOn)).Append(" |\n");
        sb.Append("| Standard release | ").Append(M(standard.Release)).Append(" |\n");
        sb.Append("| Status | ").Append(M(standard.Status)).Append(" |\n\n");

        sb.Append("## How changes are made\n\nNothing is switched on without review. A policy is created inactive: Conditional Access ")
          .Append("policies are created disabled, and device policies are created with nothing assigned to them. An engineer then checks the ")
          .Append("settings, tests where appropriate, and activates each one as a separate deliberate step. Every change is recorded with ")
          .Append("evidence of the before and after state where available; incomplete verification is reported separately. Toolkit-created sign-in candidates retain the reviewed emergency-account and operator exclusions. Recovery access must still be tested and maintained.\n\n");

        foreach (var category in standard.Controls.Select(c => c.Category).Distinct(StringComparer.Ordinal))
        {
            sb.Append("## ").Append(M(category)).Append("\n\n");
            foreach (var control in standard.Controls.Where(c => string.Equals(c.Category, category, StringComparison.Ordinal)))
            {
                sb.Append("### ").Append(M(control.Name)).Append(" (").Append(M(control.Id)).Append(")\n\n");
                Line(sb, "Why it matters", control.Purpose);
                Line(sb, "What good looks like", control.DesiredState);
                Line(sb, "Without it", control.BusinessImpact);
                Line(sb, "How it is applied", Delivery(control));
                Line(sb, "Intended scope after rollout", control.ExpectedProduction.Assignment);
                var (required, defaulted) = Inputs(control, standard);
                if (required.Count > 0) Line(sb, "We need from you", string.Join("; ", required));
                if (defaulted.Count > 0) Line(sb, "Assumed unless you say otherwise", string.Join("; ", defaulted));
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    private static void Line(StringBuilder sb, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) sb.Append("**").Append(label).Append(".** ").Append(M(value)).Append("\n\n");
    }

    private static string DefaultDescription(JsonNode? value) => value switch
    {
        JsonValue scalar when scalar.TryGetValue<string>(out var text) => ClientText(text),
        JsonValue scalar when scalar.TryGetValue<bool>(out var flag) => flag ? "yes" : "no",
        JsonValue scalar => ClientText(scalar.ToJsonString()),
        _ => "reviewed configuration; confirm with your engineer"
    };

    private static void Field(StringBuilder sb, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append("<p><span class=\"label\">").Append(H(label)).Append("</span> ").Append(H(value)).Append("</p>");
    }

    private static void Meta(StringBuilder sb, string term, string value) =>
        sb.Append("<dt>").Append(H(term)).Append("</dt><dd>").Append(H(value)).Append("</dd>");

    private static void Tile(StringBuilder sb, int count, string label) =>
        sb.Append("<div class=\"tile\"><b>").Append(count).Append("</b><span>").Append(H(label)).Append("</span></div>");

    private const string Css = """
        <style>
        :root{--fg:#1a2028;--muted:#5a6675;--line:#d9dee5;--bg:#f7f9fc;--accent:#1769aa;--critical:#b3261e;--high:#c2530a;--medium:#b06000;--low:#5a6675}
        *{box-sizing:border-box}body{font-family:"Segoe UI",system-ui,sans-serif;color:var(--fg);max-width:900px;margin:0 auto;padding:40px 24px;line-height:1.55;font-size:15px}
        h1{font-size:30px;margin:0 0 8px}h2{font-size:20px;margin:36px 0 12px;border-bottom:1px solid var(--line);padding-bottom:6px}h3{font-size:16px;margin:0 0 6px}
        .brand{font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:var(--muted)}
        .lede{font-size:16px;color:var(--muted);margin:0 0 20px}
        dl.meta{display:grid;grid-template-columns:180px 1fr;gap:4px 16px;background:var(--bg);padding:16px;border-radius:6px;font-size:14px}dl.meta dt{color:var(--muted)}dl.meta dd{margin:0}
        .tiles{display:flex;flex-wrap:wrap;gap:12px;margin:14px 0}.tile{background:var(--bg);border-radius:6px;padding:14px 18px;min-width:170px;flex:1}.tile b{display:block;font-size:30px;line-height:1.1}.tile span{font-size:13px;color:var(--muted)}
        .control{border:1px solid var(--line);border-left-width:6px;border-radius:6px;padding:16px 18px;margin-bottom:14px}
        .sev-critical{border-left-color:var(--critical)}.sev-high{border-left-color:var(--high)}.sev-medium{border-left-color:var(--medium)}.sev-low{border-left-color:var(--low)}
        .control p{margin:6px 0}.label{color:var(--muted);font-size:13px;text-transform:uppercase;letter-spacing:.03em;margin-right:6px}
        .badge{display:inline-block;font-size:11px;font-weight:600;padding:2px 8px;border-radius:10px;background:#eef1f5;color:var(--muted);text-transform:uppercase;letter-spacing:.04em;margin-right:6px}
        table{border-collapse:collapse;width:100%;font-size:14px;margin:8px 0 16px}th{text-align:left;padding:8px 10px;border-bottom:1px solid var(--line);background:var(--bg);font-size:12px;text-transform:uppercase;letter-spacing:.04em;color:var(--muted)}td{padding:8px 10px;border-bottom:1px solid var(--line);vertical-align:top}
        footer{margin-top:44px;padding-top:16px;border-top:1px solid var(--line);font-size:13px;color:var(--muted)}
        @media print{body{max-width:none;padding:0}.control,table{break-inside:avoid}}
        </style>
        """;
}
