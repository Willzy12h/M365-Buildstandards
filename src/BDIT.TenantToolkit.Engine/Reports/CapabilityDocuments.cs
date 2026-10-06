using System.Text;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Setup;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>Catalogue-derived implementation scope. A recipe or route is never a claim of live acceptance.</summary>
public static class CapabilityDocuments
{
    public static string Markdown(StandardCatalogue standard)
    {
        var text = new StringBuilder("# Capability and acceptance scope\n\n");
        text.Append("Standard ").Append(M(standard.Release)).Append(" · SHA-256 ").Append(standard.IntegrityDigest).Append("\n\n")
            .Append("This generated inventory describes available catalogue/transport paths. All live service, pilot effectiveness and recovery acceptance is **Not run** for this product release; no row is a production support certification. Candidate eligibility still depends on complete evidence, licences, reviewed inputs, ownership and exact preview. Unknown or unsupported cases stop for engineer review.\n\n")
            .Append("| Control | Read assessment | Evidence read | Licence (service plans) | Candidate recipe | Activation / assignment | Effective protection | Recovery transport | Acceptance |\n|---|---|---|---|---|---|---|---|---|\n");
        foreach (var c in standard.Controls)
        {
            var def = standard.FindCollection(c.Collection);
            var recipe = c.HasRecipe && def?.Writable == true;
            var recovery = def?.Writable == true && RecoverySafety.Supports(def.ApiVersion, def.BasePath);
            text.Append('|').Append(M(c.Id + " — " + c.Name)).Append('|').Append(ReadRoute(standard, c))
                .Append('|').Append(M(EvidenceRead(standard, c)))
                .Append('|').Append(M(c.Licence.ServicePlans.Count == 0 ? "None recorded" : string.Join(", ", c.Licence.ServicePlans)))
                .Append('|').Append(recipe ? "Preview recipe; inert candidate" : "Engineer/manual; no candidate recipe")
                .Append('|').Append(recipe ? "Separate scope review; adapter eligibility checked at preview" : "Engineer/manual procedure")
                .Append("|Engineer/device/sign-in verification required|").Append(recovery ? "Owned object only; exact recovery preview required" : "No generic recovery route; engineer escalation")
                .Append("|Not run live|\n");
        }
        text.Append("\n## Connection and application permissions\n\nDerived from the actual connection/setup scope definitions and this catalogue. Tenant role assignments, admin consent, resource grants and successful reads are checked separately; a declared scope does not prove effective access.\n\n")
            .Append("Assessment: ").Append(M(string.Join(", ", TenantConnectionService.DiagnosticScopes.Concat(standard.ReadScopes()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))))
            .Append("\n\nDeployment additionally: ").Append(M(string.Join(", ", standard.WriteScopes().Order(StringComparer.OrdinalIgnoreCase))))
            .Append("\n\nApplication bootstrap (separate administrator approval): ").Append(M(string.Join(", ", ApplicationSetupService.SetupScopes)))
            .Append("\n\nExchange/Purview uses its own delegated connection, supported module and service RBAC; Graph consent does not grant it. Missing permissions or truncated reads remain unknown. See the packaged Exchange/Purview and application setup guides.\n");
        return text.ToString();
    }
    /// <summary>How the assessment engine reads a control, in the order AssessmentEngine routes it.</summary>
    public enum Route { ExchangeCapture, ObservedEvidence, SettingsComparison, EquivalenceEvidence, ManualOnly }

    /// <summary>
    /// The engine's actual read route for this control. Reporting the catalogue's assessment mode alone labelled
    /// evidence-assessed controls "Manual" (CLA-20261006-02).
    /// </summary>
    public static Route RouteOf(StandardCatalogue standard, ControlDefinition c)
    {
        if (standard.SchemaVersion >= 5 && ExchangeAssessment.ControlIds.Contains(c.Id)) return Route.ExchangeCapture;
        if (standard.SchemaVersion >= 5 && ReleaseIdentityAssessment.ControlIds.Contains(c.Id)) return Route.ObservedEvidence;
        if (standard.FindCollection(c.Collection) is not null && c.Assessment.Mode == AssessmentMode.Settings && c.Payload is not null) return Route.SettingsComparison;
        if (c.Equivalence is { Signals.Count: > 0 }) return Route.EquivalenceEvidence;
        return Route.ManualOnly;
    }

    public static string ReadRoute(StandardCatalogue standard, ControlDefinition c) => RouteOf(standard, c) switch
    {
        Route.ExchangeCapture => "Exchange/Purview read capture (separate connection)",
        Route.ObservedEvidence => "Observed evidence; engineer confirms",
        Route.SettingsComparison => "Settings comparison",
        Route.EquivalenceEvidence => "Equivalence evidence; engineer confirms",
        _ => "Manual only"
    };

    private static string EvidenceRead(StandardCatalogue standard, ControlDefinition c)
    {
        var route = RouteOf(standard, c);
        if (route == Route.ExchangeCapture) return "Exchange Online / Purview cmdlets (service RBAC)";
        // Settings comparison reads the control's own collection; the other routes read the collection the
        // equivalence rule names, defaulting to the control's.
        var key = route == Route.SettingsComparison ? c.Collection : c.Equivalence?.Collection ?? c.Collection;
        var def = standard.FindCollection(key);
        return def is null ? "None" : def.Label + (string.IsNullOrWhiteSpace(def.Scope) ? "" : " (" + def.Scope + ")");
    }

    private static string M(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
}
