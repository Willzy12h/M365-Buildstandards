using System.Globalization;
using System.Text;
using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Exchange;
namespace BuildStandard.Engine.Reports;
/// <summary>Strict original-data pack. Integrity is consistency, never source authentication or permission to join identities.</summary>
public static class UsageReviewSchema
{
    public const int MaximumBytes=40*1024*1024;
    // Size-only staging guard; this is not schema/integrity admission and grants no execution/evidence authority.
    internal static void RequireCandidateSize(UsageReviewReport report)=>RequireSize(report,MaximumBytes-64*1024);
    public static string Serialise(UsageReviewReport report)
    { Validate(report,report.TenantId,true);return JsonSerializer.Serialize(report,RegisteredReportJson.Strict); }
    public static UsageReviewReport Read(string json,string tenant)
    {
        if(Encoding.UTF8.GetByteCount(json)>MaximumBytes)throw Bad("The usage pack exceeds40MiB; it was not admitted.");
        try
        {
            using var document=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=48});
            ExchangeCaptureSchema.RejectDuplicates(document.RootElement,StringComparer.Ordinal);
            var report=JsonSerializer.Deserialize<UsageReviewReport>(json,RegisteredReportJson.Strict)??throw Bad("Empty usage pack.");
            Validate(report,tenant,true);return report;
        }
        catch(JsonException){throw Bad("Invalid or unknown usage pack fields.");}
    }
    public static void Seal(UsageReviewReport report)
    {
        Validate(report,report.TenantId,false);report.IntegrityDigest=EvidenceIntegrity.Compute(report);RequireSize(report);
    }
    public static IReadOnlyList<UsageCsvResult> Project(UsageReviewReport report)=>Validate(report,report.TenantId,true);
    private static IReadOnlyList<UsageCsvResult> Validate(UsageReviewReport report,string tenant,bool integrity)
    {
        if(report.SchemaVersion!=1||report.Kind!="usageReviewReport"||!ReportValues.IsCanonicalGuid(report.Id)||!ReportValues.IsCanonicalGuid(report.TenantId))throw Bad("Unsupported usage pack identity/version.");
        if(!ReportValues.IsCanonicalGuid(tenant)||tenant!=report.TenantId)throw new TenantMismatchException("Usage report belongs to a different tenant.");
        if(report.SourceMode is not ("live" or "historical")||report.SourceMode=="live"&&!ReportValues.IsCanonicalGuid(report.AccountObjectId)
            ||report.AccountObjectId is not null&&!ReportValues.IsCanonicalGuid(report.AccountObjectId))throw Bad("Unsupported usage report account/source provenance.");
        if(!Utc(report.StartedAt,out var start)||!Utc(report.EndedAt,out var end)||end<start)throw Bad("Invalid usage collection interval.");
        if(string.IsNullOrWhiteSpace(report.ToolkitVersion)||report.ToolkitVersion.Length>128||report.ToolkitVersion.Any(char.IsControl))throw Bad("Missing usage toolkit provenance.");
        if(report.Sources is null||report.Sources.Count is <1 or >4||report.Sources.Any(s=>s is null)
            ||report.Sources.Select(s=>s.SourceId).Distinct(StringComparer.Ordinal).Count()!=report.Sources.Count)throw Bad("Missing/duplicate usage sources.");
        var projected=new List<UsageCsvResult>();
        foreach(var source in report.Sources)
        {
            _=UsageReportRegistry.Find(source.SourceId).Route(source.Period);
            if(!ReportReadState.All.Contains(source.Status,StringComparer.Ordinal)||source.PrivacyProvenance!="Unknown")throw Bad("Unverified privacy provenance or read status cannot establish a licence/identity join.");
            if(source.Status==ReportReadState.Collected ? source.Reason is not null : string.IsNullOrWhiteSpace(source.Reason))throw Bad("Incomplete usage sources need a reason; a successful source cannot hide an error.");
            if(source.Reason is not null&&(source.Reason.Length>1024||source.Reason.Any(char.IsControl)||source.Reason.Contains("reports.office.com",StringComparison.OrdinalIgnoreCase)))throw Bad("Usage diagnostics cannot retain download addresses.");
            if(source.DownloadHost is not (null or "reports.office.com"))throw Bad("Only the registered download host, never a secret-bearing URL, may be retained.");
            if(source.OriginalCsvBase64 is null)
            { if(source.CsvSha256 is not null||source.Status==ReportReadState.Collected)throw Bad("Successful usage reads require original CSV bytes and digest.");continue; }
            if(source.Status is not (ReportReadState.Collected or ReportReadState.Partial)||source.OriginalCsvBase64.Length>4*((UsageCsv.MaximumBytes+2)/3))throw Bad("Unsuccessful/oversized usage CSV cannot become a complete empty source.");
            if(report.SourceMode=="live"&&source.DownloadHost!="reports.office.com")throw Bad("Live CSV provenance needs the registered download host.");
            byte[] csv;
            try{csv=Convert.FromBase64String(source.OriginalCsvBase64);}catch(FormatException){throw Bad("Invalid original CSV encoding.");}
            if(Convert.ToBase64String(csv)!=source.OriginalCsvBase64)throw Bad("Original CSV encoding must be canonical.");
            var parsed=UsageCsv.Parse(source.SourceId,source.Period,csv);
            if(source.CsvSha256!=parsed.Sha256)throw Bad("Original usage CSV failed its exact digest check.");
            projected.Add(parsed);
        }
        if(report.Status!=ReportEvidenceSchema.Overall(report.Sources.Select(s=>s.Status)))throw Bad("Overall usage status conceals a source outcome.");
        RequireSize(report);
        if(integrity&&(report.IntegrityDigest is not {Length:64}||report.IntegrityDigest.Any(c=>c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))||!EvidenceIntegrity.Verify(report,report.IntegrityDigest)))throw Bad("Usage pack failed its integrity check; digests do not authenticate source claims.");
        return projected.AsReadOnly();
    }
    private static bool Utc(string? text,out DateTimeOffset value)=>DateTimeOffset.TryParseExact(text,
        ["yyyy-MM-dd'T'HH:mm:ss'Z'","yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out value);
    private static void RequireSize(UsageReviewReport report,int maximum=MaximumBytes)
    {using var size=new SizeOnlyStream(maximum);JsonSerializer.Serialize(size,report,RegisteredReportJson.Strict);}
    private sealed class SizeOnlyStream(int maximum):Stream
    {
        private long _count;
        public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;
        public override long Length=>_count;public override long Position{get=>_count;set=>throw new NotSupportedException();}
        public override void Write(byte[] buffer,int offset,int count)=>Write(buffer.AsSpan(offset,count));
        public override void Write(ReadOnlySpan<byte> buffer){if(buffer.Length>maximum-_count)throw Bad("The usage pack exceeds40MiB; original bytes were not saved or clipped.");_count+=buffer.Length;}
        public override void Flush(){}public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
    }
    private static ConfigurationException Bad(string message)=>new(message);
}
