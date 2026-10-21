using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class PartnerReportsPageTests
{
    private sealed class Source:IOperationalReportSource
    {
        public string TenantId=>TestData.TenantA;public string ReportId=>"partner-relationships";public string? CustomerTenantId=>TestData.TenantB;public int Reads;
        public Task<OperationalReportPage>ReadNextAsync(OperationalReadKind kind,string? parent,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();Reads++;
            if(kind==OperationalReadKind.PartnerAssignments){Assert.Equal("synthetic-parent",parent);return Task.FromResult(new OperationalReportPage([],false));}
            Assert.Equal(OperationalReadKind.PartnerRelationships,kind);Assert.Null(parent);
            return Task.FromResult(new OperationalReportPage([new JsonObject{["id"]="synthetic-parent",["displayName"]="Synthetic partner relationship",["@odata.type"]="#microsoft.graph.delegatedAdminRelationship",["customer"]=new JsonObject{["tenantId"]=TestData.TenantB},["status"]="active"}],false));
        }
    }
    private static OperationalReportsViewModel Page(ConnectedReportsFixture fixture,Source source)
    {
        fixture.Workspace.Session!.Scopes.Add("DelegatedAdminRelationship.Read.All");
        fixture.Controller.PartnerSourceFactory=(connection,customer,_)=>{Assert.Same(fixture.Workspace.Connection,connection);Assert.Equal(TestData.TenantB,customer);return source;};
        return new(new ShellViewModel(fixture.Workspace),fixture.Controller){SelectedReportId="partner-relationships",CustomerTenantId=TestData.TenantB,PartnerDeclared=true};
    }
    [Fact]public async Task Native_partner_journey_retains_source_customer_original_and_never_authenticates_or_changes_profile()
    {
        using var f=new ConnectedReportsFixture();var source=new Source();var page=Page(f,source);page.Acknowledged=true;var profile=f.Workspace.Profile;
        await page.ReadAsync();Assert.True(page.HasPendingOriginal);Assert.Contains("partner",page.Outcome,StringComparison.OrdinalIgnoreCase);Assert.Contains(TestData.TenantB,page.Outcome);
        await page.SavePendingOriginalAsync();var saved=OperationalContextFile.ReadFile(page.LastExport,TestData.TenantA);Assert.Equal(TestData.TenantB,saved.Report.CustomerTenantId);Assert.Equal("Partial",saved.Report.Status);Assert.Equal(2,source.Reads);Assert.Same(profile,f.Workspace.Profile);Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);
    }
    [Fact]public async Task Target_A_B_A_during_original_retention_refuses_display_but_keeps_original_source_bytes()
    {
        using var f=new ConnectedReportsFixture();var source=new Source();var page=Page(f,source);page.Acknowledged=true;
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.SerialiseOriginal=async report=>{entered.SetResult();await release.Task;return Encoding.UTF8.GetBytes(OperationalContextSchema.Serialise(report));};
        var reading=page.ReadAsync();var winner=await Task.WhenAny(entered.Task,reading,Task.Delay(TimeSpan.FromSeconds(10)));
        try{Assert.Same(entered.Task,winner);page.CustomerTenantId=TestData.TenantA;page.CustomerTenantId=TestData.TenantB;Assert.True(f.Workspace.Busy);}
        finally{release.TrySetResult();await Assert.ThrowsAnyAsync<ToolkitException>(()=>reading);}
        Assert.True(page.HasPendingOriginal);Assert.Contains("No current",page.Outcome);await page.SavePendingOriginalAsync();Assert.Equal(TestData.TenantB,OperationalContextFile.ReadFile(page.LastExport,TestData.TenantA).Report.CustomerTenantId);Assert.Equal(2,source.Reads);
    }
    [Theory][InlineData("customer")][InlineData("declaration")][InlineData("scope")][InlineData("acknowledgement")]
    public async Task Invalid_partner_parameters_refuse_before_source_or_tokens(string fault)
    {
        using var f=new ConnectedReportsFixture();var source=new Source();var page=Page(f,source);
        if(fault=="customer")page.CustomerTenantId=TestData.TenantA;if(fault=="declaration")page.PartnerDeclared=false;
        if(fault=="scope")f.Workspace.Session!.Scopes.Remove("DelegatedAdminRelationship.Read.All");page.Acknowledged=fault!="acknowledgement";
        await Assert.ThrowsAnyAsync<ToolkitException>(page.ReadAsync);Assert.Equal(0,source.Reads);Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);
    }
}
