using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Engine.Evidence;

namespace BuildStandard.Engine.Exchange;

public static class DomainReadinessSchema
{
    public const int MaximumBytes=5*1024*1024;
    private static readonly JsonSerializerOptions Strict=Options();
    private static JsonSerializerOptions Options()
    {
        var options=new JsonSerializerOptions(ToolkitJson.Options)
        {
            PropertyNameCaseInsensitive=false,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling=JsonCommentHandling.Disallow,AllowTrailingCommas=false,NumberHandling=JsonNumberHandling.Strict,
            RespectNullableAnnotations=true,RespectRequiredConstructorParameters=true,MaxDepth=32
        };
        options.Converters.Clear();options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues:false));return options;
    }
    public static DomainReadinessReport Read(byte[] bytes,string? expectedTenantId=null)
    {
        if(bytes.Length==0||bytes.Length>MaximumBytes)throw Bad("Empty report or report exceeds 5 MiB.");
        try
        {
            var json=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');
            using var document=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=32});var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)throw Bad("Expected a report object.");
            ExchangeCaptureSchema.RejectDuplicates(root,StringComparer.Ordinal);
            foreach(var pair in new[]{("observations",32),("references",500),("results",4),("requestedChecks",3),("expectedMx",32),("limitations",100)})
                BoundArray(root,pair.Item1,pair.Item2);
            foreach(var observation in root.GetProperty("observations").EnumerateArray())BoundArray(observation,"answers",100);
            foreach(var result in root.GetProperty("results").EnumerateArray())BoundArray(result,"details",500);
            var report=JsonSerializer.Deserialize<DomainReadinessReport>(json,Strict)??throw Bad("Null report.");
            if(root.GetProperty("status").GetString()!=report.Status.ToString())throw Bad("Non-canonical report status.");
            var checkRows=root.GetProperty("requestedChecks");
            for(var i=0;i<report.RequestedChecks.Count;i++)CanonicalEnum(checkRows[i],report.RequestedChecks[i]);
            var resultRows=root.GetProperty("results");
            for(var i=0;i<report.Results.Count;i++)
            { CanonicalEnum(resultRows[i].GetProperty("check"),report.Results[i].Check);CanonicalEnum(resultRows[i].GetProperty("meaning"),report.Results[i].Meaning); }
            var observationRows=root.GetProperty("observations");
            for(var i=0;i<report.Observations.Count;i++)
            {
                CanonicalEnum(observationRows[i].GetProperty("type"),report.Observations[i].Type);CanonicalEnum(observationRows[i].GetProperty("state"),report.Observations[i].State);
                var answerRows=observationRows[i].GetProperty("answers");
                for(var j=0;j<report.Observations[i].Answers.Count;j++)
                {
                    if(report.Observations[i].Answers[j] is null)throw Bad("Null answer.");
                    CanonicalEnum(answerRows[j].GetProperty("type"),report.Observations[i].Answers[j].Type);
                }
            }
            Validate(report,expectedTenantId);
            return report;
        }
        catch(Exception ex) when(ex is JsonException or DecoderFallbackException or InvalidOperationException or KeyNotFoundException)
        { throw Bad("Invalid report JSON: "+ex.Message); }
    }
    private static void Validate(DomainReadinessReport r,string? expectedTenantId)
    {
        if(r.Kind!="domainReadinessReport"||r.SchemaVersion!=1||r.SourceMode!="localDns"||r.Cloud!="Public"||r.AdapterVersion!="1")throw Bad("Unsupported report kind, schema, source or cloud.");
        GuidValue(r.Id);GuidValue(r.RunId);
        if(r.TenantId is not null)GuidValue(r.TenantId);if(r.AccountObjectId is not null)GuidValue(r.AccountObjectId);
        if(r.AccountObjectId is not null&&r.TenantId is null)throw Bad("Account identity has no tenant binding.");
        if(expectedTenantId is not null)
        {
            GuidValue(expectedTenantId);
            if(!string.Equals(r.TenantId,expectedTenantId,StringComparison.OrdinalIgnoreCase))throw new TenantMismatchException("Domain report is unbound or belongs to another client. It cannot be relabelled for this tenant.");
        }
        if(MailDomain.Validate(r.Domain)!=r.Domain)throw Bad("Domain must be canonical.");
        Text(r.ToolkitVersion,64);var start=Utc(r.StartedAt);var end=Utc(r.CompletedAt);if(end<start)throw Bad("Report interval is reversed.");
        if(!Enum.IsDefined(r.Status)||r.RequestedChecks.Count==0||r.RequestedChecks.Distinct().Count()!=r.RequestedChecks.Count||r.RequestedChecks.Any(c=>!Enum.IsDefined(c)))throw Bad("Invalid requested checks or report status.");
        if(r.ExpectedMx.Any(s=>s is null||DomainDnsName.Reference(s).Canonical!=s)||r.ExpectedMx.Distinct(StringComparer.Ordinal).Count()!=r.ExpectedMx.Count
            ||r.ExpectedMx.Count>0&&string.IsNullOrWhiteSpace(r.ExpectedMxSource))throw Bad("Expected gateways need distinct canonical targets and provenance.");
        if(r.ExpectedMxSource is not null)Text(r.ExpectedMxSource,1024);
        if(r.Limitations.Any(s=>string.IsNullOrWhiteSpace(s)))throw Bad("Invalid limitation.");
        if(r.Observations.Any(o=>o is null)||r.References.Any(e=>e is null)||r.Results.Any(x=>x is null))throw Bad("Null observation, reference or result.");
        var observations=new Dictionary<DomainDnsQuestion,DomainDnsObservation>();var total=r.References.Count;
        foreach(var o in r.Observations)
        {
            var question=new DomainDnsQuestion(o.Name,o.Type);
            var admitted=DomainDnsResponse.Parse(JsonSerializer.Serialize(o,ToolkitJson.Compact),question);
            if(!observations.TryAdd(question,admitted))throw Bad("Duplicate question.");
            if(Utc(o.StartedAt)<start||Utc(o.CompletedAt)>end)throw Bad("Observation lies outside the report interval.");
            total+=1+o.Answers.Count;
        }
        if(total>500)throw Bad("Report exceeds 500 observations/reference edges.");
        var allowed=new HashSet<DomainDnsQuestion>();
        if(r.RequestedChecks.Contains(DomainReadinessCheck.Mx))allowed.Add(new(r.Domain,DomainDnsType.MX));
        if(r.RequestedChecks.Contains(DomainReadinessCheck.Spf))allowed.Add(new(r.Domain,DomainDnsType.TXT));
        if(r.RequestedChecks.Contains(DomainReadinessCheck.WindowsEnrolment))
        {allowed.Add(new("enterpriseenrollment."+r.Domain,DomainDnsType.CNAME));allowed.Add(new("enterpriseregistration."+r.Domain,DomainDnsType.CNAME));}
        var required=allowed.ToArray();
        var remaining=r.References.ToList();
        while(remaining.Count>0)
        {
            var reached=false;
            foreach(var edge in remaining.ToArray())
            {
                if(!r.RequestedChecks.Contains(DomainReadinessCheck.Spf))throw Bad("SPF references were not requested.");
                var parent=new DomainDnsQuestion(edge.SourceDomain,DomainDnsType.TXT);
                if(!allowed.Contains(parent))continue;
                if(!observations.TryGetValue(parent,out var origin)||origin.State!=DomainDnsState.Observed)throw Bad("Reference lacks its source TXT observation.");
                if(edge.Depth is <1 or >11 || edge.State is not ("Reference" or "Loop" or "Limited")||Utc(edge.ObservedAt)!=Utc(origin.CompletedAt))throw Bad("Invalid reference provenance.");
                var policies=origin.Answers.Where(a=>a.Type==DomainDnsType.TXT).Select(a=>a.Text!).Where(t=>t.Equals("v=spf1",StringComparison.OrdinalIgnoreCase)||t.StartsWith("v=spf1 ",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(policies.Length!=1)throw Bad("Reference has no unique SPF policy.");
                var policy=SpfPolicyAnalysis.Parse(policies[0]);
                if(policy.Problems.Count>0||!policy.Terms.Any(t=>t.Reachable&&t.Original==edge.OriginalTerm&&t.Reference==edge.TargetDomain))throw Bad("Reference is not a reachable literal term in its original policy.");
                allowed.Add(new(edge.TargetDomain,DomainDnsType.TXT));remaining.Remove(edge);reached=true;
            }
            if(!reached)throw Bad("Reference graph is disconnected from the selected domain.");
        }
        if(observations.Keys.Any(q=>!allowed.Contains(q)))throw Bad("An observation is outside the requested checks and their references.");
        if(r.Results.Count==0||r.Results.Any(x=>!r.RequestedChecks.Contains(x.Check)||!Enum.IsDefined(x.Meaning)||x.Details is null||x.Details.Any(s=>s is null)))throw Bad("Missing or unrequested results.");
        foreach(var result in r.Results){Text(result.Summary,65536);Text(result.DnsOwnerNextStep,65536);}
        foreach(var check in r.RequestedChecks)
        {
            var count=r.Results.Count(x=>x.Check==check);
            if(count<1||count>(check==DomainReadinessCheck.WindowsEnrolment?2:1)||r.Status==DomainReadinessState.Complete&&check==DomainReadinessCheck.WindowsEnrolment&&count!=2)
                throw Bad("Result coverage does not match the requested checks.");
        }
        if(r.Status==DomainReadinessState.Complete&&(required.Any(q=>!observations.ContainsKey(q))
            ||observations.Values.Any(o=>o.State is DomainDnsState.Failed or DomainDnsState.Limited or DomainDnsState.Cancelled)
            ||r.References.Any(e=>e.State!="Reference"||!observations.ContainsKey(new(e.TargetDomain,DomainDnsType.TXT)))))throw Bad("Incomplete reads cannot become a Complete report.");
        if(r.Status==DomainReadinessState.Complete&&r.RequestedChecks.Contains(DomainReadinessCheck.Spf))
            RequireCompleteSpfCoverage(r,observations);
        if(r.IntegrityDigest.Length!=64||r.IntegrityDigest.Any(c=>!char.IsAsciiDigit(c)&&c is not (>= 'a' and <= 'f'))||!EvidenceIntegrity.Verify(r,r.IntegrityDigest))throw Bad("Report integrity check failed. A digest does not prove authenticity.");
    }
    private static void RequireCompleteSpfCoverage(DomainReadinessReport report,
        IReadOnlyDictionary<DomainDnsQuestion,DomainDnsObservation> observations)
    {
        // A digest is not an authenticity claim. Independently prove the narrower claimed
        // read coverage from original TXT records; a supplied edge list can omit dependencies.
        var remaining=report.References.ToList();
        var path=new HashSet<string>(StringComparer.Ordinal);
        var terms=0;
        void Visit(string owner,int depth)
        {
            if(depth>10||!path.Add(owner))throw Bad("Complete SPF coverage contains a loop or exceeds the traversal depth.");
            try
            {
                if(!observations.TryGetValue(new(owner,DomainDnsType.TXT),out var read))
                    throw Bad("Complete SPF coverage is missing a reachable TXT read.");
                if(read.State!=DomainDnsState.Observed)return; // Explicit native negative read, not invented absence.
                var policies=read.Answers.Where(a=>a.Type==DomainDnsType.TXT).Select(a=>a.Text!)
                    .Where(t=>t.Equals("v=spf1",StringComparison.OrdinalIgnoreCase)||t.StartsWith("v=spf1 ",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(policies.Length!=1)return; // Missing/multiple policies are findings, with no followed dependencies.
                var policy=SpfPolicyAnalysis.Parse(policies[0]);
                if(policy.Problems.Count>0)return;
                foreach(var term in policy.Terms.Where(t=>t.Reachable))
                {
                    if(term.DnsDependent&&++terms>10||term.NeedsContext)
                        throw Bad("Complete SPF coverage requires unperformed context or exceeds the term budget.");
                    if(term.Reference is null)continue;
                    var index=remaining.FindIndex(e=>e.SourceDomain==owner&&e.OriginalTerm==term.Original
                        &&e.TargetDomain==term.Reference&&e.Depth==depth+1&&e.State=="Reference"&&Utc(e.ObservedAt)==Utc(read.CompletedAt));
                    if(index<0)throw Bad("Complete SPF coverage is missing an original reachable term occurrence.");
                    remaining.RemoveAt(index);
                    Visit(term.Reference,depth+1);
                }
            }
            finally { path.Remove(owner); }
        }
        Visit(report.Domain,0);
        if(remaining.Count!=0)throw Bad("Complete SPF coverage contains unsupported reference occurrences.");
    }
    private static void BoundArray(JsonElement parent,string field,int count)
    {
        if(parent.ValueKind!=JsonValueKind.Object||!parent.TryGetProperty(field,out var array)||array.ValueKind!=JsonValueKind.Array||array.GetArrayLength()>count)throw Bad("Missing or oversized "+field+" array.");
    }
    private static void CanonicalEnum<T>(JsonElement value,T parsed) where T:struct,Enum
    {if(value.ValueKind!=JsonValueKind.String||value.GetString()!=parsed.ToString())throw Bad("Non-canonical enum field.");}
    private static void GuidValue(string value){if(!Guid.TryParseExact(value,"D",out var guid)||guid==Guid.Empty)throw Bad("Invalid identity GUID.");}
    private static void Text(string value,int maximum){if(string.IsNullOrWhiteSpace(value)||value.Length>maximum)throw Bad("Missing or oversized text.");}
    private static DateTimeOffset Utc(string value)
    {
        if(!DateTimeOffset.TryParseExact(value,new[]{"yyyy-MM-dd'T'HH:mm:ss'Z'","yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'","yyyy-MM-dd'T'HH:mm:sszzz","yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"},CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var result)||result.Offset!=TimeSpan.Zero)throw Bad("Expected an explicit UTC timestamp.");
        return result;
    }
    private static ConfigurationException Bad(string message)=>new("Domain report refused: "+message);
}
