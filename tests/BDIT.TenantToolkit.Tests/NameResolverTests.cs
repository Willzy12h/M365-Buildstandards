using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// Names are display only, but every page and report that shows tenant objects builds a resolver from the whole
/// capture. A captured value of an unexpected type used to throw from inside that, taking the page with it.
/// </summary>
public class NameResolverTests
{
    private static TenantSnapshot Capture(params JsonObject[] groups)
    {
        var snapshot = new TenantSnapshot { TenantId = "11111111-1111-1111-1111-111111111111" };
        snapshot.Collections["groups"] = new CollectionCapture { Status = CaptureStatus.Collected, Items = groups.ToList(), Count = groups.Length };
        return snapshot;
    }

    [Fact]
    public void An_object_whose_name_is_not_text_is_skipped_rather_than_stopping_resolution()
    {
        var resolver = NameResolver.FromSnapshot(Capture(
            new JsonObject { ["id"] = "aaaaaaaa-aaaa-aaaa-aaaa-000000000001", ["displayName"] = 42 },
            new JsonObject { ["id"] = 7, ["displayName"] = "Numeric id" },
            new JsonObject { ["id"] = "aaaaaaaa-aaaa-aaaa-aaaa-000000000002", ["displayName"] = "Pilot devices" }));

        Assert.Equal("Pilot devices [aaaaaaaa-aaaa-aaaa-aaaa-000000000002]", resolver.Display("aaaaaaaa-aaaa-aaaa-aaaa-000000000002"));
        Assert.Equal("Unresolved [aaaaaaaa-aaaa-aaaa-aaaa-000000000001]", resolver.Display("aaaaaaaa-aaaa-aaaa-aaaa-000000000001"));
    }

    [Fact]
    public void An_assignment_target_with_an_unexpected_type_is_summarised_rather_than_thrown()
    {
        var resolver = NameResolver.FromSnapshot(Capture());
        var item = new JsonObject
        {
            ["_assignments"] = new JsonArray(new JsonObject { ["target"] = new JsonObject { ["@odata.type"] = 3, ["groupId"] = true } })
        };

        var summary = resolver.AssignmentSummary(item);

        Assert.Equal("Assigned (targets unreadable)", summary);
    }

    private static TenantSnapshot Capture(string collection, params JsonObject[] items)
    {
        var snapshot = new TenantSnapshot { TenantId = "11111111-1111-1111-1111-111111111111" };
        snapshot.Collections[collection] = new CollectionCapture { Status = CaptureStatus.Collected, Items = items.ToList(), Count = items.Length };
        return snapshot;
    }

    [Fact]
    public void A_user_reads_as_display_name_and_sign_in_name()
    {
        var resolver = NameResolver.FromSnapshot(Capture("users",
            new JsonObject { ["id"] = "u1", ["displayName"] = "Emergency Access 01", ["userPrincipalName"] = "ea01@contoso.example" }));

        Assert.Equal("Emergency Access 01 (ea01@contoso.example) [u1]", resolver.Display("u1"));
    }

    /// <summary>Conditional Access names roles by template ID, not by the directory role's object ID.</summary>
    [Fact]
    public void A_directory_role_resolves_by_its_template_id()
    {
        var resolver = NameResolver.FromSnapshot(Capture("directoryRoles",
            new JsonObject { ["id"] = "role-object", ["roleTemplateId"] = "62e90394-69f5-4237-9190-012177145e10", ["displayName"] = "Global Administrator" }));

        Assert.Equal("Global Administrator [62e90394-69f5-4237-9190-012177145e10]", resolver.Display("62e90394-69f5-4237-9190-012177145e10"));
    }

    [Fact]
    public void Policies_and_other_captured_objects_resolve_too()
    {
        var resolver = NameResolver.FromSnapshot(Capture("settingsCatalogue",
            new JsonObject { ["id"] = "p1", ["name"] = "M365 - Windows BitLocker" }));

        Assert.Equal("M365 - Windows BitLocker [p1]", resolver.Display("p1"));
    }

    [Fact]
    public void Recorded_exclusion_accounts_resolve_without_a_users_capture()
    {
        var profile = new TenantProfile
        {
            ExclusionAccounts = { new ExclusionAccount { ObjectId = "e1", DisplayName = "Break Glass 02", UserPrincipalName = "bg02@contoso.example" } }
        };

        var resolver = NameResolver.FromSnapshot(new TenantSnapshot(), profile);

        Assert.Equal("Break Glass 02 (bg02@contoso.example) [e1]", resolver.Display("e1"));
    }

    [Fact]
    public void A_directory_name_wins_over_the_same_id_in_another_collection()
    {
        var snapshot = Capture("groups", new JsonObject { ["id"] = "g1", ["displayName"] = "GRP - Pilot Devices" });
        snapshot.Collections["configuration"] = new CollectionCapture { Status = CaptureStatus.Collected, Items = { new JsonObject { ["id"] = "g1", ["displayName"] = "Something else" } } };

        Assert.Equal("GRP - Pilot Devices [g1]", NameResolver.FromSnapshot(snapshot).Display("g1"));
    }
}
