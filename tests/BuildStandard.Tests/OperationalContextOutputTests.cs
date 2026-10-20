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
public sealed class OperationalContextOutputTests
{
    internal static OperationalContextReport Report()
    {
        var report = OperationalContextSchemaTests.Health();
        report.Sections[0].Rows.Add(JsonSerializer.SerializeToNode(new ServiceHealthOverviewRow
        { Id = "=SYNTHETIC()", Name = "<script>synthetic only</script>", Status = "serviceOperational", ReadStatus = ReportReadState.Collected }, RegisteredReportJson.Strict)!.AsObject());
        report.Sections[1].Status = ReportReadState.Failed; report.Sections[1].Error = "Synthetic unavailable source."; report.Status = ReportReadState.Partial;
        OperationalContextSchema.Seal(report); return report;
    }
    [Fact]
    public void Original_store_and_backup_preserve_bytes_without_snapshot_admission_or_overwrite()
    {
        using var root = new TempRoot(); var store = new EvidenceStore(root.Paths, NullLog.Instance); var report = Report();
        var bytes = Encoding.UTF8.GetBytes("\uFEFF  " + OperationalContextSchema.Serialise(report) + "\n");
        var file = store.SaveOperationalContext(bytes, TestData.TenantA, report.Id);
        Assert.Equal(bytes, File.ReadAllBytes(file)); Assert.Contains("operational-context-evidence", file); Assert.DoesNotContain("snapshots", file);
        Assert.Equal(report.Id, OperationalContextFile.ReadFile(file, TestData.TenantA).Report.Id);
        var copy = OperationalContextFile.ReadFile(file, TestData.TenantA).CopyOriginal(); copy[0] ^= 1; Assert.Equal(bytes, File.ReadAllBytes(file));
        var backupFile = new WorkspaceBackup(root.Paths).Create();
        using var backup = ZipFile.OpenRead(backupFile);
        var entry = Assert.Single(backup.Entries, item => item.FullName.EndsWith("/operational-context-evidence/" + report.Id + ".json", StringComparison.Ordinal));
        using var stream = entry.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory); Assert.Equal(bytes, memory.ToArray());
        var restored = new WorkspaceBackup(root.Paths).RestoreSeparate(backupFile, Path.Combine(root.Root, "restored operational"), WorkspaceBackup.ArchiveDigest(backupFile));
        var restoredFile = Path.Combine(restored, "data", Path.GetRelativePath(root.Paths.DataDirectory, file));
        Assert.Equal(bytes, OperationalContextFile.ReadFile(restoredFile, TestData.TenantA).CopyOriginal());
        Assert.True(WorkspaceBackup.VerifyRestored(restored) > 0);
        Assert.ThrowsAny<ToolkitException>(() => store.SaveOperationalContext(bytes, TestData.TenantA, report.Id)); Assert.Equal(bytes, File.ReadAllBytes(file));
    }
    [Theory][InlineData(ExportFormat.Html)][InlineData(ExportFormat.Json)][InlineData(ExportFormat.Csv)][InlineData(ExportFormat.Xlsx)]
    public void Exports_preserve_read_failures_unknown_meaning_and_original_json_without_active_content(ExportFormat format)
    {
        using var root = new TempRoot(); var report = Report(); var bytes = Encoding.UTF8.GetBytes("\uFEFF  " + OperationalContextSchema.Serialise(report) + "\n");
        var file = new ReportExporter(root.Paths, "Synthetic company").ExportOperationalContext(report, format, suppliedFile: true, originalBytes: bytes);
        Assert.NotEmpty(File.ReadAllBytes(file));
        if (format == ExportFormat.Json) Assert.Equal(bytes, File.ReadAllBytes(file));
        if (format == ExportFormat.Html)
        {
            var text = File.ReadAllText(file); Assert.Contains("&lt;script&gt;", text); Assert.DoesNotContain("<script>", text);
            Assert.Contains("Partial", text); Assert.Contains("Failed", text); Assert.Contains("not proof", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Offline supplied file", text); Assert.Contains("Synthetic unavailable source.", text);
            Assert.DoesNotContain("Checked successfully; no objects returned.", text);
        }
        if (format == ExportFormat.Csv)
        {
            using var zip = ZipFile.OpenRead(file); using var reader = new StreamReader(Assert.Single(zip.Entries, entry => entry.FullName.Contains("health_overviews", StringComparison.Ordinal)).Open());
            Assert.Contains("'=SYNTHETIC()", reader.ReadToEnd());
        }
    }
    [Theory][InlineData("tenant")][InlineData("record")][InlineData("utf8")][InlineData("size")][InlineData("changed-original")]
    public void Invalid_identity_bytes_and_mismatching_export_original_are_refused_without_records(string fault)
    {
        using var root = new TempRoot(); var report = Report(); var bytes = Encoding.UTF8.GetBytes(OperationalContextSchema.Serialise(report));
        if (fault == "utf8") bytes = [0xff];
        if (fault == "size") bytes = new byte[BuildStandard.Core.Reporting.OperationalReportRegistry.MaximumBytes + 1];
        if (fault == "changed-original")
        {
            var changed = OperationalContextSchemaTests.Health(); OperationalContextSchema.Seal(changed);
            Assert.ThrowsAny<ToolkitException>(() => new ReportExporter(root.Paths, "Synthetic").ExportOperationalContext(report, ExportFormat.Json, originalBytes: Encoding.UTF8.GetBytes(OperationalContextSchema.Serialise(changed))));
        }
        else Assert.ThrowsAny<ToolkitException>(() => new EvidenceStore(root.Paths, NullLog.Instance).SaveOperationalContext(bytes, fault == "tenant" ? TestData.TenantB : TestData.TenantA, fault == "record" ? TestData.Operator : report.Id));
        Assert.Empty(Directory.GetFiles(root.Paths.DataDirectory, "*.json", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
    }
    [Fact]
    public void Original_readback_and_link_guards_refuse_lossy_or_indirect_files()
    {
        using var root = new TempRoot(); var report = Report(); var bytes = Encoding.UTF8.GetBytes(OperationalContextSchema.Serialise(report));
        var file = Path.Combine(root.Root, "source.json"); File.WriteAllBytes(file, bytes);
        OperationalContextFile.VerifyOriginalExport(file, bytes);
        File.WriteAllBytes(file, bytes.Concat(new byte[] { (byte)' ' }).ToArray());
        Assert.Throws<IntegrityException>(() => OperationalContextFile.VerifyOriginalExport(file, bytes));
        if (!OperatingSystem.IsWindows())
        {
            var link = Path.Combine(root.Root, "linked.json"); File.CreateSymbolicLink(link, file);
            Assert.Throws<ConfigurationException>(() => OperationalContextFile.ReadFile(link, TestData.TenantA));
        }
    }
}
