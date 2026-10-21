using System.IO.Compression;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class UsageReviewStorageTests
{
    internal static byte[] Original()
    {var report=UsageReviewSchemaTests.Report();UsageReviewSchema.Seal(report);return Encoding.UTF8.GetBytes("\uFEFF  "+UsageReviewSchema.Serialise(report)+"\n");}
    [Fact] public void Managed_save_retains_original_bytes_and_ordinary_backup_includes_them_without_snapshot_admission()
    {
        using var root=new TempRoot();var store=new EvidenceStore(root.Paths,NullLog.Instance);var bytes=Original();
        var file=store.SaveUsageReview(bytes,TestData.TenantA,TestData.Office);
        Assert.Equal(bytes,File.ReadAllBytes(file));Assert.Contains("usage-review-evidence",file);Assert.DoesNotContain("snapshots",file);
        Assert.Equal(TestData.Office,UsageReviewImport.ReadFile(file,TestData.TenantA).Report.Id);
        var backup=new WorkspaceBackup(root.Paths).Create();using var zip=ZipFile.OpenRead(backup);
        using var stream=Assert.Single(zip.Entries,e=>e.FullName.EndsWith("/usage-review-evidence/"+TestData.Office+".json",StringComparison.Ordinal)).Open();
        using var memory=new MemoryStream();stream.CopyTo(memory);Assert.Equal(bytes,memory.ToArray());
        Assert.ThrowsAny<ToolkitException>(()=>store.SaveUsageReview(bytes,TestData.TenantA,TestData.Office));Assert.Equal(bytes,File.ReadAllBytes(file));
    }
    [Theory][InlineData("tenant")][InlineData("id")][InlineData("size")][InlineData("utf8")]
    public void Wrong_binding_or_oversized_original_is_not_inserted(string fault)
    {
        using var root=new TempRoot();var store=new EvidenceStore(root.Paths,NullLog.Instance);var bytes=Original();
        if(fault=="size")bytes=new byte[EvidenceStore.MaximumManagedUsageBytes+1];if(fault=="utf8")bytes=[0xff];
        Assert.ThrowsAny<ToolkitException>(()=>store.SaveUsageReview(bytes,fault=="tenant"?TestData.TenantB:TestData.TenantA,fault=="id"?TestData.Operator:TestData.Office));
        Assert.Empty(Directory.Exists(root.Paths.DataDirectory)?Directory.GetFiles(root.Paths.DataDirectory,"*.json",SearchOption.AllDirectories):[]);
    }
    [Fact] public void Original_import_copies_and_export_readback_preserve_bytes_and_refuse_changed_or_linked_files()
    {
        using var root=new TempRoot();var file=Path.Combine(root.Root,"original.json");var bytes=Original();File.WriteAllBytes(file,bytes);
        var imported=UsageReviewImport.ReadFile(file,TestData.TenantA);var copy=imported.CopyOriginal();copy[0]^=1;Assert.Equal(bytes,imported.CopyOriginal());
        UsageReviewImport.VerifyOriginalExport(file,bytes);File.WriteAllBytes(file,bytes.Select((b,i)=>i==bytes.Length-1?(byte)' ':b).ToArray());
        Assert.Throws<IntegrityException>(()=>UsageReviewImport.VerifyOriginalExport(file,bytes));File.WriteAllBytes(file,[0xff]);
        Assert.Throws<ConfigurationException>(()=>UsageReviewImport.ReadFile(file,TestData.TenantA));
        if(!OperatingSystem.IsWindows()){var link=Path.Combine(root.Root,"linked.json");File.CreateSymbolicLink(link,file);Assert.Throws<ConfigurationException>(()=>UsageReviewImport.ReadFile(link,TestData.TenantA));}
    }
}
