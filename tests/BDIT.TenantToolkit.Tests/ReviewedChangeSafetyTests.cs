using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// Reviewed changes are the only path that can enable a Conditional Access policy, change authentication methods or
/// widen MDM scope. The guard derives route, permission and payload from a closed set of kinds and must refuse any
/// plan whose recorded route or payload differs, so nothing in a plan's free-text fields can widen a write.
/// </summary>
public class ReviewedChangeSafetyTests
{
    private const string AuthScope = "Policy.ReadWrite.AuthenticationMethod";

    private static ReviewedChangePlan AuthPlan(ReviewedChangeKind kind, string id, string state) => new()
    {
        Kind = kind, Api = GraphApi.V1, Method = "PATCH", Path = ReviewedChangeSafety.AuthenticationPath + id, RequiredScope = AuthScope,
        Before = new JsonObject { ["@odata.type"] = "#microsoft.graph.smsAuthenticationMethodConfiguration", ["id"] = id, ["state"] = "enabled" },
        Payload = new JsonObject { ["@odata.type"] = "#microsoft.graph.smsAuthenticationMethodConfiguration", ["state"] = state }
    };

    [Theory]
    [InlineData(ReviewedChangeKind.DisableSms, "Sms", "disabled")]
    [InlineData(ReviewedChangeKind.DisableVoice, "Voice", "disabled")]
    [InlineData(ReviewedChangeKind.EnableAuthenticator, "MicrosoftAuthenticator", "enabled")]
    public void Authentication_method_changes_are_state_only_on_the_named_method(ReviewedChangeKind kind, string id, string state)
    {
        ReviewedChangeSafety.Assert(AuthPlan(kind, id, state));

        var wrongState = AuthPlan(kind, id, state == "disabled" ? "enabled" : "disabled");
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(wrongState));
        var otherMethod = AuthPlan(kind, id, state);
        otherMethod.Path = ReviewedChangeSafety.AuthenticationPath + "Email";
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(otherMethod));
        var extraProperty = AuthPlan(kind, id, state);
        extraProperty.Payload["isSoftwareOathEnabled"] = true;
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(extraProperty));
        var incompleteRead = AuthPlan(kind, id, state);
        incompleteRead.Before.Remove("state");
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(incompleteRead));
        var targeted = AuthPlan(kind, id, state);
        targeted.IncludeGroups.Add(Guid.NewGuid().ToString());
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(targeted));
    }

    [Fact]
    public void Plan_free_text_route_method_permission_or_api_cannot_widen_a_reviewed_change()
    {
        var plan = AuthPlan(ReviewedChangeKind.DisableSms, "Sms", "disabled");
        plan.Path = "/policies/authenticationMethodsPolicy";
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));

        plan = AuthPlan(ReviewedChangeKind.DisableSms, "Sms", "disabled");
        plan.Method = "PUT";
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));

        plan = AuthPlan(ReviewedChangeKind.DisableSms, "Sms", "disabled");
        plan.RequiredScope = "Policy.ReadWrite.ConditionalAccess";
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));

        plan = AuthPlan(ReviewedChangeKind.DisableSms, "Sms", "disabled");
        plan.Api = GraphApi.Beta;
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));
    }

    [Fact]
    public void Temporary_access_pass_requires_exactly_one_group_and_fixed_one_hour_single_use_settings()
    {
        var group = Guid.NewGuid().ToString();
        var plan = new ReviewedChangePlan
        {
            Kind = ReviewedChangeKind.ConfigureTap, Api = GraphApi.V1, Method = "PATCH",
            Path = ReviewedChangeSafety.AuthenticationPath + "TemporaryAccessPass", RequiredScope = AuthScope, IncludeGroups = { group },
            Payload = new JsonObject
            {
                ["@odata.type"] = "#microsoft.graph.temporaryAccessPassAuthenticationMethodConfiguration", ["state"] = "enabled",
                ["defaultLength"] = 16, ["defaultLifetimeInMinutes"] = 60, ["minimumLifetimeInMinutes"] = 10, ["maximumLifetimeInMinutes"] = 60, ["isUsableOnce"] = true,
                ["includeTargets"] = new JsonArray(new JsonObject { ["id"] = group, ["targetType"] = "group" })
            }
        };
        ReviewedChangeSafety.Assert(plan);

        plan.Payload["isUsableOnce"] = false;
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));
        plan.Payload["isUsableOnce"] = true;
        plan.Payload["maximumLifetimeInMinutes"] = 480;
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));
        plan.Payload["maximumLifetimeInMinutes"] = 60;
        plan.IncludeGroups.Add(Guid.NewGuid().ToString());
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));
    }

    [Fact]
    public void Mdm_scope_change_only_targets_the_intune_policy_and_only_sets_all()
    {
        var id = Guid.NewGuid().ToString();
        const string intune = "https://enrollment.manage.microsoft.com/enrollmentserver/discovery.svc";
        var plan = new ReviewedChangePlan
        {
            Kind = ReviewedChangeKind.MdmAll, ObjectId = id, Api = GraphApi.Beta, Method = "PATCH",
            Path = "/policies/mobileDeviceManagementPolicies/" + id, RequiredScope = "Policy.ReadWrite.MobilityManagement",
            Before = new JsonObject { ["id"] = id, ["discoveryUrl"] = intune, ["appliesTo"] = "selected" },
            Payload = new JsonObject { ["appliesTo"] = "all" }
        };
        ReviewedChangeSafety.Assert(plan);

        plan.Before["discoveryUrl"] = "https://mdm.example.com/discovery";
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));
        plan.Before["discoveryUrl"] = intune;
        plan.Payload["appliesTo"] = "none";
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));
        plan.Payload["appliesTo"] = "all";
        plan.ObjectId = "not-a-guid";
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));
    }

    [Fact]
    public void Tenant_compliance_change_preserves_every_other_setting_and_requires_a_complete_read()
    {
        var plan = new ReviewedChangePlan
        {
            Kind = ReviewedChangeKind.SecureCompliance, Api = GraphApi.V1, Method = "PATCH", Path = "/deviceManagement",
            RequiredScope = "DeviceManagementConfiguration.ReadWrite.All",
            Before = new JsonObject { ["settings"] = new JsonObject { ["secureByDefault"] = false, ["deviceComplianceCheckinThresholdDays"] = 30 } },
            Payload = new JsonObject { ["settings"] = new JsonObject { ["secureByDefault"] = true, ["deviceComplianceCheckinThresholdDays"] = 30 } }
        };
        ReviewedChangeSafety.Assert(plan);

        plan.Payload["settings"]!["deviceComplianceCheckinThresholdDays"] = 7;
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));
        plan.Payload["settings"]!["deviceComplianceCheckinThresholdDays"] = 30;
        plan.Before = new JsonObject { ["settings"] = new JsonObject() };
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));
    }

    [Theory]
    [InlineData(ReviewedChangeKind.EnableConditionalAccess, "enabled")]
    [InlineData(ReviewedChangeKind.ReportOnlyConditionalAccess, "enabledForReportingButNotEnforced")]
    [InlineData(ReviewedChangeKind.DisableConditionalAccess, "disabled")]
    public void Conditional_access_activation_is_state_only_on_a_v1_policy_addressed_by_id(ReviewedChangeKind kind, string state)
    {
        var id = Guid.NewGuid().ToString();
        var plan = new ReviewedChangePlan
        {
            Kind = kind, ObjectId = id, Api = GraphApi.V1, Method = "PATCH",
            Path = ConditionalAccessSafety.ConditionalAccessPolicyPath + "/" + id, RequiredScope = "Policy.ReadWrite.ConditionalAccess",
            Payload = new JsonObject { ["state"] = state }
        };
        ReviewedChangeSafety.Assert(plan);

        plan.Payload["conditions"] = new JsonObject();
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));
        plan.Payload.Remove("conditions");
        plan.Api = GraphApi.Beta;
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));
        plan.Api = GraphApi.V1;
        plan.Path = "/deviceManagement/deviceConfigurations/" + id;
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(plan));
    }

    [Fact]
    public void Group_assignment_targets_only_resolved_group_ids_and_removal_takes_no_replacement_targets()
    {
        var id = Guid.NewGuid().ToString();
        var group = Guid.NewGuid().ToString();
        const string root = "/deviceManagement/deviceCompliancePolicies";
        var assign = new ReviewedChangePlan
        {
            Kind = ReviewedChangeKind.AssignGroups, ObjectId = id, Api = GraphApi.V1, Method = "POST",
            Path = root + "/" + id + "/assign", RequiredScope = "DeviceManagementConfiguration.ReadWrite.All", IncludeGroups = { group },
            Payload = ReviewedChangeSafety.AssignmentPayload(root, new[] { group }, Array.Empty<string>())
        };
        ReviewedChangeSafety.Assert(assign);
        assign.IncludeGroups.Clear();
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(assign));

        var remove = new ReviewedChangePlan
        {
            Kind = ReviewedChangeKind.RemoveAssignments, ObjectId = id, Api = GraphApi.V1, Method = "POST",
            Path = root + "/" + id + "/assign", RequiredScope = "DeviceManagementConfiguration.ReadWrite.All",
            Payload = ReviewedChangeSafety.AssignmentPayload(root, Array.Empty<string>(), Array.Empty<string>())
        };
        ReviewedChangeSafety.Assert(remove);
        remove.IncludeGroups.Add(group);
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(remove));

        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.AssignmentType(ConditionalAccessSafety.ConditionalAccessPolicyPath));
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.AssignmentPayload(root, new[] { "not-a-guid" }, Array.Empty<string>()));
    }

    [Fact]
    public void Autopatch_enrolment_takes_one_to_fifty_distinct_device_ids_and_nothing_else()
    {
        var device = Guid.NewGuid().ToString();
        static ReviewedChangePlan Plan(params string[] devices) => new()
        {
            Kind = ReviewedChangeKind.EnrolFeatureUpdates, Api = GraphApi.Beta, Method = "POST",
            Path = ReviewedChangeSafety.UpdatesPath + "/enrollAssets", RequiredScope = "WindowsUpdates.ReadWrite.All", DeviceIds = devices.ToList(),
            Payload = new JsonObject
            {
                ["updateCategory"] = "feature",
                ["assets"] = new JsonArray(devices.Select(d => (JsonNode?)new JsonObject { ["@odata.type"] = "#microsoft.graph.windowsUpdates.azureADDevice", ["id"] = d }).ToArray())
            }
        };
        ReviewedChangeSafety.Assert(Plan(device));

        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(Plan()));
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(Plan(device, device)));
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(Plan(Enumerable.Range(0, 51).Select(_ => Guid.NewGuid().ToString()).ToArray())));
        var withGroup = Plan(device);
        withGroup.IncludeGroups.Add(Guid.NewGuid().ToString());
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(withGroup));
        var wrongCategory = Plan(device);
        wrongCategory.Payload["updateCategory"] = "quality";
        Assert.Throws<SafetyViolationException>(() => ReviewedChangeSafety.Assert(wrongCategory));
    }

    private static JsonObject RegistrationPolicy(bool lapsEnabled = false) => ToolkitJson.ParseObject($$"""
        {
          "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#policies/deviceRegistrationPolicy/$entity",
          "id": "deviceRegistrationPolicy", "displayName": "Device Registration Policy", "description": "Tenant-wide policy",
          "userDeviceQuota": 50, "multiFactorAuthConfiguration": "notRequired",
          "azureADRegistration": { "isAdminConfigurable": false, "allowedToRegister": { "@odata.type": "#microsoft.graph.allDeviceRegistrationMembership" } },
          "azureADJoin": { "isAdminConfigurable": true, "allowedToJoin": { "@odata.type": "#microsoft.graph.allDeviceRegistrationMembership" },
                           "localAdmins": { "enableGlobalAdmins": true, "registeringUsers": { "@odata.type": "#microsoft.graph.noDeviceRegistrationMembership" } } },
          "localAdminPassword": { "isEnabled": {{(lapsEnabled ? "true" : "false")}} }
        }
        """);

    [Fact]
    public void Laps_enablement_only_flips_the_flag_and_preserves_every_other_registration_setting()
    {
        var before = RegistrationPolicy();
        Assert.False(EntraLapsSafety.IsEnabled(before));

        var payload = EntraLapsSafety.EnablePayload(before);
        Assert.True(payload["localAdminPassword"]!["isEnabled"]!.GetValue<bool>());
        Assert.Equal(50, payload["userDeviceQuota"]!.GetValue<int>());
        Assert.Equal("notRequired", payload["multiFactorAuthConfiguration"]!.GetValue<string>());
        Assert.False(payload.ContainsKey("id"));
        Assert.False(payload.ContainsKey("@odata.context"));
        Assert.True(EntraLapsSafety.SameState(payload, RegistrationPolicy(lapsEnabled: true)));
        Assert.False(EntraLapsSafety.SameState(payload, before));
        Assert.False(EntraLapsSafety.IsEnabled(before));
    }

    [Fact]
    public void Laps_enablement_refuses_unknown_registration_properties_rather_than_silently_dropping_them()
    {
        var policy = RegistrationPolicy();
        policy["futureSetting"] = true;
        Assert.Throws<SafetyViolationException>(() => EntraLapsSafety.WritableState(policy));
    }
}
