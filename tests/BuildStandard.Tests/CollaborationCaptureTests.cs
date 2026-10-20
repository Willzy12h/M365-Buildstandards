using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CollaborationCaptureTests
{
    private static TenantSession Session(){var s=TestData.Session(mode:SessionMode.Assessment);s.Scopes=["Sites.Read.All","Group.Read.All","TeamMember.Read.All"];return s;}
    private static JsonObject Site(string id)=>new(){["id"]=id,["displayName"]="Site",["name"]="site",["siteCollection"]=new JsonObject{["hostname"]="example.invalid"},["webUrl"]="https://example.invalid",["createdDateTime"]="2026-10-20T00:00:00Z",["lastModifiedDateTime"]="2026-10-20T00:00:00Z"};
    private static JsonObject Group(bool synced=false)=>new(){["id"]=TestData.TenantA,["displayName"]="Group",["groupTypes"]=new JsonArray("Unified"),["mailEnabled"]=true,["securityEnabled"]=false,["onPremisesSyncEnabled"]=synced,["resourceProvisioningOptions"]=new JsonArray("Team")};
    private static OperationalReportPage Page(params JsonObject[] rows)=>new(rows,false);
    [Fact]public async Task Selected_parents_are_read_before_children_and_successful_empty_is_zero()
    {
        var source=new Source(new(null,["site"],[TestData.TenantA],true,true,true),(k,_)=>Task.FromResult(k==CollaborationReadKind.SiteMetadata?Page(Site("site")):k==CollaborationReadKind.GroupMetadata?Page(Group()):Page()));
        var result=await new CollaborationInventoryCapture().CaptureAsync(source,Session(),()=>{},default);
        Assert.Equal(new[]{CollaborationReadKind.SiteMetadata,CollaborationReadKind.SiteLibraries,CollaborationReadKind.GroupMetadata,CollaborationReadKind.GroupOwners,CollaborationReadKind.TeamMembers},source.Calls);
        Assert.Equal(0,result.Sources.Single(s=>s.Kind==CollaborationReadKind.SiteLibraries).ReturnedCount);
        var owners=result.Sources.Single(s=>s.Kind==CollaborationReadKind.GroupOwners);
        Assert.Equal(ReportReadState.Partial,owners.Status);Assert.Contains("service-principal",owners.Error);Assert.Equal(0,owners.ReturnedCount);
        Assert.Equal(ReportReadState.Collected,result.Sources.Single(s=>s.Kind==CollaborationReadKind.TeamMembers).Status);
    }
    [Fact]public async Task Failed_parent_blocks_child_but_keeps_other_independent_sources()
    {
        var source=new Source(new(null,["bad","good"],[],true,false,false),(k,v)=>v=="bad"?throw new ConfigurationException("private service error"):Task.FromResult(k==CollaborationReadKind.SiteMetadata?Page(Site("good")):Page()));
        var result=await new CollaborationInventoryCapture().CaptureAsync(source,Session(),()=>{},default);
        Assert.Equal(3,source.Calls.Count);Assert.Equal(ReportReadState.Failed,result.Sources[0].Status);Assert.Null(result.Sources[0].ReturnedCount);
        Assert.Equal(ReportReadState.NotAttempted,result.Sources[1].Status);Assert.Null(result.Sources[1].ReturnedCount);
        Assert.Equal(ReportReadState.Collected,result.Sources[3].Status);Assert.DoesNotContain("private",result.Sources[0].Error);
    }
    [Fact]public async Task Stop_keeps_prior_page_and_later_sources_unattempted()
    {
        using var stop=new CancellationTokenSource();var calls=0;
        var source=new Source(new("*",["site"],[],false,false,false),(_,_)=>{if(++calls==1)return Task.FromResult(new OperationalReportPage([Site("earlier")],true));stop.Cancel();throw new OperationCanceledException(stop.Token);});
        var result=await new CollaborationInventoryCapture().CaptureAsync(source,Session(),()=>{},stop.Token);
        Assert.Single(result.Sources[0].Rows);Assert.Equal(ReportReadState.Partial,result.Sources[0].Status);Assert.Null(result.Sources[0].ReturnedCount);Assert.Equal(ReportReadState.NotAttempted,result.Sources[1].Status);Assert.Equal(2,source.Calls.Count);
    }
    [Fact]public async Task Unsupported_owner_population_never_requests_owners()
    {
        var source=new Source(new(null,[],[TestData.TenantA],false,true,false),(_,_)=>Task.FromResult(Page(Group(true))));
        var result=await new CollaborationInventoryCapture().CaptureAsync(source,Session(),()=>{},default);
        Assert.Single(source.Calls);Assert.Equal(ReportReadState.NotAttempted,result.Sources[1].Status);Assert.Contains("unsupported",result.Sources[1].Error);Assert.Null(result.Sources[1].ReturnedCount);
    }
    [Fact]public async Task Duplicate_search_rows_are_retained_and_explicitly_partial()
    {
        var source=new Source(new("*",[],[],false,false,false),(_,_)=>Task.FromResult(Page(Site("same"),Site("same"))));
        var result=await new CollaborationInventoryCapture().CaptureAsync(source,Session(),()=>{},default);
        Assert.Equal(2,result.Sources[0].Rows.Count);Assert.Equal(ReportReadState.Partial,result.Sources[0].Status);Assert.All(result.Sources[0].Rows,r=>Assert.Equal(ReportReadState.Partial,r.ReadStatus));
    }
    [Fact]public async Task Wrong_returned_parent_never_admits_results_or_children()
    {
        var source=new Source(new(null,["site"],[],true,false,false),(_,_)=>Task.FromResult(Page(Site("other"))));
        await Assert.ThrowsAsync<TenantMismatchException>(()=>new CollaborationInventoryCapture().CaptureAsync(source,Session(),()=>{},default));Assert.Single(source.Calls);
    }
    [Fact]public async Task Context_change_after_await_refuses_results()
    {
        var current=true;var source=new Source(new("*",[],[],false,false,false),(_,_)=>{current=false;return Task.FromResult(Page());});
        await Assert.ThrowsAsync<TenantMismatchException>(()=>new CollaborationInventoryCapture().CaptureAsync(source,Session(),()=>{if(!current)throw new TenantMismatchException("changed");},default));Assert.Single(source.Calls);
    }
    [Theory][InlineData("tenant")][InlineData("account")][InlineData("scope")]
    public async Task Invalid_binding_refuses_before_read(string fault)
    {
        var session=Session();if(fault=="tenant")session.TenantId=TestData.TenantB;if(fault=="account")session.OperatorObjectId=TestData.TenantB;if(fault=="scope")session.Scopes=[];
        var source=new Source(new("*",[],[],false,false,false),(_,_)=>Task.FromResult(Page()));
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>new CollaborationInventoryCapture().CaptureAsync(source,session,()=>{},default));Assert.Empty(source.Calls);
    }
    [Fact]public async Task Request_limit_stops_at_one_hundred_without_empty_success()
    {
        var source=new Source(new("*",["site"],[],false,false,false),(_,_)=>Task.FromResult(new OperationalReportPage([],true)));
        var result=await new CollaborationInventoryCapture().CaptureAsync(source,Session(),()=>{},default);
        Assert.Equal(100,source.Calls.Count);Assert.Equal(ReportReadState.Failed,result.Sources[0].Status);Assert.Null(result.Sources[0].ReturnedCount);Assert.Equal(ReportReadState.NotAttempted,result.Sources[1].Status);
    }
    [Fact]public async Task Row_limit_reserves_independent_source_diagnostics()
    {
        var source=new Source(new("*",[],[],false,false,false),(_,_)=>Task.FromResult(Page(Enumerable.Range(0,5000).Select(i=>Site("site"+i)).ToArray())));
        var result=await new CollaborationInventoryCapture().CaptureAsync(source,Session(),()=>{},default);
        Assert.Equal(4999,result.Sources[0].Rows.Count);Assert.Equal(ReportReadState.Partial,result.Sources[0].Status);Assert.Null(result.Sources[0].ReturnedCount);
    }
    [Fact]public async Task Missing_parent_is_not_zero_or_child_admission()
    {
        var source=new Source(new(null,["site"],[],true,false,false),(_,_)=>Task.FromResult(Page()));
        var result=await new CollaborationInventoryCapture().CaptureAsync(source,Session(),()=>{},default);
        Assert.Single(source.Calls);Assert.Equal(ReportReadState.Failed,result.Sources[0].Status);Assert.Null(result.Sources[0].ReturnedCount);Assert.Equal(ReportReadState.NotAttempted,result.Sources[1].Status);
    }
    [Fact]public async Task Missing_team_provisioning_never_becomes_an_empty_membership()
    {
        var group=Group();group.Remove("resourceProvisioningOptions");
        var source=new Source(new(null,[],[TestData.TenantA],false,false,true),(_,_)=>Task.FromResult(Page(group)));
        var result=await new CollaborationInventoryCapture().CaptureAsync(source,Session(),()=>{},default);
        Assert.Single(source.Calls);Assert.Equal(ReportReadState.NotAttempted,result.Sources[1].Status);Assert.Null(result.Sources[1].ReturnedCount);
    }
    [Fact]public async Task Authentication_interruption_keeps_earlier_sources_and_never_retries()
    {
        var source=new Source(new("*",["site","later"],[],false,false,false),(kind,_)=>kind==CollaborationReadKind.SiteSearch?Task.FromResult(Page(Site("observed"))):throw new AuthenticationRequiredException("private"));
        var result=await new CollaborationInventoryCapture().CaptureAsync(source,Session(),()=>{},default);
        Assert.Equal(2,source.Calls.Count);Assert.Single(result.Sources[0].Rows);Assert.Equal(ReportReadState.Failed,result.Sources[1].Status);Assert.Equal(ReportReadState.NotAttempted,result.Sources[2].Status);
    }
    private sealed class Source(CollaborationReadSelection selection,Func<CollaborationReadKind,string,Task<OperationalReportPage>> read):ICollaborationReportSource
    {
        public string TenantId=>TestData.TenantA;public CollaborationReadSelection Selection=>selection;public List<CollaborationReadKind> Calls{get;}=[];
        public Task<OperationalReportPage>ReadCollaborationNextAsync(CollaborationReadKind kind,string value,CancellationToken cancellation){Calls.Add(kind);return read(kind,value);}
    }
}
