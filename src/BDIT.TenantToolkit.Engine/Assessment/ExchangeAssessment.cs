using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Exchange;

namespace BDIT.TenantToolkit.Engine.Assessment;

public static class ExchangeAssessment
{
    public static IReadOnlyList<string> ControlIds { get; } = new[] { "EX-001", "EX-002", "EX-003", "EX-004", "EX-005", "EX-006", "EX-007", "EX-008", "PUR-001", "PUR-002" };

    public static bool Apply(ControlDefinition control, ExchangeCapture? capture, ControlFinding finding, DateTimeOffset now)
    {
        if (!ControlIds.Contains(control.Id)) return false;
        finding.Status = FindingStatus.UnableToAssess;
        finding.Reason = "Import a tenant-bound Exchange/Purview capture. Missing or incomplete observations do not establish missing configuration.";
        if (capture is null) return true;
        finding.Notes.Add($"Engineer-imported observations from {capture.CapturedAt}; module {capture.ModuleVersion}. Source provenance is not independently authenticated. No live tenant check or write was performed by the toolkit.");
        if (Timestamps.TryParse(capture.CapturedAt, out var captured) && now - captured > TimeSpan.FromDays(1))
        { finding.Reason = "The Exchange capture is over 24 hours old; import a fresh capture before assessing current settings."; return true; }
        if (control.Id is "EX-001" or "EX-007" or "EX-008")
        {
            if (!ExchangeCaptureSchema.Complete(capture, "acceptedDomains", out var domains)
                || !domains.Any(d => string.Equals(Text(d, "DomainName"), capture.Domain, StringComparison.OrdinalIgnoreCase)))
            { finding.Reason = "The entered domain is not established as an accepted domain by complete imported evidence."; return true; }
            finding.ObservedObjects.Add("Engineer-entered mail domain: " + capture.Domain);
        }
        switch (control.Id)
        {
            case "PUR-001": BooleanSetting(capture, finding, "auditConfig", "UnifiedAuditLogIngestionEnabled", true,
                "Unified audit ingestion is enabled in the imported configuration. Check subsequent searchable events and licensing; availability can be delayed."); break;
            case "EX-004": BooleanSetting(capture, finding, "transportConfig", "SmtpClientAuthenticationDisabled", true,
                "Organisation SMTP AUTH is disabled in the imported configuration. Mailbox overrides and other authentication controls require separate review."); break;
            case "EX-005": BooleanSetting(capture, finding, "externalTags", "Enabled", true,
                "External tagging is enabled in the imported configuration. Review the preserved allow-list and check supported Outlook clients."); break;
            case "EX-006": BooleanSetting(capture, finding, "organisation", "AuditDisabled", false,
                "The organisation-wide mailbox audit switch is on. Check bypass associations, mailbox/event coverage and searchable results separately."); break;
            case "PUR-002": Retention(capture, finding); break;
            case "EX-001": Bypass(capture, finding); break;
            case "EX-002": Preset(capture, finding); break;
            case "EX-003": Forwarding(capture, finding); break;
            case "EX-007": Dkim(capture, finding, now); break;
            case "EX-008": Dmarc(capture, finding, now); break;
        }
        return true;
    }

    private static void BooleanSetting(ExchangeCapture capture, ControlFinding f, string key, string property, bool expected, string success)
    {
        if (!ExchangeCaptureSchema.Complete(capture, key, out var items) || items.Count != 1) return;
        var actual = items[0][property]!.GetValue<bool>();
        f.Status = actual == expected ? FindingStatus.RequiresManualReview : FindingStatus.PartialMatch;
        f.Reason = actual == expected ? success : $"Imported {property} is {actual}; the standard requires {expected}.";
        foreach (var field in items[0]) f.ObservedObjects.Add(field.Key + " = " + field.Value?.ToJsonString());
    }

    private static void Retention(ExchangeCapture capture, ControlFinding f)
    {
        f.Notes.Add("Microsoft's default audit retention is 180 days. Longer retention needs appropriate per-user entitlement and policy or an external export. No retention or DLP settings are changed.");
        if (!ExchangeCaptureSchema.Complete(capture, "auditRetention", out var items)) return;
        f.Status = FindingStatus.RequiresManualReview;
        f.Reason = $"Imported {items.Count} audit retention policy/policies. Confirm affected-user licences, event coverage and effective retention; an empty list does not prove a tenant-wide retention duration.";
        foreach (var item in items) f.ObservedObjects.Add(item.ToJsonString());
    }

