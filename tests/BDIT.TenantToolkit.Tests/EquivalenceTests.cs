using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// Equivalence answers "is this control already covered by something the client built themselves?". These tests pin the
/// boundary: it must recognise a differently named, differently shaped policy, and it must never turn that recognition
/// into a claim of compliance.
/// </summary>
public class EquivalenceTests
{
    private static readonly FixedClock Clock = new();
    private static readonly AssessmentEngine Engine = new(Clock, "test");

    private static ControlFinding Assess(JsonObject? policy)
    {
        var standard = TestData.Standard();
        var snapshot = TestData.Snapshot(standard);
        if (policy is not null) snapshot.Collections["conditionalAccess"].Items.Add(policy);
        var result = Engine.Assess(snapshot, standard, TestData.Profile(), TestData.Mappings(), Array.Empty<Deviation>(), "t");
        return result.Findings.First(f => f.ControlId == "CA-001");
    }

    /// <summary>A client's own MFA policy: right intent, unrecognisable name, different shape. This is the common case.</summary>
    private static JsonObject ClientMfaPolicy(string state = "enabled", string? grant = "mfa", bool allUsers = true, bool allApps = true, string[]? excludeUsers = null)
    {
        var policy = new JsonObject
        {
            ["id"] = "client-1",
            ["displayName"] = "MFA for staff (set up by previous IT)",
            ["state"] = state,
            ["conditions"] = new JsonObject
            {
                ["users"] = new JsonObject
                {
                    ["includeUsers"] = new JsonArray(allUsers ? "All" : "11111111-1111-4111-8111-111111111111"),
                    ["excludeUsers"] = new JsonArray((excludeUsers ?? Array.Empty<string>()).Select(u => (JsonNode?)u).ToArray())
                },
                ["applications"] = new JsonObject { ["includeApplications"] = new JsonArray(allApps ? "All" : "Office365") }
            }
        };
        if (grant is not null) policy["grantControls"] = new JsonObject { ["operator"] = "OR", ["builtInControls"] = new JsonArray(grant) };
        return policy;
    }

    [Fact]
    public void Singleton_policy_arrays_are_evaluated_by_element_key_so_authentication_methods_can_be_assessed()
    {
        var def = new CollectionDefinition { Api = "v1.0", Path = "/policies/authenticationMethodsPolicy", Scope = "Policy.Read.AuthenticationMethod", Singleton = true, Label = "Authentication methods policy" };
        var policy = ToolkitJson.ParseObject("""{"id":"authenticationMethodsPolicy","displayName":"Authentication Methods Policy","authenticationMethodConfigurations":[{"id":"MicrosoftAuthenticator","state":"enabled"},{"id":"Sms","state":"enabled"},{"id":"Voice","state":"disabled"},{"id":"TemporaryAccessPass","state":"enabled","isUsableOnce":false}]}""");
        var capture = new CollectionCapture { Status = CaptureStatus.Collected, Api = "v1.0", Path = def.Path, Items = { policy }, Count = 1 };
        var rule = new EquivalenceRule
        {
            Note = "test",
            Signals =
            {
                new() { Key = "auth", Label = "Authenticator enabled", Path = "authenticationMethodConfigurations[id=MicrosoftAuthenticator].state", Operator = SignalOperator.Equals, Value = "enabled" },
                new() { Key = "sms", Label = "SMS disabled", Path = "authenticationMethodConfigurations[id=Sms].state", Operator = SignalOperator.Equals, Value = "disabled" }
            },
            Caveats = { new() { Key = "tap", Label = "TAP reusable", Path = "authenticationMethodConfigurations[id=TemporaryAccessPass].isUsableOnce", Operator = SignalOperator.Equals, Value = false } }
        };

        var observation = Assert.Single(EquivalenceEvaluator.Evaluate(rule, capture, def, new NameResolver()));
        Assert.False(observation.Covered);
        Assert.True(observation.Signals.Single(s => s.Key == "auth").Matched);
        Assert.False(observation.Signals.Single(s => s.Key == "sms").Matched);
        Assert.Contains(observation.Caveats, c => c.StartsWith("TAP reusable", StringComparison.Ordinal));

        policy["authenticationMethodConfigurations"]![1]!["state"] = "disabled";
        Assert.True(Assert.Single(EquivalenceEvaluator.Evaluate(rule, capture, def, new NameResolver())).Covered);
    }

    [Fact]
    public void Differently_named_policy_meeting_every_condition_is_a_partial_match_not_missing()
    {
        var finding = Assess(ClientMfaPolicy());

        Assert.Equal(FindingStatus.PartialMatch, finding.Status);
        Assert.Contains("Equivalent configuration observed", finding.Reason, StringComparison.Ordinal);
        Assert.Contains("MFA for staff (set up by previous IT)", finding.Reason, StringComparison.Ordinal);
        var observation = Assert.Single(finding.Equivalence, e => e.Covered);
        Assert.All(observation.Signals.Where(s => s.Required), s => Assert.True(s.Matched));
    }

