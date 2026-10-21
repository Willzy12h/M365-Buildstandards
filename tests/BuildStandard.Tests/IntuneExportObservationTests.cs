using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using BuildStandard.Graph;
using Xunit;
namespace BuildStandard.Tests;
public sealed class IntuneExportObservationTests
{
    internal static DeploymentCoverageSelectionTests.Fixture Scope(CoverageFamily family=CoverageFamily.Application)
    {var f=new DeploymentCoverageSelectionTests.Fixture(family);f.Session.Mode=SessionMode.Deployment;f.Session.Scopes.Add(IntuneExportRequest.Prepare(family,TestData.Office).RequiredScope);return f;}
    internal static IntuneExportReceipt Receipt(DeploymentCoverageSelection selection,string? tenant=null,string? account=null,string? client=null,string? digest=null,byte[]? archive=null)
    {var request=IntuneExportRequest.Prepare(selection.Family,selection.ObjectId);var csv=Encoding.UTF8.GetBytes(string.Join(',',request.Columns)+"\n"+string.Join(',',request.Columns.Select((c,i)=>i==0?selection.ObjectId:""))+"\n");var now=Timestamps.Format(DateTimeOffset.UtcNow);return new(tenant??selection.TenantId,account??selection.AccountObjectId,client??selection.ClientId,"report_opaque-1",digest??request.Sha256,now,now,archive??DeploymentCoverageBundleTests.Zip([("report.csv",csv,0)]));}
    [Theory][InlineData(CoverageFamily.Application)][InlineData(CoverageFamily.ClassicConfiguration)]
    public void Original_report_is_bound_to_selection_actor_exact_request_and_original_archive(CoverageFamily family)
    {var selection=Scope(family).Prepare();var receipt=Receipt(selection);var result=IntuneExportObservation.Create(selection,receipt);Assert.Equal(selection.ObjectId,result.Request.SelectedObjectId);Assert.Equal(receipt.RequestSha256,result.Request.Sha256);Assert.Equal(receipt.OriginalArchiveBytes,result.Archive.OriginalArchiveBytes);Assert.Equal("report_opaque-1",result.JobId);Assert.False(result.IsHistorical);Assert.Single(result.Archive.Table.Rows);}
    [Theory][InlineData("tenant")][InlineData("account")][InlineData("client")][InlineData("request")]
    public void A_different_actor_or_report_cannot_become_this_selected_controls_observation(string defect)
    {var selection=Scope().Prepare();var receipt=Receipt(selection,tenant:defect=="tenant"?TestData.TenantB:null,account:defect=="account"?TestData.Office:null,client:defect=="client"?TestData.Office:null,digest:defect=="request"?new string('0',64):null);Assert.ThrowsAny<ToolkitException>(()=>IntuneExportObservation.Create(selection,receipt));}
    [Fact] public void Malformed_originals_never_become_an_empty_or_zero_success_observation()
    {var selection=Scope().Prepare();Assert.Throws<ConfigurationException>(()=>IntuneExportObservation.Create(selection,Receipt(selection,archive:[1,2,3])));}
    [Theory][InlineData("assessment")][InlineData("scope")]
    public void Read_only_or_missing_broader_purpose_scope_is_refused_even_for_supplied_receipt(string defect)
    {var f=Scope();if(defect=="assessment")f.Session.Mode=SessionMode.Assessment;else f.Session.Scopes.Remove(IntuneExportRequest.Prepare(CoverageFamily.Application,TestData.Office).RequiredScope);var selection=f.Prepare();Assert.Throws<SafetyViolationException>(()=>IntuneExportObservation.Create(selection,Receipt(selection)));}
}
