using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CollaborationInventoryRegistryTests
{
    [Fact] public void Search_is_one_bounded_encoded_value_not_an_odata_expression()
    {
        const string text="Finance & legal?x=$expand=permissions";
        Assert.Equal("/sites?search="+Uri.EscapeDataString(text)+"&$select=id,name,displayName,webUrl,siteCollection,createdDateTime,lastModifiedDateTime",CollaborationInventoryRegistry.Path(CollaborationReadKind.SiteSearch,text));
        Assert.Contains("search=%2A",CollaborationInventoryRegistry.Path(CollaborationReadKind.SiteSearch,"*"));
    }
    [Theory][InlineData(CollaborationReadKind.SiteMetadata,"")][InlineData(CollaborationReadKind.SiteLibraries,"/drives")]
    public void Opaque_composite_site_identity_is_preserved_as_one_segment(CollaborationReadKind kind,string suffix)
    {
        const string id="synthetic.sharepoint.com,aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee,11111111-2222-3333-4444-555555555555";
        var path=CollaborationInventoryRegistry.Path(kind,id);Assert.StartsWith("/sites/"+Uri.EscapeDataString(id)+suffix+"?$select=",path);
        Assert.Equal("Sites.Read.All",CollaborationInventoryRegistry.Scope(kind));
    }
    [Theory][InlineData(CollaborationReadKind.GroupMetadata,"/groups/","")][InlineData(CollaborationReadKind.GroupOwners,"/groups/","/owners")][InlineData(CollaborationReadKind.TeamMembers,"/teams/","/members")]
    public void Group_and_team_routes_require_exact_guid_and_fixed_projection(CollaborationReadKind kind,string prefix,string suffix)
    {
        Assert.StartsWith(prefix+TestData.TenantA+suffix+"?$select=",CollaborationInventoryRegistry.Path(kind,TestData.TenantA));
        Assert.Equal(kind==CollaborationReadKind.TeamMembers?"TeamMember.Read.All":"Group.Read.All",CollaborationInventoryRegistry.Scope(kind));
    }
    [Theory][InlineData("")][InlineData(" ")][InlineData("../permissions")][InlineData("a/b")][InlineData("a%2fb")][InlineData("a?x")][InlineData("a#x")][InlineData("a\\b")][InlineData("a\nb")]
    public void Untrusted_site_path_shapes_cannot_select_content_permissions_or_other_routes(string value)
    {Assert.Throws<ConfigurationException>(()=>CollaborationInventoryRegistry.Path(CollaborationReadKind.SiteMetadata,value));}
    [Theory][InlineData(" ")][InlineData("x\ny")]
    public void Blank_or_control_search_is_refused(string value)
    {Assert.Throws<ConfigurationException>(()=>CollaborationInventoryRegistry.Path(CollaborationReadKind.SiteSearch,value));}
    [Fact]public void Search_and_identity_budgets_and_unknown_routes_refuse()
    {
        Assert.Throws<ConfigurationException>(()=>CollaborationInventoryRegistry.Path(CollaborationReadKind.SiteSearch,new string('x',257)));
        Assert.Throws<ConfigurationException>(()=>CollaborationInventoryRegistry.Path(CollaborationReadKind.SiteMetadata,new string('x',513)));
        Assert.Throws<ConfigurationException>(()=>CollaborationInventoryRegistry.Path(CollaborationReadKind.GroupOwners,"display-name"));
        Assert.Throws<ConfigurationException>(()=>CollaborationInventoryRegistry.Path((CollaborationReadKind)99,TestData.TenantA));
        Assert.Throws<ConfigurationException>(()=>CollaborationInventoryRegistry.Scope((CollaborationReadKind)99));
    }
}
