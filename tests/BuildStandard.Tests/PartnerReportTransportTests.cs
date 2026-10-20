using System.Net;
using System.Text;
using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Graph.Auth;
using BuildStandard.Graph.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class PartnerReportTransportTests
{
    private const string Parent = "https://graph.microsoft.com/v1.0/tenantRelationships/delegatedAdminRelationships";
    private const string Relationship = "11111111-1111-1111-1111-111111111111-22222222-2222-2222-2222-222222222222";
    private static TenantSession Session() { var s=TestData.Session();s.Scopes=["DelegatedAdminRelationship.Read.All"];return s; }
    private static string Row(string id,string? customer=TestData.TenantB)=>JsonSerializer.Serialize(new{id,customer=new{tenantId=customer}});
    private static HttpResponseMessage Page(params string[] rows)=>Json("{\"value\":["+string.Join(',',rows)+"]}");
    private static HttpResponseMessage Json(string body)=>new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")};
    [Fact]
    public async Task Exact_selected_parent_admits_only_its_opaque_child_and_does_not_switch_source()
    {
        var calls=new List<string>();var s=Session();
        using var client=new HttpClient(new Handler(r=>{calls.Add(r.RequestUri!.OriginalString);return calls.Count==1?Page(Row(Relationship)):Page();}));
        using var source=new OperationalReportTransport(client,new Tokens(),s,"partner-relationships",()=>{},customerTenantId:TestData.TenantB,partnerDeclared:true);
        await source.ReadNextAsync(OperationalReadKind.PartnerRelationships,null,default);
        await source.ReadNextAsync(OperationalReadKind.PartnerAssignments,Relationship,default);
        Assert.Equal(new[]{Parent+"?$top=300",Parent+"/"+Relationship+"/accessAssignments"},calls);
        Assert.Equal(TestData.TenantA,s.TenantId);
    }
    [Theory][InlineData("foreign")][InlineData("missing-customer")][InlineData("duplicate")][InlineData("not-returned")][InlineData("incomplete")]
    public async Task Unadmitted_ambiguous_or_unfinished_parent_never_authorises_a_child_request(string fault)
    {
        var calls=0;
        using var client=new HttpClient(new Handler(_=>{calls++;return fault switch{
            "foreign"=>Page(Row(Relationship,TestData.TenantA)),"missing-customer"=>Page(Row(Relationship,null)),
            "duplicate"=>Page(Row(Relationship),Row(Relationship)),"not-returned"=>Page(),
            _=>Json("{\"value\":["+Row(Relationship)+"],\"@odata.nextLink\":\""+Parent+"?$skiptoken=next\"}")};}));
        using var source=new OperationalReportTransport(client,new Tokens(),Session(),"partner-relationships",()=>{},customerTenantId:TestData.TenantB,partnerDeclared:true);
        await source.ReadNextAsync(OperationalReadKind.PartnerRelationships,null,default);
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>source.ReadNextAsync(OperationalReadKind.PartnerAssignments,Relationship,default));
        Assert.Equal(1,calls);
    }
    [Theory][InlineData("same")][InlineData("missing")][InlineData("not-declared")][InlineData("scope")]
    public void Unknown_source_customer_or_purpose_is_refused_before_tokens_or_requests(string fault)
    {
        var tokens=new Tokens();var calls=0;var s=Session();if(fault=="scope")s.Scopes=["ServiceHealth.Read.All"];
        using var client=new HttpClient(new Handler(_=>{calls++;return Page();}));
        Assert.ThrowsAny<ToolkitException>(()=>new OperationalReportTransport(client,tokens,s,"partner-relationships",()=>{},customerTenantId:fault=="same"?TestData.TenantA:fault=="missing"?null:TestData.TenantB,partnerDeclared:fault!="not-declared"));
        Assert.Equal(0,calls);Assert.Equal(0,tokens.Calls);
    }
    [Fact]
    public async Task Distinct_children_have_independent_cursors_and_finished_children_cannot_replay()
    {
        var calls=0;var second=Relationship+"-second";
        using var client=new HttpClient(new Handler(_=>{calls++;return calls==1?Page(Row(Relationship),Row(second)):Page();}));
        using var source=new OperationalReportTransport(client,new Tokens(),Session(),"partner-relationships",()=>{},customerTenantId:TestData.TenantB,partnerDeclared:true);
        await source.ReadNextAsync(OperationalReadKind.PartnerRelationships,null,default);
        await source.ReadNextAsync(OperationalReadKind.PartnerAssignments,Relationship,default);
        await source.ReadNextAsync(OperationalReadKind.PartnerAssignments,second,default);
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>source.ReadNextAsync(OperationalReadKind.PartnerAssignments,Relationship,default));Assert.Equal(3,calls);
    }
    [Fact]
    public async Task Child_waits_for_last_parent_page_and_later_duplicate_revokes_early_candidate()
    {
        var calls=0;
        using var client=new HttpClient(new Handler(_=>{calls++;return calls==1?Json("{\"value\":["+Row(Relationship)+"],\"@odata.nextLink\":\""+Parent+"?$skiptoken=next\"}"):Page(Row(Relationship));}));
        using var source=new OperationalReportTransport(client,new Tokens(),Session(),"partner-relationships",()=>{},customerTenantId:TestData.TenantB,partnerDeclared:true);
        await source.ReadNextAsync(OperationalReadKind.PartnerRelationships,null,default);
        await source.ReadNextAsync(OperationalReadKind.PartnerRelationships,null,default);
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>source.ReadNextAsync(OperationalReadKind.PartnerAssignments,Relationship,default));Assert.Equal(2,calls);
    }
    [Theory][InlineData(301)][InlineData(1001)]
    public async Task Documented_parent_and_issue_page_limits_are_enforced_before_returning_objects(int count)
    {
        var partner=count==301;var calls=0;var tokens=new Tokens();
        using var client=new HttpClient(new Handler(_=>{calls++;return Page(Enumerable.Repeat("{}",count).ToArray());}));
        var s=Session();if(!partner)s.Scopes=["ServiceHealth.Read.All"];
        using var source=new OperationalReportTransport(client,tokens,s,partner?"partner-relationships":"service-health",()=>{},customerTenantId:partner?TestData.TenantB:null,partnerDeclared:partner);
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>source.ReadNextAsync(partner?OperationalReadKind.PartnerRelationships:OperationalReadKind.HealthIssues,null,default));Assert.Equal(1,calls);
    }
    [Fact]
    public async Task Parent_pagination_cannot_increase_documented_page_size()
    {
        var calls=0;
        using var client=new HttpClient(new Handler(_=>{calls++;return Json("{\"value\":[],\"@odata.nextLink\":\""+Parent+"?$skiptoken=next&$top=301\"}");}));
        using var source=new OperationalReportTransport(client,new Tokens(),Session(),"partner-relationships",()=>{},customerTenantId:TestData.TenantB,partnerDeclared:true);
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>source.ReadNextAsync(OperationalReadKind.PartnerRelationships,null,default));Assert.Equal(1,calls);
    }
    [Fact]
    public async Task Fifty_child_budget_cannot_be_bypassed_by_retrying_a_refused_fifty_first_parent()
    {
        var calls=0;var ids=Enumerable.Range(0,51).Select(i=>Relationship+"-"+i).ToArray();
        using var client=new HttpClient(new Handler(_=>{calls++;return calls==1?Page(ids.Select(id=>Row(id)).ToArray()):Page();}));
        using var source=new OperationalReportTransport(client,new Tokens(),Session(),"partner-relationships",()=>{},customerTenantId:TestData.TenantB,partnerDeclared:true);
        await source.ReadNextAsync(OperationalReadKind.PartnerRelationships,null,default);
        foreach(var id in ids.Take(50))await source.ReadNextAsync(OperationalReadKind.PartnerAssignments,id,default);
        for(var attempt=0;attempt<2;attempt++)
            await Assert.ThrowsAsync<OperationalReadLimitException>(()=>source.ReadNextAsync(OperationalReadKind.PartnerAssignments,ids[50],default));
        Assert.Equal(51,calls);
    }
    private sealed class Tokens:IAccessTokenProvider
    {public int Calls;public Task<string> GetAccessTokenAsync(CancellationToken ct)=>GetAccessTokenAsync(false,ct);public Task<string> GetAccessTokenAsync(bool refresh,CancellationToken ct){ct.ThrowIfCancellationRequested();Calls++;return Task.FromResult("synthetic");}}
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> reply):HttpMessageHandler
    {protected override Task<HttpResponseMessage>SendAsync(HttpRequestMessage r,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(reply(r));}}
}
