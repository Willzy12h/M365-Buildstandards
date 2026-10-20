using System.Data;
using System.Text;
using System.Windows;
using System.Windows.Input;
using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;

namespace BuildStandard.App.ViewModels;

public sealed class ConditionalAccessEvaluationViewModel:PageViewModel
{
    private readonly ConditionalAccessEvaluationController _controller;
    private string _context="",_resultContext="",_user="",_application="",_platform="Windows",_client="Browser",_ip="",_country="",_signInRisk="Omitted",_userRisk="Omitted",_compliant="Omitted",_source="";
    private bool _acknowledged,_recoveryAcknowledged,_reading,_stopRequested;
    private int _selection;
    private ConditionalAccessEvaluationCapture? _result;
    public ConditionalAccessEvaluationViewModel(ShellViewModel shell):base(shell,"Hypothetical CA evaluation")
    {
        _controller=new(Workspace);
        ReadCommand=Command(ReadAsync,()=>ReadGuidance.Length==0);
        StopCommand=Sync(()=>{_stopRequested=true;Workspace.CancelOperation();},()=>_reading&&Workspace.Busy);
        ChooseCommand=Sync(()=>
        {
            var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Original evaluation JSON ZIP (*.zip)|*.zip",Title="Open supplied historical CA evaluation"};
            if(dialog.ShowDialog()==true)SourceFile=dialog.FileName;
        },()=>Workspace.Idle&&Workspace.Profile is not null&&_controller.Pending is null);
        ImportCommand=Command(ImportAsync,()=>Workspace.Idle&&Workspace.Profile is not null&&_controller.Pending is null&&SourceFile.Length>0);
        ExportHtmlCommand=Command(()=>ExportAsync(ExportFormat.Html),CanExport);
        ExportJsonCommand=Command(()=>ExportAsync(ExportFormat.Json),CanExport);
        ExportCsvCommand=Command(()=>ExportAsync(ExportFormat.Csv),CanExport);
        ExportXlsxCommand=Command(()=>ExportAsync(ExportFormat.Xlsx),CanExport);
        SaveOriginalCommand=Command(()=>RecoverAsync(false),CanRecover);
        ExportOriginalCommand=Command(()=>RecoverAsync(true),CanRecover);
        DiscardOriginalCommand=Sync(()=>
        {
            var id=_controller.Pending?.Metadata.Id??"";
            if(MessageBox.Show("Discard only this unsaved original?\n\n"+PendingSummary,"Discard original report",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
            _controller.Discard(id,RecoveryAcknowledged);_recoveryAcknowledged=false;Outcome="Original explicitly discarded; existing disk records were unchanged.";Notify();
        },CanRecover);
        Refresh();
    }
    public IReadOnlyList<string> Platforms {get;}=["Windows","iOS","Android"];
    public IReadOnlyList<string> Clients {get;}=["Browser","Mobile apps and desktop clients","Exchange ActiveSync","EAS supported","Other"];
    public IReadOnlyList<string> Risks {get;}=["Omitted","none","low","medium","high"];
    public IReadOnlyList<string> ComplianceChoices {get;}=["Omitted","True","False"];
    public string UserId{get=>_user;set=>Input(ref _user,value,nameof(UserId));}
    public string ApplicationId{get=>_application;set=>Input(ref _application,value,nameof(ApplicationId));}
    public string Platform{get=>_platform;set=>Input(ref _platform,value,nameof(Platform));}
    public string Client{get=>_client;set=>Input(ref _client,value,nameof(Client));}
    public string IpAddress{get=>_ip;set=>Input(ref _ip,value,nameof(IpAddress));}
    public string Country{get=>_country;set=>Input(ref _country,value,nameof(Country));}
    public string SignInRisk{get=>_signInRisk;set=>Input(ref _signInRisk,value,nameof(SignInRisk));}
    public string UserRisk{get=>_userRisk;set=>Input(ref _userRisk,value,nameof(UserRisk));}
    public string DeviceCompliant{get=>_compliant;set=>Input(ref _compliant,value,nameof(DeviceCompliant));}
    public string SourceFile{get=>_source;set=>Input(ref _source,value,nameof(SourceFile));}
    public bool Acknowledged{get=>_acknowledged;set{if(SetProperty(ref _acknowledged,value))Notify();}}
    public bool RecoveryAcknowledged{get=>_recoveryAcknowledged;set{if(SetProperty(ref _recoveryAcknowledged,value))Notify();}}
    public bool HasPending=>_controller.Pending is not null;
    public bool CanEdit=>Workspace.Idle&&_controller.Pending is null;
    public string Limitations=>ConditionalAccessEvaluationResponseCodec.Limitations+" Microsoft may default omitted conditions. Risk-feature entitlement is not inferred from Business Premium. Experimental / live-unverified; no activation or tenant change.";
    public string ContextSummary=>Workspace.Profile is null?"Select a client in Client and connection. Supplied evidence can be opened offline for that exact tenant.":"Selected client: "+Workspace.Profile.Company+" · "+Workspace.Profile.TenantId+" · "+(Workspace.Session is null?"Offline / no connection":Workspace.Session.Account+" · "+Workspace.Session.Mode);
    public string ReadGuidance
    {
        get
        {
            var blocker=_controller.ReadBlocker;if(blocker.Length>0)return blocker;
            try{_=Prepared();}catch(ToolkitException ex){return ex.Message;}
            return Acknowledged?"":"Review the exact scenario and acknowledge the experimental read first. No new sign-in or consent is requested.";
        }
    }
    public string RequestPreview
    {
        get{try{var p=Prepared();return Encoding.UTF8.GetString(p.Bytes)+"\n\nOmitted conditions: "+string.Join(", ",p.OmittedConditions)+"\nRequest SHA-256: "+p.Sha256;}catch(ToolkitException ex){return ex.Message;}}
    }
    public string Outcome{get;private set;}="No evaluation attempted. An unavailable result is not an empty successful check.";
    public string LastExport{get;private set;}="";
    public string OriginalReportDetails{get;private set;}="No original report accepted for this selection.";
    public DataView? Rows{get;private set;}
    public string PendingSummary
    {
        get{var r=_controller.Pending?.Metadata;return r is null?"No retained unsaved original.":"NOT SAVED · original report "+r.Id+"\nOriginal tenant: "+r.TenantId+" · account: "+r.AccountObjectId+"\nRecorded "+r.StartedAt+" to "+r.CompletedAt+" · "+r.Status+"\nRecovery retains this original identity even if the selected client has changed. No read is repeated.";}
    }
    public string ExportGuidance=>_result is null?"Read or open an evaluation before exporting. Resolve any retained original separately.":"Exports preserve every original row and unknown value. JSON is the original metadata/response ZIP; imports remain historical and unverified.";
    public ICommand ReadCommand{get;}public ICommand StopCommand{get;}public ICommand ChooseCommand{get;}public ICommand ImportCommand{get;}
    public ICommand ExportHtmlCommand{get;}public ICommand ExportJsonCommand{get;}public ICommand ExportCsvCommand{get;}public ICommand ExportXlsxCommand{get;}
    public ICommand SaveOriginalCommand{get;}public ICommand ExportOriginalCommand{get;}public ICommand DiscardOriginalCommand{get;}
    private void Input(ref string field,string value,string name)
    {
        if(!SetProperty(ref field,value,name))return;
        _selection++;_acknowledged=false;ClearResult();Notify();
    }
    private PreparedConditionalAccessScenario Prepared()=>ConditionalAccessScenarioCodec.Prepare(new(UserId.Trim(),ApplicationId.Trim(),Platform switch{"Windows"=>"windows","iOS"=>"iOS","Android"=>"android",_=>"unsupported"},
        Client switch{"Browser"=>"browser","Mobile apps and desktop clients"=>"mobileAppsAndDesktopClients","Exchange ActiveSync"=>"exchangeActiveSync","EAS supported"=>"easSupported","Other"=>"other",_=>"unsupported"},
        Empty(IpAddress),Empty(Country),SignInRisk=="Omitted"?null:SignInRisk,UserRisk=="Omitted"?null:UserRisk,DeviceCompliant switch{"Omitted"=>null,"True"=>true,"False"=>false,_=>throw new ConfigurationException("Choose omitted, true or false compliance explicitly.")}));
    private static string? Empty(string s)=>s.Length==0?null:s;
    private bool CanExport()=>_result is not null&&Workspace.Idle&&_resultContext==_controller.ContextKey;
    private bool CanRecover()=>Workspace.Idle&&_controller.Pending is not null&&RecoveryAcknowledged;
    public async Task ReadAsync()
    {
        if(ReadGuidance.Length>0)throw new ToolkitException(ReadGuidance);
        var context=_context;var selection=_selection;var scenario=Prepared().Scenario;ClearResult();_stopRequested=false;_reading=true;Outcome="Reading the explicit scenario; no current result is claimed yet.";Notify();
        try
        {
            var outcome=await _controller.ReadAsync(scenario,Acknowledged,context,()=>_selection==selection&&Acknowledged);
            if(_selection!=selection)throw new TenantMismatchException("The scenario changed; no result was admitted for this selection.");
            await AcceptAsync(outcome.Capture,context);Outcome=outcome.NotSavedReason??"Original evaluation saved: "+outcome.File+". "+outcome.Capture.Metadata.Status+"; applicability is not enforcement.";
        }
        catch
        {
            Outcome=_stopRequested?"Stopped before a current result was accepted. Review any retained original separately; no successful evaluation is claimed.":"Could not accept a current evaluation. Review the error and any retained original; unavailable results are not an empty successful check.";
            throw;
        }
        finally{_reading=false;_acknowledged=false;Notify();}
    }
    public async Task ImportAsync()
    {
        var context=_context;var selection=_selection;ClearResult();_acknowledged=false;
        try
        {
            var report=await _controller.ImportAsync(SourceFile,context);
            if(_selection!=selection)throw new TenantMismatchException("The supplied selection changed; no report was admitted.");
            await AcceptAsync(report,context);Outcome="Historical / unverified supplied evidence · "+report.Metadata.Status+". No current check, sign-in or deployment authority.";
        }
        catch{Outcome="Could not open supplied evidence. No report was accepted; this is not an empty successful check.";throw;}
        finally{Notify();}
    }
    private async Task AcceptAsync(ConditionalAccessEvaluationCapture capture,string context)
    {
        var selection=_selection;
        var table=await Task.Run(()=>
        {
            var sheet=ConditionalAccessEvaluationDocuments.Sheets(capture).Last();var t=new DataTable();foreach(var header in sheet.Rows[0])t.Columns.Add(header,typeof(string));foreach(var row in sheet.Rows.Skip(1))t.Rows.Add(row.Cast<object>().ToArray());
            return (Rows:t.DefaultView,Original:ConditionalAccessEvaluationDocuments.OriginalContext(capture));
        });
        if(_controller.ContextKey!=context||_selection!=selection)throw new TenantMismatchException("The presentation changed during projection; no result was admitted.");
        _result=capture;_resultContext=context;Rows=table.Rows;OriginalReportDetails=table.Original;Notify();
    }
    public async Task ExportAsync(ExportFormat format)
    {
        if(!CanExport())throw new ToolkitException("Open a report for the current client before exporting.");
        var report=_result!;var context=_resultContext;var selection=_selection;
        var file=await _controller.ExportAsync(report,format,context);
        if(_selection!=selection||!ReferenceEquals(report,_result))throw new TenantMismatchException("An original file may exist, but no export was attributed to the changed selection.");
        LastExport=file;Notify();
    }
    private async Task RecoverAsync(bool export)
    {
        var id=_controller.Pending?.Metadata.Id??"";var file=await _controller.RecoverAsync(id,RecoveryAcknowledged,export);
        _recoveryAcknowledged=false;Outcome="Retained original verified at "+file+". Its tenant/account identity was preserved; no new read.";Notify();
    }
    private void ClearResult(){_result=null;_resultContext="";Rows=null;LastExport="";OriginalReportDetails="No original report accepted for this selection.";Outcome="No current-client result. Older saved or retained originals remain separate.";}
    public override void Refresh()
    {
        var context=_controller.ContextKey;if(context!=_context){_context=context;_selection++;_acknowledged=false;_recoveryAcknowledged=false;ClearResult();}Notify();
    }
    private void Notify()
    {
        foreach(var name in new[]{nameof(ContextSummary),nameof(ReadGuidance),nameof(RequestPreview),nameof(Acknowledged),nameof(RecoveryAcknowledged),nameof(CanEdit),nameof(HasPending),nameof(Outcome),nameof(LastExport),nameof(OriginalReportDetails),nameof(Rows),nameof(PendingSummary),nameof(ExportGuidance)})OnPropertyChanged(name);
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }
}
