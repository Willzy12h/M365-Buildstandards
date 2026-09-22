using System.Globalization;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Assessment;

/// <summary>
/// Measures captured objects against a control's declared equivalence signals.
///
/// Client tenants almost never use the Build Standard's policy names, and a policy assembled by someone else will not
/// match a recipe property for property. Equivalence answers a narrower, answerable question: does an object exist
/// whose values satisfy every condition this control actually cares about?
///
/// Three rules keep this evidence rather than opinion:
///  - every signal is declared in the standard, so the test can be read and changed without touching code;
///  - every result carries the observed value, so an engineer can check the claim instead of trusting it;
///  - caveats (exclusions, narrow scoping) are reported whenever they are true, because an object that satisfies every
///    signal while excluding half the tenant is not coverage.
/// Nothing here decides compliance; it produces observations the assessment presents for confirmation.
/// </summary>
public static class EquivalenceEvaluator
{
    /// <summary>
    /// Prefix for the synthetic group given to a required signal that declares none, so that an ungrouped signal is
    /// its own group and the default stays "every required signal must match".
    ///
    /// It begins with a control character deliberately. A collision between this synthetic key and a group a
    /// catalogue actually declares would silently merge two independent requirements into one alternative, so only
    /// one of them would have to match and a tenant missing the other would still be reported as covered. That is a
    /// wrong answer rather than an error, which is the worst kind. <see cref="Standards.StandardsLoader"/> rejects a
    /// declared group containing a control character, so a catalogue cannot reach this prefix.
    /// </summary>
    private const string UngroupedGroupPrefix = "\u0000ungrouped:";

