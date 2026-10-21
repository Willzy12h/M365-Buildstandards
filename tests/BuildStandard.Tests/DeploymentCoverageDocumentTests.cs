using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class DeploymentCoverageDocumentTests
{
    [Fact] public async Task Shared_sheets_show_original_context_scoped_pilots_source_intervals_and_exact_byte_digests()
    {var f=new DeploymentCoverageServiceTests.Fixture();f.GroupAssignment();var capture=await f.Capture();var sheets=DeploymentCoverageDocuments.Sheets(capture);var all=string.Join('\n',sheets.SelectMany(s=>s.Rows).Select(r=>string.Join(" | ",r)));Assert.Contains("CMP-WIN-001",all);Assert.Contains(TestData.Operator,all);Assert.Contains(capture.Selection.StandardDigest,all);Assert.Contains(capture.Selection.SourceSnapshotDigest,all);Assert.Contains(capture.Sources[0].StartedAt,all);Assert.Contains(CanonicalJson.Sha256Hex(capture.Sources[0].Capture.OriginalPages[0]),all);Assert.Contains("not effective targeting",all);Assert.Contains("not an Intune",all);}
    [Fact] public async Task Failed_source_is_not_an_empty_success_or_zero_verified_population()
    {var f=new DeploymentCoverageServiceTests.Fixture();f.Override=(_,_)=>Task.FromException<CoverageReadResponse>(new HttpRequestException());var html=DeploymentCoverageDocuments.Html(await f.Capture(),"Synthetic company");Assert.Contains("Failed",html);Assert.Contains("Not established",html);Assert.DoesNotContain("Checked successfully; no source rows",html);Assert.Contains("Candidate state unknown",html);}
    [Fact] public async Task Explicit_empty_assignments_have_observed_unassigned_meaning_but_no_installation_claim()
    {var html=DeploymentCoverageDocuments.Html(await new DeploymentCoverageServiceTests.Fixture().Capture(),"Synthetic");Assert.Contains("Observed unassigned",html);Assert.Contains("Checked successfully; no source rows",html);Assert.Contains("not installation",html);}
    [Fact] public async Task Null_status_and_unreturned_text_remain_unknown_in_tables()
    {var f=new DeploymentCoverageServiceTests.Fixture();f.Override=(r,_)=>Task.FromResult(CoverageResponseCodec.Read(Encoding.UTF8.GetBytes(r.Path.EndsWith("/deviceStatuses")?"{\"value\":[{\"id\":\"row\",\"status\":null}]}":r.IsCollection?"{\"value\":[]}":"{\"id\":\""+TestData.Office+"\"}"),r));var sheets=DeploymentCoverageDocuments.Sheets(await f.Capture());var status=sheets.Single(s=>s.Name=="Service observations");Assert.Contains(status.Rows.Skip(1),r=>r.Contains("Unknown / not returned"));Assert.Contains(status.Rows.Skip(1),r=>r.Contains("Unknown"));}
    [Fact] public async Task HTML_uses_existing_CSP_and_encoders_for_hostile_source_text()
    {var f=new DeploymentCoverageServiceTests.Fixture();f.Data.Standard.Controls[0].Name="<script>alert(1)</script>";var capture=await f.Capture();var html=DeploymentCoverageDocuments.Html(capture,"<img src=x onerror=evil()>");Assert.Contains("default-src 'none'",html);Assert.DoesNotContain("<script>",html);Assert.DoesNotContain("<img ",html);Assert.Contains("&lt;script&gt;",html);}
    [Fact] public async Task CSV_and_Excel_reuse_text_only_encoding_for_formula_like_control_names()
    {var f=new DeploymentCoverageServiceTests.Fixture();f.Data.Standard.Controls[0].Name="=HYPERLINK(\"bad\")";var sheets=DeploymentCoverageDocuments.Sheets(await f.Capture());var csv=CsvWriter.Write(sheets[0].Rows);Assert.Contains("'=HYPERLINK",csv);var xlsx=XlsxWriter.Write(sheets);Assert.NotEmpty(xlsx);}
    [Fact] public async Task Original_context_is_distinct_from_an_editable_or_later_client_selection()
    {var f=new DeploymentCoverageServiceTests.Fixture();var capture=await f.Capture();f.Data.Session.TenantId=TestData.TenantB;var text=DeploymentCoverageDocuments.OriginalContext(capture);Assert.Contains("Tenant ID: "+TestData.TenantA,text);Assert.DoesNotContain(TestData.TenantB,text);Assert.Contains("Source snapshot ID: "+capture.Selection.SourceSnapshotId,text);}
}
