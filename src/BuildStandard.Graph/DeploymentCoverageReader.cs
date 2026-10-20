using System.Net;
using System.Net.Http.Headers;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Graph.Auth;
namespace BuildStandard.Graph;

/// <summary>Owned, fixed-descriptor source reads for one selected-control capture. No arbitrary URLs, writes or interactive authentication.</summary>
public sealed class DeploymentCoverageReader:IDisposable
{
    private static readonly TimeSpan Budget=TimeSpan.FromMinutes(5);
    private readonly HttpClient _http;
    private readonly IAccessTokenProvider _tokens;
    private readonly string _tenant,_account,_client;
    private readonly SessionMode _mode;
    private readonly TenantSession _session;
    private readonly Func<bool> _current;
    private readonly string[] _scopes;
    private readonly TimeProvider _clock;
    private readonly long _started;
    private readonly CancellationTokenSource _deadline,_lifetime=new();
    private int _disposed,_active,_sends,_rows,_bytes;
    internal DeploymentCoverageReader(HttpClient http,IAccessTokenProvider tokens,string tenant,SessionMode mode,TenantSession session,Func<bool> current,TimeProvider? clock=null)
    {
        _http=http;_tokens=tokens;_tenant=tenant;_mode=mode;_session=session;_current=current;_account=session.AccountObjectId;_client=session.ClientId;
        _scopes=session.Scopes?.ToArray()??[];_clock=clock??TimeProvider.System;_started=_clock.GetTimestamp();_deadline=new(Budget,_clock);
    }
    public async Task<CoverageReadResponse> ReadAsync(PreparedCoverageRead request,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed)!=0,this);ct.ThrowIfCancellationRequested();
        if(Interlocked.CompareExchange(ref _active,1,0)!=0)throw new ConfigurationException("A coverage source read is already in progress. No overlapping read was started.");
        try
        {
            using var operation=CancellationTokenSource.CreateLinkedTokenSource(ct,_deadline.Token,_lifetime.Token);
            for(var attempt=0;attempt<2;attempt++)
            {
                RequireCurrent();
                if(_sends>=100||_rows>=CoverageResponseCodec.MaximumRows||_bytes>=CoverageResponseCodec.MaximumBytes)
                    throw new ConfigurationException("The selected-control capture reached its page, row or byte limit. Retain earlier rows as partial; no further source was read.");
                var token=await Bounded(_tokens.GetAccessTokenAsync(attempt==1,operation.Token)).ConfigureAwait(false);RequireCurrent();
                if(string.IsNullOrWhiteSpace(token)||token.Any(c=>char.IsWhiteSpace(c)||char.IsControl(c)))
                    throw new AuthenticationRequiredException("The current access token is unavailable or malformed. Reconnect deliberately; coverage does not sign in or request consent.");
                using var message=new HttpRequestMessage(HttpMethod.Get,"https://graph.microsoft.com/v1.0"+request.Path);
                message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
                RequireCurrent();_sends++;
                using var response=await Bounded(_http.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,operation.Token),static r=>r.Dispose()).ConfigureAwait(false);RequireCurrent();
                if(response.StatusCode==HttpStatusCode.Unauthorized&&attempt==0)continue;
                if(response.StatusCode!=HttpStatusCode.OK)throw new GraphRequestException((int)response.StatusCode,"GET",request.Path,"coverageReadFailed","The coverage source could not be read. This is not an empty result; no automatic retry or permission upgrade was performed.");
                if(response.Content.Headers.ContentLength is >0&&response.Content.Headers.ContentLength>CoverageResponseCodec.MaximumBytes-_bytes)
                    throw new ConfigurationException("The source exceeds the selected-control byte budget. Earlier results remain partial.");
                await using var stream=await Bounded(response.Content.ReadAsStreamAsync(operation.Token),static s=>s.Dispose()).ConfigureAwait(false);RequireCurrent();
                using var buffer=new MemoryStream();var chunk=new byte[16*1024];
                while(true)
                {
                    var count=await Bounded(stream.ReadAsync(chunk,operation.Token).AsTask()).ConfigureAwait(false);RequireCurrent();
                    if(count==0)break;
                    if(count>CoverageResponseCodec.MaximumBytes-_bytes)throw new ConfigurationException("The streamed source exceeds the selected-control byte budget. No smaller complete result is claimed.");
                    _bytes+=count;buffer.Write(chunk,0,count);
                }
                var result=CoverageResponseCodec.Read(buffer.ToArray(),request);RequireCurrent();
                var countRows=result.Rows.Count;
                if(countRows>CoverageResponseCodec.MaximumRows-_rows)throw new ConfigurationException("The selected-control capture exceeds 5,000 rows. Earlier source pages remain partial.");
                _rows+=countRows;RequireCurrent();return result;
            }
            throw new InvalidOperationException("The coverage renewal loop did not terminate.");

            async Task<T> Bounded<T>(Task<T> task,Action<T>? retireLate=null)
            {
                try{return await task.WaitAsync(operation.Token).ConfigureAwait(false);}
                catch
                {
                    if(retireLate is not null)_=task.ContinueWith(t=>{try{retireLate(t.Result);}catch(Exception){}},CancellationToken.None,TaskContinuationOptions.OnlyOnRanToCompletion|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
                    _=task.ContinueWith(static t=>{_=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);throw;
                }
            }
            void RequireCurrent()
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed)!=0,this);ct.ThrowIfCancellationRequested();
                if(_deadline.IsCancellationRequested||_clock.GetElapsedTime(_started)>=Budget)throw Timeout();operation.Token.ThrowIfCancellationRequested();
                if(!ReportValues.TryGuid(_tenant,out _)||!ReportValues.TryGuid(_account,out _)||!ReportValues.TryGuid(_client,out _)
                    ||_mode is not (SessionMode.Assessment or SessionMode.Deployment)||!_session.TenantVerified||!_session.OperatorVerified
                    ||_session.Mode!=_mode||!Same(_session.TenantId,_tenant)||!Same(_session.AccountObjectId,_account)||!Same(_session.OperatorObjectId,_account)||!Same(_session.ClientId,_client)
                    ||_session.Scopes is null||!new HashSet<string>(_scopes,StringComparer.OrdinalIgnoreCase).SetEquals(_session.Scopes)||!_current())
                    throw new TenantMismatchException("The verified tenant/account/access context changed. No source is admitted as current coverage.");
                if(!_scopes.Any(s=>Same(s,request.Scope)||Same(s,"https://graph.microsoft.com/"+request.Scope)))
                    throw new ConfigurationException("The current connection lacks the registered coverage read scope. No consent, access-mode change or permission fallback is attempted.");
            }
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested)
        {ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed)!=0,this);if(_deadline.IsCancellationRequested||_clock.GetElapsedTime(_started)>=Budget)throw Timeout();throw new GraphRequestException(0,"GET",request.Path,"coverageInterrupted","Coverage was interrupted; no complete result is claimed.");}
        catch(HttpRequestException){throw new GraphRequestException(0,"GET",request.Path,"coverageTransport","The coverage source could not be read. Earlier source pages remain partial; no retry was performed.");}
        finally{Volatile.Write(ref _active,0);}
    }
    private static bool Same(string? left,string right)=>string.Equals(left,right,StringComparison.OrdinalIgnoreCase);
    private static GraphRequestException Timeout()=>new(0,"GET","coverage","timeout","The five-minute selected-control capture budget ended. Earlier source pages remain partial; no complete population is claimed.");
    public void Dispose(){if(Interlocked.Exchange(ref _disposed,1)!=0)return;_lifetime.Cancel();_deadline.Cancel();_http.Dispose();_lifetime.Dispose();_deadline.Dispose();}
}
