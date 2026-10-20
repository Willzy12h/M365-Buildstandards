using System.Text.Json;
using BuildStandard.Core.Json;

namespace BuildStandard.Core.Reporting;

public sealed record ConditionalAccessPolicyEvaluation(string PolicyId,string? DisplayName,string? State,bool? PolicyApplies,string? AnalysisReasons,string Meaning);
public sealed record ConditionalAccessEvaluationResponse(IReadOnlyList<ConditionalAccessPolicyEvaluation> Rows,bool IsPartial,IReadOnlyList<string> ReadLimitations,string OriginalSha256);

/// <summary>Admits the fixed evaluation read, preserving unknowns. It never interprets policy applicability as enforcement.</summary>
public static class ConditionalAccessEvaluationResponseCodec
{
    public const int MaximumBytes=5*1024*1024;
    public const string Limitations="Hypothetical evaluation, not a real sign-in or proof of enforcement. Disabled candidates are not evaluated; service dependencies are not expanded. Report-only applicability does not enforce access.";
    private const string Context="https://graph.microsoft.com/v1.0/$metadata#Collection(microsoft.graph.whatIfAnalysisResult)";
    private static readonly HashSet<string> FalseReasons=new(StringComparer.Ordinal)
    {
        "users","workloadIdentities","application","userActions","authenticationContext",
        "devicePlatform","devices","clientApps","location","signInRisk","userRisk","time","insiderRisk","authenticationFlow"
    };
    public static ConditionalAccessEvaluationResponse Read(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length is 0 or >MaximumBytes)throw Bad("The evaluation response is empty or exceeds 5 MiB.");
        var original=bytes.ToArray();
        try
        {
            using var document=JsonDocument.Parse(original,new JsonDocumentOptions{MaxDepth=32});var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)throw Bad("The evaluation response must be an object with an explicit result array.");
            RejectDuplicates(root);
            if(root.TryGetProperty("@odata.context",out var context)&&(context.ValueKind!=JsonValueKind.String||context.GetString()!=Context))
                throw Bad("The evaluation response has an unexpected resource context.");
            if(!root.TryGetProperty("value",out var values)||values.ValueKind!=JsonValueKind.Array||values.GetArrayLength()>1000)
                throw Bad("The evaluation result array is missing, malformed or exceeds 1,000 rows.");
            var limitations=new List<string>();var rows=new List<ConditionalAccessPolicyEvaluation>();var identities=new HashSet<string>(StringComparer.Ordinal);
            if(root.TryGetProperty("@odata.nextLink",out var next)&&next.ValueKind!=JsonValueKind.Null)
            {
                if(next.ValueKind!=JsonValueKind.String||string.IsNullOrEmpty(next.GetString())||next.GetString()!.Length>4096)throw Bad("A further-page declaration is malformed.");
                limitations.Add("Microsoft declared further results; this fixed evaluation does not follow next links. Coverage is incomplete.");
            }
            foreach(var value in values.EnumerateArray())
            {
                if(value.ValueKind!=JsonValueKind.Object)throw Bad("An evaluation result row is not an object.");
                if(value.TryGetProperty("@odata.type",out var type)&&(type.ValueKind!=JsonValueKind.String||type.GetString()!="#microsoft.graph.whatIfAnalysisResult"))
                    throw Bad("An evaluation row has an unsupported subtype.");
                var rawId=Text(value,"id",36);
                if(rawId is not {Length:36}||!Guid.TryParseExact(rawId,"D",out var id)||id==Guid.Empty||!identities.Add(id.ToString("D")))
                    throw Bad("An evaluation policy identity is missing, malformed or duplicated.");
                var name=Text(value,"displayName",512);var state=Text(value,"state",128);var reason=Text(value,"analysisReasons",1024);
                bool? applies=null;
                if(value.TryGetProperty("policyApplies",out var property)&&property.ValueKind!=JsonValueKind.Null)
                {
                    if(property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw Bad("Policy applicability is not a returned boolean.");
                    applies=property.GetBoolean();
                }
                var supported=state is "enabled" or "enabledForReportingButNotEnforced";
                var meaning=supported&&applies==true&&reason=="notSet"?"Applies":supported&&applies==false&&reason is not null&&FalseReasons.Contains(reason)?"DoesNotApply":"Unknown";
                if(meaning=="Unknown")limitations.Add("Policy "+id.ToString("D")+" has incomplete, unsupported or inconsistent evaluation values; no applicability conclusion is made.");
                rows.Add(new(id.ToString("D"),name,state,applies,reason,meaning));
            }
            return new(Array.AsReadOnly(rows.ToArray()),limitations.Count>0,Array.AsReadOnly(limitations.ToArray()),CanonicalJson.Sha256Hex(original));
        }
        catch(JsonException){throw Bad("The evaluation response is malformed JSON.");}
    }
    private static string? Text(JsonElement row,string name,int maximum)
    {
        if(!row.TryGetProperty(name,out var value)||value.ValueKind==JsonValueKind.Null)return null;
        if(value.ValueKind!=JsonValueKind.String)throw Bad("An evaluation field has an invalid type.");
        var text=value.GetString()!;
        if(text.Length==0||text.Length>maximum||text.Any(char.IsControl))throw Bad("An evaluation field is empty, oversized or contains control characters.");
        return text;
    }
    private static void RejectDuplicates(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in element.EnumerateObject()){if(!names.Add(p.Name))throw Bad("The evaluation response contains duplicate JSON properties.");RejectDuplicates(p.Value);}
        }
        else if(element.ValueKind==JsonValueKind.Array)foreach(var item in element.EnumerateArray())RejectDuplicates(item);
    }
    private static ConfigurationException Bad(string message)=>new(message);
}
