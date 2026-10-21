using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;

namespace BuildStandard.Engine.Evidence;

internal sealed record LifecycleWriteState(string Id,string ActionId,string Acceptance,bool Unresolved,string Reason);

public sealed partial class EvidenceStore
{
    /// <summary>One read-only decision over original materialised records, shared by the write interlock and portfolio.
    /// No filesystem reads, mutations, approval construction or execution authority.</summary>
    internal static IReadOnlyList<LifecycleWriteState> ReviewLifecycleWrites(string tenant,IEnumerable<JsonObject> originals)
    {
        var records=new Dictionary<string,JsonObject>(StringComparer.Ordinal);
        foreach(var original in originals)
        {
            var kind=Text(original,"kind");
            if(!kind.StartsWith("lifecycle-",StringComparison.Ordinal))throw new SafetyViolationException("Unrecognised lifecycle record kind.");
            var family=kind["lifecycle-".Length..];var id=Text(original,"id");
            ValidateLifecycleEnvelope(original,tenant,family,id);
            if(Text(original,"integrityDigest")!=LifecycleDigest(original)||!records.TryAdd(family+"/"+id,original))
                throw new SafetyViolationException("Lifecycle history is duplicated or its integrity failed.");
        }
        JsonObject Require(string family,string id)=>records.GetValueOrDefault(family+"/"+id)
            ??throw new SafetyViolationException("Required original lifecycle "+family+" evidence is missing. Preserve history; do not replay.");
        void RequireReadback(JsonObject citation)
        {
            var after=Require("before",Text(citation,"afterId"));
            if(Text(after,"integrityDigest")!=Text(citation,"afterDigest")||after["complete"] is not JsonValue complete||!complete.TryGetValue<bool>(out var yes)||!yes)
                throw new SafetyViolationException("Lifecycle result lacks its intact complete original readback. A label alone cannot resolve a write.");
        }
        foreach(var run in records.Where(pair=>pair.Key.StartsWith("run/",StringComparison.Ordinal)))
            if(!records.ContainsKey("intent/"+Text(run.Value,"id")))throw new SafetyViolationException("Lifecycle outcome has no durable intent. Reconcile before further writes.");
        var states=new List<LifecycleWriteState>();
        foreach(var intent in records.Where(pair=>pair.Key.StartsWith("intent/",StringComparison.Ordinal)).Select(pair=>pair.Value))
        {
            var id=Text(intent,"id");var plan=records.GetValueOrDefault("plan/"+id);
            if(plan is not null && Text(plan,"integrityDigest")!=Text(intent,"planDigest"))throw new SafetyViolationException("Lifecycle intent cites a different original plan.");
            var action=plan?["operation"]?["actionId"]?.GetValue<string>()??"Unknown action";
            if(!records.TryGetValue("run/"+id,out var run))
            {states.Add(new(id,action,WriteAcceptance.Unknown,true,"A lifecycle intent has no intact final outcome. Its request may have escaped; no replay or further write is allowed."));continue;}
            if(Text(run,"intentDigest")!=Text(intent,"integrityDigest")||plan is not null&&Text(run,"planDigest")!=Text(plan,"integrityDigest"))
                throw new SafetyViolationException("Lifecycle outcome does not cite its original plan and intent.");
            var acceptance=run["writeAcceptance"] is JsonValue scalar&&scalar.TryGetValue<string>(out var text)?text:WriteAcceptance.Unknown;
            if(WriteAcceptance.IsUncertain(acceptance))
            {states.Add(new(id,action,acceptance,true,"Lifecycle write acceptance is unknown or unclassified. Observing a value cannot prove whether the original request escaped; no replay is allowed."));continue;}
            if(acceptance!=WriteAcceptance.Accepted){states.Add(new(id,action,acceptance,false,"Known original non-acceptance; no replay authority."));continue;}
            if(run["verification"]?.GetValue<string>()==ConfigurationVerification.Pass)
            {RequireReadback(run);states.Add(new(id,action,acceptance,false,"Accepted with intact original readback."));continue;}
            var verification=records.Where(pair=>pair.Key.StartsWith("verification/",StringComparison.Ordinal)).Select(pair=>pair.Value)
                .FirstOrDefault(value=>value["runId"]?.GetValue<string>()==id&&value["runDigest"]?.GetValue<string>()==Text(run,"integrityDigest")
                    &&value["verified"] is JsonValue verified&&verified.TryGetValue<bool>(out var yes)&&yes);
            if(verification is null)
            {states.Add(new(id,action,acceptance,true,"Accepted lifecycle action requires explicit read-only verification before further writes."));continue;}
            RequireReadback(verification);states.Add(new(id,action,acceptance,false,"Accepted with intact separate read-only verification; original history unchanged."));
        }
        return states.AsReadOnly();
    }
}
