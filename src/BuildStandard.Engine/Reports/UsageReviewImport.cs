using System.Security.Cryptography;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Models;
namespace BuildStandard.Engine.Reports;
/// <summary>Original supplied bytes, separate from live context, managed evidence and source authentication.</summary>
public sealed class UsageReviewImport
{
    private readonly byte[] _original;
    public UsageReviewReport Report {get;}
    public string OriginalSha256 {get;}
    private UsageReviewImport(byte[] original,UsageReviewReport report)
    {_original=original;Report=report;OriginalSha256=Convert.ToHexStringLower(SHA256.HashData(original));}
    public byte[] CopyOriginal()=>_original.ToArray();
    public static UsageReviewImport ReadFile(string file,string tenant)
    {
        ReportEnvelopeDispatcher.RefuseLinks(file);
        using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>UsageReviewSchema.MaximumBytes)throw new ConfigurationException("Usage pack exceeds40MiB.");
        using var reader=new BinaryReader(stream);var bytes=reader.ReadBytes(UsageReviewSchema.MaximumBytes+1);
        if(bytes.Length>UsageReviewSchema.MaximumBytes)throw new ConfigurationException("Usage pack exceeds40MiB.");
        string json;
        try{json=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');}
        catch(DecoderFallbackException){throw new ConfigurationException("Usage pack must be strict UTF-8.");}
        return new(bytes,UsageReviewSchema.Read(json,tenant));
    }
    public static void VerifyOriginalExport(string file,byte[] original)
    {
        if(original.Length>UsageReviewSchema.MaximumBytes)throw new ConfigurationException("Original usage pack exceeds40MiB.");
        ReportEnvelopeDispatcher.RefuseLinks(file);
        using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length!=original.Length)throw new IntegrityException("Original usage export readback length differs; original bytes must be retained.");
        using var sha=SHA256.Create();
        if(!CryptographicOperations.FixedTimeEquals(sha.ComputeHash(stream),SHA256.HashData(original)))
            throw new IntegrityException("Original usage export readback differs; original bytes must be retained.");
    }
}
