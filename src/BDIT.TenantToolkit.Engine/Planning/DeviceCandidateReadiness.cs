using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Assessment;

namespace BDIT.TenantToolkit.Engine.Planning;

public static class DeviceCandidateReadiness
{
    public static string? Problem(JsonObject payload, StandardCatalogue standard, TenantSnapshot snapshot)
    {
        if (payload["@odata.type"]?.ToString() == "#microsoft.graph.androidManagedStoreApp"
            && (!snapshot.Collections.TryGetValue("googlePlay", out var play) || !play.Usable || play.Items.Count != 1
                || play.Items[0]["bindStatus"]?.ToString() != "boundAndValidated"))
            return "Managed Google Play must be bound and validated (ENR-006), with complete captured evidence, before creating Android store candidates.";
        if (payload["omaSettings"] is JsonArray settings && settings.OfType<JsonObject>().Any(s =>
            s["omaUri"]?.ToString() == "./Device/Vendor/MSFT/LAPS/Policies/BackupDirectory" && s["value"]?.ToString() == "1"))
        {
            var key = standard.Collections.FirstOrDefault(c => c.Value.ApiVersion == GraphApi.V1 && c.Value.BasePath == EntraLapsSafety.Path).Key;
            if (key is null || !snapshot.Collections.TryGetValue(key, out var capture) || !capture.Usable || capture.Items.Count != 1)
                return "Capture the Entra LAPS tenant prerequisite before creating the Windows LAPS candidate.";
            try
            {
                if (!EntraLapsSafety.IsEnabled(capture.Items[0])) return "Entra LAPS is disabled. Review and enable the tenant prerequisite, then capture a fresh snapshot.";
            }
            catch (ToolkitException) { return "Entra LAPS prerequisite evidence is incomplete or unsupported. Capture and review it again."; }
        }
        if (payload["@odata.type"]?.ToString() == "#microsoft.graph.windowsDefenderAdvancedThreatProtectionConfiguration")
        {
            var licence = LicenceEvaluator.FromSnapshot(snapshot);
            if (!licence.Available || (!licence.Has("MDE_SMB") && !licence.Has("WINDEFATP")))
                return "Defender EDR needs a provisioned Defender for Business (MDE_SMB) or Defender for Endpoint P2 (WINDEFATP) service plan. Intune or Endpoint P1 alone does not confirm EDR entitlement.";
        }
        return null;
    }
}
