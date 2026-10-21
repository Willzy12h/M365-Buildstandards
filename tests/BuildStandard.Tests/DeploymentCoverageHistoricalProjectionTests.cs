using System.IO.Compression;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class DeploymentCoverageHistoricalProjectionTests
{
    private static async Task<DeploymentCoverageEvidence> Original(bool hidden=false)
    {var fixture=new DeploymentCoverageServiceTests.Fixture();fixture.GroupAssignment();if(hidden)fixture.Group=fixture.Group.Replace("\"Private\"","\"HiddenMembership\"");var e=DeploymentCoverageEvidence.Create(await fixture.Capture());return DeploymentCoverageEvidence.Read(e.MetadataBytes,e.OriginalPages,TestData.TenantA);}
    [Fact] public async Task Production_evidence_projection_keeps_interpreted_dimensions_without_changing_original_bytes()
    {var e=await Original();var before=e.MetadataBytes;var pages=e.OriginalPages;var sheets=DeploymentCoverageDocuments.Sheets(e);foreach(var name in new[]{"Assignment configuration","Group populations","Direct member observations","Pilot group observations","Service observations"})Assert.Contains(sheets,s=>s.Name==name);Assert.Contains(sheets.Single(s=>s.Name=="Pilot group observations").Rows,r=>r.Contains("Observed present"));Assert.Contains(sheets[0].Rows,r=>r.Contains("Candidate observation"));Assert.Contains(sheets.Single(s=>s.Name=="Group populations").Rows,r=>r.Any(c=>c.Contains("v1 omits service principals",StringComparison.Ordinal)));Assert.Contains(sheets.Single(s=>s.Name=="Service observations").Rows,r=>r.Any(c=>c.Contains("freshness",StringComparison.OrdinalIgnoreCase)));Assert.Equal(before,e.MetadataBytes);foreach(var p in pages)Assert.Equal(p.Value,e.OriginalPages[p.Key]);Assert.True(e.IsHistorical);}
    [Fact] public async Task Unsupported_population_stays_unknown_in_historical_pilot_projection()
    {var e=await Original(hidden:true);var sheets=DeploymentCoverageDocuments.Sheets(e);var pilots=sheets.Single(s=>s.Name=="Pilot group observations");Assert.Contains(pilots.Rows,r=>r.Contains("Unknown"));Assert.DoesNotContain(pilots.Rows,r=>r.Contains("Observed absent"));Assert.Contains("Historical / unverified",DeploymentCoverageDocuments.Html(e,"Synthetic"));}
    [Theory][InlineData(ExportFormat.Html)][InlineData(ExportFormat.Csv)][InlineData(ExportFormat.Xlsx)]
    public async Task Production_human_readable_exports_include_pilot_and_candidate_meaning(ExportFormat format)
    {using var root=new TempRoot();var e=await Original();var file=new ReportExporter(root.Paths,"Synthetic").ExportDeploymentCoverage(e,format);string text;if(format==ExportFormat.Html)text=File.ReadAllText(file);else{using var zip=ZipFile.OpenRead(file);text=string.Join('\n',zip.Entries.Select(entry=>{using var reader=new StreamReader(entry.Open());return reader.ReadToEnd();}));}Assert.Contains("Observed present",text);Assert.Contains("Candidate observation",text);Assert.Contains("Historical / unverified",text);}
}
