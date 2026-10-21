using System.Data;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
namespace BuildStandard.App.ViewModels;

public sealed record CoverageControlChoice(string Id,string Name,string Collection)
{public string Label=>Name+" ("+Id+")";}

public sealed class DeploymentCoverageViewModel:PageViewModel
{
    private readonly DeploymentCoverageController _controller;
    private string _context="",_catalogue="",_control="",_users="",_devices="",_source="",_resultContext="",_table="";
    private int _selection;
    private bool _acknowledged,_modern,_recovery,_reading,_importing,_stopped;
    private DeploymentCoverageEvidence? _result;
    private Dictionary<string,DataView> _tables=new(StringComparer.Ordinal);
    public DeploymentCoverageViewModel(ShellViewModel shell):base(shell,"Deployment coverage")
    {
        _controller=new(Workspace);
        ReadCommand=Command(ReadAsync,()=>ReadGuidance.Length==0);
        StopCommand=Sync(()=>{_stopped=true;Workspace.CancelOperation();},()=>_reading&&!_importing&&Workspace.Busy);
        ChooseCommand=Sync(()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Original coverage JSON ZIP (*.zip)|*.zip",Title="Open historical coverage originals"};if(dialog.ShowDialog()==true)SourceFile=dialog.FileName;},()=>CanEdit&&Workspace.Profile is not null);
        ImportCommand=Command(ImportAsync,()=>CanEdit&&Workspace.Profile is not null&&SourceFile.Length>0);
        ExportHtmlCommand=Command(()=>ExportAsync(ExportFormat.Html),CanExport);ExportJsonCommand=Command(()=>ExportAsync(ExportFormat.Json),CanExport);
        ExportCsvCommand=Command(()=>ExportAsync(ExportFormat.Csv),CanExport);ExportXlsxCommand=Command(()=>ExportAsync(ExportFormat.Xlsx),CanExport);
        SaveOriginalCommand=Command(()=>RecoverAsync("save"),()=>CanRecover()&&_controller.PendingEvidence is not null);
        ExportOriginalCommand=Command(()=>RecoverAsync("json"),()=>CanRecover()&&_controller.PendingEvidence is not null);
        ExportFilesCommand=Command(()=>RecoverAsync("files"),()=>CanRecover()&&_controller.PendingCapture is not null);
        DiscardOriginalCommand=Sync(()=>{if(MessageBox.Show("Discard only this retained original?\n\n"+PendingSummary,"Discard original coverage",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;_controller.Discard(_controller.PendingId,RecoveryAcknowledged);_recovery=false;Outcome="Original explicitly discarded; stored records were unchanged.";Notify();},CanRecover);
        Refresh();
    }
    public IReadOnlyList<CoverageControlChoice> Controls {get;private set;}=[];
    public ObservableCollection<string> Tables {get;}=[];
    public string ControlId {get=>_control;set=>Input(ref _control,value,nameof(ControlId));}
    public string PilotUsers {get=>_users;set=>Input(ref _users,value,nameof(PilotUsers));}
    public string PilotDevices {get=>_devices;set=>Input(ref _devices,value,nameof(PilotDevices));}
    public string SourceFile {get=>_source;set=>Input(ref _source,value,nameof(SourceFile));}
    public bool Acknowledged {get=>_acknowledged;set{if(SetProperty(ref _acknowledged,value))Notify();}}
    public bool ModernAcknowledged {get=>_modern;set{if(SetProperty(ref _modern,value)){_selection++;_acknowledged=false;ClearResult();Notify();}}}
    public bool RecoveryAcknowledged {get=>_recovery;set{if(SetProperty(ref _recovery,value))Notify();}}
    public string SelectedTable {get=>_table;set{if(SetProperty(ref _table,value??""))OnPropertyChanged(nameof(Rows));}}
    public DataView? Rows=>string.IsNullOrEmpty(_table)?null:_tables.GetValueOrDefault(_table);
    public bool HasPending=>_controller.HasPending;
    public bool CanEdit=>!_reading&&Workspace.Idle&&!HasPending;
    private CoverageControlChoice? Choice=>Controls.SingleOrDefault(c=>c.Id==ControlId);
    public bool CanUseModern=>CanEdit&&Choice?.Collection is ("apps" or "configuration")&&Workspace.Session?.Mode==SessionMode.Deployment;
    public string ContextSummary=>Workspace.Profile is null?"Select a client in Client and connection. Historical originals open offline for that exact tenant.":Workspace.Profile.Company+" · "+Workspace.Profile.TenantId+" · "+(Workspace.Session is null?"Offline / no connection":Workspace.Session.Account+" · "+Workspace.Session.Mode);
    public string Limitations=>"Read-only source observations, not effective targeting, installation, protection, complete deployment before-evidence or approval. Unknown is not zero or success. CA targeting/exclusions are preserved; disabled candidates stay disabled and Intune assignment is never changed. Direct group membership can be incomplete or unsupported. Experimental / live-unverified.";
    public string ModernGuidance=>"Optional app/classic-configuration results need deliberately selected deployment access and its existing write-capable report scope. This checkbox does not switch access or grant consent. One fixed export job may run; no configuration is changed and no ambiguous create is replayed. Leave off for basic assignment observations/manual Intune results. Classic compliance uses its registered read-only service source.";
    public string ReadGuidance
    {
        get
        {
            if(_reading)return "Wait for the current read, import or display preparation to finish before another action.";
            var blocker=_controller.ReadBlocker;if(blocker.Length>0)return blocker;
            if(Choice is null)return "Choose one supported standard control. Exact local creation/readback proof must establish its mapped object before any tenant read; names alone are insufficient.";
            try{var users=Pilots(PilotUsers);var devices=Pilots(PilotDevices);if(users.Length+devices.Length>5000)return "Select at most 5,000 pilot IDs across the two explicit namespaces.";}catch(ToolkitException ex){return ex.Message;}
            if(ModernAcknowledged&&!CanUseModern)return "Modern results require an app/configuration control and deliberately selected deployment access; use Client and connection. No permission upgrade happens here.";
            return Acknowledged?"":"Acknowledge the experimental source read and its limitations first. This is separate from exact approval for changes.";
        }
    }
    public string Outcome {get;private set;}="No coverage read attempted. Not checked is different from a successful empty result.";
    public string OriginalReportDetails {get;private set;}="No original accepted for this selection.";
    public string LastExport {get;private set;}="";
    public string ExportGuidance=>_result is null?"Read or open an original report first; resolve retained originals separately.":"Projections keep unknowns explicit; original JSON ZIP preserves source bytes. Excel has cell-length limits. Ordinary projection exports do not resolve an unsaved original; use recovery below.";
    public string PendingSummary
    {
        get
        {
            if(!HasPending)return "No retained original.";
            var raw=_controller.PendingCapture;var report=_controller.PendingEvidence?.Metadata;
            return "NOT SAVED · original "+_controller.PendingId+"\nOriginal tenant: "+(raw?.Selection.TenantId??report?.TenantId)+" · engineer account: "+(raw?.Selection.AccountObjectId??report?.AccountObjectId)
                +"\nOriginal control: "+(raw?.Selection.ControlName??report?.Selection.ControlName)+" · "+(raw?.Selection.ControlId??report?.Selection.ControlId)
                +"\n"+(_controller.PendingEvidence is null?"No conforming bounded report is available. Export original files for manual review; this does not increase the report cap or create complete evidence.":"A conforming original is retained. A disk record may already exist; inspect its exact ID before saving again.")
                +"\nRecovery always uses this original identity, even after a client change. No read is repeated.";
        }
    }
    public ICommand ReadCommand {get;}public ICommand StopCommand {get;}public ICommand ChooseCommand {get;}public ICommand ImportCommand {get;}
    public ICommand ExportHtmlCommand {get;}public ICommand ExportJsonCommand {get;}public ICommand ExportCsvCommand {get;}public ICommand ExportXlsxCommand {get;}
    public ICommand SaveOriginalCommand {get;}public ICommand ExportOriginalCommand {get;}public ICommand ExportFilesCommand {get;}public ICommand DiscardOriginalCommand {get;}
    private bool CanExport()=>_result is not null&&!_reading&&Workspace.Idle&&_resultContext==_controller.ContextKey;
    private bool CanRecover()=>!_reading&&Workspace.Idle&&HasPending&&RecoveryAcknowledged;
    private void Input(ref string field,string value,string name)
    {if(!SetProperty(ref field,value??"",name))return;_selection++;_acknowledged=false;if(name==nameof(ControlId))_modern=false;ClearResult();Notify();}
    private static string[] Pilots(string text)
    {
        if(text.Length>200000)throw new ConfigurationException("Pilot input is too large; use at most 5,000 exact directory IDs.");
        var values=text.Replace(',',' ').Replace(';',' ').Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(values.Length>5000)throw new ConfigurationException("Pilot input exceeds 5,000 IDs.");
        var result=new HashSet<string>(StringComparer.Ordinal);
        foreach(var value in values){if(!ReportValues.TryGuid(value,out var id)||!result.Add(id))throw new ConfigurationException("Pilot IDs must be unique non-empty directory GUIDs in their labelled user/device namespace. Do not use names, UPNs or Intune managed-device IDs.");}
        return result.ToArray();
    }
    public async Task ReadAsync()
    {
        if(ReadGuidance.Length>0)throw new ToolkitException(ReadGuidance);
        var context=_context;var generation=_selection;var id=ControlId;var users=Pilots(PilotUsers);var devices=Pilots(PilotDevices);var modern=ModernAcknowledged;
        ClearResult();_reading=true;_stopped=false;Outcome="Checking original local scope, then only the registered selected-control sources. No current result claimed yet.";Notify();
        try
        {
            var result=await _controller.ReadAsync(id,users,devices,Acknowledged,modern,context,()=>_selection==generation&&Acknowledged);
            if(_selection!=generation)throw new TenantMismatchException("The selection changed; no result is admitted for the new input.");
            if(result.Evidence is not null)await AcceptAsync(result.Evidence,context);
            Outcome=result.NotSavedReason??"Original coverage saved: "+result.File+". Source read completeness is not effective deployment or device protection.";
        }
        catch{Outcome=_stopped?"Stopped; inspect retained originals. No successful empty check or replay is claimed.":"Could not accept current coverage. Review the error and any retained original; not checked is not successful absence.";throw;}
        finally{_reading=false;_acknowledged=false;Notify();}
    }
    public async Task ImportAsync()
    {
        if(!CanEdit||Workspace.Profile is null||SourceFile.Length==0)throw new ToolkitException("Select a client and original ZIP, wait for current display preparation and resolve retained originals before importing.");
        _reading=true;_importing=true;
        var context=_context;var generation=_selection;ClearResult();_acknowledged=false;
        try{var result=await _controller.ImportAsync(SourceFile,context);if(_selection!=generation)throw new TenantMismatchException("The supplied selection changed.");await AcceptAsync(result,context);Outcome="Historical / unverified original coverage. No current read, sign-in, approval or deployment authority.";}
        catch{Outcome="Could not open original coverage. No successful empty check is inferred.";throw;}
        finally{_reading=false;_importing=false;Notify();}
    }
    private async Task AcceptAsync(DeploymentCoverageEvidence result,string context)
    {
        var generation=_selection;
        var projection=await Task.Run(()=>
        {
            var sheets=DeploymentCoverageDocuments.Sheets(result);var tables=new Dictionary<string,DataView>(StringComparer.Ordinal);
            foreach(var sheet in sheets){var table=new DataTable();foreach(var heading in sheet.Rows[0])table.Columns.Add(heading,typeof(string));foreach(var row in sheet.Rows.Skip(1))table.Rows.Add(row.Cast<object>().ToArray());tables.Add(sheet.Name,table.DefaultView);}
            return(Tables:tables,Context:string.Join('\n',sheets[0].Rows.Skip(1).Select(r=>r[0]+": "+r[1])));
        });
        if(_controller.ContextKey!=context||_selection!=generation)throw new TenantMismatchException("The client/input changed during projection; no result was admitted.");
        _result=result;_resultContext=context;_tables=projection.Tables;OriginalReportDetails=projection.Context;Tables.Clear();foreach(var key in _tables.Keys)Tables.Add(key);SelectedTable="Source read states";Notify();
    }
    public async Task ExportAsync(ExportFormat format)
    {
        if(!CanExport())throw new ToolkitException(ExportGuidance);var result=_result!;var context=_resultContext;var generation=_selection;
        var file=await _controller.ExportAsync(result,format,context);
        if(_selection!=generation||!ReferenceEquals(result,_result))throw new TenantMismatchException("An original file may exist but is not attributed to changed input.");LastExport=file;Notify();
    }
    private async Task RecoverAsync(string action)
    {var file=await _controller.RecoverAsync(_controller.PendingId,RecoveryAcknowledged,action);_recovery=false;Outcome="Exact original recovered at "+file+". Original identity preserved; no tenant query was repeated.";Notify();}
    private void ClearResult(){_result=null;_resultContext="";_tables.Clear();Tables.Clear();_table="";LastExport="";OriginalReportDetails="No original accepted for this selection.";Outcome="No current-client result for this selection; older originals remain separate.";}
    public override void Refresh()
    {
        var context=_controller.ContextKey;
        if(context!=_context){_context=context;_selection++;_acknowledged=false;_modern=false;_recovery=false;ClearResult();Outcome="Client/evidence context changed. Saved or retained originals stay separate; no result accepted for this selection.";}
        var catalogue=(Workspace.Standard?.Release??"")+"|"+(Workspace.Standard?.IntegrityDigest??"");
        if(catalogue!=_catalogue){_catalogue=catalogue;Controls=(Workspace.Standard?.Controls??[]).Where(c=>c.Collection is "conditionalAccess" or "apps" or "compliance" or "configuration").Select(c=>new CoverageControlChoice(c.Id,c.Name,c.Collection!)).ToArray();if(!Controls.Any(c=>c.Id==_control))_control="";OnPropertyChanged(nameof(Controls));}
        Notify();
    }
    private void Notify()
    {
        foreach(var name in new[]{nameof(ControlId),nameof(CanEdit),nameof(CanUseModern),nameof(ContextSummary),nameof(ReadGuidance),nameof(Acknowledged),nameof(ModernAcknowledged),nameof(RecoveryAcknowledged),nameof(HasPending),nameof(PendingSummary),nameof(Outcome),nameof(Rows),nameof(OriginalReportDetails),nameof(LastExport),nameof(ExportGuidance)})OnPropertyChanged(name);
        CommandManager.InvalidateRequerySuggested();
    }
}
