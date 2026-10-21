using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class PartnerContextOutputTests
{
    internal static OperationalContextReport Report()
    {
        var r=PartnerContextSchemaTests.Empty();var p=PartnerContextSchemaTests.ParentRow();p.Name="=SYNTHETIC(<script>)";
        r.PartnerCoverage!.ParentRowsRead=r.PartnerCoverage.SelectedCustomerRows=1;
        r.Sections[0].Rows.Add(JsonSerializer.SerializeToNode(p,RegisteredReportJson.Strict)!.AsObject());OperationalContextSchema.Seal(r);return r;
    }
    [Theory][InlineData(ExportFormat.Html)][InlineData(ExportFormat.Json)][InlineData(ExportFormat.Csv)][InlineData(ExportFormat.Xlsx)]
    public void Complete_exports_preserve_distinct_source_customer_counts_and_direct_observations(ExportFormat format)
    {
        using var root=new TempRoot();var r=Report();var original=Encoding.UTF8.GetBytes("\uFEFF "+OperationalContextSchema.Serialise(r)+"\n");
        var file=new ReportExporter(root.Paths,"Synthetic company").ExportOperationalContext(r,format,true,original);Assert.NotEmpty(File.ReadAllBytes(file));
        var sheets=RegisteredReportDocuments.OperationalSheets(r,true);var metadata=string.Join('\n',sheets[0].Rows.Select(row=>string.Join('|',row)));
        Assert.Contains("Selected customer tenant ID|"+TestData.TenantB,metadata);Assert.Contains("Source tenant ID|"+TestData.TenantA,metadata);
        Assert.Contains("Excluded other-customer rows|0",metadata);Assert.Contains("Parent inventory complete|True",metadata);
        Assert.Contains("Unknown is not false",metadata);Assert.Equal(3,sheets.Count);
        if(format==ExportFormat.Json)Assert.Equal(original,File.ReadAllBytes(file));
        if(format==ExportFormat.Html){var html=File.ReadAllText(file);Assert.Contains("&lt;script&gt;",html);Assert.DoesNotContain("<script>",html);Assert.Contains("EndedAtOrBeforeAsOf",html);Assert.Contains("active",html);}
        if(format==ExportFormat.Csv){using var zip=ZipFile.OpenRead(file);using var reader=new StreamReader(Assert.Single(zip.Entries,e=>e.Name.Contains("partner_relationships",StringComparison.Ordinal)).Open());Assert.Contains("'=SYNTHETIC",reader.ReadToEnd());}
    }
    [Fact]public void Original_partner_source_identity_survives_store_and_backup_and_cannot_rebind_to_customer()
    {
        using var root=new TempRoot();var r=Report();var bytes=Encoding.UTF8.GetBytes(OperationalContextSchema.Serialise(r));var store=new EvidenceStore(root.Paths,NullLog.Instance);
        var file=store.SaveOperationalContext(bytes,TestData.TenantA,r.Id);Assert.Equal(bytes,File.ReadAllBytes(file));
        Assert.Throws<TenantMismatchException>(()=>store.SaveOperationalContext(bytes,TestData.TenantB,r.Id));
        var archive=new WorkspaceBackup(root.Paths).Create();var restored=new WorkspaceBackup(root.Paths).RestoreSeparate(archive,Path.Combine(root.Root,"restored partner"),WorkspaceBackup.ArchiveDigest(archive));
        var saved=Path.Combine(restored,"data",Path.GetRelativePath(root.Paths.DataDirectory,file));Assert.Equal(bytes,OperationalContextFile.ReadFile(saved,TestData.TenantA).CopyOriginal());
    }
}
