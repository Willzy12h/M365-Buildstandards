using System.Globalization;
using System.Text;
using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;

namespace BuildStandard.Engine.Reports;

/// <summary>A strict metadata/original-response pair. Neither file is a configuration snapshot or authority.</summary>
public static class ConditionalAccessEvaluationSchema
{
    public const int MaximumBytes=5*1024*1024;
    public static ConditionalAccessEvaluationReport Read(ReadOnlySpan<byte> header,ReadOnlySpan<byte> response,string? expectedTenantId=null)
    {
        Bounds(header.Length,response.Length);
        try
        {
            var json=new UTF8Encoding(false,true).GetString(header);
            using var document=RegisteredReportJson.Parse(json);
            var report=JsonSerializer.Deserialize<ConditionalAccessEvaluationReport>(json,RegisteredReportJson.Strict)
                ??throw Bad("The evaluation metadata is empty.");
            var mode=document.RootElement.GetProperty("permissionMode");
            if(mode.ValueKind!=JsonValueKind.String||mode.GetString()!=report.PermissionMode.ToString())throw Bad("The original permission mode must be an exact supported name.");
            Validate(report,response,true);
            if(expectedTenantId is not null&&(!ReportValues.TryGuid(expectedTenantId,out var tenant)||report.TenantId!=tenant))
                throw new TenantMismatchException("The evaluation report belongs to another recorded tenant. It cannot enter this client's results.");
            return report;
        }
        catch(JsonException){throw Bad("The evaluation metadata contains unsupported, missing or malformed fields.");}
        catch(DecoderFallbackException){throw Bad("The evaluation metadata is not valid UTF-8.");}
    }
    public static void Seal(ConditionalAccessEvaluationReport report,ReadOnlySpan<byte> response)
    {
        Validate(report,response,false);report.IntegrityDigest=EvidenceIntegrity.Compute(report);
        Bounds(Encoding.UTF8.GetByteCount(ToolkitJson.Serialize(report)),response.Length);
    }
    private static void Bounds(int metadata,int response)
    {
        if(metadata<=0||response<=0||metadata>MaximumBytes||response>MaximumBytes||metadata>MaximumBytes-response)
            throw Bad("The metadata and original response exceed the 5 MiB report bound or a required file is empty. No complete saved report is available.");
    }
    private static void Validate(ConditionalAccessEvaluationReport r,ReadOnlySpan<byte> response,bool verifyDigest)
    {
        if(r is null||r.Kind!="conditionalAccessEvaluationReport"||r.SchemaVersion!=1||r.AdapterVersion!="conditional-access-evaluation/1"
            ||r.Resource!="https://graph.microsoft.com/"||r.SourceMode is not ("live" or "historical"))throw Bad("Unsupported evaluation kind, schema, adapter, resource or source mode.");
        foreach(var id in new[]{r.Id,r.TenantId,r.AccountObjectId,r.ClientId})if(!ReportValues.IsCanonicalGuid(id))throw Bad("A recorded evaluation identity is not a canonical non-empty GUID.");
        if(!Enum.IsDefined(r.PermissionMode))throw Bad("The original permission mode is unsupported.");
        if(r.Scopes is not {Count:>0 and <=128}||r.Scopes.Any(s=>s is null||s.Length is 0 or >256||s.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)))
            ||r.Scopes.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=r.Scopes.Count)throw Bad("Recorded evaluation scopes are malformed or duplicated.");
        if(r.SourceMode=="live"&&!r.Scopes.Any(s=>string.Equals(s,"Policy.Read.All",StringComparison.OrdinalIgnoreCase)||string.Equals(s,"https://graph.microsoft.com/Policy.Read.All",StringComparison.OrdinalIgnoreCase)))
            throw Bad("The recorded live evaluation is missing its required Policy.Read.All scope. Imported scope text grants no access.");
        if(r.ToolkitVersion is not {Length:>0 and <=64}||r.ToolkitVersion.Any(char.IsControl))throw Bad("The recorded toolkit version is missing or malformed.");
        var started=Time(r.StartedAt);var ended=Time(r.CompletedAt);
        if(ended<started||ended-started>TimeSpan.FromSeconds(120))throw Bad("The evaluation collection interval is inconsistent or exceeds its budget.");
        var prepared=ConditionalAccessScenarioCodec.Prepare(r.Scenario);
        if(prepared.Scenario!=r.Scenario||r.RequestSha256!=prepared.Sha256)throw Bad("The recorded scenario is not the exact canonical reviewed request.");
        if(response.Length!=r.ResponseBytes||r.ResponseSha256!=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(response)).ToLowerInvariant())throw new IntegrityException("The original evaluation response length or fingerprint differs from the recorded source.");
        var result=ConditionalAccessEvaluationResponseCodec.Read(response);
        if(r.Status!=(result.IsPartial?"Partial":"Complete"))throw Bad("The report cannot claim different completeness from its original response.");
        if(verifyDigest&&!EvidenceIntegrity.Verify(r,r.IntegrityDigest))throw new IntegrityException("The evaluation metadata fingerprint does not match its fields.");
    }
    private static DateTimeOffset Time(string value)
    {
        if(!DateTimeOffset.TryParseExact(value,"yyyy-MM-dd'T'HH:mm:ss.fff'Z'",CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var time))throw Bad("An evaluation timestamp is not the recorded UTC format.");
        return time;
    }
    private static ConfigurationException Bad(string message)=>new(message);
}
