using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Windows.Input;
using BuildStandard.App.Services;
using BuildStandard.App.Infrastructure;
using BuildStandard.App.Views;
using BuildStandard.Core;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Execution;
using BuildStandard.Engine.Reports;
using BuildStandard.Graph;
namespace BuildStandard.App.ViewModels;

/// <summary>Native coordination only. Readiness, approval, evidence, dispatch and reconciliation remain in shared services.</summary>
public sealed class LifecycleViewModel : PageViewModel
{
    public sealed record Choice(string Id,string Label);
    public sealed class ServicePlanChoice(string id,string label,Action changed) : ObservableObject
    {
        private bool _disabled;
        public string Id { get; }=id;
        public string Label { get; }=label;
        public bool Disabled {get=>_disabled;set{if(SetProperty(ref _disabled,value))changed();}}
    }
    public static IReadOnlyList<Choice> Actions {get;}=LifecycleActionRegistry.ActionIds.Select(id=>new Choice(id,LifecycleActionRegistry.Label(id))).ToArray();
    private string _user="",_group="",_sku="",_action="disable-account",_disabled="",_journey="Mover",_family="run";
    private bool _protected,_downstream,_preservation;
    private PresentationContextKey? _context,_previewContext;
    private LifecyclePreview? _preview;
    private long _inputGeneration,_previewGeneration;
    private DateTimeOffset _previewExpires;
    private LifecycleAttemptSummary? _attempt;
    private string _lastVerification="";
    private IReadOnlyList<Choice> _users=[],_groups=[],_skus=[];
    private Choice? _userChoice,_groupChoice,_skuChoice;
    public Choice? SelectedUserChoice {get=>_userChoice;set{if(SetProperty(ref _userChoice,value)&&value is not null)SelectedUserId=value.Id;}}
    public Choice? SelectedGroupChoice {get=>_groupChoice;set{if(SetProperty(ref _groupChoice,value)&&value is not null)SelectedGroupId=value.Id;}}
    public Choice? SelectedSkuChoice {get=>_skuChoice;set{if(SetProperty(ref _skuChoice,value)&&value is not null)SelectedSkuId=value.Id;}}
    public LifecycleViewModel(ShellViewModel shell):base(shell,"Existing-user actions")
    {
        SourceFactory=(operation,current)=>Workspace.RequireConnection().Graph is GraphClient graph
            ?graph.ForLifecycleReads(Workspace.Session!,operation,current):throw new ConfigurationException("Lifecycle reads require the guarded connection and its fixed read registry.");
        Confirm=viewModel=>new LifecycleConfirmationDialog(viewModel){Owner=System.Windows.Application.Current.MainWindow}.ShowDialog()==true;
        ReadinessCommand=Command(CheckReadinessAsync,()=>CanCheckReadiness);
        PreviewCommand=Command(PreviewAsync,()=>CanPreview);
        ApplyCommand=Command(ApplyAsync,()=>CanReviewAction);
        HistoryCommand=Command(LoadHistoryAsync,()=>Workspace.Idle&&Workspace.Profile is not null);
        ReconcileCommand=Command(ReconcileAsync,()=>Workspace.Idle&&Workspace.IsConnected&&SelectedAttempt is not null);
        JsonCommand=Command(()=>ExportHistoryAsync(ExportFormat.Json),()=>Workspace.Idle&&SelectedAttempt is not null);
        HtmlCommand=Command(()=>ExportHistoryAsync(ExportFormat.Html),()=>Workspace.Idle&&SelectedAttempt is not null);
        ReceiptCommand=Command(ExportReceiptAsync,()=>Workspace.Idle&&_lastVerification.Length>0);
        CaptureCommand=Command(Workspace.CaptureAsync,()=>Workspace.Idle&&Workspace.IsConnected);
        AccessCommand=Sync(()=>{Shell.Page<ConnectViewModel>().SelectLifecycleAccess(SelectedActionId);Shell.Navigate("connect");},()=>Workspace.Idle);
        Refresh();
    }
    internal Func<LifecycleOperation,Action,ILifecycleReadSource> SourceFactory {get;set;}
    internal Func<LifecycleViewModel,bool> Confirm {get;set;}
    internal IClock Clock {get;set;}=SystemClock.Instance;
    public IReadOnlyList<Choice> ActionChoices=>Actions;
    public IReadOnlyList<string> Journeys {get;}=new[]{"Joiner","Mover","Leaver"};
    public IReadOnlyList<Choice> RecordFamilies {get;}=new[]{new Choice("plan","Reviewed preview"),new Choice("intent","Original attempt intent"),new Choice("run","Recorded outcome")};
    public IReadOnlyList<Choice> Users=>_users;
    public IReadOnlyList<Choice> Groups=>_groups;
    public IReadOnlyList<Choice> Skus=>_skus;
    public ObservableCollection<ServicePlanChoice> ServicePlans {get;}=new();
    [AllowNull]public string SelectedUserId {get=>_user;set{if(SetProperty(ref _user,value??"")){_userChoice=_users.FirstOrDefault(choice=>string.Equals(choice.Id,_user,StringComparison.OrdinalIgnoreCase));InputsChanged();}}}
    [AllowNull]public string SelectedGroupId {get=>_group;set{if(SetProperty(ref _group,value??"")){_groupChoice=_groups.FirstOrDefault(choice=>string.Equals(choice.Id,_group,StringComparison.OrdinalIgnoreCase));InputsChanged();}}}
    [AllowNull]public string SelectedSkuId {get=>_sku;set{if(SetProperty(ref _sku,value??"")){_skuChoice=_skus.FirstOrDefault(choice=>string.Equals(choice.Id,_sku,StringComparison.OrdinalIgnoreCase));BuildServicePlans();InputsChanged();}}}
    [AllowNull]public string SelectedActionId {get=>_action;set{if(SetProperty(ref _action,value??""))InputsChanged();}}
    [AllowNull]public string DisabledPlansText {get=>_disabled;set{if(SetProperty(ref _disabled,value??""))InputsChanged();}}
    [AllowNull]public string SelectedJourney {get=>_journey;set{if(SetProperty(ref _journey,value??""))InputsChanged();}}
    [AllowNull]public string SelectedRecordFamily {get=>_family;set=>SetProperty(ref _family,value??"run");}
    public bool ProtectedReviewed {get=>_protected;set{SetProperty(ref _protected,value);Notify();}}
    public bool DownstreamReviewed {get=>_downstream;set{SetProperty(ref _downstream,value);Notify();}}
    public bool PreservationReviewed {get=>_preservation;set{SetProperty(ref _preservation,value);Notify();}}
    public bool NeedsGroup=>SelectedActionId is "add-member" or "remove-member";
    public bool NeedsSku=>SelectedActionId is "assign-license" or "remove-license";
    public bool CanDisablePlans=>SelectedActionId=="assign-license";
    public bool PreservationRequired=>_preview?.PreservationRequired==true;
    public bool HasPreview=>_preview is not null&&_previewContext==_context&&_previewGeneration==Interlocked.Read(ref _inputGeneration)&&Clock.UtcNow<_previewExpires;
    public bool CanCheckReadiness=>Workspace.Idle&&Workspace.IsConnected&&TryOperation(out var operation)&&MissingReadAccess(operation!).Count==0;
    public bool CanPreview=>CanCheckReadiness&&Workspace.IsDeploymentSession&&Workspace.Snapshot?.Complete==true&&HasScope(Operation().WriteScope);
    public bool CanReviewAction=>Workspace.Idle&&Workspace.IsDeploymentSession&&Workspace.ExperimentalChangesEnabled&&HasPreview;
    public bool CanApply=>CanReviewAction&&ProtectedReviewed&&DownstreamReviewed&&(!PreservationRequired||PreservationReviewed);
    public string ReadGuidance
    {
        get
        {
            if(!Workspace.Idle)return "Wait for the owned operation. Stop never retries a request that may have been sent.";
            if(!Workspace.IsConnected)return "Select and connect to the intended client. Navigation performs no tenant reads.";
            if(!TryOperation(out var operation))return "Choose one registered action and exact user/group/SKU IDs. Captured names are selection aids; fresh reads establish authority.";
            var missing=MissingReadAccess(operation!);return missing.Count>0?"Required read access is missing: "+string.Join(", ",missing)+". Review selected access on Client and connection; no consent is requested here.":"Check target readiness with registered read-only requests. Missing, partial or unavailable privilege/licence evidence blocks readiness; it is never an empty-safe result.";
        }
    }
    public string PreviewGuidance=>!CanCheckReadiness?ReadGuidance:!Workspace.IsDeploymentSession?"Deliberately enable deployment access for this selected action, then prepare a fresh preview. Read-only readiness grants no write authority."
        :Workspace.Snapshot?.Complete!=true?"Capture and save a complete tenant configuration first. Scoped checks and reports cannot supply deployment before-evidence."
        :!HasScope(Operation().WriteScope)?"Selected write access is missing: "+Operation().WriteScope+". Review the deployment registration and deliberately reconnect; no permission is granted here."
        :"Prepare one exact preview using a fresh target capture plus the saved complete configuration snapshot. Original expiry, protected targets, privilege and licence checks still apply.";
    public string ApplyGuidance=>!Workspace.Idle?"Wait for the active action and its durable outcome. Do not repeat a possibly sent request.":!HasPreview?"Prepare a fresh exact preview; changed inputs/context or original expiry invalidate it."
        :!Workspace.ExperimentalChangesEnabled?ExperimentalOperationGuard.ClosedReason:!ProtectedReviewed||!DownstreamReviewed||PreservationRequired&&!PreservationReviewed?"In the final native review, confirm protected-account and downstream ownership checks and any required preservation/retention/business-owner review.":"Approve only this exact action. Complete fresh preflight and final admission still run before the single send. This is not an atomic Microsoft transaction.";
    public string Outcome {get;private set;}="No lifecycle action has been prepared or run.";
    public string ReadinessGuidance {get;private set;}="Target readiness has not been checked.";
    public IReadOnlyList<LifecycleReadinessRow> ReadinessRows {get;private set;}=[];
    public string PreviewSummary {get;private set;}="No current preview. Historical records cannot be resumed or approved.";
    public string ExactRequest {get;private set;}="";
    public string BeforeDetails {get;private set;}="";
    public string PreviewIdentity {get;private set;}="";
    public string LastExport {get;private set;}="";
    public string HistoryStatus {get;private set;}="Local attempt history has not been loaded.";
    public IReadOnlyList<LifecycleAttemptSummary> Attempts {get;private set;}=[];
    public LifecycleAttemptSummary? SelectedAttempt {get=>_attempt;set{if(SetProperty(ref _attempt,value)){ClearPreview();SelectedRecordFamily=value?.Status=="No final outcome"?"intent":"run";if(value is not null)Outcome=$"Selected historical action {value.Id}: original acceptance {value.WriteAcceptance}, original readback {value.Verification}. This cannot authorise or resume a write.";Notify();}}}
    public string ManualSteps=>SelectedJourney switch
    {
        "Joiner"=>"Create/authorise the account separately first. This page supports existing cloud-managed members only. Confirm ownership, intended direct groups/licences and workload/device setup. Each automated step needs its own fresh review; account/password creation and Exchange work remain manual.",
        "Leaver"=>"Obtain business-owner approval and preservation/retention decisions before service withdrawal. Review Exchange/mailbox, applications, devices, external privileges and ownership separately. Disabling/revoking/removing one entitlement does not complete offboarding. Never repeat an uncertain step.",
        _=>"Confirm old/new access and downstream ownership, direct versus inherited licences and any service withdrawal. Review Exchange/app/device dependencies manually. Each selected action is independent; no batch, automatic continuation or promised rollback."
    };
    public ICommand ReadinessCommand {get;} public ICommand PreviewCommand {get;} public ICommand ApplyCommand {get;}
    public ICommand HistoryCommand {get;} public ICommand ReconcileCommand {get;} public ICommand JsonCommand {get;} public ICommand HtmlCommand {get;} public ICommand ReceiptCommand {get;}
    public ICommand CaptureCommand {get;} public ICommand AccessCommand {get;}

