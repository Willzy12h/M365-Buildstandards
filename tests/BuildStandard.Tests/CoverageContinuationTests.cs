using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CoverageContinuationTests
{
    private static PreparedCoverageRead Request()=>DeploymentCoverageReads.Group(TestData.Office,true);
    private static string Root=>"https://graph.microsoft.com/v1.0/groups/"+TestData.Office+"/members";
    [Fact] public void Exact_relation_continuation_keeps_the_registered_projection_and_identity()
    {var request=Request();var next=DeploymentCoverageReads.Continue(request,Root+"?$skiptoken=opaque%2Btoken");Assert.Equal(request.Scope,next.Scope);Assert.Equal(request.TargetId,next.TargetId);Assert.True(next.IsCollection);Assert.True(next.IsGroupMembers);Assert.Equal(request.Path+"&$skiptoken=opaque%2Btoken",next.Path);}
    [Theory][InlineData("http://graph.microsoft.com")][InlineData("https://evil.invalid")][InlineData("https://graph.microsoft.com.evil.invalid")][InlineData("https://user@graph.microsoft.com")][InlineData("https://graph.microsoft.com:444")]
    public void Host_scheme_port_or_credentials_are_refused(string origin)
    {Assert.Throws<ConfigurationException>(()=>DeploymentCoverageReads.Continue(Request(),origin+"/v1.0/groups/"+TestData.Office+"/members?$skiptoken=x"));}
    [Theory][InlineData("/beta/groups/")][InlineData("/v1.0/users/")]
    public void API_or_relation_cannot_change(string prefix)
    {Assert.Throws<ConfigurationException>(()=>DeploymentCoverageReads.Continue(Request(),"https://graph.microsoft.com"+prefix+TestData.Office+"/members?$skiptoken=x"));}
    [Theory][InlineData("?$skiptoken=x&$expand=members")][InlineData("?$skiptoken=x&$select=id,displayName")][InlineData("?$skiptoken=x&$top=999")][InlineData("?$skiptoken=x&$filter=id%20ne%20null")][InlineData("?$skiptoken=x&$skiptoken=y")][InlineData("?$skiptoken=x&%24SKIPTOKEN=y")][InlineData("?$skiptoken=x&$skip=1")][InlineData("?$top=100")][InlineData("?$skiptoken=")][InlineData("?$skip=-1")][InlineData("?$skip=1.0")][InlineData("?$skiptoken=x%0Ay")][InlineData("?$skiptoken=x#fragment")]
    public void Added_or_changed_query_and_malformed_continuations_are_refused(string query)
    {Assert.Throws<ConfigurationException>(()=>DeploymentCoverageReads.Continue(Request(),Root+query));}
    [Fact] public void Same_original_select_and_top_are_allowed_but_never_expanded()
    {var next=DeploymentCoverageReads.Continue(Request(),Root+"?$select=id&$top=100&$skip=100");Assert.Equal(Request().Path+"&$skip=100",next.Path);}
    [Fact] public void A_selected_object_cannot_become_a_paged_collection()
    {var request=DeploymentCoverageReads.Prepare(CoverageFamily.ClassicCompliance,TestData.Office,CoverageReadKind.Object);Assert.Throws<ConfigurationException>(()=>DeploymentCoverageReads.Continue(request,"https://graph.microsoft.com/v1.0"+request.Path+"?$skiptoken=x"));}
    [Fact] public void Next_continuation_replaces_the_previous_cursor_without_widening_the_projection()
    {var first=DeploymentCoverageReads.Continue(Request(),Root+"?$skiptoken=first");var second=DeploymentCoverageReads.Continue(first,Root+"?$skiptoken=second");Assert.Equal(Request().Path+"&$skiptoken=second",second.Path);}
    [Theory][InlineData("/members/other")][InlineData("/transitiveMembers")][InlineData("/members/$count")]
    public void A_different_child_relation_is_refused(string suffix)
    {Assert.Throws<ConfigurationException>(()=>DeploymentCoverageReads.Continue(Request(),"https://graph.microsoft.com/v1.0/groups/"+TestData.Office+suffix+"?$skiptoken=x"));}
}
