using System.IO.Compression;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;

/// <summary>One bounded original CSV archive, not deployment evidence. Never extracts or runs source files.</summary>
public sealed class IntuneExportArchive
{
    private const int MaximumCompressedBytes=5*1024*1024,MaximumExpandedBytes=20*1024*1024;
    private static readonly uint[] CrcTable=Enumerable.Range(0,256).Select(value=>
    {var crc=(uint)value;for(var bit=0;bit<8;bit++)crc=(crc&1)!=0?0xedb88320U^(crc>>1):crc>>1;return crc;}).ToArray();
    private readonly byte[] _archive;
    public byte[] OriginalArchiveBytes=>(byte[])_archive.Clone();
    public string OriginalArchiveSha256 {get;}
    public IntuneExportTable Table {get;}
    private IntuneExportArchive(byte[] archive,IntuneExportTable table)
    {_archive=(byte[])archive.Clone();OriginalArchiveSha256=CanonicalJson.Sha256Hex(_archive);Table=table;}
    public static IntuneExportArchive Read(byte[] bytes,PreparedIntuneExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(bytes);ArgumentNullException.ThrowIfNull(request);
        if(bytes.Length is 0 or >MaximumCompressedBytes)throw Bad("Empty or compressed input exceeds 5 MiB.");
        try
        {
            using var zip=new ZipArchive(new MemoryStream(bytes,false),ZipArchiveMode.Read);
            if(zip.Entries.Count!=1)throw Bad("Exactly one original regular root CSV entry is required; entries are never silently dropped.");
            var entry=zip.Entries[0];var name=entry.FullName;var mode=(entry.ExternalAttributes>>16)&0xF000;
            if(name.Length is 0 or >256||name.Any(c=>char.IsControl(c)||"/\\:".Contains(c))||name.EndsWith('.')||name.EndsWith(' ')
                ||!name.EndsWith(".csv",StringComparison.OrdinalIgnoreCase)||mode is not (0 or 0x8000)||(entry.ExternalAttributes&0x10)!=0||entry.IsEncrypted)
                throw Bad("Unsupported path, nested/archive type, directory, link or encrypted entry; no extraction is attempted.");
            if(entry.Length is <=0 or >MaximumExpandedBytes||entry.CompressedLength is <=0 or >MaximumCompressedBytes)
                throw Bad("Original CSV length exceeds the 20 MiB expanded bound or compressed length is unsupported.");
            if(entry.Length>entry.CompressedLength*20)throw Bad("Archive expansion exceeds the fixed 20:1 limit.");
            using var source=entry.Open();using var output=new MemoryStream();var buffer=new byte[16*1024];var crc=uint.MaxValue;
            while(true)
            {
                var count=source.Read(buffer);if(count==0)break;
                if(output.Length>MaximumExpandedBytes-count||output.Length>entry.Length-count)throw Bad("Actual expanded bytes exceed the admitted original length.");
                for(var i=0;i<count;i++)crc=CrcTable[(crc^buffer[i])&0xff]^(crc>>8);
                output.Write(buffer,0,count);
            }
            if(output.Length!=entry.Length)throw Bad("The original CSV readback is incomplete.");
            // ZipArchive exposes the recorded CRC but does not verify it on read. This is format integrity, not authentication.
            if(~crc!=entry.Crc32)throw Bad("Original CSV CRC differs from the archive inventory; malformed data is not admitted.");
            return new(bytes,IntuneExportCsv.Read(output.ToArray(),request));
        }
        catch(InvalidDataException){throw Bad("Malformed archive data; no original report was admitted.");}
        catch(IOException){throw Bad("Original archive could not be read completely.");}
        catch(NotSupportedException){throw Bad("Unsupported archive encoding, compression or encryption.");}
    }
    private static ConfigurationException Bad(string reason)=>new("Intune original export was not admitted. "+reason);
}
