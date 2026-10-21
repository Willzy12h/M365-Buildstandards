using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using BuildStandard.Graph;
using Xunit;
namespace BuildStandard.Tests;
public sealed class IntuneExportJobCodecTests
{
    private static PreparedIntuneExportRequest Request()=>IntuneExportRequest.Prepare(CoverageFamily.Application,TestData.Office);
    internal static byte[] Body(PreparedIntuneExportRequest request,string id="report_opaque-1",string status="notStarted",string? url=null)
    {var node=JsonNode.Parse(request.Bytes)!.AsObject();node["id"]=id;node["status"]=status;node["url"]=url;node["expirationDateTime"]="2026-10-20T12:00:00Z";return Encoding.UTF8.GetBytes(node.ToJsonString());}
    [Theory][InlineData("notStarted","NotStarted")][InlineData("inProgress","InProgress")][InlineData("failed","Failed")][InlineData("unknown","Unknown")][InlineData("futureStatus","Unknown")]
    public void Bound_states_are_distinct_and_future_status_never_becomes_completed(string status,string expected)
    {var read=IntuneExportJobCodec.Read(Body(Request(),status:status),Request());Assert.Equal(expected,read.State.ToString());Assert.Equal(status,read.RawStatus);Assert.Null(read.Download);Assert.Equal("/deviceManagement/reports/exportJobs/report_opaque-1",read.PollPath);}
    [Fact] public void Completed_public_blob_url_is_private_transport_state_not_a_printable_report_or_log()
    {const string url="https://synthetic.blob.core.windows.net/reports/export.zip?sig=synthetic-not-a-secret";var read=IntuneExportJobCodec.Read(Body(Request(),status:"completed",url:url),Request(),"report_opaque-1",new DateTimeOffset(2026,10,20,11,0,0,TimeSpan.Zero));Assert.Equal(IntuneExportJobState.Completed,read.State);Assert.Equal(url,read.Download!.AbsoluteUri);Assert.DoesNotContain("synthetic-not-a-secret",read.ToString());Assert.DoesNotContain("blob.core",read.ToString());}
    [Theory][InlineData("id")][InlineData("reportName")][InlineData("filter")][InlineData("select")][InlineData("format")][InlineData("localizationType")]
    public void Missing_or_mismatched_job_association_is_refused_without_echoing_source_fields(string field)
    {var node=JsonNode.Parse(Body(Request()))!.AsObject();node[field]=field=="select"?new JsonArray("unrequested"):JsonValue.Create("private-foreign-value");var error=Assert.ThrowsAny<ToolkitException>(()=>IntuneExportJobCodec.Read(Encoding.UTF8.GetBytes(node.ToJsonString()),Request(),"report_opaque-1"));Assert.DoesNotContain("private-foreign-value",error.Message);node.Remove(field);Assert.ThrowsAny<ToolkitException>(()=>IntuneExportJobCodec.Read(Encoding.UTF8.GetBytes(node.ToJsonString()),Request()));}
    [Theory][InlineData("../other")][InlineData("..")][InlineData(".")][InlineData("a/b")][InlineData("a\\b")][InlineData("a?b")][InlineData("a#b")][InlineData("a%2fb")][InlineData("a%252fb")][InlineData("a%2eb")][InlineData("a b")][InlineData("")]
    public void Unsafe_job_identity_cannot_change_a_poll_relation(string id)
    {Assert.Throws<ConfigurationException>(()=>IntuneExportJobCodec.Read(Body(Request(),id),Request()));}
    [Fact] public void Opaque_non_GUID_ID_is_escaped_as_one_segment_without_guessing_a_GUID()
    {var read=IntuneExportJobCodec.Read(Body(Request(),"job('opaque')"),Request());Assert.Equal("/deviceManagement/reports/exportJobs/job%28%27opaque%27%29",read.PollPath);}
    [Theory][InlineData("http://synthetic.blob.core.windows.net/report.zip?sig=synthetic-secret")][InlineData("https://synthetic.blob.core.windows.net.evil.test/report.zip?sig=synthetic-secret")][InlineData("https://a.b.blob.core.windows.net/report.zip?sig=synthetic-secret")][InlineData("https://synthetic.blob.core.windows.net:444/report.zip?sig=synthetic-secret")][InlineData("https://user@synthetic.blob.core.windows.net/report.zip?sig=synthetic-secret")][InlineData("https://synthetic.blob.core.windows.net/report.zip#synthetic-secret")][InlineData("https://127.0.0.1/report.zip?sig=synthetic-secret")]
    public void Unsupported_download_hosts_schemes_ports_and_userinfo_are_refused_without_leaking_addresses(string url)
    {var error=Assert.Throws<ConfigurationException>(()=>IntuneExportJobCodec.Read(Body(Request(),status:"completed",url:url),Request(),now:new DateTimeOffset(2026,10,20,11,0,0,TimeSpan.Zero)));Assert.DoesNotContain("synthetic-secret",error.Message);}
    [Fact] public void Completed_without_url_or_with_expired_job_is_not_admitted_for_download()
    {Assert.Throws<ConfigurationException>(()=>IntuneExportJobCodec.Read(Body(Request(),status:"completed"),Request()));Assert.Throws<ConfigurationException>(()=>IntuneExportJobCodec.Read(Body(Request(),status:"completed",url:"https://synthetic.blob.core.windows.net/report.zip"),Request(),now:new DateTimeOffset(2026,10,20,13,0,0,TimeSpan.Zero)));}
    [Fact] public void Error_duplicate_envelopes_and_oversize_are_not_successful_empty_jobs()
    {var body=Encoding.UTF8.GetString(Body(Request()));Assert.Throws<ConfigurationException>(()=>IntuneExportJobCodec.Read(Encoding.UTF8.GetBytes(body.Insert(1,"\"id\":\"duplicate\",")),Request()));var node=JsonNode.Parse(body)!.AsObject();node["error"]=new JsonObject();Assert.Throws<ConfigurationException>(()=>IntuneExportJobCodec.Read(Encoding.UTF8.GetBytes(node.ToJsonString()),Request()));Assert.Throws<ConfigurationException>(()=>IntuneExportJobCodec.Read(new byte[32769],Request()));}
}