    public Task CheckReadinessAsync()=>Workspace.RunExclusiveAsync("Checking target readiness (read-only)",async _=>
    {
        var generation=Interlocked.Read(ref _inputGeneration);var context=Workspace.CapturePresentationContext();var session=Workspace.Session??throw new ToolkitException("Connect first.");var profile=Workspace.Profile!;var operation=Operation();
        using var source=SourceFactory(operation,()=>RequireInputContext(context,generation));
        var result=await new LifecycleReadinessService(Workspace.Evidence,Clock).CaptureAsync(source,session,profile,operation,()=>RequireInputContext(context,generation),Workspace.OperationToken);
        RequireInputContext(context,generation);ReadinessRows=result.Rows;ReadinessGuidance=result.Guidance;Outcome="Read-only target observations saved. This is not plan approval or complete offboarding.";Notify();
    });
    public Task PreviewAsync()=>Workspace.RunExclusiveAsync("Preparing one lifecycle preview (read-only)",async _=>
    {
        var generation=Interlocked.Read(ref _inputGeneration);var context=Workspace.CapturePresentationContext();var connection=Workspace.RequireConnection();var operation=Operation();
        var snapshot=Workspace.Snapshot??throw new ToolkitException("Capture and save complete configuration first.");ClearPreview();
        using var source=SourceFactory(operation,()=>RequireInputContext(context,generation));
        var preview=await new LifecyclePreparationService(Workspace.Evidence,Clock).PreviewAsync(source,connection.Session,Workspace.Profile!,Workspace.RequireStandard(),snapshot,operation,()=>RequireInputContext(context,generation),Workspace.OperationToken);
        RequireInputContext(context,generation);_preview=preview;_previewContext=context;_previewGeneration=generation;
        BeforeDetails=preview.ReviewDetails;
        var document=preview.GetDocument();_previewExpires=new[]{Parse(document,"originalTokenExpiresAt"),Parse(document,"createdAt").AddMinutes(10),Parse(document,"snapshotAt").AddMinutes(20)}.Min();
        PreviewSummary=$"{Actions.Single(a=>a.Id==operation.ActionId).Label} · {Text(document["before"]?["user"],"displayName")} [{operation.UserId}]\n{Text(document,"consequence")}\nOnly this action can be approved. Manual workflow steps remain separate.";
        ExactRequest=operation.Method+" "+operation.Path+"\n"+(operation.PayloadJson??"No body");
        PreviewIdentity=$"Tenant {preview.TenantId}\nEngineer {connection.Session.Account} [{connection.Session.AccountObjectId}]\nRegistration {connection.Session.ClientId}\nPlan {preview.Id}\nExact digest {preview.IntegrityDigest}\nComplete saved snapshot {snapshot.Id} · {snapshot.CapturedAt}\nOriginal approval expires no later than {Timestamps.Format(_previewExpires)}";
        Outcome="Preview stored; no write sent. Review exact target, access, consequences and manual dependencies.";Notify();
    });
    public async Task ApplyAsync()
    {
        if(!CanReviewAction)throw new ToolkitException(ApplyGuidance);
        var reviewed=_preview!;var context=_previewContext!;var generation=_previewGeneration;var digest=reviewed.IntegrityDigest;
        if(!Confirm(this)){ResetAcknowledgements();Notify();return;}
        if(!ReferenceEquals(reviewed,_preview)||digest!=_preview.IntegrityDigest||!CanApply)throw new PlanValidationException("Lifecycle preview or review changed during confirmation. Nothing was approved or sent.");
        RequireInputContext(context,generation);
        var protectedReview=ProtectedReviewed;var downstreamReview=DownstreamReviewed;var preservationReview=PreservationReviewed;
        try
        {
            await Workspace.RunExclusiveAsync("Applying one exact lifecycle action",async _=>
            {
                Workspace.RequireExperimentalChanges(ExperimentalOperation.Lifecycle);RequireInputContext(context,generation);
                var connection=Workspace.RequireConnection();var profile=Workspace.Profile!;var standard=Workspace.RequireStandard();var snapshot=Workspace.Snapshot!;
                var service=new LifecycleRunService(Workspace.Evidence,Clock);
                var approval=await Task.Run(()=>service.Approve(reviewed,connection.Session,profile,standard,snapshot,digest,protectedReview,downstreamReview,preservationReview,()=>RequireInputContext(context,generation)));
                try
                {
                    var run=await service.ExecuteAsync(connection.Graph,connection.Session,profile,standard,snapshot,approval,operation=>SourceFactory(operation,()=>RequireInputContext(context,generation)),()=>RequireInputContext(context,generation),Workspace.OperationToken);
                    if(Workspace.IsCurrent(context))
                    {
                        Workspace.InvalidatePolicyState();
                        Outcome=$"Recorded action {run["actionId"]} · acceptance {run["writeAcceptance"]} · readback {run["verification"]} · {run["status"]}. Record {run["id"]}. Load local history for exact evidence. This does not complete the whole workflow.";
                    }
                }
                catch
                {
                    if(Workspace.IsCurrent(context)){Workspace.InvalidatePolicyState();Outcome="The action was refused or its final outcome could not be recorded. Preserve local history, review the error and never repeat an existing plan.";}
                    throw;
                }
            });
        }
        finally{ClearPreview();Notify();}
    }
    public Task LoadHistoryAsync()=>Workspace.RunExclusiveAsync("Reading local lifecycle attempt history",async _=>
    {
        var context=Workspace.CapturePresentationContext();var tenant=Workspace.Profile?.TenantId??throw new ToolkitException("Select a client first.");
        var token=Workspace.OperationToken;var rows=await Task.Run(()=>Workspace.Evidence.ListLifecycleAttempts(tenant,5000,token),token);
        RequireContext(context);Attempts=rows;HistoryStatus=rows.Count==0?"No lifecycle attempts are recorded for this client in this local evidence root.":$"{rows.Count} historical attempts. Original acceptance and readback are retained; unknown/intent-only records are not safe to repeat.";Notify();
    });
    public Task ReconcileAsync()=>Workspace.RunExclusiveAsync("Re-reading one recorded lifecycle effect (read-only)",async _=>
    {
        var context=Workspace.CapturePresentationContext();var id=SelectedAttempt?.Id??throw new ToolkitException("Select a saved attempt first.");var connection=Workspace.RequireConnection();
        var result=await new LifecycleRunService(Workspace.Evidence,Clock).ReconcileAsync(connection.Session,Workspace.Profile!,Workspace.RequireStandard(),id,operation=>SourceFactory(operation,()=>RequireContext(context)),()=>RequireContext(context),Workspace.OperationToken);
        RequireContext(context);_lastVerification=result["id"]!.GetValue<string>();
        Outcome=$"Read-only receipt {_lastVerification} · observed intended effect: {result["verified"]}. Original acceptance remains {result["originalWriteAcceptance"]}. Acceptance still unresolved: {result["acceptanceRemainsUnresolved"]}. No write was repeated.";Notify();
    });
    private async Task ExportHistoryAsync(ExportFormat format)
    {
        var context=Workspace.CapturePresentationContext();var tenant=Workspace.Profile?.TenantId??throw new ToolkitException("Select a client first.");var id=SelectedAttempt?.Id??throw new ToolkitException("Select a historical attempt first.");var family=SelectedRecordFamily;
        var file=await Workspace.ExportAsync(()=>Workspace.Exporter.ExportLifecycleRecord(Workspace.Evidence.ReadLifecycleRecord(tenant,family,id),tenant,format));
        RequireContext(context);LastExport=file;Notify();
    }
    private async Task ExportReceiptAsync()
    {
        var context=Workspace.CapturePresentationContext();var tenant=Workspace.Profile?.TenantId??throw new ToolkitException("Select a client first.");var id=_lastVerification;
        var file=await Workspace.ExportAsync(()=>Workspace.Exporter.ExportLifecycleRecord(Workspace.Evidence.ReadLifecycleRecord(tenant,"verification",id),tenant,ExportFormat.Json));
        RequireContext(context);LastExport=file;Notify();
    }
    public override void Refresh()
    {
        var context=Workspace.CapturePresentationContext();
        if(_context!=context)
        {
            if(_context?.ProfileInstance!=context.ProfileInstance||_context?.ConnectionInstance!=context.ConnectionInstance||_context?.SessionInstance!=context.SessionInstance||_context?.ProfileIdentity!=context.ProfileIdentity)
                _user=_group=_sku=_disabled="";
            _context=context;ClearPreview();ReadinessRows=[];ReadinessGuidance="Target readiness has not been checked in this context.";
            Attempts=[];_attempt=null;HistoryStatus="Load this client's local lifecycle history explicitly.";_lastVerification="";LastExport="";
            Outcome="No current lifecycle result. Original records remain under their source client.";
            _users=Choices("users","id","displayName");_groups=Choices("groups","id","displayName");_skus=Choices("licences","skuId","skuPartNumber");
            _userChoice=_users.FirstOrDefault(choice=>choice.Id==_user);_groupChoice=_groups.FirstOrDefault(choice=>choice.Id==_group);_skuChoice=_skus.FirstOrDefault(choice=>choice.Id==_sku);
            BuildServicePlans();
        }
        Notify();
    }
    private LifecycleOperation Operation()=>LifecycleActionRegistry.Create(SelectedActionId,SelectedUserId,NeedsGroup?SelectedGroupId:null,NeedsSku?SelectedSkuId:null,
        CanDisablePlans?ServicePlans.Where(p=>p.Disabled).Select(p=>p.Id).Concat(DisabledPlansText.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)).ToArray():null);
    private bool TryOperation(out LifecycleOperation? operation){try{operation=Operation();return true;}catch(ToolkitException){operation=null;return false;}}
    private IReadOnlyList<string> MissingReadAccess(LifecycleOperation operation)=>LifecycleReadRegistry.For(operation).Select(r=>r.Scope).Distinct(StringComparer.OrdinalIgnoreCase).Where(scope=>!HasScope(scope)).ToArray();
    private bool HasScope(string expected)=>Workspace.Session?.Scopes.Any(scope=>string.Equals(scope.StartsWith("https://graph.microsoft.com/",StringComparison.OrdinalIgnoreCase)?scope[28..]:scope,expected,StringComparison.OrdinalIgnoreCase))==true;
    private void RequireInputContext(PresentationContextKey context,long generation)
    {RequireContext(context);if(Interlocked.Read(ref _inputGeneration)!=generation)throw new PlanValidationException("Lifecycle selection changed, including a change back to earlier inputs. The old preview/approval cannot dispatch.");}
    private void RequireContext(PresentationContextKey context){if(!Workspace.IsCurrent(context))throw new TenantMismatchException("Lifecycle client, connection, catalogue or evidence context changed. Original records remain under their source client.");}
    private IReadOnlyList<Choice> Choices(string collection,string id,string label)=>Workspace.Snapshot?.TenantId!=Workspace.Profile?.TenantId?[]:Workspace.Snapshot?.Collections.GetValueOrDefault(collection)?.Items.Where(row=>Guid.TryParseExact(Text(row,id),"D",out var guid)&&guid!=Guid.Empty).Select(row=>new Choice(Text(row,id),Text(row,label)+" · "+Text(row,id))).ToArray()??[];
    private void BuildServicePlans()
    {
        ServicePlans.Clear();if(Workspace.Snapshot?.TenantId!=Workspace.Profile?.TenantId)return;var sku=Workspace.Snapshot?.Collections.GetValueOrDefault("licences")?.Items.FirstOrDefault(row=>string.Equals(Text(row,"skuId"),SelectedSkuId,StringComparison.OrdinalIgnoreCase));
        if(sku?["servicePlans"] is not JsonArray plans)return;
        foreach(var plan in plans.OfType<JsonObject>())if(Guid.TryParseExact(Text(plan,"servicePlanId"),"D",out var id)&&id!=Guid.Empty)ServicePlans.Add(new(id.ToString("D"),Text(plan,"servicePlanName")+" · "+id.ToString("D"),InputsChanged));
    }
    private void InputsChanged(){Interlocked.Increment(ref _inputGeneration);ClearPreview();ReadinessRows=[];ReadinessGuidance="Inputs changed. Check target readiness or prepare a fresh exact preview.";Outcome="Inputs changed. No action has been approved for this selection. Prior results remain in local history.";Notify();}
    private void ClearPreview(){_preview=null;_previewContext=null;_previewExpires=default;PreviewSummary="No current preview. Historical records cannot be resumed or approved.";ExactRequest="";BeforeDetails="";PreviewIdentity="";ResetAcknowledgements();}
    private void ResetAcknowledgements(){_protected=_downstream=_preservation=false;}
    private static string Text(JsonNode? node,string key)=>node?[key] is JsonValue value&&value.TryGetValue<string>(out var text)?text:"Unknown";
    private static DateTimeOffset Parse(JsonObject node,string key)=>Timestamps.TryParse(node[key]?.GetValue<string>(),out var at)?at:throw new ConfigurationException("Original lifecycle preview time is unknown.");
    private void Notify(){RaiseAll();CommandManager.InvalidateRequerySuggested();}
}
