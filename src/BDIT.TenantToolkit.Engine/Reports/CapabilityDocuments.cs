using System.Text;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
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
            .Append("| Control | Read assessment | Candidate recipe | Activation / assignment | Effective protection | Recovery transport | Acceptance |\n|---|---|---|---|---|---|---|\n");
        foreach (var c in standard.Controls)
        {
            var def = standard.FindCollection(c.Collection);
            var recipe = c.HasRecipe && def?.Writable == true;
            var recovery = def?.Writable == true && RecoverySafety.Supports(def.ApiVersion, def.BasePath);
            text.Append('|').Append(M(c.Id + " — " + c.Name)).Append('|').Append(M(c.Assessment.Mode.ToString()))
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
    private static string M(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
}
