using System.Windows.Input;
using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Exchange;

namespace BuildStandard.App.ViewModels;

public sealed class DomainReadinessViewModel:PageViewModel
{
    private readonly DomainReadinessController _controller;
    private PresentationContextKey _context;
    private long _generation;
    private string _domain="",_expectedMx="",_expectedSource="";
    private bool _mx=true,_spf=true,_enrolment,_acknowledged,_recoveryAcknowledged;
    public DomainReadinessViewModel(ShellViewModel shell):this(shell,new DomainReadinessController(shell.Workspace)) { }
    internal DomainReadinessViewModel(ShellViewModel shell,DomainReadinessController controller):base(shell,"Domain readiness")
    {
        _controller=controller;_context=Workspace.CapturePresentationContext();_domain=Workspace.Profile?.Domain??"";
        RunCommand=Command(RunAsync,()=>CanRun);StopCommand=Sync(Workspace.CancelOperation,()=>_controller.IsRunning);
        ImportCommand=Command(ChooseImportAsync,()=>Workspace.Idle&&!HasPending);
        SaveCommand=Command(async()=>{await _controller.ExportAsync(Workspace.Paths.ReportsDirectory);Refresh();},()=>Workspace.Idle&&_controller.Current is not null);
        RecoverCommand=Command(ChooseRecoveryAsync,()=>Workspace.Idle&&HasPending&&RecoveryAcknowledged);
        DiscardCommand=Sync(()=>{_controller.Discard(RecoveryAcknowledged);RecoveryAcknowledged=false;Refresh();},()=>Workspace.Idle&&HasPending&&RecoveryAcknowledged);
        CopyCommand=CopyText(()=>CurrentSummary+"\n\n"+Details);
    }
    public string Domain {get=>_domain;set{if(SetProperty(ref _domain,value??""))InputsChanged();}}
    public string ExpectedMx {get=>_expectedMx;set{if(SetProperty(ref _expectedMx,value??""))InputsChanged();}}
    public string ExpectedSource {get=>_expectedSource;set{if(SetProperty(ref _expectedSource,value??""))InputsChanged();}}
    public bool CheckMx {get=>_mx;set{if(SetProperty(ref _mx,value))InputsChanged();}}
    public bool CheckSpf {get=>_spf;set{if(SetProperty(ref _spf,value))InputsChanged();}}
    public bool CheckEnrolment {get=>_enrolment;set{if(SetProperty(ref _enrolment,value))InputsChanged();}}
    public bool Acknowledged {get=>_acknowledged;set{SetProperty(ref _acknowledged,value);RaiseAll();CommandManager.InvalidateRequerySuggested();}}
    public bool RecoveryAcknowledged {get=>_recoveryAcknowledged;set{SetProperty(ref _recoveryAcknowledged,value);CommandManager.InvalidateRequerySuggested();}}
    public bool HasPending=>_controller.HasPending;
    public string PendingSummary=>_controller.PendingSummary;
    public string CurrentSummary=>_controller.CurrentSummary;
    public string Details=>_controller.CurrentDetails;
    public IReadOnlyList<DomainReadinessResult> Results=>_controller.CurrentResults;
    public string Message=>_controller.Message;
    public string SavedDirectory=>_controller.SavedDirectory;
    public string InputProblem
    {
        get{try{_=Request();return "";}catch(ToolkitException ex){return ex.Message;}}
    }
    public bool CanRun=>Workspace.Idle&&!HasPending&&Acknowledged&&InputProblem.Length==0;
    public string Availability=>!Workspace.Idle?"Wait for the active task. Stop applies only to this page's DNS read."
        :HasPending?"Preserve or explicitly discard the original unsaved report before a new read or import."
        :InputProblem.Length>0?InputProblem
        :!Acknowledged?"Review the exact domain/checks below and acknowledge this experimental DNS read to enable Run."
        :"Ready for the selected DNS reads. Microsoft sign-in is not needed and no tenant/DNS configuration will be changed.";
    public string ReviewText=>$"Selected domain: {Domain}\nChecks: {string.Join(", ",SelectedChecks())}\nMX expectations: {(ExpectedMx.Length==0?"not configured — observed routing only":ExpectedMx)}\nExpectation source: {(ExpectedSource.Length==0?"not supplied":ExpectedSource)}\nWindows configured DNS resolver only; Windows PowerShell/DnsClient required. 20 seconds per question, 120 seconds overall, at most32questions and500observations/reference edges. Stop retains admitted partial observations. No module installation, policy bypass, alternative server or automatic retry. Public-cloud Windows enrolment reference only; macOS is unsupported. DNS does not establish tenant ownership, delivery, licences or device enrolment.";
    public string OutputGuidance=>HasPending?"NOT SAVED. Save report pack for a current result, or use original-report recovery after a context change. Save/export never repeats the DNS read."
        :_controller.Current is null?"No current result to save. Import a historical report or deliberately run the selected checks. Copy includes only the displayed current result."
        :"Save report pack writes a new folder under reports: original JSON, HTML worksheet, CSV tables and manifest. Imported records remain historical/unverified. Stop is available only during this page's DNS read.";
    public ICommand RunCommand{get;}public ICommand StopCommand{get;}public ICommand ImportCommand{get;}public ICommand SaveCommand{get;}
    public ICommand RecoverCommand{get;}public ICommand DiscardCommand{get;}public ICommand CopyCommand{get;}
    private IReadOnlyList<DomainReadinessCheck> SelectedChecks()
    {
        var checks=new List<DomainReadinessCheck>();if(CheckMx)checks.Add(DomainReadinessCheck.Mx);if(CheckSpf)checks.Add(DomainReadinessCheck.Spf);if(CheckEnrolment)checks.Add(DomainReadinessCheck.WindowsEnrolment);return checks;
    }
    private DomainReadinessRequest Request()=>DomainReadinessCollector.PrepareRequest(new(Domain.Trim(),SelectedChecks(),
        ExpectedMx.Split([',',';','\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries),string.IsNullOrWhiteSpace(ExpectedSource)?null:ExpectedSource.Trim()));
    private void InputsChanged(){_generation++;_acknowledged=false;_recoveryAcknowledged=false;RaiseAll();CommandManager.InvalidateRequerySuggested();}
    public async Task RunAsync()
    {
        var generation=_generation;var request=Request();
        try{await _controller.CollectAsync(request,Acknowledged,()=>generation==_generation);}
        finally{_acknowledged=false;Refresh();}
    }
    public async Task ImportAsync(string file)
    {
        var generation=_generation;
        try{await _controller.ImportAsync(file,()=>generation==_generation);}
        finally{Refresh();}
    }
    private async Task ChooseImportAsync()
    {
        var dialog=new Microsoft.Win32.OpenFileDialog{Title="Open a historical domain-readiness report",Filter="Domain report JSON (*.json)|*.json"};
        if(dialog.ShowDialog()==true)await ImportAsync(dialog.FileName);
    }
    private async Task ChooseRecoveryAsync()
    {
        var dialog=new Microsoft.Win32.OpenFolderDialog{Title="Preserve the original domain report — no client rebinding"};
        if(dialog.ShowDialog()!=true)return;
        try{await _controller.ExportAsync(dialog.FolderName,true,RecoveryAcknowledged);}
        finally{RecoveryAcknowledged=false;Refresh();}
    }
    public override void Refresh()
    {
        var context=Workspace.CapturePresentationContext();
        if(context!=_context){_context=context;InputsChanged();}
        RaiseAll();CommandManager.InvalidateRequerySuggested();
    }
}
