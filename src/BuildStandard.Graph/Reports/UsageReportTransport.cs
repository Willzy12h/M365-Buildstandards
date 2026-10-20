using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Graph.Auth;
namespace BuildStandard.Graph.Reports;

/// <summary>INT-129 fixed Graph302 and credential-free download; no fallback, interactive renewal or URL retention.</summary>
public sealed class UsageReportTransport : IUsageReportSource, IDisposable
{
    private const int MaximumBytes = 10 * 1024 * 1024;
    private readonly HttpClient _graph, _download;
    private readonly IAccessTokenProvider _tokens;
    private readonly TenantSession _session;
    private readonly Action _requireCurrent;
    private readonly string _binding;
    private readonly bool _ownsClients;
    private bool _disposed;
    public string TenantId { get; }

    internal UsageReportTransport(HttpClient graph, HttpClient download, IAccessTokenProvider tokens,
        TenantSession session, Action requireCurrent, bool ownsClients = false)
    {
        _graph=graph;_download=download;_tokens=tokens;_session=session;_requireCurrent=requireCurrent;_ownsClients=ownsClients;
        TenantId=session.TenantId;
        RequireVerified(session);
        _binding=Binding(session);
        AssertCurrent(default);
    }
    internal static UsageReportTransport Create(IAccessTokenProvider tokens,TenantSession session,Action requireCurrent)
    {
        // These clients are dedicated to this bounded operation. Do not relax the ordinary Graph client's policy.
        static HttpClient Client()=>new(new HttpClientHandler
        {AllowAutoRedirect=false,UseCookies=false,UseDefaultCredentials=false}){Timeout=Timeout.InfiniteTimeSpan};
        var graph=Client();var download=Client();
        try{return new(graph,download,tokens,session,requireCurrent,ownsClients:true);}
        catch{graph.Dispose();download.Dispose();throw;}
    }
    public async Task<byte[]> DownloadAsync(string reportId,string period,CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        var route=UsageReportRegistry.Find(reportId).Route(period);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var ct=deadline.Token;
        try
        {
            Uri? location=null;
            for(var attempt=0;attempt<2;attempt++)
            {
                AssertCurrent(ct);
                var token=await _tokens.GetAccessTokenAsync(attempt==1,ct).ConfigureAwait(false);
                AssertCurrent(ct);
                using var request=new HttpRequestMessage(HttpMethod.Get,GraphClient.Host+"/v1.0"+route);
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
                using var response=await _graph.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
                AssertCurrent(ct);
                if(response.StatusCode==HttpStatusCode.Unauthorized&&attempt==0)continue;
                if(response.StatusCode==HttpStatusCode.Unauthorized)throw new AuthenticationRequiredException("The usage request was rejected after one silent renewal. Reconnect deliberately; nothing was downloaded.");
                if(response.StatusCode!=HttpStatusCode.Found)throw Refuse("Graph did not return the registered302 usage response. No download or fallback was attempted.");
                location=UsageDownloadAdmission.Require(response.Headers.Location);
                break;
            }
            if(location is null)throw Refuse("No usage download was admitted.");
            AssertCurrent(ct);
            if(_download.DefaultRequestHeaders.Authorization is not null || _download.DefaultRequestHeaders.Contains("Cookie"))
                throw Refuse("The isolated usage downloader cannot carry credentials or cookies.");
            using var downloadRequest=new HttpRequestMessage(HttpMethod.Get,location);
            downloadRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));
            downloadRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            using var downloadResponse=await _download.SendAsync(downloadRequest,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
            AssertCurrent(ct);
            if(downloadResponse.StatusCode!=HttpStatusCode.OK)throw Refuse("Usage download failed or redirected again. No output was admitted.");
            var mediaType=downloadResponse.Content.Headers.ContentType?.MediaType;
            if(!string.Equals(mediaType,"text/csv",StringComparison.OrdinalIgnoreCase)&&!string.Equals(mediaType,"application/octet-stream",StringComparison.OrdinalIgnoreCase))
                throw Refuse("Usage download did not return an admitted CSV media type. No output was admitted.");
            var length=downloadResponse.Content.Headers.ContentLength;
            if(length is <0 or >MaximumBytes)throw Refuse("Usage download exceeds10MiB. No truncated CSV was admitted.");
            await using var stream=await downloadResponse.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            AssertCurrent(ct);
            using var output=new MemoryStream(length is { } known?(int)known:0);
            var buffer=new byte[81920];
            while(true)
            {
                AssertCurrent(ct);
                var count=await stream.ReadAsync(buffer.AsMemory(),ct).ConfigureAwait(false);
                AssertCurrent(ct);
                if(count==0)break;
                if(count>MaximumBytes-output.Length)throw Refuse("Usage download exceeds10MiB. No truncated CSV was admitted.");
                output.Write(buffer,0,count);
            }
            if(length is not null&&length!=output.Length)throw Refuse("Usage download ended before its declared length. No empty or truncated success was admitted.");
            AssertCurrent(ct);
            // Strict CSV/header/row validation is the engine service's next admission step, before sealing any evidence.
            return output.ToArray();
        }
        catch(OperationCanceledException) when(!cancellation.IsCancellationRequested)
        {throw Refuse("The bounded usage request timed out. No partial download was admitted.");}
        catch(OperationCanceledException)
        {throw new OperationCanceledException("Usage report read stopped; no partial download was admitted.",cancellation);}
        catch(FormatException){throw Refuse("The usage response headers or address could not be admitted. No signed address or output was retained.");}
        catch(HttpRequestException){throw Refuse("The usage transport failed. No signed address, response body or partial download was retained.");}
        catch(IOException){throw Refuse("The usage stream could not be read completely. No partial download was admitted.");}
    }
    private void AssertCurrent(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();_requireCurrent();RequireVerified(_session);
        if(Binding(_session)!=_binding)throw new TenantMismatchException("The usage tenant, account, registration, permission mode or returned scopes changed. No result was admitted.");
    }
    private static void RequireVerified(TenantSession session)
    {
        if(!session.TenantVerified||!session.OperatorVerified||!ReportValues.IsCanonicalGuid(session.TenantId)
            ||!ReportValues.IsCanonicalGuid(session.AccountObjectId)||session.OperatorObjectId!=session.AccountObjectId
            ||!ReportValues.IsCanonicalGuid(session.ClientId)||string.IsNullOrWhiteSpace(session.Account)||!Enum.IsDefined(session.Mode)
            ||session.Scopes is null||!session.Scopes.Any(s=>string.Equals(s,"Reports.Read.All",StringComparison.OrdinalIgnoreCase)
                ||string.Equals(s,"https://graph.microsoft.com/Reports.Read.All",StringComparison.OrdinalIgnoreCase)))
            throw Refuse("Usage collection requires this verified delegated Graph account and an actually returned Reports.Read.All scope. No consent or privilege elevation is performed.");
    }
    private static string Binding(TenantSession session)=>JsonSerializer.Serialize(new{session.TenantId,session.AccountObjectId,session.Account,session.ClientId,session.Mode,
        Scopes=session.Scopes.OrderBy(scope=>scope,StringComparer.Ordinal).ToArray(),Resource="https://graph.microsoft.com"});
    private static ToolkitException Refuse(string reason)=>new(reason);
    public void Dispose(){if(_disposed)return;_disposed=true;if(_ownsClients){_graph.Dispose();_download.Dispose();}}
}
