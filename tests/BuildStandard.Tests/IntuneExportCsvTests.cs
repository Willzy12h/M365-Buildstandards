using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class IntuneExportCsvTests
{
    private static PreparedIntuneExportRequest Request(CoverageFamily family=CoverageFamily.Application)=>IntuneExportRequest.Prepare(family,TestData.Office);
    private static string Q(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
    private static byte[] Csv(PreparedIntuneExportRequest request,Dictionary<string,string>? values=null,IEnumerable<string>? columns=null)
    {var names=(columns??request.Columns).ToArray();return Encoding.UTF8.GetBytes(string.Join(',',names)+"\r\n"+string.Join(',',names.Select(n=>Q(values?.GetValueOrDefault(n)??(n==request.Columns[0]?request.SelectedObjectId:""))))+"\r\n");}
    [Theory][InlineData(CoverageFamily.Application)][InlineData(CoverageFamily.ClassicConfiguration)]
    public void Exact_raw_columns_and_selected_object_bind_without_inventing_cell_types(CoverageFamily family)
    {var request=Request(family);var bytes=Csv(request);var result=IntuneExportCsv.Read(bytes,request);Assert.Equal(request.Columns,result.Columns);Assert.Equal(TestData.Office,Assert.Single(result.Rows)[request.Columns[0]]);Assert.Equal(bytes,result.OriginalBytes);Assert.Equal(CanonicalJson.Sha256Hex(bytes),result.OriginalSha256);Assert.Contains("not deployment",result.Limitations);}
    [Fact] public void Reordered_columns_and_optional_localised_values_are_bound_by_exact_heading()
    {var request=Request();var names=request.Columns.Reverse().Append("InstallState_loc");var result=IntuneExportCsv.Read(Csv(request,new(){["InstallState"]="0",["InstallState_loc"]="Localised label"},names),request);Assert.Equal("0",result.Rows[0]["InstallState"]);Assert.Equal("Localised label",result.Rows[0]["InstallState_loc"]);}
    [Fact] public void Raw_blank_zero_unknown_quoted_commas_newlines_and_doubled_quotes_are_preserved()
    {var request=Request();var values=new Dictionary<string,string>{["AppInstallState"]="0",["InstallState"]="future",["InstallStateDetail"]="comma, then\r\n\"quoted\"",["ErrorCode"]="",["Platform"]="=HYPERLINK(\"untrusted\")"};var result=IntuneExportCsv.Read(Csv(request,values),request);foreach(var pair in values)Assert.Equal(pair.Value,result.Rows[0][pair.Key]);Assert.Equal("",result.Rows[0]["DeviceId"]);Assert.Contains("unestablished",result.Limitations);}
    [Fact] public void BOM_is_accepted_but_its_original_bytes_and_digest_are_unchanged()
    {var request=Request();var bytes=new byte[]{0xef,0xbb,0xbf}.Concat(Csv(request)).ToArray();var result=IntuneExportCsv.Read(bytes,request);Assert.Equal(bytes,result.OriginalBytes);Assert.Equal(CanonicalJson.Sha256Hex(bytes),result.OriginalSha256);}
    [Fact] public void Explicit_header_only_is_empty_data_not_a_missing_source()
    {var request=Request();Assert.Empty(IntuneExportCsv.Read(Encoding.UTF8.GetBytes(string.Join(',',request.Columns)+"\n"),request).Rows);Assert.Throws<ConfigurationException>(()=>IntuneExportCsv.Read([],request));}
    [Theory][InlineData("missing")][InlineData("duplicate")][InlineData("wrong-case")][InlineData("foreign")][InlineData("localised-only")][InlineData("blank")]
    public void Unsupported_headings_never_become_a_smaller_successful_table(string defect)
    {var request=Request();var names=request.Columns.ToList();switch(defect){case "missing":names.RemoveAt(1);break;case "duplicate":names.Add(names[1]);break;case "wrong-case":names[0]="applicationId";break;case "foreign":names.Add("UnrequestedColumn");break;case "localised-only":names[1]+="_loc";break;case "blank":names[1]="";break;}Assert.Throws<ConfigurationException>(()=>IntuneExportCsv.Read(Csv(request,columns:names),request));}
    [Theory][InlineData("\"unfinished")][InlineData("bare\"quote")][InlineData(" \"spaced\"")][InlineData("\"closed\"tail")]
    public void Malformed_quoting_is_refused_without_echoing_private_cells(string bad)
    {var request=Request();var text=string.Join(',',request.Columns)+"\n"+TestData.Office+","+bad;var error=Assert.Throws<ConfigurationException>(()=>IntuneExportCsv.Read(Encoding.UTF8.GetBytes(text),request));Assert.DoesNotContain(bad,error.Message);}
    [Fact] public void Blank_physical_records_are_not_silently_discarded_as_complete()
    {var request=Request();var good=Encoding.UTF8.GetString(Csv(request));Assert.Throws<ConfigurationException>(()=>IntuneExportCsv.Read(Encoding.UTF8.GetBytes(good+"\n"),request));}
    [Fact] public void Unequal_record_width_and_nonstandard_line_separator_are_refused()
    {var request=Request();Assert.Throws<ConfigurationException>(()=>IntuneExportCsv.Read(Encoding.UTF8.GetBytes(string.Join(',',request.Columns)+"\n"+TestData.Office),request));Assert.Throws<ConfigurationException>(()=>IntuneExportCsv.Read(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Csv(request)).Replace("\r\n","\r")),request));}
    [Theory][InlineData("foreign")][InlineData("missing")]
    public void A_row_cannot_claim_the_selected_report_when_its_selected_identity_is_missing_or_foreign(string defect)
    {var request=Request();Assert.Throws<ConfigurationException>(()=>IntuneExportCsv.Read(Csv(request,new(){["ApplicationId"]=defect=="missing"?"":TestData.TenantB}),request));}
    [Fact] public void Invalid_UTF8_or_UTF16_is_not_decoded_using_a_workstation_guess()
    {Assert.Throws<ConfigurationException>(()=>IntuneExportCsv.Read([0xc3,0x28],Request()));Assert.Throws<ConfigurationException>(()=>IntuneExportCsv.Read([0xff,0xfe,65,0],Request()));}
    [Fact] public void Returned_bytes_columns_and_rows_are_immutable_to_the_caller()
    {var request=Request();var result=IntuneExportCsv.Read(Csv(request),request);var bytes=result.OriginalBytes;bytes[0]=0;Assert.NotEqual(0,result.OriginalBytes[0]);Assert.False(result.Columns is string[]);var row=Assert.IsAssignableFrom<IDictionary<string,string>>(result.Rows[0]);Assert.Throws<NotSupportedException>(()=>row["DeviceId"]="changed");}
    [Fact] public void Row_field_and_total_byte_caps_are_enforced_before_a_complete_source_exists()
    {var request=Request();var csv=Encoding.UTF8.GetString(Csv(request));var row=csv[(csv.IndexOf('\n')+1)..];var tooMany=string.Join(',',request.Columns)+"\n"+string.Concat(Enumerable.Repeat(row,5001));Assert.Throws<ConfigurationException>(()=>IntuneExportCsv.Read(Encoding.UTF8.GetBytes(tooMany),request));Assert.Throws<ConfigurationException>(()=>IntuneExportCsv.Read(Csv(request,new(){["InstallStateDetail"]=new string('x',65537)}),request));Assert.Throws<ConfigurationException>(()=>IntuneExportCsv.Read(new byte[20*1024*1024+1],request));}
}
