using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;

public enum CoverageSourceState { Complete,Partial,Failed,Stopped,ContextChanged,NotAttempted,Unsupported }

/// <summary>Bounded source evidence only. Complete never means effective deployment or write authority.</summary>
public sealed class CoverageSourceCapture
{
    private readonly JsonObject[] _rows;
    private readonly byte[][] _pages;
    public CoverageSourceState State {get;}
    public IReadOnlyList<JsonObject> Rows=>Array.AsReadOnly(_rows.Select(r=>(JsonObject)r.DeepClone()).ToArray());
    public IReadOnlyList<byte[]> OriginalPages=>Array.AsReadOnly(_pages.Select(p=>(byte[])p.Clone()).ToArray());
    public string Detail {get;}
    /// <summary>Context admission at completion, not continuing authentication or authority.</summary>
    public bool IsCurrent=>State!=CoverageSourceState.ContextChanged;
    internal CoverageSourceCapture(CoverageSourceState state,IEnumerable<JsonObject> rows,IEnumerable<byte[]> pages,string detail)
    {State=state;_rows=rows.Select(r=>(JsonObject)r.DeepClone()).ToArray();_pages=pages.Select(p=>(byte[])p.Clone()).ToArray();Detail=detail+" This is a source-read state, not effective targeting, installation or device-specific verification.";}
}

/// <summary>Shared page collection; delegates only already prepared reads. Unknown/failed sources never become empty success.</summary>
public sealed class CoverageSourceCollector
{
    private readonly TimeProvider _clock;
    public CoverageSourceCollector(TimeProvider? clock=null){_clock=clock??TimeProvider.System;}
    public async Task<CoverageSourceCapture> CollectAsync(PreparedCoverageRead request,Func<PreparedCoverageRead,CancellationToken,Task<CoverageReadResponse>> read,Func<bool> current,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);ArgumentNullException.ThrowIfNull(read);ArgumentNullException.ThrowIfNull(current);
        var rows=new List<JsonObject>();var pages=new List<byte[]>();var paths=new HashSet<string>(StringComparer.Ordinal);
        var identities=new HashSet<string>(StringComparer.Ordinal);var hashes=new HashSet<string>(StringComparer.Ordinal);var bytes=0;
        using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(5),_clock);
        using var operation=CancellationTokenSource.CreateLinkedTokenSource(ct,deadline.Token);
        try
        {
            while(true)
            {
                if(!current())return Finish(CoverageSourceState.ContextChanged,"Client context changed; these originals must not be admitted as current-client results.");
                operation.Token.ThrowIfCancellationRequested();
                if(pages.Count>=100)return Finish(CoverageSourceState.Partial,"The 100-page limit was reached; further results were not read.");
                if(!paths.Add(request.Path))return Finish(CoverageSourceState.Partial,"A continuation loop was refused; earlier pages remain available.");
                var task=read(request,operation.Token);CoverageReadResponse response;
                try{response=await task.WaitAsync(operation.Token).ConfigureAwait(false);}
                catch
                {
                    _=task.ContinueWith(static t=>{_=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);throw;
                }
                var original=response.OriginalBytes;
                if(original.Length>CoverageResponseCodec.MaximumBytes-bytes)return Finish(CoverageSourceState.Partial,"The original-source byte limit was reached; the excess page was not admitted.");
                // Bind every original page to this prepared source, not a caller-supplied projection or list of rows.
                var admitted=CoverageResponseCodec.Read(original,request);var pageRows=admitted.Rows;
                if(pageRows.Count>CoverageResponseCodec.MaximumRows-rows.Count)return Finish(CoverageSourceState.Partial,"The 5,000-row limit was reached; the excess page was not admitted.");
                bytes+=original.Length;pages.Add(original);
                if(!current())return Finish(CoverageSourceState.ContextChanged,"Client context changed during the read; the original is retained separately, not current-client coverage.");
                operation.Token.ThrowIfCancellationRequested();
                if(!hashes.Add(Convert.ToHexString(SHA256.HashData(original))))return Finish(CoverageSourceState.Partial,"A repeated page was retained but not counted twice.");
                var duplicate=false;
                foreach(var row in pageRows)
                {
                    var id=row["id"]!.GetValue<string>();
                    if(request.IsGroupMembers&&ReportValues.TryGuid(id,out var canonical))id=canonical;
                    if(!identities.Add(id)){duplicate=true;continue;}
                    rows.Add(row);
                }
                if(duplicate)return Finish(CoverageSourceState.Partial,"A duplicate source-row identity across pages was retained in originals but not counted twice.");
                if(!admitted.HasMore)return Finish(CoverageSourceState.Complete,"The requested source finished, including an explicitly empty result if returned.");
                using var json=JsonDocument.Parse(original);
                try{request=DeploymentCoverageReads.Continue(request,json.RootElement.GetProperty("@odata.nextLink").GetString()!);}
                catch(ConfigurationException){return Finish(CoverageSourceState.Partial,"The next link was outside the fixed continuation contract; earlier original pages remain available.");}
            }
        }
        catch(OperationCanceledException)
        {return Finish(ct.IsCancellationRequested?CoverageSourceState.Stopped:pages.Count>0?CoverageSourceState.Partial:CoverageSourceState.Failed,ct.IsCancellationRequested?"The engineer stopped collection; admitted earlier pages remain available.":"The five-minute source deadline ended; no complete source is claimed.");}
        catch(ToolkitException){return Finish(pages.Count>0?CoverageSourceState.Partial:CoverageSourceState.Failed,"The source could not be admitted or read; this is not an empty-success result. Review access, source support and the selected client.");}
        catch(HttpRequestException){return Finish(pages.Count>0?CoverageSourceState.Partial:CoverageSourceState.Failed,"The source transport failed; earlier pages remain available and no automatic retry is performed here.");}

        CoverageSourceCapture Finish(CoverageSourceState state,string detail)=>new(current()?state:CoverageSourceState.ContextChanged,rows,pages,detail);
    }
}
