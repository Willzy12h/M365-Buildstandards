using BuildStandard.Core.Models;
using BuildStandard.Engine.Exchange;
using BuildStandard.Engine.Reports;
namespace BuildStandard.App.Services;
public sealed class DomainReadinessController
{
    public DomainReadinessController(Workspace workspace) { }
    internal DomainReadinessController(Workspace workspace,Func<DomainReadinessRequest,CancellationToken,Task<DomainReadinessReport>> collect) { }
    public DomainReadinessDocument? Current=>null;
    public DomainReadinessDocument? Pending=>null;
    public bool HasPending=>false;
    public bool IsRunning=>false;
    public string Message=>"";
    public string SavedDirectory=>"";
    public Task CollectAsync(DomainReadinessRequest request,bool acknowledged,Func<bool> selectionCurrent)=>throw new NotImplementedException();
    public Task ImportAsync(string file,Func<bool> selectionCurrent)=>throw new NotImplementedException();
    public Task<string> ExportAsync(string output,bool recoverOriginal=false,bool acknowledged=false)=>throw new NotImplementedException();
    public void Discard(bool acknowledged)=>throw new NotImplementedException();
}
