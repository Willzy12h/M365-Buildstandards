using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ReleaseIdentityAssessmentTests
{
    [Theory]
    [InlineData("ID-004", """{"id":"FIDO2","state":"enabled","keyRestrictions":{"isEnforced":false,"enforcementType":"allow","aaGuids":[123]}}""")]
    [InlineData("ID-004", """{"id":"FIDO2","state":{},"keyRestrictions":{"isEnforced":false,"enforcementType":"allow","aaGuids":[]}}""")]
    [InlineData("ID-005", """{"systemCredentialPreferences":{"state":1}}""")]
    [InlineData("ID-006", """{"registrationEnforcement":{"authenticationMethodsRegistrationCampaign":{"state":"enabled","includeTargets":["unexpected"]}}}""")]
    [InlineData("ID-006", """{"registrationEnforcement":{"authenticationMethodsRegistrationCampaign":{"state":"unknownFutureValue","includeTargets":[]}}}""")]
    [InlineData("ID-006", """{"registrationEnforcement":{"authenticationMethodsRegistrationCampaign":{"state":"enabled","includeTargets":[{}]}}}""")]
    [InlineData("ID-007", """{"defaultUserRolePermissions":{"permissionGrantPoliciesAssigned":[false]}}""")]
    [InlineData("ID-008", """{"isEnabled":true,"reviewers":[null]}""")]
    [InlineData("ID-008", """{"isEnabled":true,"reviewers":[{"query":123,"queryType":"MicrosoftGraph"}]}""")]
    public void Malformed_identity_evidence_is_unknown_without_breaking_the_assessment(string controlId, string json)
        => Assert.Equal(FindingStatus.UnableToAssess, Assess(controlId, ToolkitJson.ParseObject(json)));

    [Theory]
    [InlineData("ID-004", true)][InlineData("ID-004", false)]
    [InlineData("ID-005", true)][InlineData("ID-005", false)]
    [InlineData("ID-006", true)][InlineData("ID-006", false)]
    [InlineData("ID-007", true)][InlineData("ID-007", false)]
    [InlineData("ID-008", true)][InlineData("ID-008", false)]
    public void Well_formed_identity_evidence_distinguishes_a_match_from_a_difference(string controlId, bool matches)
    {
        var state = matches ? "enabled" : "disabled";
        var json = controlId switch
        {
            "ID-004" => new JsonObject { ["id"]="FIDO2", ["state"]=state, ["keyRestrictions"]=new JsonObject { ["isEnforced"]=false, ["enforcementType"]="allow", ["aaGuids"]=new JsonArray() } },
            "ID-005" => new JsonObject { ["systemCredentialPreferences"]=new JsonObject { ["state"]=state } },
            "ID-006" => new JsonObject { ["registrationEnforcement"]=new JsonObject { ["authenticationMethodsRegistrationCampaign"]=new JsonObject { ["state"]=state, ["includeTargets"]=new JsonArray(new JsonObject { ["id"]="all_users", ["targetedAuthenticationMethod"]="microsoftAuthenticator" }) } } },
            "ID-007" => new JsonObject { ["defaultUserRolePermissions"]=new JsonObject { ["permissionGrantPoliciesAssigned"]=matches ? new JsonArray() : new JsonArray("managePermissionGrantsForSelf.synthetic") } },
            "ID-008" => new JsonObject { ["isEnabled"]=matches, ["reviewers"]=new JsonArray(new JsonObject { ["query"]="/users/"+TestData.Operator, ["queryType"]="MicrosoftGraph" }) },
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(matches ? FindingStatus.RequiresManualReview : FindingStatus.PartialMatch, Assess(controlId, json));
    }

    private static FindingStatus Assess(string controlId, JsonObject policy)
    {
        var standard = Release20260912Tests.Standard();
        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections[standard.FindControl(controlId)!.Collection!].Items = new() { policy };
        var report = new AssessmentEngine(new FixedClock(), "test").Assess(snapshot, standard, TestData.Profile(), TestData.Mappings(), Array.Empty<Deviation>(), "synthetic");
        return report.Findings.Single(f => f.ControlId == controlId).Status;
    }
}
