using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CoverageResponseCodecTests
{
    private static PreparedCoverageRead Request(bool collection)=>DeploymentCoverageReads.Prepare(CoverageFamily.ClassicCompliance,TestData.Office,collection?CoverageReadKind.ServiceResults:CoverageReadKind.Object);
    private static byte[] Bytes(string text)=>Encoding.UTF8.GetBytes(text);
    [Fact] public void Explicit_empty_list_is_an_observed_empty_source_not_effective_deployment()
    {var result=CoverageResponseCodec.Read(Bytes("{\"value\":[]}"),Request(true));Assert.Empty(result.Rows);Assert.False(result.HasMore);Assert.Contains("not proof",result.Limitations);}
    [Theory][InlineData("{}")][InlineData("{\"value\":null}")][InlineData("{\"value\":{}}")][InlineData("[]")][InlineData("{\"value\":[null]}")][InlineData("{\"value\":[7]}")][InlineData("{\"value\":[{}]}")]
    public void Missing_or_malformed_collection_is_not_empty_success(string json)
    {Assert.Throws<ConfigurationException>(()=>CoverageResponseCodec.Read(Bytes(json),Request(true)));}
    [Theory][InlineData("{\"value\":[],\"value\":[]}")][InlineData("{\"value\":[{\"id\":\"a\",\"id\":\"b\"}]}")][InlineData("{\"value\":[{\"id\":\"a\",\"nested\":{\"x\":1,\"x\":2}}]}")]
    public void Duplicate_properties_at_any_depth_are_refused(string json)
    {Assert.Throws<ConfigurationException>(()=>CoverageResponseCodec.Read(Bytes(json),Request(true)));}
    [Fact] public void Service_row_ID_is_opaque_and_original_bytes_and_raw_unknowns_are_preserved()
    {var bytes=Bytes("{ \"value\": [{\"id\":\"opaque-row\",\"status\":\"future\",\"deviceDisplayName\":null}] }");var result=CoverageResponseCodec.Read(bytes,Request(true));Assert.Equal("opaque-row",Assert.Single(result.Rows)["id"]!.GetValue<string>());Assert.Equal(bytes,result.OriginalBytes);Assert.Equal("future",result.Rows[0]["status"]!.GetValue<string>());}
    [Fact] public void Result_arrays_and_bytes_are_defensive()
    {var result=CoverageResponseCodec.Read(Bytes("{\"value\":[{\"id\":\"row\"}]}"),Request(true));result.Rows[0]["id"]="changed";var bytes=result.OriginalBytes;bytes[0]=0;Assert.Equal("row",result.Rows[0]["id"]!.GetValue<string>());Assert.NotEqual(0,result.OriginalBytes[0]);}
    [Fact] public void Duplicate_result_rows_are_refused_not_counted_twice()
    {Assert.Throws<ConfigurationException>(()=>CoverageResponseCodec.Read(Bytes("{\"value\":[{\"id\":\"row\"},{\"id\":\"row\"}]}"),Request(true)));}
    [Theory][InlineData("")][InlineData(" ")][InlineData(" row")][InlineData("row ")][InlineData("row\n")]
    public void Invalid_row_identity_is_not_a_success(string id)
    {var json=System.Text.Json.JsonSerializer.Serialize(new{value=new[]{new{id}}});Assert.Throws<ConfigurationException>(()=>CoverageResponseCodec.Read(Bytes(json),Request(true)));}
    [Fact] public void Exact_selected_object_is_required_not_a_collection_or_other_identity()
    {Assert.Single(CoverageResponseCodec.Read(Bytes("{\"id\":\""+TestData.Office+"\",\"displayName\":null}"),Request(false)).Rows);Assert.Throws<TenantMismatchException>(()=>CoverageResponseCodec.Read(Bytes("{\"id\":\""+TestData.Operator+"\"}"),Request(false)));Assert.Throws<ConfigurationException>(()=>CoverageResponseCodec.Read(Bytes("{\"value\":[]}"),Request(false)));}
    [Fact] public void Further_page_is_partial_even_if_this_page_is_empty_and_is_never_followed_by_codec()
    {var result=CoverageResponseCodec.Read(Bytes("{\"value\":[],\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0/deviceManagement/deviceCompliancePolicies/next\"}"),Request(true));Assert.True(result.HasMore);Assert.Contains("incomplete",result.Limitations);}
    [Theory][InlineData("42")][InlineData("\"\"")][InlineData("{}")]
    public void Malformed_further_page_declaration_is_refused(string value)
    {Assert.Throws<ConfigurationException>(()=>CoverageResponseCodec.Read(Bytes("{\"value\":[],\"@odata.nextLink\":"+value+"}"),Request(true)));}
    [Fact] public void Rows_and_source_bytes_are_bounded_before_a_complete_result_exists()
    {var rows=string.Join(',',Enumerable.Range(0,5001).Select(i=>"{\"id\":\"row-"+i+"\"}"));Assert.Throws<ConfigurationException>(()=>CoverageResponseCodec.Read(Bytes("{\"value\":["+rows+"]}"),Request(true)));Assert.Throws<ConfigurationException>(()=>CoverageResponseCodec.Read(new byte[5*1024*1024+1],Request(true)));}
    [Fact] public void Group_member_ID_requires_directory_GUID_and_retains_returned_type()
    {var request=DeploymentCoverageReads.Group(TestData.Office,true);Assert.Throws<ConfigurationException>(()=>CoverageResponseCodec.Read(Bytes("{\"value\":[{\"id\":\"opaque-row\"}]}"),request));var result=CoverageResponseCodec.Read(Bytes("{\"value\":[{\"id\":\""+TestData.Operator+"\",\"@odata.type\":\"#microsoft.graph.user\"}]}"),request);Assert.Equal("#microsoft.graph.user",result.Rows[0]["@odata.type"]!.GetValue<string>());}
    [Theory][InlineData(true)][InlineData(false)]
    public void A_service_error_envelope_cannot_be_a_success_even_with_a_plausible_result(bool collection)
    {var json=collection?"{\"value\":[],\"error\":{\"code\":\"AccessDenied\"}}":"{\"id\":\""+TestData.Office+"\",\"error\":{\"code\":\"AccessDenied\"}}";Assert.Throws<ConfigurationException>(()=>CoverageResponseCodec.Read(Bytes(json),Request(collection)));}

}