    /// <summary>The central safety property: recognising equivalent configuration never asserts compliance.</summary>
    [Fact]
    public void Equivalence_never_reports_compliant()
    {
        var finding = Assess(ClientMfaPolicy());

        Assert.NotEqual(FindingStatus.Compliant, finding.Status);
        Assert.NotEqual(FindingStatus.CompliantWithDeviation, finding.Status);
        Assert.Contains("not counted as compliant", finding.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true, "mfa")]   // only some users
    [InlineData(true, false, "mfa")]   // only some applications
    [InlineData(true, true, "block")]  // does something else entirely
    [InlineData(true, true, null)]     // no grant controls at all
    public void A_policy_failing_any_required_condition_is_not_coverage(bool allUsers, bool allApps, string? grant)
    {
        var finding = Assess(ClientMfaPolicy(allUsers: allUsers, allApps: allApps, grant: grant));

        Assert.Equal(FindingStatus.Missing, finding.Status);
        Assert.DoesNotContain(finding.Equivalence, e => e.Covered);
    }

    /// <summary>A policy can satisfy every signal and still protect almost nobody; the exclusions must be visible.</summary>
    [Fact]
    public void Exclusions_are_reported_as_caveats_on_a_covering_policy()
    {
        var finding = Assess(ClientMfaPolicy(excludeUsers: new[] { "a1111111-1111-4111-8111-111111111111", "b1111111-1111-4111-8111-111111111111" }));

        var observation = Assert.Single(finding.Equivalence, e => e.Covered);
        Assert.Contains(observation.Caveats, c => c.Contains("Excludes named users", StringComparison.Ordinal));
        Assert.Contains(finding.Notes, n => n.Contains("Qualifies the match", StringComparison.Ordinal));
    }

