using System.Text.Json.Serialization;
using System.Text;
using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Evidence;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;

public sealed class CoverageReportScope
{
    [JsonRequired] public string ControlId {get;set;}="";
    [JsonRequired] public string ControlName {get;set;}="";
    [JsonRequired] public string ControlDigest {get;set;}="";
    [JsonRequired] public string ObjectId {get;set;}="";
    [JsonRequired] public string Collection {get;set;}="";
    [JsonRequired] public CoverageFamily Family {get;set;}
    [JsonRequired] public string SourceSnapshotId {get;set;}="";
    [JsonRequired] public string SourceSnapshotDigest {get;set;}="";
    [JsonRequired] public string SourceCapturedAt {get;set;}="";
    [JsonRequired] public string SourceRunId {get;set;}="";
    [JsonRequired] public string SourceRunDigest {get;set;}="";
    [JsonRequired] public string StandardRelease {get;set;}="";
    [JsonRequired] public string StandardDigest {get;set;}="";
    [JsonRequired] public List<string> PilotUserObjectIds {get;set;}=[];
    [JsonRequired] public List<string> PilotDirectoryDeviceObjectIds {get;set;}=[];
}
public sealed class CoverageReportPage
{
    [JsonRequired] public string File {get;set;}="";
    [JsonRequired] public string Sha256 {get;set;}="";
    [JsonRequired] public int Bytes {get;set;}
}
public sealed class CoverageReportSource
{
    [JsonRequired] public string Key {get;set;}="";
    [JsonRequired] public string Path {get;set;}="";
    [JsonRequired] public string Scope {get;set;}="";
    [JsonRequired] public string StartedAt {get;set;}="";
    [JsonRequired] public string CompletedAt {get;set;}="";
    [JsonRequired] public CoverageSourceState State {get;set;}
    [JsonRequired] public string Detail {get;set;}="";
    [JsonRequired] public List<string> RetainedRowIds {get;set;}=[];
    [JsonRequired] public List<CoverageReportPage> Pages {get;set;}=[];
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public CoverageModernExportRecord? ModernExport {get;set;}
}
public sealed class DeploymentCoverageReport
{
    [JsonRequired] public string Kind {get;set;}="deploymentCoverageReport";
    [JsonRequired] public int SchemaVersion {get;set;}=1;
    [JsonRequired] public string AdapterVersion {get;set;}="deployment-coverage/1";
    [JsonRequired] public string ToolkitVersion {get;set;}="";
    [JsonRequired] public string SourceMode {get;set;}="live";
    [JsonRequired] public string Id {get;set;}="";
    [JsonRequired] public string TenantId {get;set;}="";
    [JsonRequired] public string AccountObjectId {get;set;}="";
    [JsonRequired] public string ClientId {get;set;}="";
    [JsonRequired] public string Resource {get;set;}="https://graph.microsoft.com/";
    [JsonRequired] public SessionMode PermissionMode {get;set;}
    [JsonRequired] public List<string> Scopes {get;set;}=[];
    [JsonRequired] public string StartedAt {get;set;}="";
    [JsonRequired] public string CompletedAt {get;set;}="";
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public bool? WasCurrentAtCompletion {get;set;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public bool? SupportedReadsFinished {get;set;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public string? AnalysisLimitations {get;set;}
    [JsonRequired] public CoverageReportScope Selection {get;set;}=null!;
    [JsonRequired] public List<CoverageReportSource> Sources {get;set;}=[];
    [JsonRequired] public string IntegrityDigest {get;set;}="";
}

/// <summary>Original report bytes. Historical import has no conversion to an authorised collection selection or plan.</summary>
public sealed class DeploymentCoverageEvidence
{
    public const int MaximumBytes=5*1024*1024;
    private readonly byte[] _metadata;
    private readonly Dictionary<string,byte[]> _pages;
    public byte[] MetadataBytes=>(byte[])_metadata.Clone();
    public DeploymentCoverageReport Metadata=>JsonSerializer.Deserialize<DeploymentCoverageReport>(_metadata,RegisteredReportJson.Strict)!;
    public IReadOnlyDictionary<string,byte[]> OriginalPages=>new System.Collections.ObjectModel.ReadOnlyDictionary<string,byte[]>(_pages.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone(),StringComparer.Ordinal));
    public bool IsHistorical {get;}
    private DeploymentCoverageEvidence(byte[] metadata,IReadOnlyDictionary<string,byte[]> pages,bool historical)
    {_metadata=(byte[])metadata.Clone();_pages=pages.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone(),StringComparer.Ordinal);IsHistorical=historical;}
    public static DeploymentCoverageEvidence Create(DeploymentCoverageCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);var s=capture.Selection;
        var report=new DeploymentCoverageReport{ToolkitVersion=BuildStandard.Engine.ToolkitVersion.Current,Id=capture.Id,TenantId=s.TenantId,AccountObjectId=s.AccountObjectId,ClientId=s.ClientId,
            PermissionMode=s.PermissionMode,Scopes=s.Scopes.ToList(),WasCurrentAtCompletion=capture.IsCurrentAtCompletion,SupportedReadsFinished=capture.SupportedReadsComplete,AnalysisLimitations=capture.Limitations,StartedAt=capture.Sources.Min(s=>s.StartedAt)!,CompletedAt=capture.Sources.Max(s=>s.CompletedAt)!,
            Selection=new(){ControlId=s.ControlId,ControlName=s.ControlName,ControlDigest=s.ControlDigest,ObjectId=s.ObjectId,Collection=s.Collection,Family=s.Family,
                SourceSnapshotId=s.SourceSnapshotId,SourceSnapshotDigest=s.SourceSnapshotDigest,SourceCapturedAt=s.SourceCapturedAt,SourceRunId=s.SourceRunId,SourceRunDigest=s.SourceRunDigest,
                StandardRelease=s.StandardRelease,StandardDigest=s.StandardDigest,PilotUserObjectIds=s.PilotUserObjectIds.ToList(),PilotDirectoryDeviceObjectIds=s.PilotDirectoryDeviceObjectIds.ToList()}};
        var pages=new Dictionary<string,byte[]>(StringComparer.Ordinal);var index=0;
        foreach(var source in capture.Sources)
        {
            var entry=new CoverageReportSource{Key=source.Key,Path=source.Path,Scope=source.Scope,StartedAt=source.StartedAt,CompletedAt=source.CompletedAt,
                State=source.Capture.State,Detail=source.Capture.Detail,RetainedRowIds=source.Capture.Rows.Select(r=>r["id"]!.GetValue<string>()).ToList()};
            foreach(var bytes in source.Capture.OriginalPages)
            {var name=$"page-{index++:D3}.json";pages.Add(name,bytes);entry.Pages.Add(new(){File=name,Bytes=bytes.Length,Sha256=CanonicalJson.Sha256Hex(bytes)});}
            if(source.ModernExport is { } modern)
            {
                if(modern.TenantId!=s.TenantId||modern.AccountObjectId!=s.AccountObjectId||modern.ClientId!=s.ClientId)throw new TenantMismatchException("The modern original source belongs to another recorded actor/client.");
                var archive=modern.Archive.OriginalArchiveBytes;const string file="export-000.zip";
                if(!pages.TryAdd(file,archive))throw Bad("Only the selected control's one modern service-results export is registered.");
                entry.ModernExport=new(){ReportName=modern.Request.ReportName,RequestSha256=modern.Request.Sha256,JobId=modern.JobId,ArchiveFile=file,
                    ArchiveBytes=archive.Length,ArchiveSha256=modern.Archive.OriginalArchiveSha256,CsvBytes=modern.Archive.Table.OriginalBytes.Length,CsvSha256=modern.Archive.Table.OriginalSha256,SourceRows=modern.Archive.Table.Rows.Count};
            }
            report.Sources.Add(entry);
        }
        report.IntegrityDigest=EvidenceIntegrity.Compute(report);
        var metadata=JsonSerializer.SerializeToUtf8Bytes(report,RegisteredReportJson.Strict);
        Validate(metadata,pages,s.TenantId);
        return new(metadata,pages,false);
    }
    public static DeploymentCoverageEvidence Read(byte[] metadata,IReadOnlyDictionary<string,byte[]> pages,string expectedTenantId)
    {Validate(metadata,pages,expectedTenantId);return new(metadata,pages,true);}
    /// <summary>Re-project only recorded admitted rows from immutable originals; never recapture or infer missing rows.</summary>
    public IReadOnlyList<CoverageCapturedSource> Sources()
    {
        var report=Metadata;var sources=new List<CoverageCapturedSource>();
        foreach(var source in report.Sources)
        {
            var request=Request(report.Selection,source.Key,new HashSet<string>(StringComparer.Ordinal));
            var rows=new Dictionary<string,System.Text.Json.Nodes.JsonObject>(StringComparer.Ordinal);
            foreach(var page in source.Pages)
                foreach(var row in CoverageResponseCodec.Read(_pages[page.File],request!).Rows)rows.TryAdd(row["id"]!.GetValue<string>(),row);
            sources.Add(new(source.Key,source.Path,source.Scope,source.StartedAt,source.CompletedAt,
                new(source.State,source.RetainedRowIds.Select(id=>rows[id]),source.Pages.Select(p=>_pages[p.File]),source.Detail,originalDetail:true))
                {ModernExport=source.ModernExport is { } modern?IntuneExportObservation.ReadHistorical(report,source,_pages[modern.ArchiveFile]):null});
        }
        return sources.AsReadOnly();
    }
    private static void Validate(byte[] metadata,IReadOnlyDictionary<string,byte[]> pages,string expectedTenantId)
    {
        ArgumentNullException.ThrowIfNull(metadata);ArgumentNullException.ThrowIfNull(pages);
        if(metadata.Length is 0 or >MaximumBytes||pages.Count>100)throw Bad("Metadata/page count exceeds the report limit.");
        long length=metadata.Length;
        foreach(var bytes in pages.Values){if(bytes is null||bytes.Length==0)throw Bad("An original page is missing.");length+=bytes.Length;if(length>MaximumBytes)throw Bad("Metadata plus original pages exceeds 5 MiB; NOT SAVED, no smaller complete report is admitted.");}
        try
        {
            var json=new UTF8Encoding(false,true).GetString(metadata);using var document=RegisteredReportJson.Parse(json);
            var r=JsonSerializer.Deserialize<DeploymentCoverageReport>(json,RegisteredReportJson.Strict)??throw Bad("Missing report metadata.");
            if(r.Kind!="deploymentCoverageReport"||r.SchemaVersion!=1||r.AdapterVersion!="deployment-coverage/1"||r.Resource!="https://graph.microsoft.com/")throw Bad("Unsupported report identity/version/resource.");
            Text(r.ToolkitVersion,64);if(r.SourceMode is not ("live" or "historical"))throw Bad("Unsupported original source mode.");
            foreach(var id in new[]{r.Id,r.TenantId,r.AccountObjectId,r.ClientId})GuidId(id);
            if(!ReportValues.TryGuid(expectedTenantId,out var tenant)||r.TenantId!=tenant)throw new TenantMismatchException("Coverage originals belong to a different recorded tenant; no result is admitted for this client.");
            ExactEnum(document.RootElement.GetProperty("permissionMode"),r.PermissionMode);
            if(r.Scopes is not {Count:>0 and <=128}||r.Scopes.Any(s=>s is null||s.Length is 0 or >256||s.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)))||r.Scopes.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=r.Scopes.Count)throw Bad("Malformed original scope inventory.");
            var start=Time(r.StartedAt);var end=Time(r.CompletedAt);if(end<start||end-start>TimeSpan.FromMinutes(5))throw Bad("The original collection interval is outside its budget.");
            var scope=r.Selection??throw Bad("Missing original selection.");
            ExactEnum(document.RootElement.GetProperty("selection").GetProperty("family"),scope.Family);
            foreach(var id in new[]{scope.ObjectId,scope.SourceSnapshotId,scope.SourceRunId})GuidId(id);
            foreach(var digest in new[]{scope.ControlDigest,scope.SourceSnapshotDigest,scope.SourceRunDigest,scope.StandardDigest,r.IntegrityDigest})Digest(digest);
            Text(scope.ControlId,128);Text(scope.ControlName,1024);Text(scope.StandardRelease,128);
            if(!DateTimeOffset.TryParseExact(scope.SourceCapturedAt,["yyyy-MM-dd'T'HH:mm:ss'Z'","yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'","yyyy-MM-dd'T'HH:mm:sszzz","yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"],System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal,out _))throw Bad("The original source capture timestamp is malformed.");
            if(scope.Collection!=(scope.Family switch{CoverageFamily.ConditionalAccess=>"conditionalAccess",CoverageFamily.Application=>"apps",CoverageFamily.ClassicCompliance=>"compliance",_=>"configuration"}))throw Bad("The original collection/family binding differs.");
            Pilot(scope.PilotUserObjectIds);Pilot(scope.PilotDirectoryDeviceObjectIds);
            if(scope.PilotUserObjectIds.Count+scope.PilotDirectoryDeviceObjectIds.Count>5000)throw Bad("Pilot scope exceeds the report bound.");
            if(r.Sources is not {Count:>0 and <=67}||r.Sources.Any(s=>s is null)||r.Sources.Select(s=>s.Key).Distinct(StringComparer.Ordinal).Count()!=r.Sources.Count)throw Bad("Missing/duplicated source identities.");
            if(!r.Sources.Any(s=>s.Key=="object")||!r.Sources.Any(s=>s.Key=="service-results")||scope.Family!=CoverageFamily.ConditionalAccess&&!r.Sources.Any(s=>s.Key=="assignments"))throw Bad("A required read-state entry is absent; omission is not a successful empty read.");
            if(r.AnalysisLimitations is not null)Text(r.AnalysisLimitations,16384);
            if(r.SupportedReadsFinished==true&&(r.WasCurrentAtCompletion!=true||r.Sources.Any(s=>s.State!=CoverageSourceState.Complete)))throw Bad("The recorded overall read completion contradicts original context/source states.");
            var pageNumber=0;var rowCount=0;var returnedRows=0;var usedFiles=new HashSet<string>(StringComparer.Ordinal);var groups=new HashSet<string>(StringComparer.Ordinal);
            for(var sourceIndex=0;sourceIndex<r.Sources.Count;sourceIndex++)
            {
                var source=r.Sources[sourceIndex];Text(source.Key,128);if(source.Path is null||source.Scope is null)throw Bad("Original source path/scope cannot be null.");ExactEnum(document.RootElement.GetProperty("sources")[sourceIndex].GetProperty("state"),source.State);
                Text(source.Detail,4096);var from=Time(source.StartedAt);var to=Time(source.CompletedAt);
                if(from<start||to<from||to>end)throw Bad("A source interval is outside the original report interval.");
                var request=Request(scope,source.Key,groups);
                if(source.Pages is null||source.RetainedRowIds is null||source.RetainedRowIds.Count>5000||source.RetainedRowIds.Any(id=>string.IsNullOrEmpty(id))||source.RetainedRowIds.Distinct(StringComparer.Ordinal).Count()!=source.RetainedRowIds.Count)throw Bad("Malformed retained-row/page inventory.");
                if(source.ModernExport is { } modern)
                {
                    var prepared=IntuneExportRequest.Prepare(scope.Family,scope.ObjectId);
                    if(source.Key!="service-results"||source.Path!=prepared.Path||source.Scope!=prepared.RequiredScope||source.Pages.Count!=0||source.RetainedRowIds.Count!=0||source.State is not (CoverageSourceState.Complete or CoverageSourceState.ContextChanged or CoverageSourceState.Partial or CoverageSourceState.Stopped))throw Bad("Modern source registration/state differs; CSV rows cannot be relabelled as JSON or directory identities.");
                    if(r.PermissionMode!=SessionMode.Deployment||!r.Scopes.Any(s=>string.Equals(s,prepared.RequiredScope,StringComparison.OrdinalIgnoreCase)||string.Equals(s,"https://graph.microsoft.com/"+prepared.RequiredScope,StringComparison.OrdinalIgnoreCase)))throw Bad("Modern original access/purpose is incompatible with its fixed report scope.");
                    if(modern.AdapterVersion!="intune-export-csv/1"||modern.ReportName!=prepared.ReportName||modern.RequestSha256!=prepared.Sha256||modern.ArchiveFile!="export-000.zip"||!usedFiles.Add(modern.ArchiveFile)||!pages.TryGetValue(modern.ArchiveFile,out var archive))throw Bad("Unsupported modern adapter/report/request or missing original archive.");
                    IntuneExportObservation.ValidateJob(modern.JobId,source.StartedAt,source.CompletedAt);
                    if(archive.Length!=modern.ArchiveBytes||CanonicalJson.Sha256Hex(archive)!=modern.ArchiveSha256)throw new IntegrityException("Original modern archive differs from the recorded bytes/fingerprint.");
                    var admitted=IntuneExportArchive.Read(archive,prepared);
                    if(admitted.Table.OriginalBytes.Length!=modern.CsvBytes||admitted.Table.OriginalSha256!=modern.CsvSha256||admitted.Table.Rows.Count!=modern.SourceRows)throw new IntegrityException("The original modern CSV fingerprint, length or raw row inventory differs.");
                    returnedRows+=admitted.Table.Rows.Count;if(returnedRows>5000)throw Bad("Original returned JSON/CSV rows exceed 5,000; NOT SAVED, not a smaller complete report.");
                    continue;
                }
                if(source.Key=="service-results"&&scope.Family is (CoverageFamily.Application or CoverageFamily.ClassicConfiguration)&&source.Path.Length>0)
                {
                    var prepared=IntuneExportRequest.Prepare(scope.Family,scope.ObjectId);
                    if(source.Path!=prepared.Path||source.Scope!=prepared.RequiredScope||source.Pages.Count!=0||source.RetainedRowIds.Count!=0||source.State is not (CoverageSourceState.Failed or CoverageSourceState.Stopped or CoverageSourceState.ContextChanged or CoverageSourceState.NotAttempted))throw Bad("An unavailable modern source cannot claim complete/empty results without its original archive.");
                    continue;
                }
                if(source.Path.Length==0&&source.Scope.Length==0)
                {if(source.Pages.Count!=0||source.RetainedRowIds.Count!=0||source.State==CoverageSourceState.Complete)throw Bad("An unread source cannot contain pages or claim complete.");continue;}
                if(request is null||source.Path!=request.Path||source.Scope!=request.Scope)throw Bad("A source path/scope differs from its fixed registered relation.");
                var available=new HashSet<string>(StringComparer.Ordinal);var identities=new HashSet<string>(StringComparer.Ordinal);var allRows=new List<string>();var hasMore=false;var duplicate=false;var pageRequest=request;var pageIndex=0;var paths=new HashSet<string>(StringComparer.Ordinal){request.Path};
                foreach(var page in source.Pages)
                {
                    if(page is null||page.File!=$"page-{pageNumber++:D3}.json"||!usedFiles.Add(page.File)||!pages.TryGetValue(page.File,out var bytes))throw Bad("Original page files are missing, reordered or outside the generated inventory.");
                    if(bytes.Length!=page.Bytes||CanonicalJson.Sha256Hex(bytes)!=page.Sha256)throw new IntegrityException("Coverage original page bytes differ from the recorded length/fingerprint.");
                    var decoded=CoverageResponseCodec.Read(bytes,pageRequest);hasMore=decoded.HasMore;
                    returnedRows+=decoded.Rows.Count;if(returnedRows>5000)throw Bad("Original returned rows exceed the 5,000-row capture limit; NOT SAVED rather than a smaller complete report.");
                    foreach(var row in decoded.Rows){var id=row["id"]!.GetValue<string>();var identity=request.IsGroupMembers&&ReportValues.TryGuid(id,out var canonical)?canonical:id;if(!identities.Add(identity))duplicate=true;else{available.Add(id);allRows.Add(id);}}
                    if(++pageIndex<source.Pages.Count)
                    {
                        if(!hasMore)throw Bad("An extra original page has no preceding continuation declaration.");
                        using var original=JsonDocument.Parse(bytes);
                        pageRequest=DeploymentCoverageReads.Continue(pageRequest,original.RootElement.GetProperty("@odata.nextLink").GetString()!);
                        if(!paths.Add(pageRequest.Path))throw Bad("Original pages claim a continuation loop; no complete source is admitted.");
                    }
                }
                if(source.RetainedRowIds.Any(id=>!available.Contains(id)))throw Bad("An admitted row does not exist in the original source pages.");
                if(source.State==CoverageSourceState.Complete&&(source.Pages.Count==0||hasMore||duplicate||!allRows.SequenceEqual(source.RetainedRowIds)))throw Bad("A complete source cannot omit rows, contain duplicates or declare an unread further page.");
                rowCount+=source.RetainedRowIds.Count;if(rowCount>5000)throw Bad("Retained rows exceed the capture-wide bound.");
            }
            if(usedFiles.Count!=pages.Count)throw Bad("Unexpected original source files cannot be silently dropped.");
            if(!EvidenceIntegrity.Verify(r,r.IntegrityDigest))throw new IntegrityException("Coverage metadata fingerprint differs from its original fields.");
        }
        catch(JsonException){throw Bad("Unsupported, missing or malformed report fields.");}
        catch(DecoderFallbackException){throw Bad("Metadata is not strict UTF-8.");}
    }
    private static PreparedCoverageRead? Request(CoverageReportScope scope,string key,HashSet<string> groups)
    {
        if(key=="object")return DeploymentCoverageReads.Prepare(scope.Family,scope.ObjectId,CoverageReadKind.Object);
        if(key=="assignments")return DeploymentCoverageReads.Prepare(scope.Family,scope.ObjectId,CoverageReadKind.Assignments);
        if(key=="service-results")return scope.Family==CoverageFamily.ClassicCompliance?DeploymentCoverageReads.Prepare(scope.Family,scope.ObjectId,CoverageReadKind.ServiceResults):null;
        var parts=key.Split(':');if(parts.Length!=3||parts[0]!="group"||parts[2] is not ("metadata" or "members"))throw Bad("Unregistered source identity.");
        GuidId(parts[1]);groups.Add(parts[1]);if(groups.Count>32)throw Bad("More than 32 assignment groups.");return DeploymentCoverageReads.Group(parts[1],parts[2]=="members");
    }
    private static void GuidId(string id){if(!ReportValues.IsCanonicalGuid(id))throw Bad("Original identity is not a canonical non-empty GUID.");}
    private static void Digest(string value){if(value is not {Length:64}||value.Any(c=>!char.IsAsciiHexDigitLower(c)&&!char.IsAsciiDigit(c)))throw Bad("Original fingerprint is not a lowercase SHA-256.");}
    private static void Text(string value,int maximum){if(string.IsNullOrWhiteSpace(value)||value.Length>maximum||value.Any(char.IsControl))throw Bad("A required original text field is empty or malformed.");}
    private static void Pilot(List<string> ids){if(ids is null||ids.Count>5000||ids.Distinct(StringComparer.Ordinal).Count()!=ids.Count)throw Bad("Pilot scope is missing, duplicated or oversized.");foreach(var id in ids)GuidId(id);}
    private static DateTimeOffset Time(string value)
    {if(!DateTimeOffset.TryParseExact(value,"yyyy-MM-dd'T'HH:mm:ss.fff'Z'",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal|System.Globalization.DateTimeStyles.AdjustToUniversal,out var time))throw Bad("Original timestamp is not exact recorded UTC format.");return time;}
    private static void ExactEnum<T>(JsonElement element,T value) where T:struct,Enum
    {if(!Enum.IsDefined(value)||element.ValueKind!=JsonValueKind.String||element.GetString()!=value.ToString())throw Bad("An enum is not the exact supported name.");}
    private static ConfigurationException Bad(string detail)=>new("Coverage evidence was not admitted. "+detail);
}
