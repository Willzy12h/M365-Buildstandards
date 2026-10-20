using System.Net;
using System.Net.Http.Headers;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Graph.Auth;
namespace BuildStandard.Graph;

/// <summary>Sanitised receipt and original downloaded bytes. No signed address, token, response headers or cookies.</summary>
public sealed class IntuneExportReceipt
{
    private readonly byte[] _archive;
    public string TenantId {get;}
    public string AccountObjectId {get;}
    public string ClientId {get;}
    public string JobId {get;}
    public string RequestSha256 {get;}
    public string StartedAt {get;}
    public string CompletedAt {get;}
    public byte[] OriginalArchiveBytes=>(byte[])_archive.Clone();
    internal IntuneExportReceipt(string tenant,string account,string client,string job,string requestDigest,string started,string completed,byte[] archive)
    {TenantId=tenant;AccountObjectId=account;ClientId=client;JobId=job;RequestSha256=requestDigest;StartedAt=started;CompletedAt=completed;_archive=(byte[])archive.Clone();}
}

/// <summary>INT-127/130 fixed reporting-only job transport. Owns separate authenticated Graph and credential-free Blob clients.</summary>
public sealed class IntuneExportReader:IDisposable
{
    private static readonly TimeSpan Budget=TimeSpan.FromMinutes(5);
    private readonly HttpClient _graph,_download;
    private readonly IAccessTokenProvider _tokens;
    private readonly string _tenant,_account,_client;
    private readonly SessionMode _mode;
    private readonly string[] _scopes;
    private readonly TenantSession _session;
    private readonly Func<bool> _current;
    private readonly bool _acknowledged;
    private readonly TimeProvider _clock;
    private readonly long _started;
    private readonly string _startedAt;
    private readonly CancellationTokenSource _deadline,_lifetime=new();
    private int _disposed,_used,_gets,_requests;
    private readonly int _maximumRequests;
    public int RequestsSent=>Volatile.Read(ref _requests);
    private bool _renewed,_createSent;
    internal IntuneExportReader(HttpClient graph,HttpClient download,IAccessTokenProvider tokens,string tenant,SessionMode mode,TenantSession session,Func<bool> current,bool broaderTokenAcknowledged,TimeProvider? clock=null,int maximumRequests=100)
    {
        if(maximumRequests is <0 or >100)throw new ConfigurationException("A report can only use the remaining bounded capture requests, from zero to100.");
        _maximumRequests=maximumRequests;
        if(ReferenceEquals(graph,download))throw new ConfigurationException("The export download must own a separate credential-free client.");
        _graph=graph;_download=download;_tokens=tokens;_tenant=tenant;_mode=mode;_session=session;_current=current;_acknowledged=broaderTokenAcknowledged;
        _account=session.AccountObjectId;_client=session.ClientId;_scopes=session.Scopes?.ToArray()??[];_clock=clock??TimeProvider.System;
        _started=_clock.GetTimestamp();_startedAt=Timestamps.Format(_clock.GetUtcNow());_deadline=new(Budget,_clock);
    }
    public async Task<IntuneExportReceipt> ReadAsync(PreparedIntuneExportRequest request,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed)!=0,this);ct.ThrowIfCancellationRequested();
        if(Interlocked.Exchange(ref _used,1)!=0)throw new SafetyViolationException("This report-job attempt cannot be reused or replayed. A job may already exist; inspect it before deliberately preparing a new request.");
        using var operation=CancellationTokenSource.CreateLinkedTokenSource(ct,_deadline.Token,_lifetime.Token);
        try
        {
            RequireCurrent();
            var (job,retry)=await SendGraph(HttpMethod.Post,request.Path,null).ConfigureAwait(false);
            while(job.State is IntuneExportJobState.NotStarted or IntuneExportJobState.InProgress)
            {
                RequireCurrent();
                var delay=retry<TimeSpan.FromSeconds(10)?TimeSpan.FromSeconds(10):retry;
                var remaining=Budget-_clock.GetElapsedTime(_started);
                // A longer server wait exhausts this original attempt; never shorten it into an early poll.
                if(delay>remaining)delay=remaining;
                await Task.Delay(delay,_clock,operation.Token).ConfigureAwait(false);RequireCurrent();
                (job,retry)=await SendGraph(HttpMethod.Get,job.PollPath,job.Id).ConfigureAwait(false);
            }
            if(job.State!=IntuneExportJobState.Completed||job.Download is null)
                throw new ConfigurationException("The report job failed or its completion state is unknown. No empty/successful inventory is inferred and no create is replayed.");
            RequireCurrent();
            RequireRequestBudget();
            using var message=new HttpRequestMessage(HttpMethod.Get,job.Download);
            // This owned client has no Graph authentication, cookies or default credentials. Never log its signed URI.
            _requests++;
            using var response=await Bounded(_download.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,operation.Token),static r=>r.Dispose()).ConfigureAwait(false);RequireCurrent();
            if(response.StatusCode!=HttpStatusCode.OK)throw new GraphRequestException((int)response.StatusCode,"GET","intune-export-download","downloadRefused","The completed report could not be downloaded. No redirect, authenticated fallback or job replay was attempted.");
            var bytes=await ReadBody(response,5*1024*1024).ConfigureAwait(false);RequireCurrent();
            return new(_tenant,_account,_client,job.Id,request.Sha256,_startedAt,Timestamps.Format(_clock.GetUtcNow()),bytes);
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested)
        {throw new OperationCanceledException(_createSent?"Stopped. The report job may still exist or be running; no configuration write is inferred and no automatic replay is allowed.":"Stopped before a report job was sent.",ct);}
        catch(OperationCanceledException)
        {ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed)!=0,this);throw Timeout();}
        catch(HttpRequestException)
        {throw new GraphRequestException(0,"POST",request.Path,"exportTransport","Report transport failed. A report job may still exist; no automatic retry/replay or configuration-write claim is made. Signed download addresses are not included.");}
        catch(IOException)
        {throw new ConfigurationException("The original report could not be read completely. A report job may still exist; no automatic replay or smaller complete inventory is claimed.");}

        async Task<(IntuneExportJob Job,TimeSpan Retry)> SendGraph(HttpMethod method,string path,string? expectedId)
        {
            var force=false;
            while(true)
            {
                RequireCurrent();RequireRequestBudget();
                if(method==HttpMethod.Get&&_gets>=30)throw new ConfigurationException("The maximum 30 status GETs was reached. The report remains incomplete; no job was recreated.");
                var token=await Bounded(_tokens.GetAccessTokenAsync(force,operation.Token)).ConfigureAwait(false);RequireCurrent();
                if(string.IsNullOrWhiteSpace(token)||token.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)))throw new AuthenticationRequiredException("A current valid token is unavailable. Reconnect deliberately; this report never signs in or requests consent.");
                using var message=new HttpRequestMessage(method,"https://graph.microsoft.com/v1.0"+path);
                message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
                if(method==HttpMethod.Post){message.Content=new ByteArrayContent(request.Bytes);message.Content.Headers.ContentType=new("application/json");}
                RequireCurrent();RequireRequestBudget();_requests++;if(method==HttpMethod.Get)_gets++;else _createSent=true;
                using var response=await Bounded(_graph.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,operation.Token),static r=>r.Dispose()).ConfigureAwait(false);RequireCurrent();
                if(response.StatusCode==HttpStatusCode.Unauthorized&&!_renewed){_renewed=true;force=true;continue;}
                var expected=method==HttpMethod.Post?HttpStatusCode.Created:HttpStatusCode.OK;
                if(response.StatusCode!=expected)throw new GraphRequestException((int)response.StatusCode,method.Method,path,"exportJobFailed","The fixed report job request failed. No redirect, permission fallback, automatic retry or new create was attempted; a job may already exist.");
                var bytes=await ReadBody(response,32768).ConfigureAwait(false);RequireCurrent();
                var job=IntuneExportJobCodec.Read(bytes,request,expectedId,_clock.GetUtcNow());
                var retry=response.Headers.RetryAfter?.Delta??(response.Headers.RetryAfter?.Date is { } at?at-_clock.GetUtcNow():TimeSpan.Zero);
                return(job,retry);
            }
        }
        async Task<byte[]> ReadBody(HttpResponseMessage response,int maximum)
        {
            if(response.Content.Headers.ContentLength>maximum)throw new ConfigurationException("The report response exceeds its fixed byte bound. No smaller complete source is inferred.");
            await using var input=await Bounded(response.Content.ReadAsStreamAsync(operation.Token),static s=>s.Dispose()).ConfigureAwait(false);RequireCurrent();
            using var output=new MemoryStream();var buffer=new byte[16*1024];
            while(true){var count=await Bounded(input.ReadAsync(buffer,operation.Token).AsTask()).ConfigureAwait(false);RequireCurrent();if(count==0)break;if(output.Length>maximum-count)throw new ConfigurationException("The streamed report exceeds its fixed byte bound. No smaller complete source is inferred.");output.Write(buffer,0,count);}
            if(output.Length==0)throw new ConfigurationException("The original report response was empty; no successful empty source is inferred.");return output.ToArray();
        }
        async Task<T> Bounded<T>(Task<T> task,Action<T>? retireLate=null)
        {
            try{return await task.WaitAsync(operation.Token).ConfigureAwait(false);}
            catch
            {
                if(retireLate is not null)_=task.ContinueWith(t=>{try{retireLate(t.Result);}catch(Exception){}},CancellationToken.None,TaskContinuationOptions.OnlyOnRanToCompletion|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
                _=task.ContinueWith(static t=>{_=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);throw;
            }
        }
        void RequireRequestBudget()
        {if(_requests>=_maximumRequests)throw new ConfigurationException("The shared100-request selected-control capture budget is exhausted. No further report request or job replay is permitted.");}
        void RequireCurrent()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed)!=0,this);ct.ThrowIfCancellationRequested();
            if(_deadline.IsCancellationRequested||_clock.GetElapsedTime(_started)>=Budget)throw Timeout();operation.Token.ThrowIfCancellationRequested();
            if(!_acknowledged||_mode!=SessionMode.Deployment||_session.Mode!=SessionMode.Deployment)
                throw new SafetyViolationException("Modern export jobs require deliberately selected deployment access and explicit acknowledgement of its existing broader token for reporting only. Assessment mode cannot create jobs; no automatic switch or consent is offered.");
            if(!ReportValues.TryGuid(_tenant,out _)||!ReportValues.TryGuid(_account,out _)||!ReportValues.TryGuid(_client,out _)
                ||!_session.TenantVerified||!_session.OperatorVerified||!Same(_session.TenantId,_tenant)||!Same(_session.AccountObjectId,_account)||!Same(_session.OperatorObjectId,_account)||!Same(_session.ClientId,_client)
                ||_session.Scopes is null||!new HashSet<string>(_scopes,StringComparer.OrdinalIgnoreCase).SetEquals(_session.Scopes)||!_current())
                throw new TenantMismatchException("The verified tenant/account/application/access context changed. No result is admitted as this client's report.");
            if(!_scopes.Any(s=>Same(s,request.RequiredScope)||Same(s,"https://graph.microsoft.com/"+request.RequiredScope)))
                throw new SafetyViolationException("The verified connection lacks the fixed existing report-job scope. No additional access, registration change or consent is requested.");
        }
    }
    private static bool Same(string? left,string right)=>string.Equals(left,right,StringComparison.OrdinalIgnoreCase);
    private static GraphRequestException Timeout()=>new(0,"POST","/deviceManagement/reports/exportJobs","timeout","The original five-minute report budget ended. A report job may still be running; no complete result, configuration write or automatic replay is claimed.");
    public void Dispose(){if(Interlocked.Exchange(ref _disposed,1)!=0)return;_lifetime.Cancel();_deadline.Cancel();_graph.Dispose();_download.Dispose();_lifetime.Dispose();_deadline.Dispose();}
}
