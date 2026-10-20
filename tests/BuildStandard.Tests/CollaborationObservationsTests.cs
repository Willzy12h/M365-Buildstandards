using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CollaborationObservationsTests
{
    [Fact]public void Site_preserves_original_metadata_and_never_derives_ownership_from_host()
    {
        var raw=new JsonObject{["id"]="synthetic.sharepoint.com,opaque-site",["name"]="returned-name",["displayName"]="Synthetic site",["siteCollection"]=new JsonObject{["hostname"]="synthetic.sharepoint.com"},["webUrl"]="https://synthetic.sharepoint.com/sites/reference",["createdDateTime"]="2026-10-01T00:00:00Z",["lastModifiedDateTime"]="2026-10-02T00:00:00Z"};
        var row=CollaborationObservations.Site(raw);Assert.Equal(raw["id"]!.GetValue<string>(),row.Id);Assert.Equal("returned-name",row.ReportedName);Assert.Equal("Synthetic site",row.Name);Assert.Equal("synthetic.sharepoint.com",row.SiteCollectionHost);Assert.Equal("Collected",row.ReadStatus);
        Assert.Throws<TenantMismatchException>(()=>CollaborationObservations.Site(raw,"other-site"));
        raw.Remove("siteCollection");row=CollaborationObservations.Site(raw);Assert.Null(row.SiteCollectionHost);Assert.Equal("Partial",row.ReadStatus);
    }
    [Fact]public void Library_preserves_exact_parent_drive_and_reported_owner_ids_without_inferring_membership()
    {
        var raw=new JsonObject{["id"]="opaque-drive",["name"]="Synthetic library",["driveType"]="documentLibrary",["webUrl"]="https://synthetic.sharepoint.com/library",["owner"]=new JsonObject{["group"]=new JsonObject{["id"]="opaque-returned-owner",["displayName"]="Reported group"}}};
        var row=CollaborationObservations.Library(raw,"opaque-site");Assert.Equal("opaque-drive",row.DriveId);Assert.Equal("opaque-site",row.SiteId);Assert.Equal("opaque-returned-owner",row.OwnerGroupId);Assert.Null(row.OwnerUserId);Assert.Equal("Collected",row.ReadStatus);
        Assert.NotEqual(row.Id,CollaborationObservations.Library(raw,"other-site").Id);
        raw.Remove("owner");row=CollaborationObservations.Library(raw,"opaque-site");Assert.Null(row.OwnerGroupId);Assert.Null(row.OwnerUserId);Assert.Equal("Partial",row.ReadStatus);
    }
    [Theory][InlineData("#microsoft.graph.user")][InlineData("#microsoft.graph.futureObject")]
    public void Owner_keeps_exact_returned_type_and_population_limits_even_for_limited_information(string type)
    {
        var raw=new JsonObject{["id"]=TestData.Operator,["@odata.type"]=type};var row=CollaborationObservations.Owner(raw,TestData.TenantA);
        Assert.Equal(TestData.Operator,row.ObjectId);Assert.Equal(type,row.ResourceType);Assert.Null(row.Name);Assert.Equal("Partial",row.ReadStatus);Assert.Contains("service-principal",row.Coverage);Assert.Contains("Exchange",row.Coverage);
    }
    private static JsonObject Group() => new()
    {
        ["id"]=TestData.TenantA,["displayName"]="Synthetic group",["groupTypes"]=new JsonArray("Unified"),
        ["mailEnabled"]=true,["securityEnabled"]=false,["onPremisesSyncEnabled"]=false,
        ["resourceProvisioningOptions"]=new JsonArray("Team"),["creationOptions"]=new JsonArray()
    };
    private static JsonObject Member() => new()
    {
        ["id"]="opaque-member",["@odata.type"]="#microsoft.graph.aadUserConversationMember",["displayName"]="Synthetic member",
        ["userId"]=TestData.Operator,["tenantId"]=TestData.TenantB,["roles"]=new JsonArray()
    };
    [Fact]public void Documented_null_sync_flag_is_never_synced_but_omission_remains_unknown()
    {
        var raw=Group();raw["onPremisesSyncEnabled"]=null;Assert.Equal("NeverSynced",CollaborationObservations.Group(raw,TestData.TenantA).SyncMeaning);
        raw.Remove("onPremisesSyncEnabled");Assert.Equal("Unknown",CollaborationObservations.Group(raw,TestData.TenantA).SyncMeaning);
        raw["onPremisesSyncEnabled"]=false;Assert.Equal("PreviouslySynced",CollaborationObservations.Group(raw,TestData.TenantA).SyncMeaning);
        raw["onPremisesSyncEnabled"]=true;Assert.Equal("Synced",CollaborationObservations.Group(raw,TestData.TenantA).SyncMeaning);
    }
    [Fact]public void Cloud_group_never_claims_complete_owner_coverage_or_known_non_exchange_provenance()
    {
        var row=CollaborationObservations.Group(Group(),TestData.TenantA);
        Assert.Equal("ReadWithCoverageLimits",row.OwnerReadDisposition);Assert.Equal("Partial",row.ReadStatus);
        Assert.Contains("service-principal",row.OwnerCoverage);Assert.Contains("Exchange",row.OwnerCoverage);Assert.True(row.TeamProvisioned);
    }
    [Theory][InlineData("synchronised")][InlineData("distribution")]
    public void Identified_unsupported_owner_population_blocks_owner_enumeration(string population)
    {
        var raw=Group();if(population=="synchronised")raw["onPremisesSyncEnabled"]=true;else raw["groupTypes"]=new JsonArray();
        var row=CollaborationObservations.Group(raw,TestData.TenantA);Assert.Equal("BlockedUnsupportedPopulation",row.OwnerReadDisposition);Assert.Equal("Partial",row.ReadStatus);
    }
    [Fact]public void Missing_group_flags_and_provisioning_never_become_false_or_known_no_team()
    {
        var raw=Group();raw.Remove("onPremisesSyncEnabled");raw.Remove("resourceProvisioningOptions");raw.Remove("mailEnabled");
        var row=CollaborationObservations.Group(raw,TestData.TenantA);Assert.Null(row.OnPremisesSyncEnabled);Assert.Null(row.MailEnabled);Assert.Null(row.TeamProvisioned);Assert.Equal("Partial",row.ReadStatus);
    }
    [Theory][InlineData("future")][InlineData("duplicate")][InlineData("empty")]
    public void Future_or_duplicate_provisioning_options_are_unknown_but_returned_empty_is_known(string shape)
    {
        var raw=Group();raw["resourceProvisioningOptions"]=shape=="future"?new JsonArray("futureProvisioning"):shape=="duplicate"?new JsonArray("Team","Team"):new JsonArray();
        var row=CollaborationObservations.Group(raw,TestData.TenantA);
        if(shape=="empty")Assert.False(row.TeamProvisioned);else Assert.Null(row.TeamProvisioned);
    }
    [Theory][InlineData("owner","Owner")][InlineData("guest","Guest")][InlineData("futureRole","Unclassified")]
    public void Roles_come_only_from_returned_values_and_future_roles_remain_partial(string role,string meaning)
    {
        var raw=Member();raw["roles"]=new JsonArray(role);var row=CollaborationObservations.Member(raw,TestData.TenantA);
        Assert.Equal(meaning,row.RoleMeaning);Assert.Equal(new[]{role},row.Roles);Assert.Equal(role=="futureRole"?"Partial":"Collected",row.ReadStatus);
    }
    [Fact]public void Other_tenant_is_not_guest_when_returned_roles_are_empty()
    {var row=CollaborationObservations.Member(Member(),TestData.TenantA);Assert.Equal("Member",row.RoleMeaning);Assert.Equal(TestData.TenantB,row.UserTenantId);Assert.Equal("Collected",row.ReadStatus);}
    [Theory][InlineData(false)][InlineData(true)]
    public void Missing_or_null_roles_are_unknown_not_empty_member(bool missing)
    {var raw=Member();if(missing)raw.Remove("roles");else raw["roles"]=null;var row=CollaborationObservations.Member(raw,TestData.TenantA);Assert.Null(row.Roles);Assert.Equal("Unknown",row.RoleMeaning);Assert.Equal("Partial",row.ReadStatus);}
    [Fact]public void Unsupported_member_type_and_limited_identity_do_not_guess_user_or_role()
    {var raw=Member();raw["@odata.type"]="#microsoft.graph.futureMember";raw.Remove("userId");var row=CollaborationObservations.Member(raw,TestData.TenantA);Assert.Null(row.UserId);Assert.Equal("Unclassified",row.RoleMeaning);Assert.Equal("Partial",row.ReadStatus);}
    [Fact]public void Parent_binding_and_oversized_roles_are_refused_or_explicitly_unknown()
    {
        Assert.Throws<TenantMismatchException>(()=>CollaborationObservations.Group(Group(),TestData.TenantB));
        Assert.Throws<ConfigurationException>(()=>CollaborationObservations.Member(Member(),"name"));
        var raw=Member();raw["roles"]=new JsonArray(Enumerable.Range(0,101).Select(i=>JsonValue.Create("role"+i)).ToArray<JsonNode?>());
        var row=CollaborationObservations.Member(raw,TestData.TenantA);Assert.Null(row.Roles);Assert.Equal("Partial",row.ReadStatus);
    }
}
