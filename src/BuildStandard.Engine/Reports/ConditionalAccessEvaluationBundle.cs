using System.IO.Compression;
using System.Text;
using BuildStandard.Core;

namespace BuildStandard.Engine.Reports;

/// <summary>Bounded original metadata/response export. Reading always produces historical, unverified evidence.</summary>
public static class ConditionalAccessEvaluationBundle
{
    public const string Notice="Original unsigned report bytes. Imported evidence is historical and unverified; it cannot grant access, approve a change, clear uncertainty or prove enforcement.";
    private const int MaximumZipBytes=ConditionalAccessEvaluationSchema.MaximumBytes+64*1024;
    private static readonly string[] Names=["metadata.json","response.json","NOTICE.txt"];
    public static byte[] Write(ConditionalAccessEvaluationCapture capture)
    {
        var metadata=capture.MetadataBytes;var response=capture.OriginalResponseBytes;
        ConditionalAccessEvaluationSchema.Read(metadata,response);
        using var memory=new MemoryStream();
        using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))
        {
            Write(Names[0],metadata);Write(Names[1],response);Write(Names[2],Encoding.UTF8.GetBytes(Notice));
            void Write(string name,byte[] bytes){using var entry=zip.CreateEntry(name,CompressionLevel.Optimal).Open();entry.Write(bytes);}
        }
        return memory.ToArray();
    }
    public static ConditionalAccessEvaluationCapture Read(byte[] bytes,string expectedTenantId)
    {
        if(bytes.Length is 0 or >MaximumZipBytes)throw Bad();
        try
        {
            using var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read);
            if(zip.Entries.Count!=Names.Length||!zip.Entries.Select(e=>e.FullName).Order(StringComparer.Ordinal).SequenceEqual(Names.Order(StringComparer.Ordinal)))throw Bad();
            var metadata=Read(zip.GetEntry(Names[0])!,ConditionalAccessEvaluationSchema.MaximumBytes);
            var response=Read(zip.GetEntry(Names[1])!,ConditionalAccessEvaluationSchema.MaximumBytes-metadata.Length);
            var notice=Read(zip.GetEntry(Names[2])!,2048);
            if(!notice.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(Notice)))throw Bad();
            ConditionalAccessEvaluationSchema.Read(metadata,response,expectedTenantId);
            return new(metadata,response);
        }
        catch(InvalidDataException){throw Bad();}
        catch(IOException){throw Bad();}
    }
    public static ConditionalAccessEvaluationCapture ReadFile(string file,string expectedTenantId)
    {
        using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
        return Read(Read(input,MaximumZipBytes),expectedTenantId);
    }
    private static byte[] Read(ZipArchiveEntry entry,int maximum)
    {
        if(entry.Length<=0||entry.Length>maximum)throw Bad();
        using var source=entry.Open();return Read(source,maximum);
    }
    private static byte[] Read(Stream source,int maximum)
    {
        using var output=new MemoryStream();var buffer=new byte[16*1024];
        while(true){var count=source.Read(buffer);if(count==0)break;if(output.Length>maximum-count)throw Bad();output.Write(buffer,0,count);}
        if(output.Length==0)throw Bad();return output.ToArray();
    }
    private static ConfigurationException Bad()=>new("The original evaluation bundle is incomplete, malformed, oversized or contains unexpected files. No report was admitted; no entries are extracted.");
}
