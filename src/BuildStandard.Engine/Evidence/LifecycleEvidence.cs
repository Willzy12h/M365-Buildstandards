using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
namespace BuildStandard.Engine.Evidence;
public sealed partial class EvidenceStore
{
    internal void SaveLifecyclePreview(JsonObject document)=>SaveLifecycleOriginal(document,"plan");
    internal void SaveLifecycleBefore(JsonObject document)=>SaveLifecycleOriginal(document,"before");
    private void SaveLifecycleOriginal(JsonObject document,string family)
    {
        var tenant=document["tenantId"]!.GetValue<string>();var id=document["id"]!.GetValue<string>();
        var file=Path.Combine(TenantDirectory(tenant),"lifecycle",family+"-"+SafeId(id)+".json");
        var bytes=System.Text.Encoding.UTF8.GetBytes(ToolkitJson.Serialize(document));
        if(bytes.Length>16*1024*1024)throw new SafetyViolationException("Lifecycle preview exceeds its evidence bound.");
        // CreateNew plus durable flush: a preview is never overwritten or used as an imported approval token.
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        output.Write(bytes);output.Flush(true);
    }
}