    public static IReadOnlyList<EquivalenceObservation> Evaluate(EquivalenceRule rule, CollectionCapture capture, CollectionDefinition def, NameResolver names)
    {
        var observations = new List<EquivalenceObservation>();
        var isConditionalAccess = Core.Safety.ConditionalAccessSafety.IsConditionalAccess(def);
        foreach (var item in capture.Items)
        {
            var observation = new EquivalenceObservation
            {
                ObjectId = item["id"]?.GetValue<string>() ?? "",
                Name = item[def.NameProperty]?.GetValue<string>() ?? item["displayName"]?.GetValue<string>() ?? item["name"]?.GetValue<string>() ?? "(unnamed)",
                Collection = def.Label,
                Enforcement = AssessmentEngine.Enforcement(item, def, isConditionalAccess)
            };

            foreach (var signal in rule.Signals)
            {
                var observed = CanonicalJson.At(item, signal.Path);
                observation.Signals.Add(new SignalResult
                {
                    Key = signal.Key,
                    Label = signal.Label,
                    Path = signal.Path,
                    Required = signal.Required,
                    Matched = Matches(signal, observed),
                    Expected = Describe(signal),
                    Observed = DescribeObserved(signal, observed, names)
                });
            }

            // Required signals sharing a group are alternatives (OR); each group must contribute at least one match,
            // and an ungrouped signal is its own group, so the default remains "every required signal must match".
            var groups = rule.Signals
                .Where(s => s.Required)
                .GroupBy(s => string.IsNullOrEmpty(s.Group) ? UngroupedGroupPrefix + s.Key : s.Group, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var results = observation.Signals.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);
            observation.Covered = groups.Count > 0
                && groups.All(g => g.Any(s => results.TryGetValue(s.Key, out var r) && r.Matched));

            foreach (var caveat in rule.Caveats)
            {
                var observed = CanonicalJson.At(item, caveat.Path);
                if (Matches(caveat, observed))
                    observation.Caveats.Add(caveat.Label + ": " + DescribeObserved(caveat, observed, names));
            }

            // Only objects that matched something are worth showing; a tenant may hold hundreds of unrelated policies.
            if (observation.Covered || observation.Signals.Any(s => s.Matched)) observations.Add(observation);
        }

        return observations
            .OrderByDescending(o => o.Covered)
            .ThenByDescending(o => o.Signals.Count(s => s.Matched))
            .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool Matches(EquivalenceSignal signal, JsonNode? observed) => signal.Operator switch
    {
        SignalOperator.Present => observed is not null,
        SignalOperator.Absent => observed is null,
        SignalOperator.NonEmpty => Count(observed) > 0,
        SignalOperator.Empty => Count(observed) == 0,
        SignalOperator.Equals => observed is not null && ScalarEquals(observed, signal.Value),
        SignalOperator.EqualsAny => observed is not null && Values(signal.Value).Any(v => ScalarEquals(observed, v)),
        SignalOperator.Contains => observed is JsonArray a && a.Any(i => ScalarEquals(i, signal.Value)),
        SignalOperator.ContainsAny => observed is JsonArray any && Values(signal.Value).Any(v => any.Any(i => ScalarEquals(i, v))),
        SignalOperator.ContainsAll => observed is JsonArray all && Values(signal.Value).All(v => all.Any(i => ScalarEquals(i, v))),
        SignalOperator.AtMost => Number(observed) is double n1 && Number(signal.Value) is double limit1 && n1 <= limit1,
        SignalOperator.AtLeast => Number(observed) is double n2 && Number(signal.Value) is double limit2 && n2 >= limit2,
        SignalOperator.CountAtMost => observed is JsonArray maxArray && CountLimit(signal.Value) is double maxCount && maxArray.Count <= maxCount,
        SignalOperator.CountAtLeast => observed is JsonArray minArray && CountLimit(signal.Value) is double minCount && minArray.Count >= minCount,
        _ => false
    };

    private static double? CountLimit(JsonNode? node)
    {
        var limit = Number(node);
        return limit is double value && double.IsFinite(value) && value >= 0 && value == Math.Truncate(value) ? value : null;
    }

    private static string DescribeObserved(EquivalenceSignal signal, JsonNode? observed, NameResolver names) =>
        signal.Operator is SignalOperator.CountAtLeast or SignalOperator.CountAtMost && observed is JsonArray array
            ? $"{array.Count} member object(s): {names.Render(array)}"
            : observed is null ? "not present" : names.Render(observed);

    /// <summary>Comparison is case-insensitive for strings: Graph is inconsistent about casing in enumerations such as "All".</summary>
    private static bool ScalarEquals(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is JsonValue va && b is JsonValue vb)
        {
            if (va.TryGetValue<string>(out var sa) && vb.TryGetValue<string>(out var sb))
                return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
            return CanonicalJson.ScalarText(va) == CanonicalJson.ScalarText(vb);
        }
        return CanonicalJson.Serialize(a) == CanonicalJson.Serialize(b);
    }

    private static IEnumerable<JsonNode?> Values(JsonNode? value) =>
        value is JsonArray array ? array.Select(i => i) : new[] { value };

    private static int Count(JsonNode? node) => node switch
    {
        null => 0,
        JsonArray array => array.Count,
        JsonObject obj => obj.Count,
        JsonValue value => value.TryGetValue<string>(out var s) ? s.Length : 1,
        _ => 0
    };

    private static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<double>(out var d)) return d;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<decimal>(out var dec)) return (double)dec;
        if (value.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
    }

    private static string Describe(EquivalenceSignal signal)
    {
        var value = signal.Value is null ? "" : CanonicalJson.Serialize(signal.Value);
        return signal.Operator switch
        {
            SignalOperator.Present => "any value present",
            SignalOperator.Absent => "not configured",
            SignalOperator.NonEmpty => "at least one entry",
            SignalOperator.Empty => "no entries",
            SignalOperator.Equals => "equals " + value,
            SignalOperator.EqualsAny => "one of " + value,
            SignalOperator.Contains => "includes " + value,
            SignalOperator.ContainsAny => "includes any of " + value,
            SignalOperator.ContainsAll => "includes all of " + value,
            SignalOperator.AtMost => "at most " + value,
            SignalOperator.AtLeast => "at least " + value,
            SignalOperator.CountAtMost => "at most " + value + " array entries",
            SignalOperator.CountAtLeast => "at least " + value + " array entries",
            _ => value
        };
    }
}
