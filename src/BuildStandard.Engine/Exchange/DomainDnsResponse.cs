using System.Globalization;
using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Models;

namespace BuildStandard.Engine.Exchange;

public static class DomainDnsResponse
{
    public const string Resolver = "Windows configured DNS resolver";
    public const string AdapterVersion = "1";
    public static DomainDnsObservation Parse(string json, DomainDnsQuestion question)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 65536) throw Bad("Missing or oversized adapter output.");
        try
        {
            using var document = JsonDocument.Parse(json,new JsonDocumentOptions { MaxDepth=16 });
            var root=document.RootElement;
            Object(root,"name","type","state","startedAt","completedAt","resolver","adapterVersion","answers","nativeErrorCode","negativeOwner","reason");
            var name=Text(root,"name");var type=EnumValue<DomainDnsType>(Text(root,"type"));
            if (name!=question.Name || type!=question.Type) throw Bad("Adapter output does not match the original question.");
            var state=EnumValue<DomainDnsState>(Text(root,"state"));
            var started=Text(root,"startedAt");var ended=Text(root,"completedAt");
            if (Utc(ended)<Utc(started)) throw Bad("Adapter interval is reversed.");
            if (Text(root,"resolver")!=Resolver || Text(root,"adapterVersion")!=AdapterVersion) throw Bad("Unsupported resolver provenance or adapter version.");
            if (!root.TryGetProperty("answers",out var records) || records.ValueKind!=JsonValueKind.Array || records.GetArrayLength()>100)
                throw Bad("Missing or oversized answer array.");
            var answers=records.EnumerateArray().Select(Answer).ToArray();
            var reached=Bound(question,answers);
            var code=Number(root,"nativeErrorCode");var negative=OptionalText(root,"negativeOwner");var reason=OptionalText(root,"reason");
            if(code is <0 or >int.MaxValue)throw Bad("Invalid native error code.");
            if (state==DomainDnsState.Observed)
            {
                if (!answers.Any(a=>a.Type==type) || code is not null || negative is not null)
                    throw Bad("Observed requires an admitted answer of the requested type, without a negative result.");
            }
            else if (state is DomainDnsState.NameError or DomainDnsState.NoData)
            {
                if (code!=(state==DomainDnsState.NameError ? 9003 : 9501) || answers.Any(a=>a.Type!=DomainDnsType.CNAME))
                    throw Bad("A negative response requires its explicit native code and no successful data record.");
                if (negative is not null && !reached.Contains(DomainDnsName.Reference(negative).Canonical))
                    throw Bad("The negative owner is not connected to the question.");
                if (negative is null) reason=$"Lookup returned {(state==DomainDnsState.NameError ? "NXDOMAIN" : "no-record response")}; negative owner unknown.";
            }
            else if (string.IsNullOrWhiteSpace(reason) || negative is not null)
                throw Bad("An incomplete observation needs a reason and cannot establish an absent owner.");
            return new(name,type,state,started,ended,Resolver,AdapterVersion,Array.AsReadOnly(answers),(int?)code,negative,reason);
        }
        catch (JsonException ex) { throw Bad("Malformed adapter JSON: "+ex.Message); }
    }

    private static DomainDnsAnswer Answer(JsonElement row)
    {
        Object(row,"owner","type","section","ttl","textChunks","exchange","preference","target");
        var owner=Text(row,"owner");_=DomainDnsName.Reference(owner);
        var type=EnumValue<DomainDnsType>(Text(row,"type"));var section=Text(row,"section");
        if (section!="Answer") throw Bad("Only explicit Answer-section records can establish readiness.");
        var ttl=Number(row,"ttl");if(ttl is < 0 or > uint.MaxValue)throw Bad("Invalid TTL.");
        IReadOnlyList<string>? chunks=null;string? exchange=null;int? preference=null;string? target=null;
        switch(type)
        {
            case DomainDnsType.TXT:
                if (!row.TryGetProperty("textChunks",out var strings) || strings.ValueKind!=JsonValueKind.Array || strings.GetArrayLength()==0
                    || strings.EnumerateArray().Any(s=>s.ValueKind!=JsonValueKind.String)) throw Bad("TXT needs each returned string chunk.");
                chunks=Array.AsReadOnly(strings.EnumerateArray().Select(s=>s.GetString()!).ToArray());
                Absent(row,"exchange","preference","target");break;
            case DomainDnsType.MX:
                exchange=Text(row,"exchange");if(exchange!=".")_=DomainDnsName.Reference(exchange);
                var number=Number(row,"preference");if(number is null or <0 or >65535)throw Bad("MX preference is missing or invalid.");
                preference=(int)number.Value;Absent(row,"textChunks","target");break;
            case DomainDnsType.CNAME:
                target=Text(row,"target");_=DomainDnsName.Reference(target);Absent(row,"textChunks","exchange","preference");break;
        }
        return new(owner,type,section,ttl,chunks,exchange,preference,target);
    }
    private static HashSet<string> Bound(DomainDnsQuestion question,DomainDnsAnswer[] answers)
    {
        var aliases=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var alias in answers.Where(a=>a.Type==DomainDnsType.CNAME))
            if(!aliases.TryAdd(DomainDnsName.Reference(alias.Owner).Canonical,DomainDnsName.Reference(alias.Target!).Canonical))
                throw Bad("Duplicate or branched alias ownership is ambiguous.");
        var reached=new HashSet<string>(StringComparer.Ordinal){question.Name};var current=question.Name;var depth=0;
        while(aliases.TryGetValue(current,out var next))
        {
            if(++depth>10 || !reached.Add(next))throw Bad("The alias chain is cyclic or exceeds ten aliases.");
            current=next;
        }
        foreach(var answer in answers)
        {
            var owner=DomainDnsName.Reference(answer.Owner).Canonical;
            if(!reached.Contains(owner) || answer.Type!=DomainDnsType.CNAME && answer.Type!=question.Type)
                throw Bad("An answer is unrelated to the exact question and its connected aliases.");
            if(answer.Type!=DomainDnsType.CNAME && aliases.ContainsKey(owner))throw Bad("An alias owner also contains data of another type.");
        }
        return reached;
    }
    private static void Object(JsonElement value,params string[] allowed)
    {
        if(value.ValueKind!=JsonValueKind.Object)throw Bad("Expected an object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in value.EnumerateObject())if(!seen.Add(field.Name)||!allowed.Contains(field.Name,StringComparer.Ordinal))throw Bad("Duplicate or unknown field: "+field.Name);
    }
    private static string Text(JsonElement value,string field)
        =>OptionalText(value,field) is { Length:>0 } text ? text : throw Bad("Missing text field: "+field);
    private static string? OptionalText(JsonElement value,string field)
    {
        if(!value.TryGetProperty(field,out var item)||item.ValueKind==JsonValueKind.Null)return null;
        if(item.ValueKind!=JsonValueKind.String)throw Bad("Invalid text field: "+field);
        return item.GetString();
    }
    private static long? Number(JsonElement value,string field)
    {
        if(!value.TryGetProperty(field,out var item)||item.ValueKind==JsonValueKind.Null)return null;
        if(item.ValueKind!=JsonValueKind.Number||!item.TryGetInt64(out var number))throw Bad("Invalid integer field: "+field);
        return number;
    }
    private static void Absent(JsonElement value,params string[] fields)
    {
        foreach(var field in fields)if(value.TryGetProperty(field,out _))throw Bad("Answer field does not apply to its record type: "+field);
    }
    private static T EnumValue<T>(string text) where T:struct,Enum
        =>Enum.TryParse<T>(text,false,out var result)&&Enum.IsDefined(result)&&result.ToString()==text ? result : throw Bad("Unknown enum value.");
    private static DateTimeOffset Utc(string text)
    {
        var formats=new[]{"yyyy-MM-dd'T'HH:mm:ss'Z'","yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'","yyyy-MM-dd'T'HH:mm:sszzz","yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"};
        if(!DateTimeOffset.TryParseExact(text,formats,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var value)||value.Offset!=TimeSpan.Zero)
            throw Bad("Expected an explicit UTC timestamp.");
        return value;
    }
    private static ConfigurationException Bad(string message)=>new("DNS response refused: "+message);
}
