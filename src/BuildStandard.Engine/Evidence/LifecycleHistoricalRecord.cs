using System.Text.Json.Nodes;
using BuildStandard.Core.Json;
namespace BuildStandard.Engine.Evidence;

/// <summary>Validated historical bytes for inspection/export only; no approval, execution or write-resume interface.</summary>
public sealed class LifecycleHistoricalRecord
{
    private readonly JsonObject _document;
    private readonly byte[] _original;
    internal LifecycleHistoricalRecord(JsonObject document,byte[] original){_document=document.DeepClone().AsObject();_original=original.ToArray();}
    public string Id=>_document["id"]!.GetValue<string>();
    public string TenantId=>_document["tenantId"]!.GetValue<string>();
    public string Kind=>_document["kind"]!.GetValue<string>();
    public string OriginalSha256=>CanonicalJson.Sha256Hex(_original);
    public JsonObject GetDocument()=>_document.DeepClone().AsObject();
    public byte[] GetOriginalBytes()=>_original.ToArray();
}
public sealed record LifecycleAttemptSummary(string Id,string ActionId,string TargetName,string TargetId,string Status,
    string WriteAcceptance,string Verification,string StartedAt,string SourceDigest);
