using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Graph;

namespace BuildStandard.Engine.Reports;

/// <summary>Current-client member admission and original report capture. Never authenticates or mutates.</summary>
public sealed class ConditionalAccessEvaluationService(TimeProvider? clock=null)
{
    private readonly TimeProvider _clock=clock??TimeProvider.System;
    public async Task<ConditionalAccessEvaluationCapture> CaptureAsync(IGraphClient graph,ConditionalAccessEvaluator evaluator,
        TenantSession session,ConditionalAccessScenario scenario,Func<bool> isCurrent,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var prepared=ConditionalAccessScenarioCodec.Prepare(scenario);
        var tenant=session.TenantId;var account=session.AccountObjectId;var client=session.ClientId;var mode=session.Mode;
        var scopes=session.Scopes?.ToArray()??[];
        var started=_clock.GetUtcNow();var timestamp=_clock.GetTimestamp();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(120),_clock);
        using var operation=CancellationTokenSource.CreateLinkedTokenSource(ct,deadline.Token);
        RequireCurrent();
        if(!HasScope("User.Read.All")||!HasScope("Policy.Read.All"))
            throw new ConfigurationException("This evaluation needs existing User.Read.All for an exact member lookup and Policy.Read.All. Review the current connection; this report never requests consent or broader directory access.");
        try
        {
            // The canonical ID is the only variable. No caller-provided path/filter or display-name join.
            var path="/users?$select=id,userType,creationType,externalUserState,accountEnabled&$filter="
                +Uri.EscapeDataString("id eq '"+prepared.Scenario.UserObjectId+"'")+"&$top=2";
            var lookup=await Bounded(graph.GetAsync(GraphApi.V1,path,operation.Token));
            RequireCurrent();RequireMember(lookup,prepared.Scenario.UserObjectId);
            var read=await Bounded(evaluator.EvaluateAsync(prepared.Scenario,operation.Token));
            RequireCurrent();
            if(read.TenantId!=tenant||read.AccountObjectId!=account||read.ClientId!=client||read.PermissionMode!=mode
                ||read.Scenario.Sha256!=prepared.Sha256||!new HashSet<string>(scopes,StringComparer.OrdinalIgnoreCase).SetEquals(read.Scopes))
                throw new TenantMismatchException("The evaluation receipt does not match this exact connection and reviewed scenario. No current-client report was admitted.");
            var raw=read.OriginalResponseBytes;
            var report=new ConditionalAccessEvaluationReport
            {
                Id=Guid.NewGuid().ToString("D"),TenantId=tenant,AccountObjectId=account,ClientId=client,PermissionMode=mode,Scopes=scopes.ToList(),
                StartedAt=Timestamps.Format(started),CompletedAt=Timestamps.Format(_clock.GetUtcNow()),ToolkitVersion=ToolkitVersion.Current,
                Scenario=prepared.Scenario,RequestSha256=prepared.Sha256,ResponseSha256=read.Result.OriginalSha256,ResponseBytes=raw.Length,
                Status=read.Result.IsPartial?"Partial":"Complete"
            };
            ConditionalAccessEvaluationSchema.Seal(report,raw);RequireCurrent();
            return new(report,raw);
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested&&Expired())
        {throw Timeout();}

        bool Expired()=>deadline.IsCancellationRequested||_clock.GetElapsedTime(timestamp)>=TimeSpan.FromSeconds(120);
        bool HasScope(string scope)=>scopes.Any(s=>string.Equals(s,scope,StringComparison.OrdinalIgnoreCase)
            ||string.Equals(s,"https://graph.microsoft.com/"+scope,StringComparison.OrdinalIgnoreCase));
        GraphRequestException Timeout()=>new(0,"POST","/identity/conditionalAccess/evaluate","timeout",
            "The 120-second evaluation budget, including the member lookup, ended. No complete result or retry is claimed.");
        void RequireCurrent()
        {
            ct.ThrowIfCancellationRequested();if(Expired())throw Timeout();operation.Token.ThrowIfCancellationRequested();
            if(!ReportValues.IsCanonicalGuid(tenant)||!ReportValues.IsCanonicalGuid(account)||!ReportValues.IsCanonicalGuid(client)
                ||mode is not (SessionMode.Assessment or SessionMode.Deployment)||!session.TenantVerified||!session.OperatorVerified
                ||session.TenantId!=tenant||session.AccountObjectId!=account||session.ClientId!=client||session.OperatorObjectId!=account||session.Mode!=mode
                ||graph.TenantId!=tenant||graph.Mode!=mode||session.Scopes is null
                ||!new HashSet<string>(scopes,StringComparer.OrdinalIgnoreCase).SetEquals(session.Scopes)||!isCurrent())
                throw new TenantMismatchException("The verified client, engineer or access context changed. No result can enter the current client's reports.");
        }
        async Task<T> Bounded<T>(Task<T> task)
        {
            try{return await task.WaitAsync(operation.Token).ConfigureAwait(false);}
            catch
            {
                _=task.ContinueWith(static t=>{_=t.Exception;},CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
                throw;
            }
        }
    }
    private static void RequireMember(JsonObject lookup,string id)
    {
        if(lookup["value"] is not JsonArray {Count:1} rows||rows[0] is not JsonObject member
            ||lookup["@odata.nextLink"] is not null||Text(member,"id")!=id||Text(member,"userType")!="Member"
            ||!member.ContainsKey("creationType")||member["creationType"] is not null
            ||!member.ContainsKey("externalUserState")||member["externalUserState"] is not null
            ||member["accountEnabled"] is not JsonValue enabled||!enabled.TryGetValue<bool>(out _))
            throw new ConfigurationException("The exact user lookup did not establish one supported tenant member with returned origin metadata. Guests, external, ambiguous or incomplete identities cannot be evaluated in this version; no substitute user is chosen.");
    }
    private static string? Text(JsonObject item,string key)=>item[key] is JsonValue v&&v.TryGetValue<string>(out var s)?s:null;
}

/// <summary>Defensive original-file pair. Unsaved capture is not durable evidence or execution authority.</summary>
public sealed class ConditionalAccessEvaluationCapture
{
    private readonly byte[] _metadata,_response;
    public bool IsHistorical {get;}
    internal ConditionalAccessEvaluationCapture(ConditionalAccessEvaluationReport report,byte[] response)
    {_metadata=Encoding.UTF8.GetBytes(ToolkitJson.Serialize(report));_response=(byte[])response.Clone();}
    internal ConditionalAccessEvaluationCapture(byte[] metadata,byte[] response)
    {ConditionalAccessEvaluationSchema.Read(metadata,response);_metadata=(byte[])metadata.Clone();_response=(byte[])response.Clone();IsHistorical=true;}
    public ConditionalAccessEvaluationReport Metadata=>ConditionalAccessEvaluationSchema.Read(_metadata,_response);
    public byte[] MetadataBytes=>(byte[])_metadata.Clone();
    public byte[] OriginalResponseBytes=>(byte[])_response.Clone();
}
