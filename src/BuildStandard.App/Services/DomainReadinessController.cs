using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Exchange;
using BuildStandard.Engine.Reports;

namespace BuildStandard.App.Services;

/// <summary>Local DNS orchestration. Original unsaved bytes outlive presentation, never authorise deployment or another query.</summary>
public sealed class DomainReadinessController
{
    private readonly Workspace _workspace;
    private readonly Func<DomainReadinessRequest,CancellationToken,Task<DomainReadinessReport>> _collect;
    private DomainReadinessDocument? _result,_pending;
    private PresentationContextKey? _ticket;
    private Func<bool> _selectionCurrent=()=>false;
    private IReadOnlyList<DomainReadinessResult> _rows=[];
    private string _summary="",_details="";
    // Keep the same collector/resolver for this page lifetime: uncertain owned-helper cleanup blocks replacement.
    public DomainReadinessController(Workspace workspace):this(workspace,new DomainReadinessCollector().CollectAsync) { }
    internal DomainReadinessController(Workspace workspace,Func<DomainReadinessRequest,CancellationToken,Task<DomainReadinessReport>> collect)
    { _workspace=workspace;_collect=collect; }
    public DomainReadinessDocument? Current=>_ticket is not null&&_workspace.IsCurrent(_ticket)&&_selectionCurrent()?_result:null;
    public DomainReadinessDocument? Pending=>_pending;
    public bool HasPending=>_pending is not null;
    public bool IsRunning {get;private set;}
    public string Message {get;private set;}="Choose a domain and checks. Navigation makes no DNS query.";
    public string SavedDirectory {get;private set;}="";
    public string CurrentSummary=>Current is null?"No result for the current working context.":_summary;
    public string PendingSummary=>_pending is null?"":_summary+"\nNOT SAVED. Preserve or explicitly discard these original bytes before another run or closing.";
    public string CurrentDetails=>Current is null?"":_details;
    public IReadOnlyList<DomainReadinessResult> CurrentResults=>Current is null?[]:_rows;
    private void RequireIdle()
    {if(!_workspace.Idle)throw new ConfigurationException("Wait for the active workspace operation to finish.");}
    private void RequireNewResult()
    {RequireIdle();if(HasPending)throw new ConfigurationException("Save, export or explicitly discard the original unsaved domain report before replacing it.");}
    public async Task CollectAsync(DomainReadinessRequest request,bool acknowledged,Func<bool> selectionCurrent)
    {
        RequireNewResult();
        if(!acknowledged)throw new ConfigurationException("Acknowledge the experimental DNS read, its exact domain and limits first. This grants no Microsoft consent.");
        if(request.TenantId is not null||request.AccountObjectId is not null)throw new ConfigurationException("The form cannot supply a tenant or account binding. Only the current verified connection may provide it.");
        var frozen=DomainReadinessCollector.PrepareRequest(request);
        if(_workspace.Connection is { } connection)
        {
            var s=connection.Session;
            if(!s.TenantVerified||!s.OperatorVerified||!ProfileValidator.IsGuid(s.TenantId)||!ProfileValidator.IsGuid(s.AccountObjectId)
                ||!string.Equals(s.TenantId,_workspace.Profile?.TenantId,StringComparison.OrdinalIgnoreCase)
                ||!string.Equals(s.AccountObjectId,s.OperatorObjectId,StringComparison.OrdinalIgnoreCase)
                ||!string.Equals(s.TenantId,connection.Graph.TenantId,StringComparison.OrdinalIgnoreCase)||s.Mode!=connection.Graph.Mode)
                throw new TenantMismatchException("The connection identity is inconsistent. Disconnect or verify it before a new domain check.");
            frozen=frozen with{TenantId=s.TenantId,AccountObjectId=s.AccountObjectId};
        }
        var ticket=_workspace.CapturePresentationContext();
        if(!selectionCurrent())throw new ConfigurationException("The selected domain or checks changed; review them again.");
        await _workspace.RunExclusiveAsync("Reading selected domain DNS",async progress=>
        {
            IsRunning=true;_result=null;SavedDirectory="";
            try
            {
                if(!_workspace.IsCurrent(ticket)||!selectionCurrent())throw new ConfigurationException("Context changed before the DNS read. No query was started.");
                Message="Reading "+frozen.Domain+". Stop preserves any admitted partial observations.";
                progress.Report("Fixed Windows resolver; 20 seconds per question, 120 seconds overall; at most32questions/500observations. No tenant authentication or writes.");
                var ct=_workspace.OperationToken;
                var report=await Task.Run(()=>_collect(frozen,ct));
                // Validation and document projection are bounded local work, off the UI thread. Cancellation still admits a truthful Cancelled report.
                var data=await Task.Run(()=>
                {
                    var doc=DomainReadinessDocument.Collected(report);var r=doc.Report;
                    if(r.Domain!=frozen.Domain||r.Cloud!=frozen.Cloud||r.TenantId!=frozen.TenantId||r.AccountObjectId!=frozen.AccountObjectId
                        ||!r.RequestedChecks.SequenceEqual(frozen.Checks)||!r.ExpectedMx.SequenceEqual(frozen.ExpectedMx!)||r.ExpectedMxSource!=frozen.ExpectedMxSource)
                        throw new ConfigurationException("Returned DNS report does not match the exact initiating request. No result was admitted.");
                    return Presentation(doc);
                });
                Admit(data,ticket,selectionCurrent);_pending=data.Document;
                Message=Current is null?"Context changed. The original report is retained only for explicit recovery; it is not a result for this client."
                    :"Read coverage: "+report.Status+". Review the findings, then save the report pack. Complete does not mean readiness passed.";
            }
            catch(OperationCanceledException) when(_workspace.OperationToken.IsCancellationRequested)
            {Message="Stopped before a verified report was returned. No successful or empty result was admitted.";throw;}
            catch(Exception){Message="The read could not be confirmed. No unverified report was admitted; review the error and local prerequisites.";throw;}
            finally{IsRunning=false;}
        });
    }
    public async Task ImportAsync(string file,Func<bool> selectionCurrent)
    {
        RequireNewResult();var ticket=_workspace.CapturePresentationContext();
        await _workspace.RunExclusiveAsync("Opening historical domain report",async _=>
        {
            var ct=_workspace.OperationToken;
            var data=await Task.Run(()=>{ct.ThrowIfCancellationRequested();return Presentation(DomainReadinessDocument.ReadFile(file));},ct);
            ct.ThrowIfCancellationRequested();
            if(!_workspace.IsCurrent(ticket)||!selectionCurrent())throw new ConfigurationException("Context changed while opening the report. No result was relabelled; the original file is unchanged.");
            Admit(data,ticket,selectionCurrent);SavedDirectory="";Message="Historical supplied report — unverified source and recorded binding. No DNS query was made.";
        });
    }
    public async Task<string> ExportAsync(string output,bool recoverOriginal=false,bool acknowledged=false)
    {
        RequireIdle();
        var document=recoverOriginal?_pending:Current;
        if(document is null)throw new ConfigurationException("No current result is available. Use explicit original-report recovery if an unsaved report is retained.");
        if(recoverOriginal&&!acknowledged)throw new ConfigurationException("Acknowledge the original report identity before recovery. It will not be rebound to the current client.");
        bool StillCurrent()=>recoverOriginal?ReferenceEquals(_pending,document):ReferenceEquals(Current,document);
        var folder="";
        await _workspace.RunExclusiveAsync("Saving original domain report pack",async _=>
        {
            try
            {
                var ct=_workspace.OperationToken;
                folder=await Task.Run(()=>DomainReadinessDocuments.Export(document,_workspace.Paths,output,StillCurrent,ct),ct);
                if(ReferenceEquals(_pending,document))_pending=null;
                SavedDirectory=folder;Message="Original report pack saved. This did not repeat DNS reads or turn it into deployment evidence.";
            }
            catch(Exception){Message="Report save was not confirmed. Original unsaved bytes are retained; inspect output before a deliberate retry. No query is repeated.";throw;}
        });
        return folder;
    }
    public void Discard(bool acknowledged)
    {
        RequireIdle();if(!acknowledged||_pending is null)throw new ConfigurationException("Acknowledge the original unsaved report before discarding it.");
        _result=null;_pending=null;_ticket=null;_rows=[];_summary="";_details="";Message="Original unsaved report discarded explicitly. No DNS query was repeated.";
    }
    private sealed record Display(DomainReadinessDocument Document,string Summary,string Details,IReadOnlyList<DomainReadinessResult> Rows);
    private static Display Presentation(DomainReadinessDocument document)
    {
        var r=document.Report;
        var summary=$"Original domain: {r.Domain} · read coverage: {r.Status}\nRecorded initiating tenant: {r.TenantId??"Unbound — no tenant verified"}\n{r.StartedAt} to {r.CompletedAt}\n{document.SourceNotice}\nOriginal SHA-256: {document.OriginalSha256}";
        var details=string.Join("\n\n",DomainReadinessDocuments.Sheets(document).Select(s=>s.Name+"\n"+string.Join("\n",s.Rows.Select(row=>string.Join(" | ",row)))));
        if(details.Length>65536)details=details[..65536]+"\n[Display limited to65,536characters. The report pack retains the complete original observations.]";
        var rows=Array.AsReadOnly(r.Results.Select(x=>x with{Details=Array.AsReadOnly(x.Details.ToArray())}).ToArray());
        return new(document,summary,details,rows);
    }
    private void Admit(Display data,PresentationContextKey ticket,Func<bool> selectionCurrent)
    {_result=data.Document;_ticket=ticket;_selectionCurrent=selectionCurrent;_summary=data.Summary;_details=data.Details;_rows=data.Rows;}
}
