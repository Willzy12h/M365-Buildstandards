using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class PartnerRelationshipReportServiceTests
{
    private const string Parent=PartnerContextSchemaTests.Parent;
    internal static TenantSession Session(){var s=TestData.Session();s.Scopes=["DelegatedAdminRelationship.Read.All"];return s;}
    internal static JsonObject Relationship(string id=Parent,string? customer=TestData.TenantB)=>new()
    {
        ["id"]=id,["displayName"]="Synthetic selected relationship",["@odata.type"]="#microsoft.graph.delegatedAdminRelationship",["customer"]=new JsonObject{["tenantId"]=customer},["status"]="active",
        ["createdDateTime"]="2026-10-01T00:00:00Z",["activatedDateTime"]="2026-10-01T00:00:00Z",["lastModifiedDateTime"]="2026-10-01T00:00:00Z",["endDateTime"]="2026-10-19T00:00:00Z",["duration"]="P180D",["autoExtendDuration"]="PT0S",["accessDetails"]=new JsonObject{["unifiedRoles"]=new JsonArray()}
    };
    private static JsonObject Assignment(string id="child-one")=>new(){["id"]=id,["status"]="active",["accessContainer"]=new JsonObject{["accessContainerType"]="securityGroup",["accessContainerId"]=TestData.TenantA},["accessDetails"]=new JsonObject{["unifiedRoles"]=new JsonArray()}};
    private static PartnerRelationshipReportService Service()=>new(new FixedClock{UtcNow=new(2026,10,20,12,0,0,TimeSpan.Zero)},"synthetic");
    private static async Task<OperationalContextReport> Capture(Source source)
    {
        var r=await Service().CaptureAsync(source,Session(),TestData.TenantB,true,()=>{},default);
        _=OperationalContextSchema.Read(OperationalContextSchema.Serialise(r),TestData.TenantA);return r;
    }
    [Fact]public async Task Known_other_customers_are_counted_only_and_selected_children_keep_original_source()
    {
        var foreign=Relationship("foreign-hidden",TestData.TenantA);foreign["displayName"]="PRIVATE OTHER CUSTOMER";
        var source=new Source((kind,parent,_)=>Task.FromResult(new OperationalReportPage(kind==OperationalReadKind.PartnerRelationships?[foreign,Relationship()]:[Assignment()],false)));
        var r=await Capture(source);Assert.Equal("Collected",r.Status);Assert.Equal(TestData.TenantA,r.TenantId);Assert.Equal(TestData.TenantB,r.CustomerTenantId);
        Assert.Equal(1,r.PartnerCoverage!.ExcludedOtherCustomerRows);Assert.Equal(2,r.PartnerCoverage.ParentRowsRead);Assert.Single(r.Sections[0].Rows);Assert.Single(r.Sections[1].Rows);
        var json=OperationalContextSchema.Serialise(r);Assert.DoesNotContain("PRIVATE OTHER CUSTOMER",json);Assert.DoesNotContain("foreign-hidden",json);Assert.Equal("EndedAtOrBeforeAsOf",r.Sections[0].Rows[0]["expiryAtAsOf"]!.GetValue<string>());Assert.Equal("active",r.Sections[0].Rows[0]["status"]!.GetValue<string>());Assert.Equal(Parent,source.Reads[1].Parent);
    }
    [Theory][InlineData("unknown-customer")][InlineData("future-status")][InlineData("missing-roles")][InlineData("malformed-time")][InlineData("role-limit")][InlineData("unknown-subtype")][InlineData("malformed-duration")][InlineData("unknown-auto-extension")]
    public async Task Missing_unsupported_or_future_observations_remain_unknown_partial(string fault)
    {
        var row=Relationship();switch(fault){case "unknown-customer":row["customer"]=null;break;case "future-status":row["status"]="futureStatus";break;case "missing-roles":row["accessDetails"]=null;break;case "malformed-time":row["endDateTime"]="2026-10-19";break;
        case "role-limit":row["accessDetails"]=new JsonObject{["unifiedRoles"]=new JsonArray(Enumerable.Range(0,101).Select(_=>(JsonNode)new JsonObject{["roleDefinitionId"]=Guid.NewGuid().ToString("D")}).ToArray())};break;
        case "unknown-subtype":row["@odata.type"]="#microsoft.graph.futurePartnerRelationship";break;
        case "malformed-duration":row["duration"]="not-a-duration";break;case "unknown-auto-extension":row["autoExtendDuration"]="future-extension";break;}
        var source=new Source((kind,_,_)=>Task.FromResult(new OperationalReportPage(kind==OperationalReadKind.PartnerRelationships?[row]:[],false)));
        var r=await Capture(source);Assert.Equal("Partial",r.Status);
        if(fault=="unknown-customer"){Assert.Empty(r.Sections[0].Rows);Assert.Equal(1,r.PartnerCoverage!.UnattributableCustomerRows);}
        else{Assert.Equal("Partial",r.Sections[0].Rows[0]["readStatus"]!.GetValue<string>());if(fault is "missing-roles" or "role-limit")Assert.Null(r.Sections[0].Rows[0]["roleDefinitionIds"]);}
        if(fault=="unknown-subtype")Assert.Single(source.Reads);
    }
    [Fact]public async Task Duplicate_parent_identity_never_starts_child_lookup_even_across_pages()
    {
        var pages=0;var source=new Source((kind,_,_)=>{Assert.Equal(OperationalReadKind.PartnerRelationships,kind);return Task.FromResult(new OperationalReportPage([Relationship()],++pages==1));});
        var r=await Capture(source);Assert.Equal("Partial",r.Status);Assert.Equal(2,source.Reads.Count);Assert.Equal(2,r.Sections[0].Rows.Count);Assert.All(r.Sections[0].Rows,p=>Assert.Equal("NotAttempted",p["assignmentReadStatus"]!.GetValue<string>()));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Failed_child_and_missing_child_fields_do_not_turn_into_zero_or_effective_access(bool missing)
    {
        var source=new Source((kind,_,_)=>
        {
            if(kind==OperationalReadKind.PartnerRelationships)return Task.FromResult(new OperationalReportPage([Relationship()],false));
            if(!missing)throw new ToolkitException("PRIVATE RAW FAILURE");var child=Assignment();child["accessContainer"]=null;return Task.FromResult(new OperationalReportPage([child],false));
        });
        var r=await Capture(source);Assert.Equal("Partial",r.Status);var parent=r.Sections[0].Rows[0];
        Assert.Equal(missing?"Partial":"Failed",parent["assignmentReadStatus"]!.GetValue<string>());
        if(!missing){Assert.Null(parent["returnedAssignments"]);Assert.Empty(r.Sections[1].Rows);Assert.DoesNotContain("PRIVATE RAW FAILURE",OperationalContextSchema.Serialise(r));}
        else Assert.Null(r.Sections[1].Rows[0]["containerId"]);
    }
    [Fact]public async Task Bounded_incomplete_parent_scan_retains_observations_and_does_not_authorise_children()
    {
        var calls=0;var source=new Source((kind,_,_)=>{Assert.Equal(OperationalReadKind.PartnerRelationships,kind);if(++calls==2)throw new OperationalReadLimitException("synthetic limit");return Task.FromResult(new OperationalReportPage([Relationship()],true));});
        var r=await Capture(source);Assert.Equal("Partial",r.Status);Assert.False(r.PartnerCoverage!.ParentInventoryComplete);Assert.Single(r.Sections[0].Rows);Assert.Empty(r.Sections[1].Rows);Assert.Equal("NotAttempted",r.Sections[1].Status);
    }
    [Theory][InlineData("customer")][InlineData("declaration")][InlineData("scope")][InlineData("actor")]
    public async Task Invalid_source_or_customer_is_refused_before_source_read(string fault)
    {
        var s=Session();if(fault=="scope")s.Scopes=[];if(fault=="actor")s.OperatorVerified=false;var source=new Source((_,_,_)=>throw new InvalidOperationException("No read permitted."));
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>Service().CaptureAsync(source,s,fault=="customer"?TestData.TenantA:TestData.TenantB,fault!="declaration",()=>{},default));Assert.Empty(source.Reads);
    }
    [Fact]public async Task Successful_sibling_after_failed_child_gives_partial_not_failed_aggregate_and_preserves_both_states()
    {
        var source=new Source((kind,parent,_)=>{if(kind==OperationalReadKind.PartnerRelationships)return Task.FromResult(new OperationalReportPage([Relationship(),Relationship(Parent+"-two")],false));if(parent==Parent)throw new ToolkitException("synthetic denied");return Task.FromResult(new OperationalReportPage([Assignment()],false));});
        var r=await Capture(source);Assert.Equal("Partial",r.Sections[1].Status);Assert.Single(r.Sections[1].Rows);Assert.Equal("Failed",r.Sections[0].Rows[0]["assignmentReadStatus"]!.GetValue<string>());Assert.Null(r.Sections[0].Rows[0]["returnedAssignments"]);Assert.Equal("Collected",r.Sections[0].Rows[1]["assignmentReadStatus"]!.GetValue<string>());
    }
    [Fact]public async Task Bounded_duplicate_parent_bytes_survive_finalisation_without_starting_children()
    {
        var row=Relationship();row["displayName"]=new string('x',3200);
        var source=new Source((kind,_,_)=>{Assert.Equal(OperationalReadKind.PartnerRelationships,kind);return Task.FromResult(new OperationalReportPage(Enumerable.Range(0,5000).Select(_=>(JsonObject)row.DeepClone()).ToArray(),false));});
        var r=await Capture(source);Assert.Equal("Partial",r.Status);Assert.NotEmpty(r.Sections[0].Rows);Assert.False(r.PartnerCoverage!.ParentInventoryComplete);Assert.True(System.Text.Encoding.UTF8.GetByteCount(OperationalContextSchema.Serialise(r))<=OperationalReportRegistry.MaximumBytes);Assert.Single(source.Reads);
    }
    [Fact]public async Task Stop_during_child_preserves_parent_and_reports_cancelled_without_inventing_zero()
    {
        using var cancellation=new CancellationTokenSource();
        var source=new Source((kind,_,_)=>{if(kind==OperationalReadKind.PartnerRelationships)return Task.FromResult(new OperationalReportPage([Relationship()],false));cancellation.Cancel();throw new OperationCanceledException(cancellation.Token);});
        var r=await Service().CaptureAsync(source,Session(),TestData.TenantB,true,()=>{},cancellation.Token);
        _=OperationalContextSchema.Read(OperationalContextSchema.Serialise(r),TestData.TenantA);Assert.Equal("Cancelled",r.Status);Assert.Single(r.Sections[0].Rows);Assert.Empty(r.Sections[1].Rows);Assert.Null(r.Sections[0].Rows[0]["returnedAssignments"]);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Repeated_parent_id_with_foreign_or_unattributable_customer_does_not_authorise_children(bool missing)
    {
        var other=Relationship(Parent,missing?null:TestData.TenantA);
        var source=new Source((kind,_,_)=>{Assert.Equal(OperationalReadKind.PartnerRelationships,kind);return Task.FromResult(new OperationalReportPage([Relationship(),other],false));});
        var r=await Capture(source);Assert.Equal("Partial",r.Status);Assert.Single(source.Reads);Assert.Equal("Partial",r.Sections[0].Rows[0]["readStatus"]!.GetValue<string>());Assert.Equal("NotAttempted",r.Sections[0].Rows[0]["assignmentReadStatus"]!.GetValue<string>());
    }
    [Fact]public async Task Source_bound_to_another_selected_customer_is_refused_before_read()
    {
        var source=new Source((_,_,_)=>Task.FromResult(new OperationalReportPage([],false))){CustomerTenantId=TestData.TenantA};
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>Capture(source));Assert.Empty(source.Reads);
    }
    internal sealed class Source(Func<OperationalReadKind,string?,CancellationToken,Task<OperationalReportPage>> read):IOperationalReportSource
    {public string TenantId=>TestData.TenantA;public string ReportId=>"partner-relationships";public string? CustomerTenantId{get;init;}=TestData.TenantB;public List<(OperationalReadKind Kind,string? Parent)>Reads{get;}=[];public Task<OperationalReportPage>ReadNextAsync(OperationalReadKind kind,string? parent,CancellationToken ct){Reads.Add((kind,parent));return read(kind,parent,ct);}}
}
