using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// Group creation is the widest permission the toolkit asks for, so the guard constrains the payload rather than
/// trusting the recipe: an empty security group with assigned membership, never a dynamic, mail-enabled or
/// role-assignable group, and never one carrying members or owners. A named location is created untrusted with
/// client-confirmed CIDR ranges only.
/// </summary>
public class DirectoryPrerequisiteSafetyTests
{
    private static readonly CollectionDefinition Groups = new() { Path = "/groups?$select=id,displayName", Scope = "Group.Read.All", Write = "Group.ReadWrite.All", Label = "Groups" };
    private static readonly CollectionDefinition Locations = new() { Path = "/identity/conditionalAccess/namedLocations", Scope = "Policy.Read.All", Write = "Policy.ReadWrite.ConditionalAccess", Label = "Named locations" };

    private static JsonObject Group() => new()
    {
        ["displayName"] = "GRP - Managed Users",
        ["description"] = "M365 BuildStandard reviewed group",
        ["mailEnabled"] = false,
        ["mailNickname"] = "grp-managed-users",
        ["securityEnabled"] = true,
        ["groupTypes"] = new JsonArray()
    };

    private static JsonObject Location() => new()
    {
        ["@odata.type"] = "#microsoft.graph.ipNamedLocation",
        ["displayName"] = "LOC - M365 Office",
        ["isTrusted"] = false,
        ["ipRanges"] = new JsonArray(new JsonObject { ["@odata.type"] = "#microsoft.graph.iPv4CidrRange", ["cidrAddress"] = "203.0.113.0/24" })
    };

    [Fact]
    public void An_empty_security_group_is_accepted()
    {
        WritePayloadGuard.Assert(Groups, Group());
        DirectoryPrerequisiteSafety.Assert(Groups, Group());
    }

    [Theory]
    [InlineData("members")]
    [InlineData("owners")]
    [InlineData("membershipRule")]
    [InlineData("membershipRuleProcessingState")]
    [InlineData("isAssignableToRole")]
    [InlineData("members@odata.bind")]
    public void A_group_candidate_cannot_grant_access(string key)
    {
        var payload = Group();
        payload[key] = "anything";
        Assert.Throws<SafetyViolationException>(() => WritePayloadGuard.Assert(Groups, payload));
    }

    [Fact]
    public void Only_empty_assigned_membership_security_groups_are_created()
    {
        var distribution = Group();
        distribution["mailEnabled"] = true;
        Assert.Throws<SafetyViolationException>(() => DirectoryPrerequisiteSafety.Assert(Groups, distribution));

        var notSecurity = Group();
        notSecurity["securityEnabled"] = false;
        Assert.Throws<SafetyViolationException>(() => DirectoryPrerequisiteSafety.Assert(Groups, notSecurity));

        var unified = Group();
        unified["groupTypes"] = new JsonArray("Unified");
        Assert.Throws<SafetyViolationException>(() => DirectoryPrerequisiteSafety.Assert(Groups, unified));

        var unnamed = Group();
        unnamed["displayName"] = "  ";
        Assert.Throws<SafetyViolationException>(() => DirectoryPrerequisiteSafety.Assert(Groups, unnamed));

        var badNickname = Group();
        badNickname["mailNickname"] = "grp managed users";
        Assert.Throws<SafetyViolationException>(() => DirectoryPrerequisiteSafety.Assert(Groups, badNickname));
    }

    [Fact]
    public void A_named_location_is_created_untrusted_with_reviewed_ranges()
    {
        WritePayloadGuard.Assert(Locations, Location());

        var trusted = Location();
        trusted["isTrusted"] = true;
        Assert.Throws<SafetyViolationException>(() => DirectoryPrerequisiteSafety.Assert(Locations, trusted));

        var country = Location();
        country["@odata.type"] = "#microsoft.graph.countryNamedLocation";
        Assert.Throws<SafetyViolationException>(() => DirectoryPrerequisiteSafety.Assert(Locations, country));

        var empty = Location();
        empty["ipRanges"] = new JsonArray();
        Assert.Throws<SafetyViolationException>(() => DirectoryPrerequisiteSafety.Assert(Locations, empty));
    }

    [Theory]
    [InlineData("#microsoft.graph.iPv4CidrRange", "203.0.113.5")]
    [InlineData("#microsoft.graph.iPv4CidrRange", "203.0.113.0/33")]
    [InlineData("#microsoft.graph.iPv4CidrRange", "2001:db8::/32")]
    [InlineData("#microsoft.graph.iPv6CidrRange", "203.0.113.0/24")]
    [InlineData("#microsoft.graph.iPv4CidrRange", "not-an-address/24")]
    public void Each_range_must_be_a_whole_cidr_range_of_the_declared_family(string type, string cidr)
    {
        var payload = Location();
        payload["ipRanges"] = new JsonArray(new JsonObject { ["@odata.type"] = type, ["cidrAddress"] = cidr });
        Assert.Throws<SafetyViolationException>(() => DirectoryPrerequisiteSafety.Assert(Locations, payload));
    }

    [Fact]
    public void An_ipv6_range_is_accepted_and_extra_range_properties_are_refused()
    {
        var ipv6 = Location();
        ipv6["ipRanges"] = new JsonArray(new JsonObject { ["@odata.type"] = "#microsoft.graph.iPv6CidrRange", ["cidrAddress"] = "2001:db8::/32" });
        DirectoryPrerequisiteSafety.Assert(Locations, ipv6);

        var extra = Location();
        extra["ipRanges"] = new JsonArray(new JsonObject
        {
            ["@odata.type"] = "#microsoft.graph.iPv4CidrRange", ["cidrAddress"] = "203.0.113.0/24", ["isTrusted"] = true
        });
        Assert.Throws<SafetyViolationException>(() => DirectoryPrerequisiteSafety.Assert(Locations, extra));
    }

    /// <summary>The guard is scoped to these two routes; a policy payload must not be judged by group rules.</summary>
    [Fact]
    public void Other_collections_are_untouched_by_this_guard()
    {
        var compliance = new CollectionDefinition { Path = "/deviceManagement/deviceCompliancePolicies", Label = "Compliance" };
        DirectoryPrerequisiteSafety.Assert(compliance, new JsonObject { ["displayName"] = "M365 - Windows core compliance" });
    }
}
