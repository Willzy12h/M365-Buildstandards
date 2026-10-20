using System.Net;
using System.Text;
using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Graph;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Graph;
using BuildStandard.Graph.Auth;
using BuildStandard.Graph.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CollaborationTransportTests
{
    private static TenantSession Session(params string[] scopes){var s=TestData.Session(mode:SessionMode.Assessment);s.Scopes=scopes.ToList();return s;}
    private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
    [Fact]public async Task Selected_site_singleton_admits_only_its_existing_library_collection()
    {
        var paths=new List<string>();var tokens=new Tokens();var selection=new CollaborationReadSelection(null,["opaque-site"],[],true,false,false);
        using var http=new HttpClient(new Handler(r=>{paths.Add(r.RequestUri!.OriginalString);return paths.Count==1?Json(new{id="opaque-site"}):Json(new{value=Array.Empty<object>()});}));
        using var source=new OperationalReportTransport(http,tokens,Session("Sites.Read.All"),"collaboration-inventory",()=>{},collaboration:selection);
        Assert.Single((await source.ReadCollaborationNextAsync(CollaborationReadKind.SiteMetadata,"opaque-site",default)).Items);
        Assert.Empty((await source.ReadCollaborationNextAsync(CollaborationReadKind.SiteLibraries,"opaque-site",default)).Items);
        Assert.Equal(2,paths.Count);Assert.Contains("/sites/opaque-site/drives?",paths[1]);
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>source.ReadCollaborationNextAsync(CollaborationReadKind.SiteLibraries,"opaque-site",default));Assert.Equal(2,tokens.Calls);
    }
    [Fact]public async Task Unsupported_distribution_owner_population_refuses_children_without_another_token()
    {
        var calls=0;var tokens=new Tokens();var selection=new CollaborationReadSelection(null,[],[TestData.TenantA],false,true,true);
        using var http=new HttpClient(new Handler(_=>{calls++;return Json(new{id=TestData.TenantA,groupTypes=Array.Empty<string>(),mailEnabled=true,securityEnabled=false,onPremisesSyncEnabled=false,resourceProvisioningOptions=Array.Empty<string>()});}));
        using var source=new OperationalReportTransport(http,tokens,Session("Group.Read.All","TeamMember.Read.All"),"collaboration-inventory",()=>{},collaboration:selection);
        await source.ReadCollaborationNextAsync(CollaborationReadKind.GroupMetadata,TestData.TenantA,default);
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>source.ReadCollaborationNextAsync(CollaborationReadKind.GroupOwners,TestData.TenantA,default));
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>source.ReadCollaborationNextAsync(CollaborationReadKind.TeamMembers,TestData.TenantA,default));Assert.Equal(1,calls);Assert.Equal(1,tokens.Calls);
    }
    [Fact]public async Task Actually_provisioned_selected_team_admits_members_without_site_scope()
    {
        var calls=0;var selection=new CollaborationReadSelection(null,[],[TestData.TenantA],false,false,true);
        using var http=new HttpClient(new Handler(_=>++calls==1?Json(new{id=TestData.TenantA,resourceProvisioningOptions=new[]{"Team"}}):Json(new{value=Array.Empty<object>()})));
        using var source=new OperationalReportTransport(http,new Tokens(),Session("Group.Read.All","TeamMember.Read.All"),"collaboration-inventory",()=>{},collaboration:selection);
        await source.ReadCollaborationNextAsync(CollaborationReadKind.GroupMetadata,TestData.TenantA,default);
        await source.ReadCollaborationNextAsync(CollaborationReadKind.TeamMembers,TestData.TenantA,default);Assert.Equal(2,calls);
    }
    [Theory][InlineData("unselected")][InlineData("child-before-parent")][InlineData("unknown-kind")]
    public async Task Unselected_or_unadmitted_source_never_authenticates(string fault)
    {
        var tokens=new Tokens();var calls=0;var selection=new CollaborationReadSelection(null,["site"],[],true,false,false);
        using var http=new HttpClient(new Handler(_=>{calls++;return Json(new{value=Array.Empty<object>()});}));
        using var source=new OperationalReportTransport(http,tokens,Session("Sites.Read.All"),"collaboration-inventory",()=>{},collaboration:selection);
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>source.ReadCollaborationNextAsync(fault=="unknown-kind"?(CollaborationReadKind)99:fault=="unselected"?CollaborationReadKind.SiteMetadata:CollaborationReadKind.SiteLibraries,fault=="unselected"?"other":"site",default));
        Assert.Equal(0,calls);Assert.Equal(0,tokens.Calls);
    }
    [Theory][InlineData("wrong-scope")][InlineData("wrong-report")][InlineData("missing-selection")]
    public void Invalid_purpose_refuses_before_tokens(string fault)
    {
        var tokens=new Tokens();using var http=new HttpClient(new Handler(_=>throw new InvalidOperationException("No request expected")));
        Assert.ThrowsAny<ToolkitException>(()=>new OperationalReportTransport(http,tokens,Session(fault=="wrong-scope"?"ServiceHealth.Read.All":"Sites.Read.All"),fault=="wrong-report"?"service-health":"collaboration-inventory",()=>{},collaboration:fault=="missing-selection"?null:new(null,["site"],[],false,false,false)));Assert.Equal(0,tokens.Calls);
    }
    [Theory][InlineData("omitted")][InlineData("changed")][InlineData("foreign")][InlineData("expanded")]
    public async Task Search_continuation_cannot_drop_or_expand_the_frozen_search(string fault)
    {
        var calls=0;var tokens=new Tokens();var selection=new CollaborationReadSelection("*",[],[],false,false,false);
        var next="https://graph.microsoft.com/v1.0/sites?$skiptoken=next"+(fault=="omitted"?"":"&search="+(fault=="changed"?"other":"%2A"));
        if(fault=="foreign")next=next.Replace("graph.microsoft.com","evil.example");if(fault=="expanded")next+="&$expand=permissions";
        using var http=new HttpClient(new Handler(_=>{calls++;return Json(new Dictionary<string,object>{{"value",Array.Empty<object>()},{"@odata.nextLink",next}});}));
        using var source=new OperationalReportTransport(http,tokens,Session("Sites.Read.All"),"collaboration-inventory",()=>{},collaboration:selection);
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>source.ReadCollaborationNextAsync(CollaborationReadKind.SiteSearch,"*",default));Assert.Equal(1,calls);Assert.Equal(1,tokens.Calls);
    }
    [Fact]public async Task Search_pagination_keeps_the_exact_search_and_refuses_replay()
    {
        var calls=0;var selection=new CollaborationReadSelection("*",[],[],false,false,false);
        using var http=new HttpClient(new Handler(_=>++calls==1?Json(new Dictionary<string,object>{{"value",Array.Empty<object>()},{"@odata.nextLink","https://graph.microsoft.com/v1.0/sites?search=%2A&$skiptoken=next"}}):Json(new{value=Array.Empty<object>()})));
        using var source=new OperationalReportTransport(http,new Tokens(),Session("Sites.Read.All"),"collaboration-inventory",()=>{},collaboration:selection);
        Assert.True((await source.ReadCollaborationNextAsync(CollaborationReadKind.SiteSearch,"*",default)).HasMore);
        Assert.False((await source.ReadCollaborationNextAsync(CollaborationReadKind.SiteSearch,"*",default)).HasMore);
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>source.ReadCollaborationNextAsync(CollaborationReadKind.SiteSearch,"*",default));Assert.Equal(2,calls);
    }
    [Fact]public async Task Mismatched_parent_cannot_admit_libraries_or_replay_its_metadata()
    {
        var calls=0;var tokens=new Tokens();using var http=new HttpClient(new Handler(_=>{calls++;return Json(new{id="other-site"});}));
        using var source=new OperationalReportTransport(http,tokens,Session("Sites.Read.All"),"collaboration-inventory",()=>{},collaboration:new(null,["site"],[],true,false,false));
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>source.ReadCollaborationNextAsync(CollaborationReadKind.SiteMetadata,"site",default));
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>source.ReadCollaborationNextAsync(CollaborationReadKind.SiteLibraries,"site",default));
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>source.ReadCollaborationNextAsync(CollaborationReadKind.SiteMetadata,"site",default));Assert.Equal(1,calls);Assert.Equal(1,tokens.Calls);
    }
    [Fact]public void Existing_graph_factory_reuses_source_identity_without_token_acquisition()
    {
        var tokens=new Tokens();using var http=new HttpClient(new Handler(_=>throw new InvalidOperationException("No request expected")));
        var graph=new GraphClient(http,tokens,TestData.TenantA,SessionMode.Assessment,GraphRouteAllowList.ForReports(),new(),NullLog.Instance);
        var selection=new CollaborationReadSelection("*",[],[],false,false,false);
        using var source=graph.ForCollaborationReports(Session("Sites.Read.All"),selection,()=>{});
        Assert.Equal(TestData.TenantA,source.TenantId);Assert.Equal("collaboration-inventory",source.ReportId);Assert.Equal(0,tokens.Calls);
        var wrong=Session("Sites.Read.All");wrong.TenantId=TestData.TenantB;
        Assert.Throws<TenantMismatchException>(()=>graph.ForCollaborationReports(wrong,selection,()=>{}));
        wrong.TenantId=TestData.TenantA;wrong.Mode=SessionMode.Deployment;
        Assert.Throws<TenantMismatchException>(()=>graph.ForCollaborationReports(wrong,selection,()=>{}));Assert.Equal(0,tokens.Calls);
    }
    private sealed class Tokens:IAccessTokenProvider
    {public int Calls;public Task<string>GetAccessTokenAsync(CancellationToken ct)=>GetAccessTokenAsync(false,ct);public Task<string>GetAccessTokenAsync(bool refresh,CancellationToken ct){ct.ThrowIfCancellationRequested();Calls++;return Task.FromResult("synthetic-token");}}
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage>SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(send(request));}}
}
