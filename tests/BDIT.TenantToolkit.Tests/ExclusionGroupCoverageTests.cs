using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Collection;
using BDIT.TenantToolkit.Engine.Planning;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// The exclusion group makes the exempt population readable in one place. It never becomes the thing that keeps the
/// deploying engineer signed in: membership is read from a capture that can change before the write lands, so a
/// missing member is reported, not assumed, and the direct exclusion stays.
/// </summary>
public class ExclusionGroupCoverageTests
{
    private const string GroupId = "33333333-3333-3333-3333-333333333333";
    private const string OperatorId = "11111111-1111-1111-1111-111111111111";
    private const string EmergencyId = "22222222-2222-2222-2222-222222222222";

    private static StandardCatalogue Standard() => new()
    {
        Collections = { ["groups"] = new CollectionDefinition { Path = "/groups", Label = "Groups", Relationship = "members" } },
        Controls = { new ControlDefinition { Id = "PRE-001", Name = "GRP - Policy Exclusions Users", Collection = "groups", ExclusionRole = "users" } }
    };

    private static TenantSnapshot Snapshot(JsonObject? group) => new()
    {
        Collections =
        {
            ["groups"] = new CollectionCapture
            {
                Status = CaptureStatus.Collected,
                Items = group is null ? new List<JsonObject>() : new List<JsonObject> { group }
            }
        }
    };

    private static JsonObject Group(JsonArray? members)
    {
        var group = new JsonObject { ["id"] = GroupId, ["displayName"] = "GRP - Policy Exclusions Users" };
        if (members is not null) group["members"] = members;
        else group[TenantCollector.RelationshipUnknownKey] = true;
        return group;
    }

    private static JsonArray Members(params string[] ids)
    {
        var array = new JsonArray();
        foreach (var id in ids) array.Add(new JsonObject { ["id"] = id });
        return array;
    }

    private static ManagedObjectMappings Mapped() =>
        new() { ByControl = { ["PRE-001"] = new ManagedObjectMapping { ControlId = "PRE-001", ObjectId = GroupId, Collection = "groups" } } };

    private static TenantSession Session() => new() { OperatorObjectId = OperatorId, OperatorUpn = "engineer@example.test" };

    private static ExclusionGroupCoverage.Result Run(JsonObject? group, ManagedObjectMappings mappings) =>
        ExclusionGroupCoverage.ForUsers(Standard(), Snapshot(group), mappings, Session(), new[] { EmergencyId }, NameResolver.FromSnapshot(Snapshot(group)));

    [Fact]
    public void A_group_holding_the_engineer_and_the_emergency_account_is_used_without_complaint()
    {
        var result = Run(Group(Members(OperatorId, EmergencyId)), Mapped());

        Assert.Equal(GroupId, result.GroupId);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_missing_engineer_is_reported_but_the_group_is_still_excluded()
    {
        var result = Run(Group(Members(EmergencyId)), Mapped());

        Assert.Equal(GroupId, result.GroupId);
        Assert.Contains(result.Warnings, w => w.Contains("not a member", StringComparison.Ordinal) && w.Contains("excludes you directly", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_emergency_account_is_reported()
    {
        var result = Run(Group(Members(OperatorId)), Mapped());

        Assert.Contains(result.Warnings, w => w.Contains("Emergency access account", StringComparison.Ordinal));
    }

    /// <summary>Unknown membership is never read as "nobody is in it".</summary>
    [Fact]
    public void Unreadable_membership_is_reported_as_unknown()
    {
        var result = Run(Group(null), Mapped());

        Assert.Equal(GroupId, result.GroupId);
        Assert.Contains(result.Warnings, w => w.Contains("could not be read", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("not a member", StringComparison.Ordinal));
    }

    [Fact]
    public void Before_the_group_exists_the_policy_names_its_exclusions_individually()
    {
        var result = Run(null, new ManagedObjectMappings());

        Assert.Null(result.GroupId);
        Assert.Contains(result.Warnings, w => w.Contains("has not been created yet", StringComparison.Ordinal));
    }

    [Fact]
    public void A_recorded_group_missing_from_the_capture_is_reported_rather_than_assumed_empty()
    {
        var result = Run(null, Mapped());

        Assert.Equal(GroupId, result.GroupId);
        Assert.Contains(result.Warnings, w => w.Contains("was not found in the capture", StringComparison.Ordinal));
    }
}
