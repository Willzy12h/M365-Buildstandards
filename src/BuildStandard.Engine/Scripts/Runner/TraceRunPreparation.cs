using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Scripts.Runner;
/// <summary>Immutable exact trace review data. Does not supply package/authentication/opt-in or execution authority.</summary>
public sealed class TraceRunPreparation
{
    private readonly string _binding;
    private readonly string _runtime;
    public ExchangeTraceQuery Query {get;}
    private TraceRunPreparation(string binding,ExchangeTraceQuery query,string runtime){_binding=binding;Query=query;_runtime=runtime;}
    public static TraceRunPreparation Prepare(ScriptEntry entry,ReadRunContext context,IReadOnlyList<ReadRunParameter> parameters,string runtime,DateTimeOffset now)
    {
        var binding=Binding(entry,context,parameters,runtime);
        // Match the evidence clock's existing millisecond UTC precision; never round a review forwards.
        if(!Timestamps.TryParse(Timestamps.Format(now),out var frozen))throw new ConfigurationException("The trace review clock is invalid.");
        var query=FromParameters(parameters,frozen);
        if(query.MaximumRows<=query.Windows.Count)throw new ConfigurationException("The trace row limit must retain every interval diagnostic and at least one observation. Increase it before review.");
        return new(binding,query,runtime);
    }
    public void RequireMatch(ScriptEntry entry,ReadRunContext context,IReadOnlyList<ReadRunParameter> parameters,string runtime,DateTimeOffset now)
    {
        if(now<Query.FrozenNow||now-Query.FrozenNow>TimeSpan.FromSeconds(300))throw new SafetyViolationException("The trace review clock is future or expired. Prepare a fresh exact review; no query was regenerated.");
        if(_binding!=Binding(entry,context,parameters,runtime))throw new SafetyViolationException("The trace item, pins, actor, runtime or reviewed values changed. Prepare a fresh review.");
    }
    internal void RequireBinding(ScriptEntry entry,ReadRunContext context,IReadOnlyList<ReadRunParameter> parameters)
    {
        if(_binding!=Binding(entry,context,parameters,_runtime))throw new SafetyViolationException("The trace request no longer matches its exact review.");
    }
    internal void RequireResult(IReadOnlyList<ReportSection> sections)
    {
        var rows=sections.Single(s=>s.Id=="interval-reads").Rows;
        if(rows.Count!=Query.Windows.Count)throw new IntegrityException("Trace result omitted a reviewed interval.");
        for(var index=0;index<rows.Count;index++)
        {
            var row=rows[index];var window=Query.Windows[index];
            if(row["frozenAtUtc"]!.GetValue<string>()!=Timestamps.Format(Query.FrozenNow)||row["startUtc"]!.GetValue<string>()!=Timestamps.Format(window.Start)
                ||row["endUtc"]!.GetValue<string>()!=Timestamps.Format(window.End)||row["filterDigest"]!.GetValue<string>()!=Query.FilterDigest)
                throw new IntegrityException("Trace output differs from the original reviewed clock/window/filter plan.");
        }
    }
    internal static ExchangeTraceQuery FromParameters(IReadOnlyList<ReadRunParameter> parameters,DateTimeOffset frozen)
    {
        var query=ExchangeTraceQuery.Prepare(parameters.Select(p=>new ExchangeReportParameter{Name=p.Name,Type=p.Type,Value=p.Value}).ToArray(),frozen);
        for(var i=0;i<parameters.Count;i++)
        {
            var parameter=parameters[i];var bound=parameter.Type!="Text"||parameter.Value.Length!=0;
            JsonNode? expected=!bound?null:parameter.Type switch
            {
                "Boolean"=>JsonValue.Create(parameter.Value=="true"),"Integer"=>JsonValue.Create(query.MaximumRows),
                _=>JsonValue.Create(Convert.ToBase64String(new UTF8Encoding(false,true).GetBytes(parameter.Value)))
            };
            if(parameter.Bound!=bound||!JsonNode.DeepEquals(expected,parameter.Payload))throw new ConfigurationException("A trace wire value differs from its exact visible review value.");
        }
        return query;
    }
    private static string Binding(ScriptEntry entry,ReadRunContext context,IReadOnlyList<ReadRunParameter> parameters,string runtime)
    {
        var m=entry.Manifest;
        if(m.SchemaVersion!=2||m.Mode!=ScriptMode.ReadOnly||m.Execution is not {ReportId:"exo-message-trace",ReportSchemaVersion:2,Adapter:ExchangeReportRegistry.ExchangeOnlineAdapter}
            ||m.Id!="exo.message-trace-observations"||!m.SupportedRuntimes.Contains(runtime,StringComparer.Ordinal))throw new ConfigurationException("Trace preparation requires the exact registered read-only trace description/runtime.");
        if(!ReportValues.IsCanonicalGuid(context.TenantId)||!ReportValues.IsCanonicalGuid(context.AccountObjectId)||!ScriptCopy.IsAcceptableAccount(context.AccountUpn))throw new ConfigurationException("Trace review needs the current verified tenant/account.");
        if(ScriptCatalogue.Sha256(Encoding.ASCII.GetBytes(entry.Script))!=m.ScriptSha256)throw new IntegrityException("The trace body differs from its recorded pin.");
        return CanonicalJson.SerializeValue(new{Manifest=m,entry.ManifestSha256,context.TenantId,context.AccountObjectId,context.AccountUpn,Runtime=runtime,
            Parameters=parameters.Select(p=>new{p.Name,p.Type,p.Value,p.Bound,Payload=p.Payload?.DeepClone()}).ToArray()});
    }
    public JsonObject RequestContext()=>new()
    {
        ["schemaVersion"]=1,["frozenAtUtc"]=Timestamps.Format(Query.FrozenNow),["filterDigest"]=Query.FilterDigest,
        ["windows"]=new JsonArray(Query.Windows.Select(window=>(JsonNode)new JsonObject{["startUtc"]=Timestamps.Format(window.Start),["endUtc"]=Timestamps.Format(window.End)}).ToArray())
    };
}
