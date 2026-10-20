using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CollaborationSelectionInputTests
{
    [Fact]public void Newline_ids_preserve_opaque_commas_and_case_without_splitting_sharepoint_identity()
    {
        var choice=CollaborationSelectionInput.Parse("", " Example.invalid,SiteGuid,WebGuid\r\nsecond-site ",TestData.TenantA,true,true,true);
        Assert.Equal(new[]{"Example.invalid,SiteGuid,WebGuid","second-site"},choice.Sites);Assert.Equal(new[]{TestData.TenantA},choice.Groups);Assert.Null(choice.Search);
    }
    [Fact]public void Search_only_does_not_request_group_or_member_access()
    {
        var choice=CollaborationSelectionInput.Parse(" sales & support ","","",false,false,false);
        Assert.Equal("sales & support",choice.Search);Assert.Equal(new[]{"Sites.Read.All"},choice.Scopes());
    }
    [Theory][InlineData("duplicate")][InlineData("empty")][InlineData("fifty-one")][InlineData("path")][InlineData("oversized")]
    public void Invalid_or_unbounded_selection_refuses_without_silent_clipping(string fault)
    {
        var sites=fault=="duplicate"?"same\nsame":fault=="fifty-one"?string.Join('\n',Enumerable.Range(0,51).Select(i=>"site"+i)):fault=="path"?"/users/id/drive":fault=="oversized"?new string('a',32769):"";
        Assert.ThrowsAny<ToolkitException>(()=>CollaborationSelectionInput.Parse("",sites,"",false,false,false));
    }
}
