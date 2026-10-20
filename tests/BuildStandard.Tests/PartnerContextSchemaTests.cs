using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class PartnerContextSchemaTests
{
    internal const string Parent="11111111-1111-1111-1111-111111111111-22222222-2222-2222-2222-222222222222";
    internal static OperationalContextReport Empty()
    {
        var r=OperationalContextSchemaTests.Health();r.ReportId=r.AccessIntent="partner-relationships";r.CustomerTenantId=TestData.TenantB;
        r.PartnerCoverage=new(){ParentInventoryComplete=true};
        r.Sources=[new(){Api="v1.0",RegisteredRoute="/tenantRelationships/delegatedAdminRelationships",Reference="https://learn.microsoft.com/en-us/graph/api/tenantrelationship-list-delegatedadminrelationships?view=graph-rest-1.0"},new(){Api="v1.0",RegisteredRoute="/tenantRelationships/delegatedAdminRelationships/{returnedRelationshipId}/accessAssignments",Reference="https://learn.microsoft.com/en-us/graph/api/delegatedadminrelationship-list-accessassignments?view=graph-rest-1.0"}];
        r.Sections=[new(){Id="partner-relationships",Status="Collected"},new(){Id="partner-assignments",Status="Collected"}];return r;
    }
    internal static PartnerRelationshipRow ParentRow()=>new(){Id=Parent,Name="Synthetic selected relationship",CustomerTenantId=TestData.TenantB,
        ResourceType="#microsoft.graph.delegatedAdminRelationship",Status="active",CreatedAt="2026-10-01T00:00:00Z",ActivatedAt="2026-10-01T00:00:00Z",ModifiedAt="2026-10-01T00:00:00Z",EndedAt="2026-10-19T00:00:00Z",Duration="P180D",AutoExtendDuration="PT0S",ExpiryAtAsOf="EndedAtOrBeforeAsOf",RoleDefinitionIds=[],ReadStatus="Collected",AssignmentReadStatus="Collected",AssignmentReadError=null,ReturnedAssignments=0};
    [Fact]public void Empty_bounded_partner_inventory_roundtrips_without_effective_access_claim()
    {var r=Empty();OperationalContextSchema.Seal(r);var read=OperationalContextSchema.Read(OperationalContextSchema.Serialise(r),TestData.TenantA);Assert.Equal(TestData.TenantB,read.CustomerTenantId);Assert.True(read.PartnerCoverage!.ParentInventoryComplete);Assert.All(read.Sections,s=>Assert.Empty(s.Rows));}
    [Fact]public void Expiry_is_frozen_separately_from_observed_active_status_and_empty_direct_roles()
    {var r=Empty();r.PartnerCoverage!.ParentRowsRead=r.PartnerCoverage.SelectedCustomerRows=1;r.Sections[0].Rows.Add(JsonSerializer.SerializeToNode(ParentRow(),RegisteredReportJson.Strict)!.AsObject());OperationalContextSchema.Seal(r);var read=OperationalContextSchema.Read(OperationalContextSchema.Serialise(r),TestData.TenantA);Assert.Equal("active",read.Sections[0].Rows[0]["status"]!.GetValue<string>());Assert.Equal("EndedAtOrBeforeAsOf",read.Sections[0].Rows[0]["expiryAtAsOf"]!.GetValue<string>());}
    [Theory][InlineData("Failed")][InlineData("Cancelled")]
    public void GPT_G_002_failed_or_cancelled_child_read_cannot_be_imported_as_known_zero(string state)
    {
        var r=Empty();var p=ParentRow();p.AssignmentReadStatus=state;p.AssignmentReadError="Synthetic unsuccessful read.";
        r.PartnerCoverage!.ParentRowsRead=r.PartnerCoverage.SelectedCustomerRows=1;r.Sections[0].Rows.Add(JsonSerializer.SerializeToNode(p,RegisteredReportJson.Strict)!.AsObject());
        r.Sections[1].Status=state;r.Sections[1].Error=p.AssignmentReadError;r.Status=state=="Cancelled"?"Cancelled":"Partial";
        Assert.ThrowsAny<ToolkitException>(()=>OperationalContextSchema.Seal(r));
    }
    [Theory][InlineData("incomplete-scan")][InlineData("missing-id")][InlineData("subtype")]
    public void GPT_G_003_successful_empty_child_claim_still_requires_an_admitted_parent(string fault)
    {
        var r=Empty();var p=ParentRow();r.PartnerCoverage!.ParentRowsRead=r.PartnerCoverage.SelectedCustomerRows=1;
        if(fault=="incomplete-scan")r.PartnerCoverage.ParentInventoryComplete=false;
        if(fault=="missing-id")p.Id=null;if(fault=="subtype")p.ResourceType="#microsoft.graph.futureRelationship";
        p.ReadStatus="Partial";p.Error="Synthetic incomplete observation.";
        r.Sections[0].Rows.Add(JsonSerializer.SerializeToNode(p,RegisteredReportJson.Strict)!.AsObject());
        foreach(var section in r.Sections){section.Status="Partial";section.Error="Synthetic incomplete source.";}r.Status="Partial";
        Assert.ThrowsAny<ToolkitException>(()=>OperationalContextSchema.Seal(r));
    }
    [Theory][InlineData("customer")][InlineData("coverage")][InlineData("future-complete")][InlineData("false-expiry")][InlineData("missing-roles")][InlineData("invented-child-count")][InlineData("child-without-parent")]
    public void Inconsistent_selected_customer_coverage_roles_expiry_and_child_claims_are_refused(string fault)
    {
        var r=Empty();var p=ParentRow();r.PartnerCoverage!.ParentRowsRead=r.PartnerCoverage.SelectedCustomerRows=1;
        switch(fault){case "customer":p.CustomerTenantId=TestData.TenantA;break;case "coverage":r.PartnerCoverage.ExcludedOtherCustomerRows=1;break;case "future-complete":p.Status="future";break;case "false-expiry":p.ExpiryAtAsOf="AfterAsOf";break;case "missing-roles":p.RoleDefinitionIds=null;break;case "invented-child-count":p.ReturnedAssignments=1;break;
        case "child-without-parent":r.Sections[1].Rows.Add(JsonSerializer.SerializeToNode(new PartnerAccessAssignmentRow{Id="forged",AssignmentId="child",RelationshipId="not-returned",ReadStatus="Collected",RoleDefinitionIds=[]},RegisteredReportJson.Strict)!.AsObject());break;}
        r.Sections[0].Rows.Add(JsonSerializer.SerializeToNode(p,RegisteredReportJson.Strict)!.AsObject());Assert.ThrowsAny<ToolkitException>(()=>OperationalContextSchema.Seal(r));
    }
}