    private static void Bypass(ExchangeCapture capture, ControlFinding f)
    {
        f.Notes.Add("SPF authenticates the envelope sender, not necessarily the From domain. An Authentication-Results text match has an unverified trust/alignment boundary. The toolkit never declares this rule spoof-safe or writes it.");
        if (!ExchangeCaptureSchema.Complete(capture, "transportRules", out var items)) return;
        var candidates = items.Where(i => i["SetSCL"] is JsonValue value && value.TryGetValue<int>(out var scl) && scl == -1).ToList();
        f.Status = candidates.Count == 0 ? FindingStatus.Missing : FindingStatus.RequiresManualReview;
        f.Reason = candidates.Count == 0 ? "No SCL -1 rule was returned by the complete rule read. The narrow own-domain/SPF rule remains a reviewed proposal."
            : "Review every SCL -1 rule below for exact From-domain scope, SPF result trust, state, extra conditions/actions and exceptions. Name matches do not establish ownership or safety.";
        foreach (var candidate in candidates) f.ObservedObjects.Add(candidate.ToJsonString());
    }

    private static void Preset(ExchangeCapture capture, ControlFinding f)
    {
        if (!ExchangeCaptureSchema.Complete(capture, "presetEop", out var eop) || !ExchangeCaptureSchema.Complete(capture, "presetAtp", out var atp)) return;
        var standards = new[] { eop, atp }.Select(list => list.Where(p => Text(p, "Name") == "Standard Preset Security Policy").ToList()).ToList();
        if (standards.Any(s => s.Count > 1)) return;
        if (standards.Any(s => s.Count == 0)) { f.Status = FindingStatus.Missing; f.Reason = "The complete reads did not return both Standard preset rule components. Check entitlement and configure the supported preset in Defender."; return; }
        var scopeFields = new[] { "SentTo", "SentToMemberOf", "RecipientDomainIs", "ExceptIfSentTo", "ExceptIfSentToMemberOf", "ExceptIfRecipientDomainIs" };
        var allRecipients = standards.All(s => Text(s[0], "State") == "Enabled" && scopeFields.All(key => s[0][key]!.AsArray().Count == 0));
        f.Status = allRecipients ? FindingStatus.RequiresManualReview : FindingStatus.PartialMatch;
        f.Reason = allRecipients ? "Both imported Standard preset components are enabled without recipient conditions or exceptions. Confirm licensing, Strict preset precedence and manually maintain impersonation users-to-protect."
            : "At least one Standard preset component is disabled, restricted or has exceptions; the intended all-recipient scope is not established.";
        foreach (var rule in eop.Concat(atp)) f.ObservedObjects.Add(rule.ToJsonString());
    }

    private static void Forwarding(ExchangeCapture capture, ControlFinding f)
    {
        if (!ExchangeCaptureSchema.Complete(capture, "outboundPolicies", out var policies) || policies.Count == 0
            || policies.Count(p => p["IsDefault"]!.GetValue<bool>()) != 1
            || policies.Select(p => Text(p, "Identity")).Distinct(StringComparer.OrdinalIgnoreCase).Count() != policies.Count) return;
        var blocked = policies.All(p => Text(p, "AutoForwardingMode") == "Off");
        f.Status = blocked ? FindingStatus.RequiresManualReview : FindingStatus.PartialMatch;
        f.Reason = blocked ? "All imported outbound spam policies, including Default, explicitly block automatic external forwarding. Verify effective mail flow and intended exceptions manually."
            : "One or more outbound spam policies are not explicitly Off. Changing Default alone cannot establish protection for every recipient.";
        foreach (var p in policies) f.ObservedObjects.Add(p.ToJsonString());
    }

