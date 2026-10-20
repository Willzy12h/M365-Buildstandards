using System.Text;
using System.IO.Compression;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Engine.Evidence;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CollaborationEvidenceTests
{
    private static TenantSession Session(){var s=TestData.Session(mode:SessionMode.Assessment);s.Scopes=["Sites.Read.All","Group.Read.All","TeamMember.Read.All"];return s;}
    private static JsonObject Site()=>new(){["id"]="site",["displayName"]="Site",["name"]="site",["siteCollection"]=new JsonObject{["hostname"]="example.invalid"},["webUrl"]="https://example.invalid",["createdDateTime"]="2026-10-20T00:00:00Z",["lastModifiedDateTime"]="2026-10-20T00:00:00Z"};
    private static async Task<OperationalContextReport> Capture()=>await new CollaborationReportService(SystemClock.Instance,"synthetic-test").CaptureAsync(new Source(),Session(),()=>{},default);
    [Fact]public async Task Original_roundtrip_retains_exact_selection_and_successful_empty_count()
    {
        var report=await Capture();var original=Encoding.UTF8.GetBytes("\uFEFF\n"+OperationalContextSchema.Serialise(report)+"\n");
        var file=OperationalContextFile.Read(original,TestData.TenantA);Assert.Equal(original,file.CopyOriginal());
        Assert.Equal("collaboration-inventory",file.Report.ReportId);Assert.Equal(new[]{"site"},file.Report.CollaborationSelection!.Sites);Assert.Equal(new int?[]{1,0},file.Report.CollaborationSelection.ReturnedCounts);
    }
    [Fact]public async Task Export_retains_blocked_boundaries_and_original_ids()
    {
        var report=await Capture();var html=RegisteredReportDocuments.OperationalHtml(report,"Synthetic");
        Assert.Contains("OneDrive",html);Assert.Contains("FullControl",html);Assert.Contains("site",html);Assert.Contains("Selected site",html);
        Assert.Equal(3,RegisteredReportDocuments.OperationalSheets(report).Count);
    }
    [Fact]public async Task Parent_removal_cannot_preserve_successful_empty_child()
    {
        var report=await Capture();report.Sections[0].Rows.Clear();report.Sections[0].Status=ReportReadState.Failed;report.Sections[0].Error="failed";report.CollaborationSelection!.ReturnedCounts[0]=null;report.Status=ReportReadState.Partial;
        Assert.ThrowsAny<ToolkitException>(()=>OperationalContextSchema.Seal(report));
    }
    [Fact]public async Task Unselected_parent_cannot_be_relabelled_in_import()
    {
        var report=await Capture();report.CollaborationSelection!.Sites[0]="other";
        Assert.ThrowsAny<ToolkitException>(()=>OperationalContextSchema.Seal(report));
    }
    [Fact]public async Task Failed_read_cannot_claim_zero_count()
    {
        var report=await Capture();report.Sections[1].Status=ReportReadState.Failed;report.Sections[1].Error="failed";report.Status=ReportReadState.Partial;
        Assert.ThrowsAny<ToolkitException>(()=>OperationalContextSchema.Seal(report));
    }
    [Fact]public async Task Another_source_tenant_is_rejected()
    {
        var report=await Capture();Assert.Throws<TenantMismatchException>(()=>OperationalContextSchema.Read(OperationalContextSchema.Serialise(report),TestData.TenantB));
    }
    [Theory][InlineData("role")][InlineData("parent")][InlineData("team")][InlineData("coverage")][InlineData("owners-complete")][InlineData("count")][InlineData("raw-property")]
    public async Task Forged_direct_role_or_population_claim_is_refused(string fault)
    {
        var report=await new CollaborationReportService(SystemClock.Instance,"synthetic-test").CaptureAsync(new TeamSource(),Session(),()=>{},default);
        Assert.Equal(3,report.Sections.Count);
        var group=report.Sections[0].Rows.Single();var member=report.Sections[2].Rows.Single();
        if(fault=="role")member["roleMeaning"]="Owner";
        if(fault=="parent")member["teamId"]=TestData.TenantB;
        if(fault=="team")group["teamProvisioned"]=false;
        if(fault=="coverage")group["ownerCoverage"]="Complete";
        if(fault=="owners-complete"){report.Sections[1].Status=ReportReadState.Collected;report.Sections[1].Error=null;}
        if(fault=="count")report.CollaborationSelection!.ReturnedCounts[2]=0;
        if(fault=="raw-property")member["accessToken"]="not-a-token";
        Assert.ThrowsAny<ToolkitException>(()=>OperationalContextSchema.Seal(report));
    }
    [Fact]public async Task Known_empty_member_roles_are_member_not_guest_from_other_tenant()
    {
        var report=await new CollaborationReportService(SystemClock.Instance,"synthetic-test").CaptureAsync(new TeamSource(),Session(),()=>{},default);
        var copy=OperationalContextSchema.Read(OperationalContextSchema.Serialise(report),TestData.TenantA);
        Assert.Equal("Member",copy.Sections[2].Rows.Single()["roleMeaning"]!.GetValue<string>());
        Assert.Equal(TestData.TenantB,copy.Sections[2].Rows.Single()["userTenantId"]!.GetValue<string>());
        Assert.Equal(0,copy.CollaborationSelection!.ReturnedCounts[1]);Assert.Equal(ReportReadState.Partial,copy.Sections[1].Status);
    }
    [Fact]public async Task Non_collaboration_records_cannot_claim_selection()
    {
        var report=await Capture();report.ReportId="service-health";report.AccessIntent="service-health";
        Assert.ThrowsAny<ToolkitException>(()=>OperationalContextSchema.Seal(report));
    }
    [Theory][InlineData("html")][InlineData("json")][InlineData("csv")][InlineData("xlsx")]
    public async Task Real_offline_cli_keeps_original_source_and_all_export_formats(string format)
    {
        using var root=new TempRoot();var report=await Capture();var bytes=Encoding.UTF8.GetBytes("\uFEFF "+OperationalContextSchema.Serialise(report)+"\n");
        var file=Path.Combine(root.Root,"collaboration.json");File.WriteAllBytes(file,bytes);
        var wrong=await RegisteredReportCliTests.Run("operational-report-evidence","--input",file,"--tenant",TestData.TenantB,"--format",format,"--root",root.Root);
        Assert.Equal(2,wrong.Exit);Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
        var result=await RegisteredReportCliTests.Run("operational-report-evidence","--input",file,"--tenant",TestData.TenantA,"--format",format,"--root",root.Root);
        Assert.True(result.Exit==0,result.Output+result.Error);Assert.Contains("no live reads or writes",result.Output);
        var output=Assert.Single(Directory.GetFiles(root.Paths.ReportsDirectory));Assert.NotEmpty(File.ReadAllBytes(output));
        if(format=="json")Assert.Equal(bytes,File.ReadAllBytes(output));
        if(format=="html"){Assert.Contains("OneDrive",File.ReadAllText(output));Assert.DoesNotContain("<script>",File.ReadAllText(output));}
        Assert.Equal(bytes,File.ReadAllBytes(file));Assert.Empty(Directory.GetFiles(root.Paths.DataDirectory,"*",SearchOption.AllDirectories));
    }
    [Fact]public async Task Original_managed_backup_does_not_reinterpret_as_snapshot()
    {
        using var root=new TempRoot();var report=await Capture();var bytes=Encoding.UTF8.GetBytes("\uFEFF\n"+OperationalContextSchema.Serialise(report));
        var store=new EvidenceStore(root.Paths,NullLog.Instance);var file=store.SaveOperationalContext(bytes,TestData.TenantA,report.Id);Assert.Equal(bytes,File.ReadAllBytes(file));
        Assert.ThrowsAny<ToolkitException>(()=>store.SaveOperationalContext(bytes,TestData.TenantA,report.Id));
        using var zip=ZipFile.OpenRead(new WorkspaceBackup(root.Paths).Create());
        var entry=Assert.Single(zip.Entries,e=>e.FullName.EndsWith("/operational-context-evidence/"+report.Id+".json",StringComparison.Ordinal));
        using var stream=entry.Open();using var memory=new MemoryStream();stream.CopyTo(memory);Assert.Equal(bytes,memory.ToArray());
        Assert.DoesNotContain("snapshots",file);
    }
    [Fact]public void Existing_health_serialisation_has_no_new_null_selection_field()
    {
        var report=OperationalContextSchemaTests.Health();OperationalContextSchema.Seal(report);
        Assert.DoesNotContain("collaborationSelection",OperationalContextSchema.Serialise(report));
    }
    private sealed class TeamSource:ICollaborationReportSource
    {
        public string TenantId=>TestData.TenantA;public CollaborationReadSelection Selection{get;}=new(null,[],[TestData.TenantA],false,true,true);
        public Task<OperationalReportPage>ReadCollaborationNextAsync(CollaborationReadKind kind,string target,CancellationToken cancellation)
        {
            var group=new JsonObject{["id"]=TestData.TenantA,["displayName"]="Group",["groupTypes"]=new JsonArray("Unified"),["mailEnabled"]=true,["securityEnabled"]=false,["onPremisesSyncEnabled"]=null,["resourceProvisioningOptions"]=new JsonArray("Team")};
            var member=new JsonObject{["id"]="opaque-member",["@odata.type"]="#microsoft.graph.aadUserConversationMember",["displayName"]="Member",["userId"]=TestData.TenantA,["tenantId"]=TestData.TenantB,["roles"]=new JsonArray()};
            return Task.FromResult(new OperationalReportPage(kind==CollaborationReadKind.GroupMetadata?[group]:kind==CollaborationReadKind.TeamMembers?[member]:[],false));
        }
    }
    private sealed class Source:ICollaborationReportSource
    {
        public string TenantId=>TestData.TenantA;public CollaborationReadSelection Selection{get;}=new(null,["site"],[],true,false,false);
        public Task<OperationalReportPage>ReadCollaborationNextAsync(CollaborationReadKind kind,string target,CancellationToken cancellation)=>Task.FromResult(new OperationalReportPage(kind==CollaborationReadKind.SiteMetadata?[Site()]:[],false));
    }
}
