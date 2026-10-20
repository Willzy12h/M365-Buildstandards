using System.IO.Compression;
using System.Text;
using BuildStandard.Core;
namespace BuildStandard.Engine.Reports;

/// <summary>Fixed original report entries. Never extracts files, executes content or confers current-client authority.</summary>
public static class DeploymentCoverageBundle
{
    public const string Notice="Historical unsigned coverage observations; not complete before-evidence, effective protection, approval or write authority. Original fingerprints detect modification, not source authentication.";
    private const int MaximumZipBytes=DeploymentCoverageEvidence.MaximumBytes+64*1024;
    public static byte[] Write(DeploymentCoverageEvidence evidence)
    {
        var metadata=evidence.MetadataBytes;var pages=evidence.OriginalPages;
        DeploymentCoverageEvidence.Read(metadata,pages,evidence.Metadata.TenantId);
        using var output=new MemoryStream();
        using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
        {
            Add("metadata.json",metadata);foreach(var page in pages)Add(page.Key,page.Value);Add("NOTICE.txt",Encoding.UTF8.GetBytes(Notice));
            void Add(string name,byte[] bytes){using var stream=zip.CreateEntry(name,CompressionLevel.Optimal).Open();stream.Write(bytes);}
        }
        if(output.Length>MaximumZipBytes)throw Bad();return output.ToArray();
    }
    public static DeploymentCoverageEvidence Read(byte[] bytes,string expectedTenantId)
    {
        ArgumentNullException.ThrowIfNull(bytes);if(bytes.Length is 0 or >MaximumZipBytes)throw Bad();
        try
        {
            using var zip=new ZipArchive(new MemoryStream(bytes,false),ZipArchiveMode.Read);
            if(zip.Entries.Count is <2 or >102)throw Bad();
            var names=new HashSet<string>(StringComparer.Ordinal);byte[]? metadata=null;byte[]? notice=null;
            var pages=new Dictionary<string,byte[]>(StringComparer.Ordinal);var remaining=DeploymentCoverageEvidence.MaximumBytes;
            foreach(var entry in zip.Entries)
            {
                var name=entry.FullName;var mode=(entry.ExternalAttributes>>16)&0xF000;
                if(!names.Add(name)||mode is not (0 or 0x8000)||(entry.ExternalAttributes&0x10)!=0)throw Bad();
                if(name!="metadata.json"&&name!="NOTICE.txt"&&name!="export-000.zip"&&!(name.Length==13&&name.StartsWith("page-0",StringComparison.Ordinal)&&char.IsAsciiDigit(name[6])&&char.IsAsciiDigit(name[7])&&name.EndsWith(".json",StringComparison.Ordinal)))throw Bad();
                var bound=name=="NOTICE.txt"?1024:remaining;
                if(entry.Length is <=0||entry.Length>bound)throw Bad();
                using var stream=entry.Open();var content=ReadBounded(stream,bound);
                if(content.Length!=entry.Length)throw Bad();
                if(name=="NOTICE.txt")notice=content;
                else {remaining-=content.Length;if(name=="metadata.json")metadata=content;else pages.Add(name,content);}
            }
            if(metadata is null||notice is null||!notice.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(Notice)))throw Bad();
            return DeploymentCoverageEvidence.Read(metadata,pages,expectedTenantId);
        }
        catch(InvalidDataException){throw Bad();}
        catch(IOException){throw Bad();}
        catch(NotSupportedException){throw Bad();}
    }
    public static DeploymentCoverageEvidence ReadFile(string file,string expectedTenantId)
    {
        ReportEnvelopeDispatcher.RefuseLinks(file);
        if(!File.Exists(file))throw new ConfigurationException("The original coverage ZIP was not found. Choose an existing report for the recorded client.");
        using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
        return Read(ReadBounded(stream,MaximumZipBytes),expectedTenantId);
    }
    private static byte[] ReadBounded(Stream source,int maximum)
    {
        using var output=new MemoryStream();var buffer=new byte[16*1024];
        while(true){var count=source.Read(buffer);if(count==0)break;if(output.Length>maximum-count)throw Bad();output.Write(buffer,0,count);}
        if(output.Length==0)throw Bad();return output.ToArray();
    }
    private static ConfigurationException Bad()=>new("Coverage ZIP is incomplete, malformed, oversized or contains unsupported entries. No smaller complete report is admitted and no files are extracted.");
}
