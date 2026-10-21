using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
namespace BuildStandard.Engine.Evidence;

public sealed partial class EvidenceStore
{
    private const int LifecycleMaximumBytes=16*1024*1024;
    private static readonly string[] LifecycleFamilies=["plan","before","intent","run","verification"];
    internal void SaveLifecyclePreview(JsonObject document)=>SaveLifecycleOriginal(document,"plan");
    internal void SaveLifecycleBefore(JsonObject document)=>SaveLifecycleOriginal(document,"before");
    internal JsonObject SaveLifecycleIntent(JsonObject document)=>SaveLifecycleOriginal(document,"intent");
    internal JsonObject SaveLifecycleOutcome(JsonObject document)
    {
        var copy=document.DeepClone().AsObject();
        var intent=RequireLifecycleOriginal(Text(copy,"tenantId"),"intent",Text(copy,"id"));
        copy["intentDigest"]=Text(intent,"integrityDigest");copy["integrityDigest"]="";
        return SaveLifecycleOriginal(copy,"run");
    }
    internal JsonObject SaveLifecycleVerification(JsonObject document)=>SaveLifecycleOriginal(document,"verification");
    internal JsonObject RequireLifecycleOriginal(string tenant,string family,string id)=>ReadLifecycleRecord(tenant,family,id).GetDocument();
    /// <summary>Historical inspection only. This cannot construct a live preview, approval or resumable write.</summary>
    public LifecycleHistoricalRecord ReadLifecycleRecord(string tenant,string family,string id)
    {
        var file=LifecycleFile(tenant,family,id);RefuseLifecycleLinks(file);
        if(!File.Exists(file))throw new SafetyViolationException("Required lifecycle evidence is missing. Preserve history and reconcile; do not replay.");
        using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>LifecycleMaximumBytes)throw new SafetyViolationException("Lifecycle evidence exceeds its size bound.");
        using var bytes=new MemoryStream();var buffer=new byte[8192];int read;
        while((read=stream.Read(buffer))>0){if(bytes.Length+read>LifecycleMaximumBytes)throw new SafetyViolationException("Lifecycle evidence grew beyond its bound.");bytes.Write(buffer,0,read);}
        try
        {
            using var parsed=JsonDocument.Parse(bytes.ToArray(),new JsonDocumentOptions {MaxDepth=48,AllowDuplicateProperties=false});
            var value=JsonNode.Parse(parsed.RootElement.GetRawText())?.AsObject()??throw new SafetyViolationException("Lifecycle evidence is not an object.");
            ValidateLifecycleEnvelope(value,tenant,family,id);
            if(Text(value,"integrityDigest")!=LifecycleDigest(value))throw new SafetyViolationException("Lifecycle evidence integrity failed.");
            return new LifecycleHistoricalRecord(value,bytes.ToArray());
        }
        catch(JsonException error){throw new SafetyViolationException("Lifecycle evidence is malformed: "+error.GetType().Name);}
        catch(InvalidOperationException){throw new SafetyViolationException("Lifecycle evidence has an unsupported shape.");}
    }
    /// <summary>Includes intent-only attempts as unknown. Invalid/capped reads are refused, never skipped into empty success.</summary>
    public IReadOnlyList<LifecycleAttemptSummary> ListLifecycleAttempts(string tenant,int maximum=5000,CancellationToken ct=default)
    {
        if(maximum is <1 or >5000)throw new ConfigurationException("Inspect at most5,000 lifecycle attempts, or open an exact record ID.");
        var directory=Path.Combine(TenantDirectory(tenant),"lifecycle");RefuseLifecycleLinks(directory);
        if(!ReviewPathExists(directory,true))return Array.Empty<LifecycleAttemptSummary>();
        // An incomplete transfer must not turn an orphaned outcome into a successful empty history.
        foreach(var outcome in Directory.EnumerateFiles(directory,"run-*.json"))
        {
            ct.ThrowIfCancellationRequested();var outcomeId=Path.GetFileNameWithoutExtension(outcome)["run-".Length..];
            if(!File.Exists(LifecycleFile(tenant,"intent",outcomeId)))throw new SafetyViolationException("Lifecycle history has an outcome without its original intent. Preserve the files; no complete/empty history is certified.");
        }
        var rows=new List<LifecycleAttemptSummary>();
        foreach(var file in Directory.EnumerateFiles(directory,"intent-*.json"))
        {
            ct.ThrowIfCancellationRequested();if(rows.Count>=maximum)throw new SafetyViolationException("Lifecycle history exceeds this inspection bound. No complete/empty result is certified; inspect a specific record ID instead.");
            var id=Path.GetFileNameWithoutExtension(file)["intent-".Length..];
            var intent=RequireLifecycleOriginal(tenant,"intent",id);var plan=RequireLifecycleOriginal(tenant,"plan",id);
            if(Text(intent,"planDigest")!=Text(plan,"integrityDigest"))throw new SafetyViolationException("Lifecycle intent cites a different original plan.");
            var runPath=LifecycleFile(tenant,"run",id);var run=File.Exists(runPath)?RequireLifecycleOriginal(tenant,"run",id):null;
            if(run is not null && (Text(run,"intentDigest")!=Text(intent,"integrityDigest") || Text(run,"planDigest")!=Text(plan,"integrityDigest")))throw new SafetyViolationException("Lifecycle outcome does not cite its original plan and intent.");
            rows.Add(new LifecycleAttemptSummary(id,plan["operation"]?["actionId"]?.GetValue<string>()??"Unknown action",
                plan["before"]?["user"]?["displayName"]?.GetValue<string>()??"Unknown recorded target",plan["operation"]?["userId"]?.GetValue<string>()??"Unknown target ID",
                run?["status"]?.GetValue<string>()??"No final outcome",run?["writeAcceptance"]?.GetValue<string>()??WriteAcceptance.Unknown,
                run?["verification"]?.GetValue<string>()??ConfigurationVerification.Unknown,intent["startedAt"]?.GetValue<string>()??"Unknown start time",Text(run??intent,"integrityDigest")));
        }
        ct.ThrowIfCancellationRequested();return rows.OrderByDescending(row=>row.StartedAt,StringComparer.Ordinal).ThenBy(row=>row.Id,StringComparer.Ordinal).ToArray();
    }

    /// <summary>Fail-closed across all lifecycle attempts. Read-only reconciliation never rewrites original acceptance.</summary>
    public void AssertNoUnresolvedLifecycle(string tenant)
    {
        var directory=Path.Combine(TenantDirectory(tenant),"lifecycle");RefuseLifecycleLinks(directory);
        if(!ReviewPathExists(directory,true))return;
        var originals=new List<JsonObject>();
        foreach(var file in Directory.EnumerateFiles(directory,"*.json"))
        {
            var name=Path.GetFileNameWithoutExtension(file);var separator=name.IndexOf('-');
            if(separator<0)throw new SafetyViolationException("Unrecognised lifecycle history cannot be skipped.");
            originals.Add(RequireLifecycleOriginal(tenant,name[..separator],name[(separator+1)..]));
        }
        var unresolved=ReviewLifecycleWrites(tenant,originals).FirstOrDefault(state=>state.Unresolved);
        if(unresolved is not null)throw new SafetyViolationException(unresolved.Reason);
    }

    private JsonObject SaveLifecycleOriginal(JsonObject document,string family)
    {
        var copy=document.DeepClone().AsObject();var tenant=Text(copy,"tenantId");var id=Text(copy,"id");
        ValidateLifecycleEnvelope(copy,tenant,family,id);
        var digest=LifecycleDigest(copy);
        if(Text(copy,"integrityDigest").Length>0 && Text(copy,"integrityDigest")!=digest)throw new SafetyViolationException("Lifecycle evidence changed before persistence.");
        copy["integrityDigest"]=digest;
        var file=LifecycleFile(tenant,family,id);var bytes=Encoding.UTF8.GetBytes(ToolkitJson.Serialize(copy));
        if(bytes.Length>LifecycleMaximumBytes)throw new SafetyViolationException("Lifecycle evidence exceeds its byte bound.");
        RefuseLifecycleLinks(file);Directory.CreateDirectory(Path.GetDirectoryName(file)!);RefuseLifecycleLinks(file);
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(bytes);output.Flush(true);}
            // Never replace an intent, observation or outcome. Local retries cannot replace single-use history.
            try{File.Move(temporary,file,false);}catch(IOException)when(File.Exists(file)){throw new SafetyViolationException("Lifecycle evidence already exists. Existing plans and attempts cannot be overwritten or replayed.");}
        }
        finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
        return copy;
    }
    private string LifecycleFile(string tenant,string family,string id)
    {
        if(!LifecycleFamilies.Contains(family,StringComparer.Ordinal) || !Guid.TryParseExact(id,"D",out var guid) || guid==Guid.Empty || guid.ToString("D")!=id)
            throw new SafetyViolationException("Unsupported lifecycle record family or identity.");
        return Path.Combine(TenantDirectory(tenant),"lifecycle",family+"-"+id+".json");
    }
    private static void ValidateLifecycleEnvelope(JsonObject document,string tenant,string family,string id)
    {
        var kind=family=="plan"?"lifecycle-plan":family=="before"?"lifecycle-before":"lifecycle-"+family;
        if(!LifecycleFamilies.Contains(family,StringComparer.Ordinal) || Text(document,"kind")!=kind || document["schemaVersion"] is not JsonValue version || !version.TryGetValue<int>(out var number) || number!=1
            || Text(document,"id")!=id || Text(document,"tenantId")!=tenant)throw new SafetyViolationException("Lifecycle schema, family or client identity is unsupported.");
    }
    private static string LifecycleDigest(JsonObject value){var copy=value.DeepClone().AsObject();copy["integrityDigest"]="";return CanonicalJson.Sha256(copy);}
    private static string Text(JsonObject value,string key)=>value[key] is JsonValue scalar && scalar.TryGetValue<string>(out var text)?text:throw new SafetyViolationException("Required lifecycle evidence field is missing: "+key);
    private static void RefuseLifecycleLinks(string path)
    {for(var current=new FileInfo(Path.GetFullPath(path));current is not null;current=current.Directory is { } parent?new FileInfo(parent.FullName):null)if((File.Exists(current.FullName)||Directory.Exists(current.FullName))&&(File.GetAttributes(current.FullName)&FileAttributes.ReparsePoint)!=0)throw new SafetyViolationException("Lifecycle evidence cannot follow symbolic links or reparse points.");}

}
