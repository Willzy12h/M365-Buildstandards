using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CollaborationReadSelectionTests
{
    [Fact]public void Exact_selection_is_frozen_against_caller_list_changes_and_does_not_add_unselected_scopes()
    {
        var sites=new List<string>{"opaque-site"};var groups=new List<string>{TestData.TenantA};
        var selection=new CollaborationReadSelection(null,sites,groups,true,true,false);sites[0]="other-site";groups.Clear();
        Assert.Equal(new[]{"opaque-site"},selection.Sites);Assert.Equal(new[]{TestData.TenantA},selection.Groups);
        Assert.Equal(new[]{"Sites.Read.All","Group.Read.All"},selection.Scopes());
        Assert.StartsWith("/sites/opaque-site/drives?",selection.RequirePath(CollaborationReadKind.SiteLibraries,"opaque-site"));
        Assert.Throws<ConfigurationException>(()=>selection.RequirePath(CollaborationReadKind.SiteLibraries,"other-site"));
        Assert.Throws<ConfigurationException>(()=>selection.RequirePath(CollaborationReadKind.TeamMembers,TestData.TenantA));
    }
    [Fact]public void Search_only_cannot_admit_selected_site_or_group_children()
    {
        var selection=new CollaborationReadSelection("*",[],[],false,false,false);
        Assert.Equal(new[]{"Sites.Read.All"},selection.Scopes());Assert.Contains("search=%2A",selection.RequirePath(CollaborationReadKind.SiteSearch,"*"));
        Assert.Throws<ConfigurationException>(()=>selection.RequirePath(CollaborationReadKind.SiteSearch,"changed"));
        Assert.Throws<ConfigurationException>(()=>selection.RequirePath(CollaborationReadKind.SiteMetadata,"unselected"));
        Assert.Throws<ConfigurationException>(()=>selection.RequirePath(CollaborationReadKind.GroupOwners,TestData.TenantA));
    }
    [Fact]public void Team_section_needs_group_metadata_and_its_own_scope_without_site_access()
    {
        var selection=new CollaborationReadSelection(null,[],[TestData.TenantA],false,false,true);
        Assert.Equal(new[]{"Group.Read.All","TeamMember.Read.All"},selection.Scopes());
        Assert.StartsWith("/teams/",selection.RequirePath(CollaborationReadKind.TeamMembers,TestData.TenantA));
        Assert.Throws<ConfigurationException>(()=>selection.RequirePath(CollaborationReadKind.GroupOwners,TestData.TenantA));
    }
    [Theory][InlineData("duplicate-site")][InlineData("duplicate-group")][InlineData("budget")][InlineData("empty")][InlineData("bad-site")][InlineData("bad-group")]
    public void Empty_ambiguous_unsafe_or_over_budget_selection_refuses(string fault)
    {
        var sites=fault switch{"duplicate-site"=>new[]{"site","site"},"budget"=>Enumerable.Range(0,51).Select(i=>"site"+i).ToArray(),"bad-site"=>new[]{"../permissions"},_=>Array.Empty<string>()};
        var groups=fault switch{"duplicate-group"=>new[]{TestData.TenantA,TestData.TenantA},"bad-group"=>new[]{"name"},_=>Array.Empty<string>()};
        Assert.Throws<ConfigurationException>(()=>new CollaborationReadSelection(null,sites,groups,false,false,false));
    }
}
