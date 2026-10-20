using System.IO.Compression;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class IntuneExportArchiveTests
{
    private static PreparedIntuneExportRequest Request()=>IntuneExportRequest.Prepare(CoverageFamily.Application,TestData.Office);
    private static byte[] Csv(string id="",string last="")
    {var request=Request();return Encoding.UTF8.GetBytes(string.Join(',',request.Columns)+"\n"+string.Join(',',request.Columns.Select((c,i)=>i==0?(id.Length>0?id:TestData.Office):i==request.Columns.Count-1?last:""))+"\n");}
    private static byte[] Zip(params (string Name,byte[] Bytes,int Attributes)[] files)=>DeploymentCoverageBundleTests.Zip(files);
    [Fact] public void Original_archive_and_CSV_are_preserved_with_separate_digests_and_defensive_bytes()
    {var csv=Csv();var zip=Zip(("Microsoft-report.csv",csv,0));var read=IntuneExportArchive.Read(zip,Request());Assert.Equal(zip,read.OriginalArchiveBytes);Assert.Equal(csv,read.Table.OriginalBytes);Assert.Equal(CanonicalJson.Sha256Hex(zip),read.OriginalArchiveSha256);Assert.Equal(CanonicalJson.Sha256Hex(csv),read.Table.OriginalSha256);var changed=read.OriginalArchiveBytes;changed[0]=0;Assert.NotEqual(0,read.OriginalArchiveBytes[0]);Assert.Single(read.Table.Rows);}
    [Theory][InlineData("../outside.csv")][InlineData("/root.csv")][InlineData("nested/report.csv")][InlineData("nested\\report.csv")][InlineData("report.csv:stream")][InlineData("report.json")][InlineData("report.zip")][InlineData("report.csv/")]
    public void Unsupported_names_paths_and_nested_archives_are_not_extracted_or_admitted(string name)
    {Assert.Throws<ConfigurationException>(()=>IntuneExportArchive.Read(Zip((name,Csv(),0)),Request()));}
    [Theory][InlineData("duplicate")][InlineData("second-file")][InlineData("link")][InlineData("directory")][InlineData("empty")][InlineData("bad-csv")][InlineData("foreign-object")]
    public void Non_single_regular_original_CSV_is_refused_without_dropping_entries(string defect)
    {var files=new List<(string,byte[],int)>{("report.csv",Csv(),0)};switch(defect){case "duplicate":files.Add(files[0]);break;case "second-file":files.Add(("other.csv",Csv(),0));break;case "link":files[0]=("report.csv",Csv(),unchecked((int)0xA1FF0000));break;case "directory":files[0]=("report.csv",Csv(),0x10);break;case "empty":files.Clear();break;case "bad-csv":files[0]=("report.csv",Encoding.UTF8.GetBytes("missing,headings"),0);break;case "foreign-object":files[0]=("report.csv",Csv(TestData.TenantB),0);break;}Assert.Throws<ConfigurationException>(()=>IntuneExportArchive.Read(Zip(files.ToArray()),Request()));}
    [Fact] public void Twenty_to_one_expansion_limit_is_checked_before_interpreting_otherwise_bounded_valid_CSV()
    {var zip=Zip(("report.csv",Csv(last:new string('x',60000)),0));var error=Assert.Throws<ConfigurationException>(()=>IntuneExportArchive.Read(zip,Request()));Assert.Contains("expansion",error.Message,StringComparison.OrdinalIgnoreCase);}
    [Fact] public void Compressed_uncompressed_empty_and_non_ZIP_caps_are_explicit()
    {foreach(var bytes in new[]{Array.Empty<byte>(),new byte[]{1,2,3},new byte[5*1024*1024+1],Zip(("report.csv",new byte[20*1024*1024+1],0))})Assert.Throws<ConfigurationException>(()=>IntuneExportArchive.Read(bytes,Request()));}
    [Fact] public void Encrypted_flag_is_not_accepted_as_an_ordinary_CSV_entry()
    {var zip=Zip(("report.csv",Csv(),0));for(var i=0;i<zip.Length-10;i++){if(zip[i]!=0x50||zip[i+1]!=0x4b)continue;if(zip[i+2]==3&&zip[i+3]==4)zip[i+6]|=1;if(zip[i+2]==1&&zip[i+3]==2)zip[i+8]|=1;}Assert.Throws<ConfigurationException>(()=>IntuneExportArchive.Read(zip,Request()));}
    [Fact] public void An_inconsistent_archive_CRC_is_not_an_admitted_original_export()
    {var zip=Zip(("report.csv",Csv(),0));for(var i=0;i<zip.Length-20;i++)if(zip[i]==0x50&&zip[i+1]==0x4b&&zip[i+2]==1&&zip[i+3]==2){zip[i+16]^=0x40;break;}Assert.Throws<ConfigurationException>(()=>IntuneExportArchive.Read(zip,Request()));}
}
