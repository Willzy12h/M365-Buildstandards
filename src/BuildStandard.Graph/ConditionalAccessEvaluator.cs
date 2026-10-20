using System.Net;
using System.Net.Http.Headers;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Graph.Auth;

namespace BuildStandard.Graph;

/// <summary>One immutable read receipt. It never grants permission or proves a real sign-in succeeded.</summary>
public sealed class ConditionalAccessEvaluationRead
{
    private readonly byte[] _bytes;
    public string TenantId {get;}
    public string AccountObjectId {get;}
    public string Resource=>"https://graph.microsoft.com/";
    public PreparedConditionalAccessScenario Scenario {get;}
    public ConditionalAccessEvaluationResponse Result {get;}
    public byte[] OriginalResponseBytes=>(byte[])_bytes.Clone();
    public IReadOnlyList<string> Scopes {get;}
    public DateTimeOffset StartedAt {get;}
    public DateTimeOffset CompletedAt {get;}
    internal ConditionalAccessEvaluationRead(string tenant,string account,PreparedConditionalAccessScenario scenario,
        ConditionalAccessEvaluationResponse result,byte[] bytes,IEnumerable<string> scopes,DateTimeOffset started,DateTimeOffset completed)
    {
        TenantId=tenant;AccountObjectId=account;Scenario=scenario;Result=result;_bytes=(byte[])bytes.Clone();
        Scopes=Array.AsReadOnly(scopes.ToArray());StartedAt=started;CompletedAt=completed;
    }
}

/// <summary>Exactly one typed read-only POST route, with separate owned HTTP containment and the existing verified provider.</summary>
public sealed class ConditionalAccessEvaluator:IDisposable
{
    private const string Path="/identity/conditionalAccess/evaluate";
    private const string Endpoint="https://graph.microsoft.com/v1.0"+Path;
    private static readonly TimeSpan Budget=TimeSpan.FromSeconds(120);
    private readonly HttpClient _http;
    private readonly IAccessTokenProvider _tokens;
    private readonly string _tenant,_account,_client;
    private readonly SessionMode _mode;
    private readonly string[] _scopes;
    private readonly TenantSession _session;
    private readonly Func<bool> _current;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _lifetime=new();
    private int _disposed;

