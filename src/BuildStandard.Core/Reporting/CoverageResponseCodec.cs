using System.Text.Json;
using System.Text.Json.Nodes;
namespace BuildStandard.Core.Reporting;

/// <summary>One admitted source page. Its rows are not a coverage conclusion or deployment evidence.</summary>
public sealed class CoverageReadResponse
{
    private readonly JsonObject[] _rows;
    private readonly byte[] _bytes;
    public IReadOnlyList<JsonObject> Rows=>Array.AsReadOnly(_rows.Select(r=>(JsonObject)r.DeepClone()).ToArray());
    public byte[] OriginalBytes=>(byte[])_bytes.Clone();
    public bool HasMore {get;}
    public string Limitations=>"Source read only; not proof of effective targeting, installation or device-specific verification. "
        +(HasMore?"Further results were declared; this page is incomplete and no population denominator is established.":"An explicit empty source is not a missing/failed read or a tenant-wide coverage conclusion.");
    internal CoverageReadResponse(JsonObject[] rows,byte[] bytes,bool hasMore){_rows=rows;_bytes=bytes;HasMore=hasMore;}
}
public static class CoverageResponseCodec
{
    public const int MaximumBytes=5*1024*1024;
    public const int MaximumRows=5000;
    public static CoverageReadResponse Read(ReadOnlySpan<byte> bytes,PreparedCoverageRead request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if(bytes.Length is 0 or >MaximumBytes)throw Bad("The coverage response is empty or exceeds 5 MiB.");
        var original=bytes.ToArray();
        try
        {
            using var document=JsonDocument.Parse(original,new JsonDocumentOptions{MaxDepth=32});var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)throw Bad("The coverage response must be an explicit object.");
            RejectDuplicates(root);
            if(root.TryGetProperty("error",out _))throw Bad("A service error envelope is not an admitted coverage result, even when result fields are also present.");
            var more=false;
            if(root.TryGetProperty("@odata.nextLink",out var next)&&next.ValueKind!=JsonValueKind.Null)
            {
                if(!request.IsCollection||next.ValueKind!=JsonValueKind.String||next.GetString() is not {Length:>0 and <=4096} link||link.Any(char.IsControl))
                    throw Bad("A further-page declaration has an unsupported shape.");
                // The codec never navigates a returned URL. A collector must independently admit any next request.
                more=true;
            }
            var rows=new List<JsonObject>();var ids=new HashSet<string>(StringComparer.Ordinal);
            if(request.IsCollection)
            {
                if(!root.TryGetProperty("value",out var values)||values.ValueKind!=JsonValueKind.Array||values.GetArrayLength()>MaximumRows)
                    throw Bad("The coverage result list is missing, malformed or exceeds 5,000 rows; no empty-success result is inferred.");
                foreach(var row in values.EnumerateArray())Add(row);
            }
            else
            {
                if(root.TryGetProperty("value",out _))throw Bad("A selected object read returned a list instead of the selected object.");
                Add(root);
            }
            return new(rows.ToArray(),original,more);

            void Add(JsonElement row)
            {
                if(row.ValueKind!=JsonValueKind.Object||!row.TryGetProperty("id",out var idProperty)||idProperty.ValueKind!=JsonValueKind.String)
                    throw Bad("A source row has no explicit string identity.");
                var id=idProperty.GetString()!;
                if(id.Length>256||!ReportValues.IsTextIdentity(id)||id.Any(char.IsControl))throw Bad("A source row identity is empty, malformed or exceeds its bound.");
                if(!request.IsCollection||request.IsGroupMembers)
                {
                    if(!ReportValues.TryGuid(id,out var canonical))throw Bad("This source requires an exact directory/policy object GUID, not a report-row identity.");
                    id=canonical;
                    if(!request.IsCollection&&id!=request.TargetId)throw new TenantMismatchException("The returned object is not the selected coverage object.");
                }
                if(!ids.Add(id))throw Bad("The source repeats a row identity; no complete result or denominator is inferred.");
                rows.Add(JsonNode.Parse(row.GetRawText())!.AsObject());
            }
        }
        catch(JsonException){throw Bad("The coverage response is malformed JSON.");}
    }
    private static void RejectDuplicates(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in element.EnumerateObject())
            {if(!names.Add(property.Name))throw Bad("The source contains duplicate JSON properties.");RejectDuplicates(property.Value);}
        }
        else if(element.ValueKind==JsonValueKind.Array)foreach(var item in element.EnumerateArray())RejectDuplicates(item);
    }
    private static ConfigurationException Bad(string reason)=>new(reason);
}
