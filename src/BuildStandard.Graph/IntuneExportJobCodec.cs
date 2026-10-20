using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Graph;

internal enum IntuneExportJobState { Unknown,NotStarted,InProgress,Completed,Failed }
/// <summary>Transient transport state. The signed URI must never enter evidence, errors or printable records.</summary>
internal sealed class IntuneExportJob(string id,IntuneExportJobState state,string rawStatus,Uri? download)
{
    internal string Id {get;}=id;
    internal IntuneExportJobState State {get;}=state;
    internal string RawStatus {get;}=rawStatus;
    internal string PollPath=>"/deviceManagement/reports/exportJobs/"+Uri.EscapeDataString(Id);
    internal Uri? Download {get;}=download;
    public override string ToString()=>"Bound Intune export job; no download address is included.";
}
internal static class IntuneExportJobCodec
{
    internal static IntuneExportJob Read(byte[] bytes,PreparedIntuneExportRequest request,string? expectedId=null,DateTimeOffset? now=null)
    {
        if(bytes.Length is 0 or >32768)throw Bad("Job metadata is empty or exceeds 32 KiB.");
        try
        {
            using var document=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=16});var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)throw Bad("An explicit job object is required.");RejectDuplicates(root);
            if(root.TryGetProperty("error",out _)||root.TryGetProperty("@odata.nextLink",out _))throw Bad("A service error/paged envelope is not a job receipt.");
            var id=Text(root,"id");
            if(id.Length is 0 or >512||id is "." or ".."||id.Any(c=>char.IsControl(c)||char.IsWhiteSpace(c)||"/\\?#%".Contains(c)))
                throw Bad("Unsupported opaque job identity; no poll relation was prepared.");
            if(expectedId is not null&&expectedId!=id)throw new TenantMismatchException("The returned job does not match the created job; it is not admitted or followed.");
            using var original=JsonDocument.Parse(request.Bytes);
            if(Text(root,"reportName")!=request.ReportName||Text(root,"filter")!=Text(original.RootElement,"filter")||Text(root,"format")!="csv"
                ||Text(root,"localizationType") is not ("localizedValuesAsAdditionalColumn" or "LocalizedValuesAsAdditionalColumn"))
                throw Bad("Returned job report/filter/format/localisation differs from the exact prepared request.");
            if(!root.TryGetProperty("select",out var select)||select.ValueKind!=JsonValueKind.Array||select.GetArrayLength()!=request.Columns.Count)throw Bad("Returned job columns do not match the prepared request.");
            var columns=new HashSet<string>(StringComparer.Ordinal);
            foreach(var column in select.EnumerateArray())
                if(column.ValueKind!=JsonValueKind.String||!columns.Add(column.GetString()!)||!request.Columns.Contains(column.GetString()!,StringComparer.Ordinal))throw Bad("Returned job columns are missing, duplicated or unrequested.");
            var raw="Not returned";
            if(root.TryGetProperty("status",out var status)&&status.ValueKind!=JsonValueKind.Null)
            {if(status.ValueKind!=JsonValueKind.String||status.GetString() is not {Length:>0 and <=64} text||text.Any(char.IsControl))throw Bad("Unsupported job status shape.");raw=text;}
            var state=raw switch{"notStarted"=>IntuneExportJobState.NotStarted,"inProgress"=>IntuneExportJobState.InProgress,"completed"=>IntuneExportJobState.Completed,"failed"=>IntuneExportJobState.Failed,_=>IntuneExportJobState.Unknown};
            Uri? download=null;
            if(state==IntuneExportJobState.Completed)
            {
                var address=Text(root,"url");
                if(address.Length is 0 or >8192||address.Any(char.IsControl)||address.Contains('\\')||!Uri.TryCreate(address,UriKind.Absolute,out download)
                    ||download.Scheme!=Uri.UriSchemeHttps||download.Port!=443||download.UserInfo.Length!=0||download.Fragment.Length!=0
                    ||!download.Host.EndsWith(".blob.core.windows.net",StringComparison.OrdinalIgnoreCase))throw Bad("Unsupported completed-job download address; no address or query is echoed.");
                var label=download.Host[..^".blob.core.windows.net".Length];
                if(label.Length is 0 or >63||label.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-')||label.StartsWith('-')||label.EndsWith('-'))throw Bad("Download host is not one public storage-account label.");
                if(!Timestamps.TryParse(Text(root,"expirationDateTime"),out var expiry)||expiry<=(now??DateTimeOffset.UtcNow))throw Bad("Completed job expiry is missing, malformed or elapsed; no download was admitted.");
            }
            else if(root.TryGetProperty("url",out var early)&&early.ValueKind!=JsonValueKind.Null)throw Bad("A non-completed job supplied a download value; it is not followed.");
            return new(id,state,raw,download);
        }
        catch(JsonException){throw Bad("Malformed job metadata; no source fields are echoed.");}
    }
    private static string Text(JsonElement value,string name)
    {if(!value.TryGetProperty(name,out var property)||property.ValueKind!=JsonValueKind.String||property.GetString() is not { } text)throw Bad("A required job association field is missing or has the wrong type.");return text;}
    private static void RejectDuplicates(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object)
        {var names=new HashSet<string>(StringComparer.Ordinal);foreach(var property in value.EnumerateObject()){if(!names.Add(property.Name))throw Bad("Duplicated job metadata properties.");RejectDuplicates(property.Value);}}
        else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())RejectDuplicates(item);
    }
    private static ConfigurationException Bad(string reason)=>new("Intune export job was not admitted. "+reason);
}
