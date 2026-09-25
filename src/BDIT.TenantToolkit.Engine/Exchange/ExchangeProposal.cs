using System.Text;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Evidence;

namespace BDIT.TenantToolkit.Engine.Exchange;

/// <summary>Produces an inert, selected engineer proposal. There is deliberately no execution API.</summary>
public static class ExchangeProposal
{
    public const string Refusal = "throw 'Review document only. This file cannot execute Exchange or Purview changes.'";
    private static readonly IReadOnlyDictionary<string, string[]> Required = new Dictionary<string, string[]>
    {
        ["EX-001"] = new[] { "acceptedDomains", "transportRules" },
        ["EX-002"] = new[] { "presetEop", "presetAtp" },
        ["EX-003"] = new[] { "outboundPolicies" }, ["EX-004"] = new[] { "transportConfig" },
        ["EX-005"] = new[] { "externalTags" }, ["EX-006"] = new[] { "organisation" },
        ["EX-007"] = new[] { "acceptedDomains", "dkim" }, ["EX-008"] = new[] { "acceptedDomains" },
        ["PUR-001"] = new[] { "auditConfig" }, ["PUR-002"] = new[] { "auditRetention" }
    };

    public static string Create(StandardCatalogue standard, TenantProfile profile, TenantSnapshot snapshot, EvidenceStore evidence,
        string controlId, string typedTenant, string enteredDomain, DateTimeOffset now)
    {
        if (!TenantConfirmation.Matches(typedTenant, profile.TenantId)) throw new PlanValidationException("Type the selected client's tenant ID before exporting its proposal.");
        if (snapshot.TenantId != profile.TenantId) throw new TenantMismatchException("Select evidence for this client.");
        var domain = MailDomain.Validate(enteredDomain);
        var control = standard.FindControl(controlId);
        if (standard.SchemaVersion < 5 || control is null || !Required.TryGetValue(control.Id, out var required)
            || control.Implementation is not { Before.Count: > 0, PortalSteps.Count: > 0, After.Count: > 0 } instructions
            || string.IsNullOrWhiteSpace(instructions.PowerShell))
            throw new ConfigurationException("Select one Exchange or Purview control with complete manual instructions.");
        var stored = evidence.LoadSnapshot(profile.TenantId, snapshot.Id) ?? throw new PlanValidationException("Save the imported capture before exporting a proposal.");
        if (!evidence.SnapshotIntegrityIntact(stored) || !evidence.SnapshotIntegrityIntact(snapshot)
            || EvidenceIntegrity.Compute(stored) != EvidenceIntegrity.Compute(snapshot))
            throw new PlanValidationException("The imported evidence changed or failed its integrity check. Import and review a fresh capture.");
        var capture = stored.ExchangeCapture ?? throw new ConfigurationException("Import an Exchange capture first.");
        ExchangeCaptureSchema.Validate(capture, profile.TenantId, now);
        if (capture.Domain != domain) throw new ConfigurationException("The proposal domain differs from the imported capture.");
        if (!Timestamps.TryParse(capture.CapturedAt, out var at) || now - at > TimeSpan.FromDays(1))
            throw new PlanValidationException("Import a fresh capture before exporting a current proposal.");
        foreach (var key in required)
            if (!ExchangeCaptureSchema.Complete(capture, key, out _)) throw new PlanValidationException($"Complete {ExchangeCaptureSchema.Definitions[key].Command} observations are required; unknown is not missing.");
        if (required.Contains("acceptedDomains") && !capture.Collections["acceptedDomains"].Items.Any(d => string.Equals(d["DomainName"]?.ToString(), domain, StringComparison.OrdinalIgnoreCase)))
            throw new PlanValidationException("The entered domain must be present in the captured accepted domains.");

        var builder = new StringBuilder().AppendLine(Refusal);
        void Comment(string text)
        {
            foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
                builder.Append("# ").AppendLine(line);
        }
        Comment($"REVIEW PROPOSAL: {control.Id} - {control.Name}\nStandard {standard.Release}; generated {Timestamps.Format(now)}");
        Comment($"Tenant {profile.TenantId}; explicitly entered domain {domain}.\nBefore snapshot {snapshot.Id}; captured {capture.CapturedAt}; digest {snapshot.IntegrityDigest}.");
        Comment("Every instruction below is commented deliberately. The toolkit does not execute it. Imported provenance and actual Microsoft behaviour are unverified. Review the local artefact as confidential client evidence; do not put it in source control.");
        Comment("Before any manual change: obtain the client's approval for the exact settings/targets; save a complete fresh before capture; compare it with this proposal and stop on drift. Record intent durably before each request. Recheck the connected tenant and type its ID before each write. Preserve all unrelated settings. After a timeout or uncertain response, stop and reconcile by reading exact objects; never repeat a write merely because it reported an error. The Microsoft module can retry internally, so this document cannot provide the toolkit's single-attempt guarantee.");
        Comment("BEFORE RUNNING THE AUTOMATION (no Exchange automation is run)");
        foreach (var step in instructions.Before) Comment(step);
        foreach (var key in required)
        {
            Comment("Observed " + ExchangeCaptureSchema.Definitions[key].Command + ":");
            foreach (var item in capture.Collections[key].Items) Comment(item.ToJsonString());
        }
        Comment("BY HAND - PORTAL");
        foreach (var step in instructions.PortalSteps) Comment(step);
        Comment("BY HAND - POWERSHELL REFERENCE (copy only separately reviewed steps)");
        Comment(DelegatedPreflight(profile.TenantId, domain));
        if (control.Id == "EX-007")
        {
            var configs = capture.Collections["dkim"].Items.Where(d => string.Equals(d["Name"]?.ToString(), domain, StringComparison.OrdinalIgnoreCase)).ToList();
            if (configs.Count == 0)
                Comment("# No configuration returned. Propose disabled creation only, then capture the actual CNAME targets.\nAssert-ManualTenant\nNew-DkimSigningConfig -DomainName $domain -Enabled $false -ErrorAction Stop\nGet-DkimSigningConfig -Identity $domain | Format-List Name,Enabled,Status,Selector1CNAME,Selector2CNAME");
            else if (configs.Count != 1 || !ExchangeAssessment.DkimDnsReady(capture, configs[0], now))
                Comment("BLOCKED: both fresh CNAME answers must match a single captured configuration. Publish its actual targets and repeat capture/DNS checks. No signing-enable command is included.");
            else
            {
                foreach (var answer in capture.Dns.Where(d => d.Kind == DnsRecordKind.Cname))
                    Comment($"Observed DNS {answer.Name}: {string.Join(", ", answer.Records)} at {answer.QueriedAt}.");
                Comment(instructions.PowerShell);
            }
        }
        else Comment(instructions.PowerShell);
        Comment("AFTER THE AUTOMATION / MANUAL CHANGE - NOT YET OBSERVED");
        foreach (var step in instructions.After) Comment(step);
        Comment("Run and import a new read-only capture, record each action's observed or unknown outcome, and complete the stated service/device checks. Exporting this proposal neither performs nor verifies a change.");
        Comment("Microsoft source: " + control.References.Microsoft);
        return builder.ToString();
    }

    public static string DelegatedPreflight(string tenant = "<tenant-id>", string domain = "<accepted-mail-domain>") => $$"""
        # Start a fresh session with a supported, installed ExchangeOnlineManagement module.
        # Connect interactively using delegated RBAC, no certificates, secrets or application-only route.
        # Import only the cmdlets needed for the selected steps with Connect-ExchangeOnline -CommandName.
        # Do not copy the following function without reviewing and preserving its tenant check.
        $expectedTenant = '{{tenant}}'
        $domain = '{{domain}}'
        function Assert-ManualTenant {
            $connections = @(Get-ConnectionInformation | Where-Object { $_.State -eq 'Connected' -and -not $_.IsEopSession })
            if ($connections.Count -ne 1 -or $connections[0].TenantID.ToString() -ine $expectedTenant) { throw 'Wrong or ambiguous Exchange tenant.' }
            $typed = Read-Host 'Type the full tenant ID for this one change'
            if ([string]::IsNullOrWhiteSpace($typed) -or $typed.Trim() -ine $expectedTenant.Trim()) { throw 'Tenant confirmation did not match.' }
            # Save the reviewed before state and durable intent before the immediately following request.
            # Stop if either cannot be saved. This function alone is not a deployment transaction.
        }
        """;
}
