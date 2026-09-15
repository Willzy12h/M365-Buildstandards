using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Planning;

/// <summary>
/// Supplies the shipped default for an input the client profile does not carry, so a recipe whose only missing value
/// is a reviewable setting still produces a candidate instead of a blocked row.
///
/// The trade is deliberate. A created candidate is inert: a Conditional Access policy is disabled, an Intune object
/// unassigned. Blocking the whole control teaches an engineer to skip it; creating it with a visible warning keeps the
/// work moving and leaves the decision in front of them at the moment it matters, which is assignment.
///
/// Identity inputs never default. Emergency accounts, the office location and targeting groups decide who a policy
/// applies to and who is exempt from it, and a plausible-looking wrong answer there is how a tenant locks itself out.
/// Those still block, and the standard expresses that simply by declaring no default for them.
/// </summary>
public static class PolicyInputDefaults
{
    public sealed record Applied(IReadOnlyDictionary<string, JsonNode?> Values, IReadOnlyList<string> Warnings);

    public static Applied Apply(JsonObject payload, StandardCatalogue standard, IReadOnlyDictionary<string, JsonNode?> values, DateTimeOffset now)
    {
        var json = payload.ToJsonString();
        Dictionary<string, JsonNode?>? merged = null;
        var warnings = new List<string>();

        foreach (var parameter in standard.Parameters)
        {
            if (!parameter.HasDefault) continue;
            if (!json.Contains("{{" + parameter.Key + "}}", StringComparison.Ordinal)) continue;
            if (values.TryGetValue(parameter.Key, out var supplied) && supplied is not null) continue;

            merged ??= new Dictionary<string, JsonNode?>(values, StringComparer.Ordinal);
            merged[parameter.Key] = parameter.Default!.DeepClone();
            warnings.Add(Describe(parameter, now));
        }

        return new Applied(merged ?? values, warnings);
    }

    private static string Describe(ParameterDefinition parameter, DateTimeOffset now)
    {
        var value = parameter.Default!.ToJsonString();
        var reviewed = string.IsNullOrWhiteSpace(parameter.ReviewedOn) ? "an unrecorded date" : parameter.ReviewedOn;
        var stale = parameter.IsStale(now)
            ? " That was more than 90 days ago, so check the vendor's currently supported releases before you rely on it."
            : "";
        return $"No client value for {parameter.Label}; the candidate uses the shipped default {value}, reviewed {reviewed}."
             + stale
             + " Confirm it with the client and update the policy before assigning or enabling it.";
    }
}
