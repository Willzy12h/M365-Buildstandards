using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Graph.Auth;
namespace BuildStandard.Graph.Lifecycle;

/// <summary>INT-137 fixed GETs, strict continuation identity and one owned bounded preview lifetime.</summary>
public sealed class LifecycleReadTransport : ILifecycleReadSource
{
    private const int MaximumBytes=16*1024*1024, MaximumRows=5000, MaximumRequests=100;
    private readonly HttpClient _http;
    private readonly IAccessTokenProvider _tokens;
    private readonly TenantSession _session;
    private readonly Action _requireCurrent;
    private readonly bool _ownsClient;
    private readonly string _binding;
    private readonly CancellationTokenSource _lifetime;
    private readonly Dictionary<string,LifecycleReadRequest> _requests;
    private readonly Dictionary<string,Uri?> _next=[];
    private readonly HashSet<string> _visited=new(StringComparer.Ordinal);
    private int _reading,_rows,_bytes;
    private bool _renewed,_faulted,_disposed;
    public string TenantId { get; }
    public string AccountObjectId { get; }
    public string ClientId { get; }
    public SessionMode Mode { get; }
    public LifecycleOperation Operation { get; }
    public int RequestCount { get; private set; }
    internal LifecycleReadTransport(HttpClient http,IAccessTokenProvider tokens,TenantSession session,LifecycleOperation operation,Action requireCurrent,bool ownsClient=false)
    {
        _http=http; _tokens=tokens; _session=session; _requireCurrent=requireCurrent; _ownsClient=ownsClient;
        Operation=LifecycleActionRegistry.RequireRegistered(operation); _requests=LifecycleReadRegistry.For(Operation).ToDictionary(request=>request.Id,StringComparer.Ordinal);
        if (!session.TenantVerified || !session.OperatorVerified || !GuidId(session.TenantId) || !GuidId(session.ClientId)
            || !GuidId(session.AccountObjectId) || !string.Equals(session.AccountObjectId,session.OperatorObjectId,StringComparison.OrdinalIgnoreCase)
            || !Enum.IsDefined(session.Mode) || !Timestamps.TryParse(session.TokenExpiresAt,out _))
            throw Refuse("Use a verified existing tenant/account context with a known token lifetime. This reader never signs in.");
        foreach(var scope in _requests.Values.Select(request=>request.Scope).Distinct(StringComparer.Ordinal))
            if(!session.Scopes.Any(value=>HasScope(value,scope))) throw Refuse("Required lifecycle before-read access is unavailable: "+scope+". Review access deliberately; no consent is requested here.");
        requireCurrent(); TenantId=session.TenantId; AccountObjectId=session.AccountObjectId; ClientId=session.ClientId; Mode=session.Mode;
        _binding=Binding(session); _lifetime=new(TimeSpan.FromMinutes(5));
    }
    internal static LifecycleReadTransport Create(IAccessTokenProvider tokens,TenantSession session,LifecycleOperation operation,Action requireCurrent)
    {
        var http=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false,UseCookies=false,UseDefaultCredentials=false }) { Timeout=Timeout.InfiniteTimeSpan };
        try { return new(http,tokens,session,operation,requireCurrent,ownsClient:true); }
        catch { http.Dispose(); throw; }
    }
    public async Task<LifecycleReadPage> ReadNextAsync(string sourceId,CancellationToken ct)
    {
        if(Interlocked.CompareExchange(ref _reading,1,0)!=0) throw Refuse("Wait for the owned lifecycle read; concurrent pages are refused.");
        try
        {
            AssertCurrent(ct);
            if(!_requests.TryGetValue(sourceId,out var request)) throw Refuse("This source is not a dependency of the registered lifecycle action.");
            var first=new Uri(GraphClient.Host+"/v1.0"+request.Path);
            var uri=_next.TryGetValue(sourceId,out var next)?next:first;
            if(uri is null) throw Refuse("This source was already read completely. A fresh preview requires a new owned capture.");
            if(_rows>=MaximumRows || RequestCount>=MaximumRequests) throw Refuse("Lifecycle preview reached its shared row/request bound; readiness remains incomplete.");
            if(!_visited.Add(uri.AbsoluteUri)) throw Refuse("Repeated lifecycle pagination cursor; readiness remains incomplete.");
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,_lifetime.Token);
            while(true)
            {
                AssertCurrent(linked.Token);
                var access=await _tokens.GetAccessTokenAsync(false,linked.Token).ConfigureAwait(false); AssertCurrent(linked.Token);
                if(RequestCount>=MaximumRequests) throw Refuse("Lifecycle preview reached 100 actual requests; readiness remains incomplete.");
                using var message=new HttpRequestMessage(HttpMethod.Get,uri);
                message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access); message.Headers.Accept.ParseAdd("application/json");
                AssertCurrent(linked.Token); RequestCount++;
                using var response=await _http.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,linked.Token).ConfigureAwait(false);
                AssertCurrent(linked.Token);
                if(response.StatusCode==HttpStatusCode.Unauthorized && !_renewed)
                {
                    _renewed=true; _=await _tokens.GetAccessTokenAsync(true,linked.Token).ConfigureAwait(false); AssertCurrent(linked.Token); continue;
                }
                if(response.StatusCode!=HttpStatusCode.OK) throw new GraphRequestException((int)response.StatusCode,"GET",request.Path,null,"A required lifecycle source did not return HTTP 200. No server body, empty-success result or automatic retry is admitted.");
                var length=response.Content.Headers.ContentLength;
                if(length is <0 || length>MaximumBytes-_bytes) throw Refuse("Lifecycle preview response exceeds its remaining 16 MiB bound.");
                await using var stream=await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false); AssertCurrent(linked.Token);
                using var buffer=new MemoryStream(); var chunk=new byte[81920];
                while(true)
                {
                    var count=await stream.ReadAsync(chunk.AsMemory(),linked.Token).ConfigureAwait(false); AssertCurrent(linked.Token);
                    if(count==0) break;
                    if(count>MaximumBytes-_bytes-buffer.Length) throw Refuse("Lifecycle preview exceeded 16 MiB; no truncated page was admitted.");
                    buffer.Write(chunk,0,count);
                }
                if(length is not null && length!=buffer.Length) throw Refuse("Lifecycle source returned a truncated body; readiness remains incomplete.");
                _bytes+=checked((int)buffer.Length);
                using var parsed=JsonDocument.Parse(buffer.ToArray(),new JsonDocumentOptions { MaxDepth=32,AllowDuplicateProperties=false });
                var root=parsed.RootElement; if(root.ValueKind!=JsonValueKind.Object) throw Refuse("Lifecycle source did not return an object.");
                var rows=new List<JsonObject>();
                if(request.Single)
                {
                    var expected=sourceId=="user"?Operation.UserId:Operation.GroupId;
                    if(root.TryGetProperty("@odata.nextLink",out _) || !root.TryGetProperty("id",out var id) || id.ValueKind!=JsonValueKind.String
                        || !string.Equals(id.GetString(),expected,StringComparison.OrdinalIgnoreCase)) throw Refuse("Returned lifecycle target does not match the exact selection.");
                    rows.Add(JsonNode.Parse(root.GetRawText())!.AsObject());
                }
                else
                {
                    if(!root.TryGetProperty("value",out var items) || items.ValueKind!=JsonValueKind.Array || items.GetArrayLength()>MaximumRows-_rows)
                        throw Refuse("Lifecycle collection is malformed or exceeds its remaining row bound.");
                    foreach(var item in items.EnumerateArray())
                    {
                        if(item.ValueKind!=JsonValueKind.Object) throw Refuse("Lifecycle collection contains an unknown row shape.");
                        rows.Add(JsonNode.Parse(item.GetRawText())!.AsObject());
                    }
                }
                if(rows.Count>MaximumRows-_rows) throw Refuse("Lifecycle preview row bound ended.");
                Uri? continuation=null;
                if(root.TryGetProperty("@odata.nextLink",out var link))
                {
                    if(link.ValueKind!=JsonValueKind.String) throw Refuse("Lifecycle continuation is not an admitted address.");
                    continuation=Continuation(link.GetString()!,first);
                    if(_visited.Contains(continuation.AbsoluteUri)) throw Refuse("Lifecycle source repeated a pagination cursor.");
                }
                AssertCurrent(linked.Token); _rows+=rows.Count; _next[sourceId]=continuation;
                return new(rows.AsReadOnly(),continuation is not null);
            }
        }
        catch(JsonException error) { _faulted=true; throw new ConfigurationException("Malformed, duplicate or over-deep lifecycle JSON; no page admitted.",error); }
        catch { _faulted=true; throw; }
        finally { Volatile.Write(ref _reading,0); }
    }
    private void AssertCurrent(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_disposed,this);
        if(_faulted) throw Refuse("This lifecycle preview ended with an incomplete read; prepare a fresh preview deliberately.");
        _requireCurrent(); if(Binding(_session)!=_binding) throw new TenantMismatchException("Lifecycle tenant/account/access context changed. No result or further request was admitted.");
    }
    private static Uri Continuation(string raw,Uri first)
    {
        if(raw.Length>8192 || !Uri.TryCreate(raw,UriKind.Absolute,out var next) || next.Scheme!=Uri.UriSchemeHttps
            || !string.Equals(next.Host,"graph.microsoft.com",StringComparison.OrdinalIgnoreCase) || next.Port!=443 || next.UserInfo.Length!=0 || next.Fragment.Length!=0
            || !string.Equals(next.AbsolutePath,first.AbsolutePath,StringComparison.Ordinal)) throw Refuse("Lifecycle continuation left the fixed origin/resource.");
        var expected=Query(first); var actual=Query(next);
        foreach(var pair in expected) if(!actual.TryGetValue(pair.Key,out var value) || value!=pair.Value) throw Refuse("Lifecycle continuation changed the original filter or selection.");
        foreach(var pair in actual)
            if(!expected.ContainsKey(pair.Key) && (pair.Key is not ("$skiptoken" or "$skip") || pair.Value.Length==0
                || pair.Key=="$skip" && (!int.TryParse(pair.Value,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var count) || count<0)))
                throw Refuse("Unknown lifecycle continuation parameter.");
        return next;
    }
    private static Dictionary<string,string> Query(Uri uri)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var pair in uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries))
        {
            var parts=pair.Split('=',2); if(parts.Length!=2) throw Refuse("Malformed lifecycle query.");
            var key=WebUtility.UrlDecode(parts[0]); var value=WebUtility.UrlDecode(parts[1]);
            if(key.Any(char.IsControl) || value.Any(char.IsControl) || !result.TryAdd(key,value)) throw Refuse("Duplicate or malformed lifecycle query parameter.");
        }
        return result;
    }
    private static bool HasScope(string raw,string expected)
    {
        var value=raw.Trim(); const string prefix="https://graph.microsoft.com/";
        if(value.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) value=value[prefix.Length..];
        return string.Equals(value,expected,StringComparison.OrdinalIgnoreCase);
    }
    private static bool GuidId(string? value)=>Guid.TryParseExact(value,"D",out var id)&&id!=Guid.Empty;
    private static string Binding(TenantSession session)=>ToolkitJson.Serialize(new {session.TenantId,session.AccountObjectId,session.OperatorObjectId,session.ClientId,session.Mode,session.TenantVerified,session.OperatorVerified,Scopes=session.Scopes.Order(StringComparer.Ordinal).ToArray()});
    private static ConfigurationException Refuse(string reason)=>new(reason);
    public void Dispose() { if(_disposed)return;_disposed=true;_lifetime.Cancel();if(_ownsClient)_http.Dispose();_lifetime.Dispose(); }
}
