using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text;
using System.Windows.Input;
using BuildStandard.App.Infrastructure;
using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using BuildStandard.Engine.Evidence;
namespace BuildStandard.App.ViewModels;

/// <summary>Native usage view over the existing report controller/session. No sign-in, grant or licence operation.</summary>
public sealed class UsageReportsViewModel : PageViewModel
{
    private readonly GraphReportsController _controller;
    private string _context="",_period="D30",_section="",_search="",_message="No usage evidence is open.",_export="";
    private bool _acknowledged,_reading,_supplied;
    private int _selectionVersion;
    private UsageReviewReport? _report;
    private byte[] _original=[];
    private byte[] _pendingOriginal=[];
    private string _pendingTenant="",_pendingId="";
    private IReadOnlyList<Sheet> _sheets=[];
    private DataView? _rows;
    public UsageReportsViewModel(ShellViewModel shell,GraphReportsController controller):base(shell,"Usage review")
    {
        _controller=controller;
        Sources=new(UsageReportRegistry.Definitions.Select(definition=>new UsageSourceChoice(definition.Id,definition.Name,SelectionChanged)));
        ReadCommand=Command(ReadAsync,()=>ReadGuidance.Length==0);
        StopCommand=Sync(()=>{if(!_reading||Workspace.Idle)throw new ToolkitException("No usage read is active.");Workspace.CancelOperation();},()=>_reading&&Workspace.Busy);
        OpenCommand=Command(ChooseAndOpenAsync,()=>Workspace.Idle&&Workspace.Profile is not null&&!HasPendingOriginal);
        HtmlCommand=Command(()=>ExportAsync(ExportFormat.Html),CanExport);
        JsonCommand=Command(()=>ExportAsync(ExportFormat.Json),CanExport);
        CsvCommand=Command(()=>ExportAsync(ExportFormat.Csv),CanExport);
        ExcelCommand=Command(()=>ExportAsync(ExportFormat.Xlsx),CanExport);
        SaveOriginalCommand=Command(SavePendingOriginalAsync,()=>Workspace.Idle&&HasPendingOriginal&&_pendingOriginal.Length<=EvidenceStore.MaximumManagedUsageBytes);
        ExportOriginalCommand=Command(ExportPendingOriginalAsync,()=>Workspace.Idle&&HasPendingOriginal);
        DiscardOriginalCommand=Sync(DiscardPending,()=>Workspace.Idle&&HasPendingOriginal);
        OpenConnectionCommand=Sync(()=>Shell.Navigate("connect"),()=>Workspace.Idle);
        Refresh();
    }
    public ObservableCollection<UsageSourceChoice> Sources {get;}
    public IReadOnlyList<string> Periods {get;}=["D7","D30","D90","D180"];
    public string Period {get=>_period;set{if(!UsageReportRegistry.IsPeriod(value))throw new ConfigurationException("Choose a registered usage period.");if(SetProperty(ref _period,value))SelectionChanged();}}
    public bool Acknowledged {get=>_acknowledged;set{EnsureContext();if(SetProperty(ref _acknowledged,value))Notify();}}
    public bool CanEdit=>Workspace.Idle&&!HasPendingOriginal;
    public string PrivacyNotice=>UsageReviewDocuments.PrivacyNotice;
    public string AccessPreview=>"Experimental read-only Graph usage reports · Reports.Read.All must already be returned for this verified account. Separate downloads receive no Graph token. Delegated reporting role/workload licences may still refuse reads. Five minutes total;10MiB per CSV/20,000 rows;40MiB complete pack. No report-settings access, licence changes or identity joins.";
    public string ReadGuidance
    {
        get
        {
            EnsureContext();
            if(!Workspace.Idle)return _reading?"Usage collection is active. Stop requests cancellation; earlier complete source reads can remain in its final evidence.":"Wait for the current operation to finish.";
            if(HasPendingOriginal)return "Resolve the original usage report below before another read. Export retains its original client; nothing is rerun.";
            var blocker=_controller.UsageReadBlocker;if(blocker.Length!=0)return blocker;
            if(!Sources.Any(source=>source.Selected))return "Select at least one usage source and its period. Selection makes no request.";
            return Acknowledged?"":"Review the selected sources, period, access and privacy limits, then tick acknowledgement before Read.";
        }
    }
    public string Outcome=>_report is null?"No current usage result.":$"Read status: {_report.Status} · {_report.StartedAt} to {_report.EndedAt}\n"+string.Join(" · ",_report.Sources.Select(source=>UsageReportRegistry.Find(source.SourceId).Name+": "+source.Status));
    public string Message=>_message;
    public string LastExport=>_export;
    public IReadOnlyList<string> Sections=>_sheets.Select(sheet=>sheet.Name).ToArray();
    public string SelectedSection {get=>_section;set{if(SetProperty(ref _section,value))BuildRows();}}
    public string Search {get=>_search;set{if(SetProperty(ref _search,value))BuildRows();}}
    public DataView? Rows=>_rows;
    public bool HasPendingOriginal=>_pendingOriginal.Length>0;
    public string PendingSummary=>HasPendingOriginal?$"Original usage report {_pendingId} · tenant {_pendingTenant}. These exact bytes remain in memory until original JSON is saved, exported or deliberately discarded. Changing client never rebinds them. A forced termination can lose this pending original.":"No pending original.";
    public ICommand ReadCommand {get;} public ICommand StopCommand {get;} public ICommand OpenCommand {get;}
    public ICommand HtmlCommand {get;} public ICommand JsonCommand {get;} public ICommand CsvCommand {get;} public ICommand ExcelCommand {get;}
    public ICommand SaveOriginalCommand {get;} public ICommand ExportOriginalCommand {get;} public ICommand DiscardOriginalCommand {get;} public ICommand OpenConnectionCommand {get;}
    internal Func<string,bool> ConfirmDiscard {get;set;}=summary=>System.Windows.MessageBox.Show(summary+"\n\nDiscard this original without another read?","Discard original usage report",System.Windows.MessageBoxButton.YesNo,System.Windows.MessageBoxImage.Warning)==System.Windows.MessageBoxResult.Yes;
    private void SelectionChanged(){_selectionVersion++;_acknowledged=false;ClearResult();Notify();}
    private void ClearResult(){_report=null;_original=[];_sheets=[];_section="";_rows=null;_export="";}
    private void EnsureContext()
    {
        if(_controller is null)return;
        var context=_controller.ContextKey;
        if(_context==context)return;
        _context=context;_selectionVersion++;_acknowledged=false;ClearResult();
        _message="Client or evidence context changed. No former result or read acknowledgement applies here; a pending original retains its own client.";
    }
    private bool CanExport()=>Workspace.Idle&&_report is not null&&_context==_controller.ContextKey;
    public async Task ReadAsync()
    {
        var reason=ReadGuidance;if(reason.Length!=0)throw new ToolkitException(reason);
        var context=_context;var version=_selectionVersion;var ids=Sources.Where(source=>source.Selected).Select(source=>source.Id).ToArray();var period=Period;
        _acknowledged=false;_reading=true;ClearResult();Notify();
        try
        {
            var report=await _controller.ReadUsageAsync(ids,period,true,context,()=>version==_selectionVersion);
            if(_context!=context||version!=_selectionVersion)throw new TenantMismatchException("Usage selection changed. No result was displayed for the new selection.");
            var original=await Task.Run(()=>new UTF8Encoding(false,true).GetBytes(UsageReviewSchema.Serialise(report)));
            _controller.RequireContext(context);
            if(version!=_selectionVersion)throw new TenantMismatchException("Usage selection changed before original evidence admission.");
            _pendingOriginal=original.ToArray();_pendingTenant=report.TenantId;_pendingId=report.Id;
            await SetReportAsync(report,original,false,context,version);
            _message="Read completed with the source states above. Save or export the original JSON before closing or reading again; HTML/CSV/Excel alone do not preserve the original CSV pack.";
        }
        finally{_reading=false;Notify();}
    }
    private async Task SetReportAsync(UsageReviewReport report,byte[] original,bool supplied,string context,int version)
    {
        var sheets=await Task.Run(()=>UsageReviewDocuments.Sheets(report,supplied));
        _controller.RequireContext(context);
        if(version!=_selectionVersion)throw new TenantMismatchException("Usage selection changed while projecting the result.");
        _report=report;_original=original.ToArray();_supplied=supplied;_sheets=sheets;_section=sheets.First().Name;_search="";BuildRows();Notify();
    }
    public async Task OpenSuppliedAsync(string file)
    {
        EnsureContext();if(!CanEdit||Workspace.Profile is null)throw new ToolkitException("Select a client, wait for the operation and resolve any pending original before opening usage JSON.");
        var context=_context;var version=_selectionVersion;var tenant=Workspace.Profile.TenantId;ClearResult();
        var loaded=await Task.Run(()=>UsageReviewImport.ReadFile(file,tenant));
        await SetReportAsync(loaded.Report,loaded.CopyOriginal(),true,context,version);
        _message="Supplied historical source claims; integrity is not source authentication. No service was contacted or evidence inserted into the managed store.";Notify();
    }
    private async Task ChooseAndOpenAsync()
    {
        var picker=new Microsoft.Win32.OpenFileDialog{Filter="Usage review evidence (*.json)|*.json",Title="Open saved or supplied usage pack (40MiB maximum)"};
        if(picker.ShowDialog()==true)await OpenSuppliedAsync(picker.FileName);
    }
    public async Task ExportAsync(ExportFormat format)
    {
        if(!CanExport())throw new ToolkitException("Open or read usage evidence for the current client first, then wait for the current operation.");
        var report=_report!;var context=_context;var version=_selectionVersion;var original=_original.ToArray();
        var file=await _controller.ExportUsageAsync(report,format,_supplied,original,context);
        if(version!=_selectionVersion)throw new TenantMismatchException("The usage selection changed during export. The file retains its original source; no new result was admitted.");
        if(format==ExportFormat.Json)ResolvePendingAfterExport(file,original);_export=file;
        _message="Complete usage report exported; visible filters did not limit it. Privacy stays Unknown and no licence recommendation is inferred.";Notify();
    }
    public async Task SavePendingOriginalAsync()
    {
        if(!Workspace.Idle||!HasPendingOriginal)throw new ToolkitException("Wait for the active operation; no original usage report is pending.");
        var original=_pendingOriginal.ToArray();var tenant=_pendingTenant;var id=_pendingId;
        var file=await Workspace.ExportAsync(()=>Workspace.Evidence.SaveUsageReview(original,tenant,id),"Saving original usage evidence","Saving original client bytes without rerun;32MiB managed limit preserves backup compatibility.");
        ResolvePendingAfterExport(file,original);_export=file;_message="Original usage pack saved and readback verified in its own client's managed evidence. Ordinary workspace backup includes it; it is never deployment before-evidence.";Notify();
    }
    public async Task ExportPendingOriginalAsync()
    {
        if(!Workspace.Idle||!HasPendingOriginal)throw new ToolkitException("Wait for the current operation; no pending original is available.");
        var original=_pendingOriginal.ToArray();var report=UsageReviewSchema.Read(new UTF8Encoding(false,true).GetString(original),_pendingTenant);
        var file=await Workspace.ExportAsync(()=>Workspace.Exporter.ExportUsageReview(report,ExportFormat.Json,originalBytes:original),"Retaining original usage JSON","Exporting the original client record, without rerun or managed-store admission.");
        ResolvePendingAfterExport(file,original);_export=file;_message="Original usage JSON exported and verified. It remains separate from managed evidence/backup.";Notify();
    }
    private void ResolvePendingAfterExport(string file,byte[] original)
    {
        UsageReviewImport.VerifyOriginalExport(file,original);
        if(_pendingOriginal.AsSpan().SequenceEqual(original)){_pendingOriginal=[];_pendingTenant="";_pendingId="";}
    }
    private void DiscardPending(){if(!Workspace.Idle||!HasPendingOriginal)return;if(ConfirmDiscard(PendingSummary)){if(_original.AsSpan().SequenceEqual(_pendingOriginal))ClearResult();_pendingOriginal=[];_pendingTenant="";_pendingId="";_message="Original usage report explicitly discarded; no source was rerun.";Notify();}}
    private void BuildRows()
    {
        _rows=null;var sheet=_sheets.SingleOrDefault(value=>value.Name==_section);
        if(sheet is not null)
        {
            var table=new DataTable();foreach(var heading in sheet.Rows[0])table.Columns.Add(heading,typeof(string));table.BeginLoadData();
            foreach(var row in sheet.Rows.Skip(1)){if(_search.Length!=0&&!row.Any(value=>value.Contains(_search,StringComparison.OrdinalIgnoreCase)))continue;table.Rows.Add(row.Cast<object>().ToArray());}
            table.EndLoadData();_rows=table.DefaultView;
        }
        Notify();
    }
    public override void Refresh(){if(_controller is null)return;EnsureContext();Notify();}
    private void Notify()
    {
        foreach(var name in new[]{nameof(CanEdit),nameof(ReadGuidance),nameof(Acknowledged),nameof(Outcome),nameof(Message),nameof(LastExport),nameof(Sections),nameof(SelectedSection),nameof(Search),nameof(Rows),nameof(HasPendingOriginal),nameof(PendingSummary)})OnPropertyChanged(name);
        CommandManager.InvalidateRequerySuggested();
    }
}
public sealed class UsageSourceChoice(string id,string name,Action changed):ObservableObject
{
    private bool _selected;public string Id {get;}=id;public string Name {get;}=name;
    public bool Selected {get=>_selected;set{if(SetProperty(ref _selected,value))changed();}}
}
