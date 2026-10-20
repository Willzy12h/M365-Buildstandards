using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using BuildStandard.Core;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CoverageOriginalFilesTests
{
    internal static async Task<DeploymentCoverageCapture> Oversize()
    {var raw=await new DeploymentCoverageServiceTests.Fixture().Capture();var prefix="{\"id\":\""+TestData.Office+"\",\"padding\":\"";const string suffix="\"}";var bytes=Encoding.UTF8.GetBytes(prefix+new string('x',DeploymentCoverageEvidence.MaximumBytes-256-prefix.Length-suffix.Length)+suffix);var source=raw.Sources.Single(s=>s.Key=="object");var original=new CoverageSourceCapture(CoverageSourceState.Complete,source.Capture.Rows,[bytes],source.Capture.Detail);return raw with{Sources=raw.Sources.Select(s=>s.Key=="object"?s with{Capture=original}:s).ToArray()};}
    [Fact] public async Task Manual_original_files_keep_exact_bytes_and_explicit_non_evidence_meaning()
    {using var root=new TempRoot();var raw=await new DeploymentCoverageServiceTests.Fixture().Capture();var folder=new ReportExporter(root.Paths,"Synthetic").ExportCoverageOriginalFiles(raw);Assert.True(Directory.Exists(folder));var original=raw.Sources.SelectMany(s=>s.Capture.OriginalPages).ToArray();for(var i=0;i<original.Length;i++)Assert.Equal(original[i],File.ReadAllBytes(Path.Combine(folder,$"page-{i:D3}.json")));var readme=File.ReadAllText(Path.Combine(folder,"ORIGINAL-CONTEXT.txt"));Assert.Contains(raw.Id,readme);Assert.Contains(TestData.TenantA,readme);Assert.Contains("not complete report evidence",readme,StringComparison.OrdinalIgnoreCase);Assert.True(File.Exists(Path.Combine(folder,"COPY-VERIFIED.txt")));foreach(var line in File.ReadAllLines(Path.Combine(folder,"SHA256SUMS.txt"))){var name=line[66..];Assert.Equal(line[..64],Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,name)))).ToLowerInvariant());}Assert.Empty(Directory.EnumerateFiles(root.Paths.TenantsDirectory,"*",SearchOption.AllDirectories));}
    [Fact] public async Task Strict_report_cap_remains_unchanged_while_original_material_can_be_recovered()
    {using var root=new TempRoot();var raw=await Oversize();Assert.Throws<ConfigurationException>(()=>DeploymentCoverageEvidence.Create(raw));var folder=new ReportExporter(root.Paths,"").ExportCoverageOriginalFiles(raw);Assert.Equal(raw.Sources[0].Capture.OriginalPages[0],File.ReadAllBytes(Path.Combine(folder,"page-000.json")));Assert.Contains("NOT SAVED",File.ReadAllText(Path.Combine(folder,"ORIGINAL-CONTEXT.txt")));}
    [Fact] public async Task Modern_archive_is_copied_as_original_bytes_without_extracting_its_CSV()
    {using var root=new TempRoot();var raw=await DeploymentCoverageModernEvidenceTests.Capture();var original=raw.Sources.Single(s=>s.Key=="service-results").ModernExport!.Archive.OriginalArchiveBytes;var folder=new ReportExporter(root.Paths,"").ExportCoverageOriginalFiles(raw);Assert.Equal(original,File.ReadAllBytes(Path.Combine(folder,"export-000.zip")));Assert.Empty(Directory.EnumerateFiles(folder,"*.csv"));Assert.DoesNotContain("sig=",File.ReadAllText(Path.Combine(folder,"ORIGINAL-CONTEXT.txt")));}
    [Fact] public async Task Repeated_copy_uses_new_folders_and_never_overwrites_the_first()
    {using var root=new TempRoot();var raw=await new DeploymentCoverageServiceTests.Fixture().Capture();var exporter=new ReportExporter(root.Paths,"");var first=exporter.ExportCoverageOriginalFiles(raw);var bytes=File.ReadAllBytes(Path.Combine(first,"page-000.json"));var second=exporter.ExportCoverageOriginalFiles(raw);Assert.NotEqual(first,second);Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(first,"page-000.json")));}
    [Fact] public async Task Blocked_destination_does_not_change_originals_or_claim_a_verified_copy()
    {using var root=new TempRoot();var raw=await new DeploymentCoverageServiceTests.Fixture().Capture();Directory.Delete(root.Paths.ReportsDirectory);File.WriteAllText(root.Paths.ReportsDirectory,"Synthetic blocked destination");var bytes=raw.Sources[0].Capture.OriginalPages[0];Assert.ThrowsAny<IOException>(()=>new ReportExporter(root.Paths,"").ExportCoverageOriginalFiles(raw));Assert.Equal(bytes,raw.Sources[0].Capture.OriginalPages[0]);}
    [Fact] public async Task Raw_record_ID_is_validated_before_it_can_form_a_directory()
    {using var root=new TempRoot();var raw=await new DeploymentCoverageServiceTests.Fixture().Capture();Assert.Throws<ConfigurationException>(()=>new ReportExporter(root.Paths,"").ExportCoverageOriginalFiles(raw with{Id="../outside"}));Assert.Empty(Directory.EnumerateFileSystemEntries(root.Paths.ReportsDirectory));}
}
