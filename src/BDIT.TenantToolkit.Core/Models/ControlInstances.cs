using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Safety;

namespace BDIT.TenantToolkit.Core.Models;

/// <summary>Profile-bound instances retain exact ownership identities when offices are reordered or renamed.</summary>
public static class ControlInstances
{
    public static IReadOnlyList<ControlDefinition> All(StandardCatalogue standard, TenantProfile? profile)
    {
        var result = new List<ControlDefinition>();
        var locations = OfficeLocationValidator.Validate(profile?.Parameters.OfficeLocations);
        foreach (var control in standard.Controls)
        {
            if (control.RepeatFor != "officeLocations" || locations is not { Count: > 0 }) { result.Add(control); continue; }
            foreach (var location in locations)
            {
                var instance = ToolkitJson.Deserialize<ControlDefinition>(ToolkitJson.Serialize(control));
                instance.Id = control.Id + "-" + location.Key;
                instance.Name = "Office location: " + location.Name;
                instance.Payload!["displayName"] = "LOC - " + location.Name;
                instance.Payload["ipRanges"] = new JsonArray(location.IpRanges.Select(cidr => (JsonNode)new JsonObject
                {
                    ["@odata.type"] = cidr.Contains(':') ? "#microsoft.graph.iPv6CidrRange" : "#microsoft.graph.iPv4CidrRange",
                    ["cidrAddress"] = cidr
                }).ToArray());
                result.Add(instance);
            }
        }
        return result;
    }

    public static ControlDefinition? Find(StandardCatalogue standard, TenantProfile? profile, string id) =>
        All(standard, profile).FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
}