    public static bool DkimDnsReady(ExchangeCapture capture, JsonObject config, DateTimeOffset now) =>
        Enumerable.Range(1, 2).All(i =>
        {
            var expected = Text(config, $"Selector{i}CNAME");
            var answer = Dns(capture, $"selector{i}._domainkey.{capture.Domain}", DnsRecordKind.Cname, now);
            return !string.IsNullOrWhiteSpace(expected) && answer is { Status: DnsResultStatus.Present, Records.Count: 1 }
                && string.Equals(answer.Records[0].TrimEnd('.'), expected.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
        });

    private static void Dkim(ExchangeCapture capture, ControlFinding f, DateTimeOffset now)
    {
        if (!ExchangeCaptureSchema.Complete(capture, "dkim", out var all)) return;
        var matches = all.Where(i => string.Equals(Text(i, "Name"), capture.Domain, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0) { f.Status = FindingStatus.Missing; f.Reason = "No DKIM configuration for the accepted domain was returned. An engineer may create a disabled configuration first and capture its actual CNAME targets."; return; }
        if (matches.Count != 1) return;
        var config = matches[0];
        for (var i = 1; i <= 2; i++) f.ObservedObjects.Add($"Publish CNAME selector{i}._domainkey.{capture.Domain} -> {Text(config, $"Selector{i}CNAME")}");
        f.ObservedObjects.Add($"Imported Enabled={config["Enabled"]}; Status={Text(config, "Status")}");
        if (!DkimDnsReady(capture, config, now))
        { f.Reason = "Both fresh CNAME answers must match the actual captured targets. Signing readiness is unverified; do not enable DKIM yet."; return; }
        f.Status = config["Enabled"]!.GetValue<bool>() && Text(config, "Status") == "Valid" ? FindingStatus.RequiresManualReview : FindingStatus.PartialMatch;
        f.Reason = "Both CNAMEs match in this resolver's current view. Review the imported signing state and verify DKIM on a new outbound message; DNS alone does not prove signing.";
    }

    private static void Dmarc(ExchangeCapture capture, ControlFinding f, DateTimeOffset now)
    {
        var answer = Dns(capture, "_dmarc." + capture.Domain, DnsRecordKind.Txt, now);
        if (answer is null || answer.Status == DnsResultStatus.Error) return;
        var records = answer.Records.Where(r => r.TrimStart().StartsWith("v=DMARC1", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var record in answer.Records) f.ObservedObjects.Add("TXT _dmarc." + capture.Domain + " = " + record);
        if (records.Count == 0) { f.Status = FindingStatus.Missing; f.Reason = "No DMARC1 TXT record was returned in this resolver's view. This check makes no DNS change."; return; }
        if (records.Count != 1 || !TryDmarcPolicy(records[0], out var policy))
        { f.Reason = "DMARC data is ambiguous or malformed. Multiple records, duplicate tags or an invalid policy cannot establish a valid configuration."; return; }
        f.Status = FindingStatus.RequiresManualReview;
        f.Reason = $"DMARC record found with p={policy}. Review alignment, reporting, subdomain policy and real mail before changing enforcement. The toolkit does not change DMARC.";
    }

    public static bool TryDmarcPolicy(string record, out string policy)
    {
        policy = ""; var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parts = record.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] != "v=DMARC1") return false;
        foreach (var part in parts)
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || pair[0].Length == 0 || pair[1].Length == 0 || !tags.TryAdd(pair[0], pair[1])) return false;
        }
        if (!tags.TryGetValue("p", out var p) || p is not ("none" or "quarantine" or "reject")) return false;
        // These documented policy fields are checked here; this is not an exhaustive DMARC validator.
        if (tags.TryGetValue("pct", out var pct) && (pct.Any(c => c is < '0' or > '9') || !int.TryParse(pct, out var percentage) || percentage > 100)) return false;
        if (tags.TryGetValue("sp", out var sp) && sp is not ("none" or "quarantine" or "reject")) return false;
        foreach (var alignment in new[] { "adkim", "aspf" })
            if (tags.TryGetValue(alignment, out var mode) && mode is not ("r" or "s")) return false;
        policy = p; return true;
    }

    private static DnsObservation? Dns(ExchangeCapture capture, string name, DnsRecordKind kind, DateTimeOffset now)
    {
        var matches = capture.Dns.Where(d => d.Name == name && d.Kind == kind && Timestamps.TryParse(d.QueriedAt, out var at)
            && at <= now.AddMinutes(5) && now - at <= TimeSpan.FromHours(1)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
    private static string Text(JsonObject item, string key) => item[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
}