    /// <summary>A matching policy that is switched off covers nothing, and the finding must say so.</summary>
    [Fact]
    public void A_disabled_equivalent_policy_is_flagged_as_not_protecting_anyone()
    {
        var finding = Assess(ClientMfaPolicy(state: "disabled"));

        Assert.Equal(FindingStatus.PartialMatch, finding.Status);
        Assert.Contains(finding.Notes, n => n.Contains("not protecting anyone", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_signal_records_the_observed_value_so_the_claim_can_be_checked()
    {
        var finding = Assess(ClientMfaPolicy());

        var observation = Assert.Single(finding.Equivalence, e => e.Covered);
        Assert.All(observation.Signals, s =>
        {
            Assert.NotEmpty(s.Label);
            Assert.NotEmpty(s.Expected);
            Assert.NotEmpty(s.Observed);
        });
    }

    [Fact]
    public void An_exact_recipe_match_keeps_its_stronger_result_rather_than_being_downgraded()
    {
        var standard = TestData.Standard();
        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections["conditionalAccess"].Items.Add(
            TestData.ConditionalAccessPolicy("p1", "Anything at all", "enabled", new[] { TestData.Emergency }));

        var finding = Engine.Assess(snapshot, standard, TestData.Profile(), TestData.Mappings(), Array.Empty<Deviation>(), "t")
            .Findings.First(f => f.ControlId == "CA-001");

        Assert.Equal(FindingStatus.Compliant, finding.Status);
    }

    [Fact]
    public void Unreadable_collections_never_produce_equivalence_claims()
    {
        var standard = TestData.Standard();
        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections["conditionalAccess"].Status = CaptureStatus.Error;
        snapshot.Collections["conditionalAccess"].Error = "403";

        var finding = Engine.Assess(snapshot, standard, TestData.Profile(), TestData.Mappings(), Array.Empty<Deviation>(), "t")
            .Findings.First(f => f.ControlId == "CA-001");

        Assert.Equal(FindingStatus.UnableToAssess, finding.Status);
        Assert.Empty(finding.Equivalence);
    }

    [Theory]
    [InlineData(SignalOperator.Contains, "[\"All\"]", "\"All\"", true)]
    [InlineData(SignalOperator.Contains, "[\"all\"]", "\"All\"", true)]      // Graph is inconsistent about casing
    [InlineData(SignalOperator.Contains, "[\"None\"]", "\"All\"", false)]
    [InlineData(SignalOperator.ContainsAll, "[\"a\",\"b\"]", "[\"a\",\"b\"]", true)]
    [InlineData(SignalOperator.ContainsAll, "[\"a\"]", "[\"a\",\"b\"]", false)]
    [InlineData(SignalOperator.AtMost, "5", "10", true)]
    [InlineData(SignalOperator.AtLeast, "5", "10", false)]
    public void Operators_behave_as_declared(SignalOperator op, string observedJson, string valueJson, bool expected)
    {
        var signal = new EquivalenceSignal { Key = "k", Label = "l", Path = "p", Operator = op, Value = ToolkitJson.ParseNode(valueJson) };

        Assert.Equal(expected, EquivalenceEvaluator.Matches(signal, ToolkitJson.ParseNode(observedJson)));
    }

    /// <summary>Alternative routes to the same outcome (MFA control or authentication strength) are ORed within a group.</summary>
    [Fact]
    public void Grouped_signals_are_alternatives_so_either_route_counts_as_coverage()
    {
        var policy = ClientMfaPolicy(grant: null);
        policy["grantControls"] = new JsonObject { ["operator"] = "OR", ["authenticationStrength"] = new JsonObject { ["id"] = "00000000-0000-0000-0000-000000000004" } };

        var standard = TestData.Standard();
        var control = standard.Controls.First(c => c.Id == "CA-001");
        control.Equivalence!.Signals.Add(new EquivalenceSignal
        {
            Key = "mfaStrength",
            Label = "Requires an authentication strength",
            Path = "grantControls.authenticationStrength",
            Operator = SignalOperator.Present,
            Group = "mfa"
        });
        foreach (var s in control.Equivalence.Signals.Where(s => s.Key == "mfa")) s.Group = "mfa";

        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections["conditionalAccess"].Items.Add(policy);
        var finding = Engine.Assess(snapshot, standard, TestData.Profile(), TestData.Mappings(), Array.Empty<Deviation>(), "t")
            .Findings.First(f => f.ControlId == "CA-001");

        Assert.Equal(FindingStatus.PartialMatch, finding.Status);
        Assert.Contains(finding.Equivalence, e => e.Covered);
    }

    /// <summary>
    /// A required signal that declares no group is its own group, so that the default stays "every required signal
    /// must match". That is implemented by prefixing the signal's key with a control character to make a synthetic
    /// group name. If the prefix were dropped, an ungrouped signal keyed "mfa" and a signal declaring group "mfa"
    /// would land in the same group and become alternatives, so satisfying either one alone would report coverage
    /// the tenant does not have. This pins the separation rather than the prefix, because the prefix is an
    /// implementation detail and the separation is the promise.
    /// </summary>
    [Fact]
    public void An_ungrouped_signal_never_merges_with_a_group_that_shares_its_key()
    {
        var standard = TestData.Standard();
        var control = standard.Controls.First(c => c.Id == "CA-001");
        // "mfa" is an existing ungrouped required signal. This one declares "mfa" as its group and cannot match, so
        // coverage is only reported if the two were wrongly treated as alternatives.
        control.Equivalence!.Signals.Add(new EquivalenceSignal
        {
            Key = "mfaByStrength",
            Label = "Requires an authentication strength",
            Path = "grantControls.authenticationStrength",
            Operator = SignalOperator.Present,
            Group = "mfa"
        });

        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections["conditionalAccess"].Items.Add(ClientMfaPolicy());
        var finding = Engine.Assess(snapshot, standard, TestData.Profile(), TestData.Mappings(), Array.Empty<Deviation>(), "t")
            .Findings.First(f => f.ControlId == "CA-001");

        Assert.DoesNotContain(finding.Equivalence, e => e.Covered);
    }

    /// <summary>
    /// The separation above holds only while a catalogue cannot declare a group that collides with the synthetic
    /// prefix, so the loader refuses one. A control character in a group name has no legitimate use.
    /// </summary>
    [Fact]
    public void A_group_name_containing_a_control_character_is_refused()
    {
        var json = TestData.StandardJson.Replace(
            "{ \"key\": \"mfa\", \"label\": \"Requires multi-factor authentication\", \"path\": \"grantControls.builtInControls\", \"operator\": \"containsAny\", \"value\": [ \"mfa\" ] }",
            "{ \"key\": \"mfa\", \"label\": \"Requires multi-factor authentication\", \"path\": \"grantControls.builtInControls\", \"operator\": \"containsAny\", \"value\": [ \"mfa\" ], \"group\": \"\\u0000ungrouped:allUsers\" }",
            StringComparison.Ordinal);
        Assert.Contains("ungrouped", json, StringComparison.Ordinal);

        var error = Assert.Throws<ConfigurationException>(() => StandardsLoader.Parse(json, "test.json"));
        Assert.Contains("must be printable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_signal_against_a_missing_property_never_matches()
    {
        var signal = new EquivalenceSignal { Key = "k", Label = "l", Path = "p", Operator = SignalOperator.Contains, Value = JsonValue.Create("All") };

        Assert.False(EquivalenceEvaluator.Matches(signal, null));
    }
}
