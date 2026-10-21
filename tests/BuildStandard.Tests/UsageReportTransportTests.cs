using System.Net;
using System.Net.Http.Headers;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Graph.Auth;
using BuildStandard.Graph.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class UsageReportTransportTests
{
    private const string Signed="https://reports.office.com/data/download/opaque?secret=synthetic-secret";
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Redirect_download_has_no_graph_credentials_and_only_one_401_silent_renewal(bool renew)
    {
        var graphCalls=0;var downloads=0;var tokens=new Tokens();var expected=new byte[]{1,2,3};
        using var graph=new HttpClient(new Handler(request=>
        {
            graphCalls++;Assert.Equal(HttpMethod.Get,request.Method);Assert.Equal("https://graph.microsoft.com/v1.0/reports/getEmailActivityUserDetail(period='D30')",request.RequestUri!.OriginalString);
            Assert.Equal("synthetic-token",request.Headers.Authorization?.Parameter);
            if(renew&&graphCalls==1)return new(HttpStatusCode.Unauthorized);
            return new(HttpStatusCode.Found){Headers={Location=new Uri(Signed)}};
        }));
        using var download=new HttpClient(new Handler(request=>
        {
            downloads++;Assert.Equal(Signed,request.RequestUri!.OriginalString);Assert.Null(request.Headers.Authorization);Assert.False(request.Headers.Contains("Cookie"));
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(expected){Headers={ContentType=new MediaTypeHeaderValue("application/octet-stream")}}};
        }));
        using var reader=new UsageReportTransport(graph,download,tokens,Session(),()=>{});
        Assert.Equal(expected,await reader.DownloadAsync("email-activity","D30",default));
        Assert.Equal(renew?2:1,graphCalls);Assert.Equal(1,downloads);Assert.Equal(renew?new[]{false,true}:new[]{false},tokens.Refreshes);
    }
    [Theory][InlineData("scope")][InlineData("identity")][InlineData("changed-after-token")][InlineData("changed-after-graph")][InlineData("redirect-host")][InlineData("second-redirect")][InlineData("media-type")][InlineData("second-401")][InlineData("response-failure")][InlineData("oversize")]
    public async Task Refusals_keep_identity_access_and_signed_urls_out_of_evidence_and_errors(string fault)
    {
        var current=true;var graphCalls=0;var downloads=0;var session=Session();var tokens=new Tokens();
        if(fault=="scope")session.Scopes=[];if(fault=="identity")session.OperatorVerified=false;
        if(fault=="changed-after-token")tokens.Acquired=()=>current=false;
        using var graph=new HttpClient(new Handler(_=>
        {
            graphCalls++;if(fault=="changed-after-graph")current=false;
            if(fault=="second-401")return new(HttpStatusCode.Unauthorized);
            if(fault=="response-failure")throw new HttpRequestException(Signed);
            return new(HttpStatusCode.Found){Headers={Location=new Uri(fault=="redirect-host"?"https://evil.example/data/download/opaque":Signed)}};
        }));
        using var download=new HttpClient(new Handler(_=>
        {
            downloads++;if(fault=="second-redirect")return new(HttpStatusCode.Found){Headers={Location=new Uri(Signed)}};
            var content=new ByteArrayContent(fault=="oversize"?new byte[10*1024*1024+1]:new byte[]{1});content.Headers.ContentType=new MediaTypeHeaderValue(fault=="media-type"?"text/html":"text/csv");
            return new(HttpStatusCode.OK){Content=content};
        }));
        var error=await Assert.ThrowsAnyAsync<ToolkitException>(async()=>{using var reader=new UsageReportTransport(graph,download,tokens,session,()=>{if(!current)throw new TenantMismatchException("Synthetic context changed.");});await reader.DownloadAsync("email-activity","D30",default);});
        Assert.DoesNotContain("synthetic-secret",error.ToString());Assert.DoesNotContain("opaque",error.ToString());
        if(fault is "scope" or "identity" or "changed-after-token")Assert.Equal(0,graphCalls);
        if(fault is "scope" or "identity" or "changed-after-token" or "changed-after-graph" or "redirect-host" or "second-401" or "response-failure")Assert.Equal(0,downloads);
        Assert.True(graphCalls<=2);Assert.True(downloads<=1);
    }
    internal static TenantSession Session()
    {
        var session=TestData.Session();session.Scopes=["User.Read","Reports.Read.All"];return session;
    }
    private sealed class Tokens:IAccessTokenProvider
    {
        public List<bool> Refreshes {get;}=[];public Action? Acquired {get;set;}
        public Task<string> GetAccessTokenAsync(CancellationToken ct)=>GetAccessTokenAsync(false,ct);
        public Task<string> GetAccessTokenAsync(bool refresh,CancellationToken ct){ct.ThrowIfCancellationRequested();Refreshes.Add(refresh);Acquired?.Invoke();return Task.FromResult("synthetic-token");}
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(respond(request));}}
}
