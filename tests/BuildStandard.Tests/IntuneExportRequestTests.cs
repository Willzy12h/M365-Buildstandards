using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;
public sealed class IntuneExportRequestTests
{
    [Theory][InlineData(CoverageFamily.Application,"DeviceInstallStatusByApp","ApplicationId","DeviceManagementApps.ReadWrite.All")][InlineData(CoverageFamily.ClassicConfiguration,"DeviceAssignmentStatusByConfigurationPolicy","PolicyId","DeviceManagementConfiguration.ReadWrite.All")]
    public void Only_exact_report_filter_columns_and_documented_CSV_are_compiled(CoverageFamily family,string reportName,string filterColumn,string scope)
    {
        var r=IntuneExportRequest.Prepare(family,TestData.Office);using var d=JsonDocument.Parse(r.Bytes);var root=d.RootElement;
        Assert.Equal("/deviceManagement/reports/exportJobs",r.Path);Assert.Equal(reportName,r.ReportName);Assert.Equal(scope,r.RequiredScope);Assert.Equal(reportName,root.GetProperty("reportName").GetString());Assert.Equal(filterColumn+" eq '"+TestData.Office+"'",root.GetProperty("filter").GetString());Assert.Equal("csv",root.GetProperty("format").GetString());Assert.Equal("localizedValuesAsAdditionalColumn",root.GetProperty("localizationType").GetString());Assert.Equal(5,root.EnumerateObject().Count());Assert.Equal(CanonicalJson.Sha256Hex(r.Bytes),r.Sha256);
        string[] expected=family==CoverageFamily.Application?["ApplicationId","DeviceId","UserId","AppInstallState","InstallState","InstallStateDetail","ErrorCode","LastModifiedDateTime","Platform"]:["PolicyId","IntuneDeviceId","AadDeviceId","AssignmentStatus","PspdpuLastModifiedTimeUtc","UnifiedPolicyPlatformType","UnifiedPolicyType","AssignmentFilterIds","ReportStatus"];
        Assert.Equal(expected,r.Columns);Assert.Equal(expected,root.GetProperty("select").EnumerateArray().Select(x=>x.GetString()));Assert.Equal(TestData.Office,r.SelectedObjectId);
    }
    [Fact] public void Request_bytes_and_column_inventory_are_not_mutable_by_callers()
    {var r=IntuneExportRequest.Prepare(CoverageFamily.Application,TestData.Office);var bytes=r.Bytes;bytes[0]=0;Assert.NotEqual(0,r.Bytes[0]);Assert.False(r.Columns is string[]);Assert.Equal(r.Bytes,IntuneExportRequest.Prepare(CoverageFamily.Application,TestData.Office.ToUpperInvariant()).Bytes);}
    [Theory][InlineData(CoverageFamily.ConditionalAccess)][InlineData(CoverageFamily.ClassicCompliance)][InlineData((CoverageFamily)99)]
    public void Unsupported_reports_cannot_be_substituted_with_a_free_form_request(CoverageFamily family)
    {Assert.Throws<ConfigurationException>(()=>IntuneExportRequest.Prepare(family,TestData.Office));}
    [Theory][InlineData("friendly name")][InlineData("../reports")][InlineData("https://graph.microsoft.com/")][InlineData("00000000-0000-0000-0000-000000000000")][InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")][InlineData("x' or true")]
    public void Query_URL_name_and_non_canonical_identity_inputs_are_refused(string id)
    {Assert.Throws<ConfigurationException>(()=>IntuneExportRequest.Prepare(CoverageFamily.Application,id));}
}
