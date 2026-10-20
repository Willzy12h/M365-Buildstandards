using System.IO.Compression;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class DeploymentCoverageBundleTests
{
    internal static byte[] Zip(IEnumerable<(string Name,byte[] Bytes,int Attributes)> files)
    {using var output=new MemoryStream();using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))foreach(var file in files){var entry=zip.CreateEntry(file.Name);entry.ExternalAttributes=file.Attributes;using var stream=entry.Open();stream.Write(file.Bytes);}return output.ToArray();}
    private static List<(string Name,byte[] Bytes,int Attributes)> Entries(DeploymentCoverageEvidence e)=>new[]{("metadata.json",e.MetadataBytes,0),("NOTICE.txt",Encoding.UTF8.GetBytes(DeploymentCoverageBundle.Notice),0)}.Concat(e.OriginalPages.Select(p=>(p.Key,p.Value,0))).ToList();
    [Fact] public async Task Bundle_keeps_exact_metadata_original_pages_and_loads_only_as_historical()
    {var e=await DeploymentCoverageEvidenceTests.Evidence();var zip=DeploymentCoverageBundle.Write(e);var read=DeploymentCoverageBundle.Read(zip,TestData.TenantA);Assert.True(read.IsHistorical);Assert.Equal(e.MetadataBytes,read.MetadataBytes);Assert.Equal(e.OriginalPages.Keys,read.OriginalPages.Keys);foreach(var page in e.OriginalPages)Assert.Equal(page.Value,read.OriginalPages[page.Key]);}
    [Theory][InlineData("../outside.json")][InlineData("/root.json")][InlineData("nested/page-000.json")][InlineData("page-000.JSON")][InlineData("extra.json")][InlineData("export-000.zip")][InlineData("page-100.json")][InlineData("page-000.json/")][InlineData("page-000.json:stream")]
    public async Task Unexpected_paths_and_names_are_refused_without_extraction(string name)
    {var e=await DeploymentCoverageEvidenceTests.Evidence();var files=Entries(e);files.Add((name,Encoding.UTF8.GetBytes("{}"),0));Assert.Throws<ConfigurationException>(()=>DeploymentCoverageBundle.Read(Zip(files),TestData.TenantA));}
    [Theory][InlineData("duplicate")][InlineData("missing")][InlineData("notice")][InlineData("link")][InlineData("directory")][InlineData("oversize")]
    public async Task Invalid_archive_inventory_is_never_a_smaller_successful_report(string defect)
    {var e=await DeploymentCoverageEvidenceTests.Evidence();var files=Entries(e);switch(defect){case "duplicate":files.Add(files[0]);break;case "missing":files.RemoveAt(0);break;case "notice":files[1]=("NOTICE.txt",Encoding.UTF8.GetBytes("other"),0);break;case "link":files[0]=("metadata.json",e.MetadataBytes,unchecked((int)0xA1FF0000));break;case "directory":files[0]=("metadata.json",e.MetadataBytes,0x10);break;case "oversize":files[0]=("metadata.json",new byte[5*1024*1024+1],0);break;}Assert.Throws<ConfigurationException>(()=>DeploymentCoverageBundle.Read(Zip(files),TestData.TenantA));}
    [Fact] public void Empty_non_zip_and_oversized_inputs_fail_with_a_safe_domain_error()
    {foreach(var bytes in new[]{Array.Empty<byte>(),new byte[]{1,2,3},new byte[5*1024*1024+65537]})Assert.Throws<ConfigurationException>(()=>DeploymentCoverageBundle.Read(bytes,TestData.TenantA));}
    [Fact] public async Task Reading_a_file_binds_tenant_and_never_rewrites_it()
    {using var root=new TempRoot();var e=await DeploymentCoverageEvidenceTests.Evidence();var file=Path.Combine(root.Root,"original.zip");var bytes=DeploymentCoverageBundle.Write(e);File.WriteAllBytes(file,bytes);Assert.Throws<TenantMismatchException>(()=>DeploymentCoverageBundle.ReadFile(file,TestData.TenantB));Assert.Equal(e.MetadataBytes,DeploymentCoverageBundle.ReadFile(file,TestData.TenantA).MetadataBytes);Assert.Equal(bytes,File.ReadAllBytes(file));}
}
