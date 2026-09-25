using System.Net;
using System.Text;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Exchange;

namespace BDIT.TenantToolkit.Engine.Reports;

public enum EngineerDocumentKind { BuildStandard, ManualGuide }

/// <summary>Catalogue-only documents: no client profile, snapshot, tenant context or connection is accepted.</summary>
public static class EngineerStandardDocuments
{
    public static string Title(EngineerDocumentKind kind) => kind switch
    {
        EngineerDocumentKind.BuildStandard => "The Build Standard",
        EngineerDocumentKind.ManualGuide => "Manual implementation and verification guide",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public static bool HasCompleteManual(ControlDefinition control) => control.Implementation is { Before.Count: > 0, PortalSteps.Count: > 0, After.Count: > 0 } m
        && !string.IsNullOrWhiteSpace(m.PowerShell) && m.Before.Concat(m.PortalSteps).Concat(m.After).All(s => !string.IsNullOrWhiteSpace(s));

    private static void Validate(StandardCatalogue standard, EngineerDocumentKind kind)
    {
        _ = Title(kind);
        if (standard.Controls.Count == 0) throw new ConfigurationException("The loaded standard has no controls.");
        if (kind == EngineerDocumentKind.ManualGuide)
        {
            var missing = standard.Controls.Where(c => !HasCompleteManual(c)).Select(c => c.Id).ToArray();
            if (missing.Length > 0) throw new ConfigurationException("Manual guide is incomplete for: " + string.Join(", ", missing) + ". Select a release with complete manual instructions.");
        }
    }

    public static IReadOnlyList<string> Inputs(ControlDefinition control, StandardCatalogue standard)
    {
        var keys = control.Payload is null ? new HashSet<string>() : ParameterUsage.Keys(control.Payload);
        var inputs = standard.Parameters.Where(p => keys.Contains(p.Key) || p.RequiredForControls?.Contains(control.Id) == true)
            .Select(p => $"{p.Label} ({p.Key}, {p.Type}): {p.Description} " + (p.HasDefault
                ? "Reviewable default: " + p.Default!.ToJsonString() + "." : "Mandatory when this control needs it; no default."))
            .ToList();
        if (control.RepeatFor == "officeLocations") inputs.Add("Office list: stable unique key, mandatory office name and at least one public IPv4/IPv6 CIDR for each office. Select and review each instance separately; locations remain untrusted.");
        if (control.Collection == "conditionalAccess") inputs.Add("Resolve two distinct emergency accounts, the current operator and the approved user exclusion group. Preserve every applicable user, group and location exclusion; never infer IDs from names.");
        if (control.Id == "PRE-011") inputs.Add("The exact returned ID of the new empty device group, and the existing Intune Provisioning Client service principal resolved by its documented Microsoft application ID. Owner assignment is a separate reviewed write.");
        if (inputs.Count == 0) inputs.Add("No catalogue parameter. The engineer must still confirm client approval, eligible users/devices, dependencies and any scope described below.");
        return inputs;
    }

    private static IEnumerable<(string Label, string Text)> Fields(ControlDefinition c, StandardCatalogue standard)
    {
        yield return ("What it does", c.DesiredState);
        yield return ("Why", c.Purpose);
        yield return ("Who it applies to", c.ExpectedProduction.Assignment + ". " + c.ExpectedProduction.Notes);
        yield return ("Intended production state", c.ExpectedProduction.State);
        yield return ("Candidate or manual delivery", BuildStandardDocument.Delivery(c) + " " + c.SafeDeployment.State + "; " + c.SafeDeployment.Assignment + ". " + c.SafeDeployment.Notes);
        yield return ("Licence and edition", (c.Licence.ServicePlans.Count > 0 ? string.Join(", ", c.Licence.ServicePlans) + ". " : "No service-plan code is asserted by the catalogue. ") + c.Licence.Note);
        yield return ("Client must supply or confirm", string.Join("\n", Inputs(c, standard)));
        if (c.Dependencies.Count > 0) yield return ("Dependencies", string.Join(", ", c.Dependencies));
        if (c.Prerequisites is { Count: > 0 }) yield return ("Prerequisites", string.Join("\n", c.Prerequisites.Select(p => p.Title + ": " + p.Details)));
        yield return ("Limitations and source notes", c.DocumentationNotes);
        if (c.Collection is { } key && standard.FindCollection(key) is { } def)
            yield return ("Documented Graph interface", def.Api + " " + def.Path + "; delegated read: " + def.Scope + (def.Writable ? "; separately consented write: " + def.Write : "; no write permission declared here"));
    }

    public static string Markdown(StandardCatalogue standard, EngineerDocumentKind kind)
    {
        Validate(standard, kind);
        var text = new StringBuilder().Append("# ").Append(Title(kind)).Append("\n\n");
        text.Append("Release **").Append(M(standard.Release)).Append("** · ").Append(standard.Controls.Count).Append(" controls · catalogue SHA-256 ").Append(M(standard.IntegrityDigest)).Append("\n\n");
        text.Append(M(Intro)).Append("\n\n");
        if (kind == EngineerDocumentKind.ManualGuide)
        {
            text.Append("## Before using any reference command\n\n").Append(M(ManualSafety)).Append("\n\n");
            text.Append("### Delegated Microsoft Graph preflight\n\n").Append(Fence(GraphPreflight, "powershell"));
            text.Append("### Delegated Exchange preflight\n\n").Append(Fence(ExchangeProposal.DelegatedPreflight(), "powershell"));
            text.Append("Reference module documentation: [Graph context](").Append(GraphContextSource).Append("), [Graph requests](").Append(GraphRequestSource).Append("), [PowerShell JSON conversion](").Append(JsonSource).Append(").\n\n");
        }
        foreach (var area in ControlAreas.Names)
        {
            text.Append("## ").Append(area).Append("\n\n");
            foreach (var c in standard.Controls.Where(c => ControlAreas.For(c) == area))
            {
                text.Append("### ").Append(M(c.Id + " - " + c.Name)).Append("\n\n");
                foreach (var (label, value) in Fields(c, standard)) text.Append("**").Append(label).Append(":** ").Append(M(value)).Append("\n\n");
                text.Append("**Settings and values:**\n\n");
                text.Append(c.Payload is { } payload ? Fence(payload.ToJsonString(ToolkitJson.Options), "json") : M(c.DesiredState) + "\n\n");
                if (kind == EngineerDocumentKind.ManualGuide)
                {
                    var m = c.Implementation!;
                    Steps(text, "Before running the automation", m.Before);
                    Steps(text, "By hand - portal", m.PortalSteps);
                    text.Append("#### By hand - PowerShell\n\n").Append(Fence(m.PowerShell, "powershell"));
                    Steps(text, "After the automation - verification and pass criteria", m.After);
                }
                if (SafeLink(c.References.Microsoft) is { } link) text.Append("[Microsoft documentation](").Append(link.Replace(")", "%29", StringComparison.Ordinal)).Append(")\n\n");
            }
        }
        text.Append("## Optional branding\n\n").Append(M(Branding)).Append('\n');
        return text.ToString();
    }

    public static string Html(StandardCatalogue standard, EngineerDocumentKind kind)
    {
        Validate(standard, kind);
        var text = new StringBuilder("<!doctype html><html lang=\"en-GB\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>");
        text.Append(H(Title(kind))).Append("</title>").Append(Css).Append("</head><body><header><p class=\"eyebrow\">M365 BUILDSTANDARD · ENGINEER REFERENCE</p><h1>")
            .Append(H(Title(kind))).Append("</h1><p>Release ").Append(H(standard.Release)).Append(" · ").Append(standard.Controls.Count)
            .Append(" controls</p><p>Catalogue SHA-256: <code>").Append(H(standard.IntegrityDigest)).Append("</code></p><p>").Append(H(Intro)).Append("</p></header>");
        text.Append("<nav aria-label=\"Document areas\">");
        foreach (var area in ControlAreas.Names) text.Append("<a href=\"#").Append(area).Append("\">").Append(area).Append("</a> ");
        text.Append("</nav><main>");
        if (kind == EngineerDocumentKind.ManualGuide)
        {
            text.Append("<h2>Before using any reference command</h2><p>").Append(H(ManualSafety)).Append("</p><h3>Delegated Microsoft Graph preflight</h3>");
            Code(text, GraphPreflight);
            text.Append("<h3>Delegated Exchange preflight</h3>"); Code(text, ExchangeProposal.DelegatedPreflight());
            text.Append("<p>Reference module documentation: <a href=\"").Append(GraphContextSource).Append("\">Graph context</a>, <a href=\"").Append(GraphRequestSource).Append("\">Graph requests</a>, <a href=\"").Append(JsonSource).Append("\">PowerShell JSON conversion</a>.</p>");
        }
        foreach (var area in ControlAreas.Names)
        {
            text.Append("<section aria-labelledby=\"").Append(area).Append("\"><h2 id=\"").Append(area).Append("\">").Append(area).Append("</h2>");
            foreach (var c in standard.Controls.Where(c => ControlAreas.For(c) == area))
            {
                text.Append("<article id=\"").Append(H(c.Id)).Append("\"><h3>").Append(H(c.Id + " - " + c.Name)).Append("</h3><dl>");
                foreach (var (label, value) in Fields(c, standard)) text.Append("<dt>").Append(H(label)).Append("</dt><dd>").Append(H(value)).Append("</dd>");
                text.Append("</dl><h4>Settings and values</h4>");
                if (c.Payload is { } payload) Code(text, payload.ToJsonString(ToolkitJson.Options));
                else text.Append("<p>").Append(H(c.DesiredState)).Append("</p>");
                if (kind == EngineerDocumentKind.ManualGuide)
                {
                    var m = c.Implementation!;
                    HtmlSteps(text, "Before running the automation", m.Before);
                    HtmlSteps(text, "By hand - portal", m.PortalSteps);
                    text.Append("<h4>By hand - PowerShell</h4>"); Code(text, m.PowerShell);
                    HtmlSteps(text, "After the automation - verification and pass criteria", m.After);
                }
                if (SafeLink(c.References.Microsoft) is { } link) text.Append("<p><a href=\"").Append(H(link)).Append("\">Microsoft documentation</a></p>");
                text.Append("</article>");
            }
            text.Append("</section>");
        }
        text.Append("<h2>Optional branding</h2><p>").Append(H(Branding)).Append("</p></main><footer>Generated only from the loaded catalogue. Observed tenant and deployment evidence belongs in separate reports.</footer></body></html>");
        return text.ToString();
    }

    private static void Steps(StringBuilder text, string title, IEnumerable<string> steps)
    {
        text.Append("#### ").Append(title).Append("\n\n"); var index = 1;
        foreach (var step in steps) text.Append(index++).Append(". ").Append(M(step)).Append('\n');
        text.Append('\n');
    }
    private static void HtmlSteps(StringBuilder text, string title, IEnumerable<string> steps)
    { text.Append("<h4>").Append(title).Append("</h4><ol>"); foreach (var step in steps) text.Append("<li>").Append(H(step)).Append("</li>"); text.Append("</ol>"); }
    private static void Code(StringBuilder text, string value) => text.Append("<pre><code>").Append(H(value)).Append("</code></pre>");
    private static string H(string? text) => WebUtility.HtmlEncode(text ?? "");
    private static string M(string? text) => (text ?? "").Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
    private static string Fence(string text, string language)
    {
        // Catalogue text cannot close the fence and introduce executable HTML into the rendered document.
        var length = Math.Max(3, System.Text.RegularExpressions.Regex.Matches(text, "`+").Select(m => m.Length).DefaultIfEmpty().Max() + 1);
        var fence = new string('`', length); return fence + language + "\n" + text + "\n" + fence + "\n\n";
    }
    private static string? SafeLink(string text) => Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;

    private const string Intro = "This engineer reference states the standard, not a tenant's observed state. It contains catalogue settings and named client inputs only; no client profile, tenant identifiers, connection data or evidence is used. Candidate creation does not prove activation, assignment, installation, licence effect or device behaviour. Microsoft behaviour is documented but remains unverified until the engineer performs the stated checks.";
    private const string ManualSafety = "Use only supported, already installed Microsoft modules and a delegated account with the listed least-privilege scopes/RBAC. Never add secrets, certificates, app-only authentication, module installation or an execution-policy bypass to these references. Review the current Microsoft source. Resolve every placeholder locally; never commit client values. Take and retain a complete before capture, resolve exact IDs, approve each selected change and record durable intent before its request. Type and verify the tenant ID before every write. Preserve operator/emergency exclusions and all unrelated settings. Keep new CA disabled, Intune objects unassigned, groups empty and locations untrusted. Record returned IDs rather than adopting objects by name. After an uncertain response stop and reconcile by reading; do not retry automatically. Reference commands are not a transaction engine and do not override these prerequisites. Graph/Exchange module retries and actual service behaviour remain unverified; use the toolkit's reviewed workflow where supported. Activation, assignment and production validation are separate steps. Portal navigation can change; use the linked Microsoft documentation when labels differ.";
    private const string Branding = "Discuss company branding, sign-in presentation and support information with the client as an optional design choice. Branding is a suggestion, not a control or an automated deployment. Remote Help, Quick Assist, Wi-Fi profiles, Universal Print, Reset this PC changes, DLP and retention-policy writes are outside this standard release.";
    private const string GraphContextSource = "https://learn.microsoft.com/en-us/powershell/module/microsoft.graph.authentication/get-mgcontext?view=graph-powershell-1.0";
    private const string GraphRequestSource = "https://learn.microsoft.com/en-us/powershell/module/microsoft.graph.authentication/invoke-mggraphrequest?view=graph-powershell-1.0";
    private const string JsonSource = "https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/convertfrom-json?view=powershell-7.5";
    public const string GraphPreflight = """
        #requires -Version 7.4
        # Manual reference only; use a fresh PowerShell session and the scopes listed for the selected control.
        $expectedTenant = (Read-Host 'Reviewed tenant ID').Trim()
        $parsedTenant = [guid]::Empty
        if (-not [guid]::TryParse($expectedTenant, [ref]$parsedTenant)) { throw 'A full tenant ID is required.' }
        # Connect-MgGraph -TenantId $expectedTenant -Scopes <selected-control-scopes> -ContextScope Process
        function Assert-ManualGraphTenant {
            $context = Get-MgContext
            if ($null -eq $context -or $context.AuthType -ne 'Delegated' -or $context.TenantId -ine $expectedTenant) { throw 'Wrong tenant or authentication type.' }
            $typed = Read-Host 'Type the full tenant ID for this one change'
            if ([string]::IsNullOrWhiteSpace($typed) -or $typed.Trim() -ine $expectedTenant.Trim()) { throw 'Tenant confirmation did not match.' }
            # The engineer must have saved complete before evidence and durable intent for this exact request.
            # Stop on save failure or changed settings; this function is not a deployment transaction.
        }
        """;
    private const string Css = """
        <style>
        :root{color-scheme:light}*{box-sizing:border-box}body{font:16px/1.6 "Segoe UI",system-ui,sans-serif;color:#172d43;max-width:1100px;margin:auto;padding:36px 24px;background:#fff}
        h1{font-size:34px;line-height:1.2}h2{margin-top:36px;border-bottom:2px solid #1767a8;padding-bottom:8px}h3{font-size:22px;line-height:1.3}h4{font-size:17px;margin-bottom:8px}
        .eyebrow{font-size:12px;letter-spacing:.08em;color:#345570}nav{padding:16px;background:#edf4fa;display:flex;gap:24px;flex-wrap:wrap}a{color:#075b9c}a:focus-visible{outline:3px solid #1767a8;outline-offset:4px}
        article{border:1px solid #bdcbd7;border-radius:6px;padding:22px;margin:24px 0}dt{font-weight:700;margin-top:12px}dd{margin:0;white-space:pre-line;overflow-wrap:anywhere}
        pre{padding:16px;background:#f1f5f8;border-left:4px solid #1767a8;white-space:pre-wrap;overflow-wrap:anywhere;font:13px/1.5 Consolas,monospace}li{margin:8px 0}footer{border-top:1px solid #bdcbd7;margin-top:32px;padding-top:20px;font-size:13px}
        @media print{body{max-width:none;padding:0}nav{display:none}h2,h3,h4{break-after:avoid}pre,li{break-inside:avoid}article{border:0;padding:0}}
        </style>
        """;
}
