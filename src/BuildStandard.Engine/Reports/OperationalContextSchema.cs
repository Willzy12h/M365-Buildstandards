using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;
namespace BuildStandard.Engine.Reports;
/// <summary>Explicit independent dispatcher. No operational record can be opened as a snapshot or approval.</summary>
public static partial class OperationalContextSchema
{
    internal static readonly JsonSerializerOptions CompactOptions = CompactEncoding();
    private static JsonSerializerOptions CompactEncoding()
    {
        var options = new JsonSerializerOptions(RegisteredReportJson.Strict) { WriteIndented = false };
        options.MakeReadOnly(); return options;
    }
    private static readonly HashSet<string> HealthStatuses=new(StringComparer.Ordinal)
    {"serviceOperational","investigating","restoringService","verifyingService","serviceRestored","postIncidentReviewPublished","serviceDegradation","serviceInterruption","extendedRecovery","falsePositive","investigationSuspended","resolved","mitigatedExternal","mitigated","resolvedExternal","confirmed","reported"};
    internal static bool IsKnownHealthStatus(string? value) => value is not null && HealthStatuses.Contains(value);
    public static void Seal(OperationalContextReport report)
    {Validate(report,report.TenantId,false);report.IntegrityDigest=EvidenceIntegrity.Compute(report);Validate(report,report.TenantId,true);}
    public static string Serialise(OperationalContextReport report)
    {Validate(report,report.TenantId,true);return JsonSerializer.Serialize(report,CompactOptions);}
    public static OperationalContextReport Read(string json,string tenant)
    {
        if(Encoding.UTF8.GetByteCount(json)>OperationalReportRegistry.MaximumBytes)throw Bad("Operational evidence exceeds16MiB.");
        using var document=RegisteredReportJson.Parse(json);
        if(!document.RootElement.TryGetProperty("sections",out var sections)||sections.ValueKind!=JsonValueKind.Array||sections.GetArrayLength()>(document.RootElement.TryGetProperty("reportId",out var reportId) && reportId.ValueKind==JsonValueKind.String && reportId.GetString()=="collaboration-inventory" ? 151 : 3))throw Bad("Operational sections are unavailable or over their bound.");
        var total=0;
        foreach(var section in sections.EnumerateArray())
        {
            if(section.ValueKind!=JsonValueKind.Object||!section.TryGetProperty("rows",out var rows)||rows.ValueKind!=JsonValueKind.Array)throw Bad("Operational rows must be arrays.");
            total=checked(total+rows.GetArrayLength());if(total>OperationalReportRegistry.MaximumRows)throw Bad("Operational rows exceed5000.");
            foreach(var row in rows.EnumerateArray())
            {
                if(row.ValueKind!=JsonValueKind.Object)throw Bad("Operational rows must be typed objects.");
                foreach(var field in row.EnumerateObject())
                    if(field.Value.ValueKind==JsonValueKind.Object||field.Value.ValueKind==JsonValueKind.Array&&field.Value.GetArrayLength()>OperationalReportRegistry.MaximumRoles)
                        throw Bad("Operational row contains unsupported nested data or an oversized list.");
            }
        }
        try
        {
            var report=JsonSerializer.Deserialize<OperationalContextReport>(json,RegisteredReportJson.Strict)??throw Bad("Empty operational evidence.");
            Validate(report,tenant,true);return report;
        }
        catch(JsonException){throw Bad("Invalid or unsupported operational evidence fields.");}
    }
    public static IReadOnlyList<ReportSource> SourceDeclarations(string reportId)=>reportId switch
    {
        "service-health"=>new[]{Source("/admin/serviceAnnouncement/healthOverviews","serviceannouncement-list-healthoverviews"),Source("/admin/serviceAnnouncement/issues","serviceannouncement-list-issues")},
        "partner-relationships"=>new[]{Source("/tenantRelationships/delegatedAdminRelationships","tenantrelationship-list-delegatedadminrelationships"),Source("/tenantRelationships/delegatedAdminRelationships/{returnedRelationshipId}/accessAssignments","delegatedadminrelationship-list-accessassignments")},
        "collaboration-inventory"=>new[]{Source("/sites?search={boundedSearch}","site-search"),Source("/sites/{selectedSiteId}","site-get"),Source("/sites/{selectedSiteId}/drives","drive-list"),Source("/groups/{selectedGroupId}","group-get"),Source("/groups/{selectedGroupId}/owners","group-list-owners"),Source("/teams/{selectedGroupId}/members","team-list-members")},
        _=>throw Bad("Operational report source is not implemented yet.")
    };
    private static ReportSource Source(string path,string reference)=>new(){Api="v1.0",RegisteredRoute=path,Reference="https://learn.microsoft.com/en-us/graph/api/"+reference+"?view=graph-rest-1.0"};
    private static void Validate(OperationalContextReport report,string tenant,bool integrity)
    {
        if(report.SchemaVersion!=1||report.Kind!="operationalContextReport"||report.AdapterVersion!="1.0"||!ReportValues.IsCanonicalGuid(report.Id)||!ReportValues.IsCanonicalGuid(report.TenantId))throw Bad("Unsupported operational identity/version.");
        if(!ReportValues.IsCanonicalGuid(tenant)||tenant!=report.TenantId)throw new TenantMismatchException("Operational evidence belongs to another source tenant.");
        if(report.ReportId is not ("service-health" or "partner-relationships" or "collaboration-inventory"))throw Bad("Operational report is not implemented yet.");
        if(report.AccessIntent!=report.ReportId||report.PermissionMode is not ("Assessment" or "Deployment"))throw Bad("Unsupported original access intent/mode.");
        if(report.SourceMode is not ("live" or "historical")||report.SourceMode=="live"&&(!ReportValues.IsCanonicalGuid(report.AccountObjectId)||!ReportValues.IsCanonicalGuid(report.ClientId))
            ||report.AccountObjectId is not null&&!ReportValues.IsCanonicalGuid(report.AccountObjectId)||report.ClientId is not null&&!ReportValues.IsCanonicalGuid(report.ClientId))throw Bad("Unsupported operational actor provenance.");
        if(!Utc(report.AsOfUtc,out var asOf)||!Utc(report.StartedAt,out var start)||!Utc(report.EndedAt,out var end)||asOf!=start||end<start)throw Bad("Invalid operational collection/as-of interval.");
        if(string.IsNullOrWhiteSpace(report.ToolkitVersion)||report.ToolkitVersion.Length>128||report.ToolkitVersion.Any(char.IsControl))throw Bad("Missing operational toolkit provenance.");
        if(report.ReportId=="collaboration-inventory")
        { ValidateCollaboration(report); ValidateFinalEnvelope(report,integrity); return; }
        if(report.CollaborationSelection is not null)throw Bad("Collaboration selection cannot be attached to another report family.");
        if(report.ReportId=="partner-relationships")
        {
            ValidatePartner(report,asOf);
            ValidateFinalEnvelope(report,integrity);
            return;
        }
        if(report.CustomerTenantId is not null||report.PartnerCoverage is not null)throw Bad("Health evidence cannot claim selected-customer or partner coverage.");
        var expected=SourceDeclarations(report.ReportId);
        if(report.Sources is null||report.Sources.Count!=expected.Count||report.Sources.Where((source,index)=>source is null||source.Api!=expected[index].Api||source.RegisteredRoute!=expected[index].RegisteredRoute||source.Reference!=expected[index].Reference).Any())throw Bad("Operational source declarations differ from the registered surface.");
        if(report.Sections is null||report.Sections.Count!=2||report.Sections[0]?.Id!="health-overviews"||report.Sections[1]?.Id!="health-issues")throw Bad("Unsupported operational section order.");
        var total=0;
        foreach(var section in report.Sections)
        {
            RequireState(section.Status,section.Error);if(section.Rows is null||section.Limitations is null)throw Bad("Missing operational section data.");
            total=checked(total+section.Rows.Count);if(total>OperationalReportRegistry.MaximumRows)throw Bad("Operational rows exceed5000.");
            if(section.Status==ReportReadState.NotAttempted&&section.Rows.Count!=0)throw Bad("An unattempted source cannot contain observations.");
            foreach(var limitation in section.Limitations)Text(limitation);
            var rows=new List<GraphReportRow>();
            foreach(var raw in section.Rows)
            {
                if(raw is null)throw Bad("Null operational row.");
                foreach(var pair in raw){if(pair.Value is JsonArray array&&array.Count>OperationalReportRegistry.MaximumRoles)throw Bad("Operational list bound exceeded.");if(pair.Value is JsonObject)throw Bad("Unsupported nested operational data.");}
                GraphReportRow row;
                try{row=section.Id=="health-overviews"?raw.Deserialize<ServiceHealthOverviewRow>(RegisteredReportJson.Strict)!:raw.Deserialize<ServiceHealthIssueRow>(RegisteredReportJson.Strict)!;}
                catch(JsonException){throw Bad("Operational row does not match its registered type.");}
                RequireState(row.ReadStatus,row.Error);
                if(row.ReadStatus is not (ReportReadState.Collected or ReportReadState.Partial))throw Bad("A returned service object must be an observed complete or partial row.");
                Text(row.Name);Text(row.Error);
                if(string.IsNullOrWhiteSpace(row.Name)&&row.ReadStatus!=ReportReadState.Partial)throw Bad("Missing service name/title must remain partial/unknown.");
                if(row.Id is not null&&!Identity(row.Id))throw Bad("Malformed original service identity.");
                if(row.Id is null&&row.ReadStatus!=ReportReadState.Partial)throw Bad("Missing identity must remain partial/unknown.");
                var status=row is ServiceHealthOverviewRow overview?overview.Status:((ServiceHealthIssueRow)row).Status;
                Text(status);
                if((status is null||!HealthStatuses.Contains(status))&&row.ReadStatus!=ReportReadState.Partial)throw Bad("An unknown service status cannot become a complete healthy result.");
                if(row is ServiceHealthIssueRow issue)
                {
                    foreach(var text in new[]{issue.Service,issue.Feature,issue.Classification,issue.ImpactSummary})Text(text);
                    if((string.IsNullOrWhiteSpace(issue.Service)||issue.Classification is not ("advisory" or "incident"))&&row.ReadStatus!=ReportReadState.Partial)throw Bad("Missing service or unknown issue classification must remain partial.");
                    foreach(var time in new[]{issue.StartedAt,issue.EndedAt,issue.ModifiedAt})if(time is not null&&!Utc(time,out _))throw Bad("Invalid reported issue time.");
                }
                rows.Add(row);
            }
            foreach(var duplicate in rows.Where(row=>row.Id is not null).GroupBy(row=>row.Id,StringComparer.Ordinal).Where(group=>group.Count()>1))
                if(duplicate.Any(row=>row.ReadStatus!=ReportReadState.Partial))throw Bad("Duplicate identities must remain explicitly partial.");
            if(section.Status==ReportReadState.Collected&&rows.Any(row=>row.ReadStatus!=ReportReadState.Collected))throw Bad("Section status conceals an incomplete observation.");
        }
        ValidateFinalEnvelope(report,integrity);
    }
    private static void ValidateFinalEnvelope(OperationalContextReport report,bool integrity)
    {
        if(report.Status!=ReportEvidenceSchema.Overall(report.Sections.Select(section=>section.Status)))throw Bad("Operational overall status conceals a source outcome.");
        using(var budget=new BudgetStream())JsonSerializer.Serialize(budget,report,CompactOptions);
        if(integrity&&!EvidenceIntegrity.Verify(report,report.IntegrityDigest))throw new IntegrityException("Operational evidence digest differs.");
    }
    private static bool Identity(string value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=512&&!value.Any(char.IsControl);
    private static void Text(string? value){if(value is not null&&(value.Length>4096||value.Any(c=>char.IsControl(c)&&c is not ('\r' or '\n' or '\t'))))throw Bad("Unsupported operational text shape.");}
    private static void RequireState(string state,string? error)
    {if(!ReportReadState.All.Contains(state,StringComparer.Ordinal)||(state==ReportReadState.Collected?error is not null:string.IsNullOrWhiteSpace(error)))throw Bad("Unknown/inconsistent operational read state.");Text(error);}
    private static bool Utc(string? text,out DateTimeOffset value)=>DateTimeOffset.TryParseExact(text,["yyyy-MM-dd'T'HH:mm:ss'Z'","yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out value);
    private static ConfigurationException Bad(string text)=>new(text);
    private sealed class BudgetStream:Stream
    {
        private long _count;public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;public override long Length=>_count;public override long Position{get=>_count;set=>throw new NotSupportedException();}
        public override void Write(byte[] buffer,int offset,int count)=>Write(buffer.AsSpan(offset,count));
        public override void Write(ReadOnlySpan<byte> buffer){_count=checked(_count+buffer.Length);if(_count>OperationalReportRegistry.MaximumBytes)throw Bad("Operational evidence exceeds16MiB.");}
        public override void Flush(){}public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
    }
}
