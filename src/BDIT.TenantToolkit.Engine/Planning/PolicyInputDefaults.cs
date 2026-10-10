using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
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
        var used = ParameterUsage.Keys(payload);
        Dictionary<string, JsonNode?>? merged = null;
        var warnings = new List<string>();

        foreach (var parameter in standard.Parameters)
        {
            if (!parameter.HasDefault) continue;
            AssertReviewable(parameter);
            if (!used.Contains(parameter.Key)) continue;
            if (values.TryGetValue(parameter.Key, out var supplied) && supplied is not null) continue;

            merged ??= new Dictionary<string, JsonNode?>(values, StringComparer.Ordinal);
            merged[parameter.Key] = parameter.Default!.DeepClone();
            warnings.Add(Describe(parameter, now));
        }

        return new Applied(merged ?? values, warnings);
    }

    /// <summary>Resolve reviewed recipe inputs, allowing only the optional ESP blocking-application list to be empty.</summary>
    public static JsonNode? Resolve(JsonObject payload, StandardCatalogue standard, IReadOnlyDictionary<string, JsonNode?> values)
    {
        PolicyInputValidator.ValidateUsed(payload, standard, values);
        return CanonicalJson.Resolve(payload, values, AllowedEmptyArrays(standard));
    }

    private static IReadOnlySet<string> AllowedEmptyArrays(StandardCatalogue standard) => standard.Parameters
        .Where(p => p.Key == "espBlockingAppIds" && p.Type == "guidList" && !p.Required)
        .Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Confirming a client input is not enough if the inert candidate still carries its earlier default.
    /// Compare only recipe values containing default-capable inputs, leaving injected safety exclusions intact.
    /// The existing live-drift check separately proves the candidate still matches this recorded payload.
    /// </summary>
    public static void AssertCandidateUsesConfirmedInputs(JsonObject payload, StandardCatalogue standard,
        IReadOnlyDictionary<string, JsonNode?> values, JsonObject? lastApplied, DateTimeOffset now)
    {
        var applied = Apply(payload, standard, values, now);
        if (applied.Warnings.Count > 0)
            throw new SafetyViolationException("Confirm and save the client values before activation or assignment: " + string.Join(" ", applied.Warnings));

        PolicyInputValidator.ValidateUsed(payload, standard, values);
        var parameters = standard.Parameters.Where(p => p.HasDefault).ToArray();
        var allowedEmptyArrays = AllowedEmptyArrays(standard);
        Check(payload, lastApplied);

        void Check(JsonNode? template, JsonNode? recorded)
        {
            if (template is JsonValue value && value.TryGetValue<string>(out var text))
            {
                var referenced = ParameterUsage.Keys(value);
                var used = parameters.Where(p => referenced.Contains(p.Key)).ToArray();
                if (used.Length == 0) return;
                var confirmed = CanonicalJson.Resolve(template, values, allowedEmptyArrays);
                if (!CanonicalJson.IsSubset(recorded, confirmed) || !CanonicalJson.IsSubset(confirmed, recorded))
                    throw new SafetyViolationException("The candidate does not contain the confirmed client values for "
                        + string.Join(", ", used.Select(p => p.Label))
                        + ". Re-plan and update the inactive candidate before activation or assignment.");
            }
            else if (template is JsonObject obj)
            {
                foreach (var pair in obj)
                    Check(pair.Value, recorded is JsonObject previous ? previous[pair.Key] : null);
            }
            else if (template is JsonArray array)
            {
                for (var i = 0; i < array.Count; i++)
                    Check(array[i], recorded is JsonArray previous && previous.Count > i ? previous[i] : null);
            }
        }
    }

    public static void AssertReviewable(ParameterDefinition parameter)
    {
        if (!parameter.HasDefault) return;
        var emptyOptionalApplications = parameter.Key == "espBlockingAppIds" && !parameter.Required
            && parameter.Type == "guidList" && parameter.Default is JsonArray { Count: 0 };
        if (!emptyOptionalApplications && (parameter.Type is "guid" or "guidList" or "jsonArray"
            || parameter.Key is "tenantId" or "officeIpRanges" or "officeLocationId" or "mamGroupId" or "caExclusionGroupId" or "emergencyAccountIds"))
            throw new ConfigurationException($"{parameter.Label}: identity, targeting and structured inputs cannot use shipped defaults.");
    }

    private static string Describe(ParameterDefinition parameter, DateTimeOffset now)
    {
        var value = parameter.Default!.ToJsonString();
        var reviewed = string.IsNullOrWhiteSpace(parameter.ReviewedOn) ? "an unrecorded date" : parameter.ReviewedOn;
        var stale = !DateTimeOffset.TryParse(parameter.ReviewedOn, out var reviewedAt)
            ? " The review date is missing or invalid; check current vendor guidance before relying on this default."
            : reviewedAt > now
                ? " The review date is in the future; check current vendor guidance before relying on this default."
                : parameter.IsStale(now)
                    ? " That was more than 90 days ago, so check the vendor's currently supported releases before you rely on it."
                    : "";
        return $"No client value for {parameter.Label}; the candidate uses the shipped default {value}, reviewed {reviewed}."
             + stale
             + " Confirm it with the client and update the policy before assigning or enabling it.";
    }
}
