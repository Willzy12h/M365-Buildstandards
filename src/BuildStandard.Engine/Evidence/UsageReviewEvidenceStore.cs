using System.Text;
using BuildStandard.Core;
using BuildStandard.Engine.Reports;
namespace BuildStandard.Engine.Evidence;
public sealed partial class EvidenceStore
{
    public const int MaximumManagedUsageBytes=32*1024*1024;
    /// <summary>Exact original pack, separately partitioned; not deployment evidence or source authentication.</summary>
    public string SaveUsageReview(byte[] original,string tenant,string id)
    {
        if(original.Length>MaximumManagedUsageBytes)throw new ConfigurationException("Usage pack exceeds32MiB managed-save/backup limit. Retain the original through separate JSON export (up to40MiB); no record was inserted.");
        var bytes=original.ToArray();string json;
        try{json=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');}
        catch(DecoderFallbackException){throw new ConfigurationException("Usage pack must be strict UTF-8.");}
        var report=UsageReviewSchema.Read(json,tenant);
        if(!Guid.TryParseExact(id,"D",out var record)||record==Guid.Empty||!string.Equals(report.Id,id,StringComparison.OrdinalIgnoreCase))
            throw new IntegrityException("Usage report record identity differs from its destination.");
        var file=Path.Combine(TenantDirectory(tenant),"usage-review-evidence",record.ToString("D")+".json");
        lock(_gate)
        {
            ReportEnvelopeDispatcher.RefuseLinks(file);
            CreateRecordFile(file,report,"usage report",bytes);
            UsageReviewImport.VerifyOriginalExport(file,bytes);
        }
        return file;
    }
}