    internal ConditionalAccessEvaluator(HttpClient http,IAccessTokenProvider tokens,string tenantId,SessionMode mode,
        TenantSession session,Func<bool> current,TimeProvider? clock=null)
    {
        _http=http;_tokens=tokens;_tenant=tenantId;_mode=mode;_session=session;_current=current;_clock=clock??TimeProvider.System;
        _account=session.AccountObjectId;_client=session.ClientId;_scopes=session.Scopes?.ToArray()??[];
    }
    public async Task<ConditionalAccessEvaluationRead> EvaluateAsync(ConditionalAccessScenario scenario,CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed)!=0,this);ct.ThrowIfCancellationRequested();
        var started=_clock.GetUtcNow();var timestamp=_clock.GetTimestamp();
        using var deadline=new CancellationTokenSource(Budget,_clock);
        using var operation=CancellationTokenSource.CreateLinkedTokenSource(ct,deadline.Token,_lifetime.Token);
        var frozen=ConditionalAccessScenarioCodec.Prepare(scenario);
        try
        {
            for(var attempt=0;attempt<2;attempt++)
            {
                RequireCurrent();
                var acquisition=_tokens.GetAccessTokenAsync(attempt==1,operation.Token);
                string token;
                try { token=await acquisition.WaitAsync(operation.Token).ConfigureAwait(false); }
                catch
                {
                    // A provider that ignores cancellation may finish later. Observe faults without using a late token.
                    _=acquisition.ContinueWith(static t=>{_=t.Exception;},CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
                    throw;
                }
                RequireCurrent();
                if(string.IsNullOrWhiteSpace(token)||token.Any(c=>char.IsWhiteSpace(c)||char.IsControl(c)))
                    throw new AuthenticationRequiredException("The current access token is unavailable or malformed. Reconnect deliberately; the report does not sign in or retry.");
                using var request=new HttpRequestMessage(HttpMethod.Post,Endpoint){Content=new ByteArrayContent(frozen.Bytes)};
                request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json");
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
                // No await or I/O between final identity/cancellation/deadline admission and this fixed send.
                RequireCurrent();
                using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,operation.Token).ConfigureAwait(false);
                RequireCurrent();
                if(response.StatusCode==HttpStatusCode.Unauthorized&&attempt==0)continue;
                if(response.StatusCode!=HttpStatusCode.OK)
                    throw new GraphRequestException((int)response.StatusCode,"POST",Path,"evaluationFailed","Microsoft could not complete the hypothetical evaluation. No automatic retry or tenant change was performed.");
                if(response.Content.Headers.ContentLength is >ConditionalAccessEvaluationResponseCodec.MaximumBytes)
                    throw new ConfigurationException("The evaluation response exceeds 5 MiB. No complete result is available.");
                await using var source=await response.Content.ReadAsStreamAsync(operation.Token).ConfigureAwait(false);RequireCurrent();
                using var buffer=new MemoryStream();var chunk=new byte[16*1024];
                while(true)
                {
                    var count=await source.ReadAsync(chunk,operation.Token).ConfigureAwait(false);RequireCurrent();
                    if(count==0)break;
                    if(buffer.Length>ConditionalAccessEvaluationResponseCodec.MaximumBytes-count)
                        throw new ConfigurationException("The evaluation response exceeds 5 MiB. No complete result is available.");
                    buffer.Write(chunk,0,count);
                }
                var bytes=buffer.ToArray();var result=ConditionalAccessEvaluationResponseCodec.Read(bytes);RequireCurrent();
                var read=new ConditionalAccessEvaluationRead(_tenant,_account,frozen,result,bytes,_scopes,started,_clock.GetUtcNow());
                RequireCurrent();return read;
            }
            throw new InvalidOperationException("The evaluation renewal loop did not terminate.");
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed)!=0,this);
            if(Expired())throw TimeoutFailure();
            throw new GraphRequestException(0,"POST",Path,"interrupted","The hypothetical evaluation was interrupted. No result or tenant change is claimed, and no retry was attempted.");
        }
        catch(HttpRequestException)
        {throw new GraphRequestException(0,"POST",Path,"transport","The hypothetical evaluation could not be read. No result or tenant change is claimed, and no retry was attempted.");}

        bool Expired()=>deadline.IsCancellationRequested||_clock.GetElapsedTime(timestamp)>=Budget;
        GraphRequestException TimeoutFailure()=>new(0,"POST",Path,"timeout","The 120-second evaluation budget ended. No complete result is available; no automatic retry was attempted.");
        void RequireCurrent()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed)!=0,this);ct.ThrowIfCancellationRequested();
            if(Expired())throw TimeoutFailure();
            operation.Token.ThrowIfCancellationRequested();
            var scopes=_session.Scopes;
            if(_tenant is not {Length:36}||_account is not {Length:36}||_client is not {Length:36}||!Guid.TryParseExact(_tenant,"D",out var tenant)||tenant==Guid.Empty||!Guid.TryParseExact(_account,"D",out var account)||account==Guid.Empty
                ||!Guid.TryParseExact(_client,"D",out var client)||client==Guid.Empty||_mode is not (SessionMode.Assessment or SessionMode.Deployment)
                ||!_session.TenantVerified||!_session.OperatorVerified||_session.Mode!=_mode
                ||!string.Equals(_session.TenantId,_tenant,StringComparison.OrdinalIgnoreCase)||!string.Equals(_session.AccountObjectId,_account,StringComparison.OrdinalIgnoreCase)
                ||!string.Equals(_session.OperatorObjectId,_account,StringComparison.OrdinalIgnoreCase)||!string.Equals(_session.ClientId,_client,StringComparison.OrdinalIgnoreCase)
                ||scopes is null||!new HashSet<string>(_scopes,StringComparer.OrdinalIgnoreCase).SetEquals(scopes)||!_current())
                throw new TenantMismatchException("The verified tenant, account or access context changed. Review the current connection before a fresh evaluation.");
            if(!_scopes.Any(s=>string.Equals(s,"Policy.Read.All",StringComparison.OrdinalIgnoreCase)||string.Equals(s,"https://graph.microsoft.com/Policy.Read.All",StringComparison.OrdinalIgnoreCase)))
                throw new ConfigurationException("The current connection does not have Policy.Read.All. Reports never request consent or switch permission mode.");
        }
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0)return;
        _lifetime.Cancel();_http.Dispose();_lifetime.Dispose();
    }
}
