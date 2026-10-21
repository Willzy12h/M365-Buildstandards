using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class DeploymentCoverageModernEvidenceTests
{
    internal static async Task<DeploymentCoverageCapture> Capture(CoverageFamily family=CoverageFamily.Application)
    {var f=new DeploymentCoverageServiceTests.Fixture(family);f.Data.Session.Mode=SessionMode.Deployment;f.Data.Session.Scopes.Add(IntuneExportRequest.Prepare(family,TestData.Office).RequiredScope);var raw=await f.Capture();var observation=IntuneExportObservation.Create(raw.Selection,IntuneExportObservationTests.Receipt(raw.Selection));var modern=new CoverageCapturedSource("service-results",observation.Request.Path,observation.Request.RequiredScope,observation.StartedAt,observation.CompletedAt,new(CoverageSourceState.Complete,[],[],"Original modern CSV read; not effective deployment.")){ModernExport=observation};return raw with{Sources=raw.Sources.Select(s=>s.Key=="service-results"?modern:s).ToArray(),SupportedReadsComplete=false};}
    [Theory][InlineData(CoverageFamily.Application)][InlineData(CoverageFamily.ClassicConfiguration)]
    public async Task Modern_original_archive_and_CSV_identity_survive_coverage_JSON_bundle_round_trip(CoverageFamily family)
    {var raw=await Capture(family);var e=DeploymentCoverageEvidence.Create(raw);var zip=DeploymentCoverageBundle.Write(e);var read=DeploymentCoverageBundle.Read(zip,TestData.TenantA);Assert.Equal(e.MetadataBytes,read.MetadataBytes);Assert.True(read.IsHistorical);var node=JsonNode.Parse(read.MetadataBytes)!;var modern=node["sources"]!.AsArray().Single(n=>n!["key"]!.GetValue<string>()=="service-results")!["modernExport"]!;Assert.Equal("export-000.zip",modern["archiveFile"]!.GetValue<string>());Assert.Equal(1,modern["sourceRows"]!.GetValue<int>());var originals=read.OriginalPages;Assert.Equal(raw.Sources.Single(s=>s.Key=="service-results").ModernExport!.Archive.OriginalArchiveBytes,originals["export-000.zip"]);Assert.DoesNotContain("sig=",Encoding.UTF8.GetString(read.MetadataBytes));Assert.DoesNotContain("url",Encoding.UTF8.GetString(read.MetadataBytes),StringComparison.OrdinalIgnoreCase);}
    [Fact] public async Task Modified_archive_cannot_be_normalised_or_resealed_as_the_original_source()
    {var e=DeploymentCoverageEvidence.Create(await Capture());var pages=e.OriginalPages.ToDictionary(p=>p.Key,p=>p.Value);pages["export-000.zip"][^1]^=1;Assert.Throws<IntegrityException>(()=>DeploymentCoverageEvidence.Read(e.MetadataBytes,pages,TestData.TenantA));}
    [Fact] public async Task Historical_exports_show_CSV_rows_and_opaque_namespaces_instead_of_a_false_empty_service_result()
    {var e=DeploymentCoverageEvidence.Create(await Capture());var read=DeploymentCoverageBundle.Read(DeploymentCoverageBundle.Write(e),TestData.TenantA);var html=DeploymentCoverageDocuments.Html(read,"Synthetic");Assert.Contains("ApplicationId",html);Assert.Contains("DeviceId",html);Assert.Contains("unestablished",html);Assert.Contains("report_opaque-1",html);var sheets=DeploymentCoverageDocuments.Sheets(read);var source=sheets.Single(s=>s.Name=="Source read states").Rows.Single(r=>r[0]=="service-results");Assert.Equal("1",source[2]);Assert.DoesNotContain("no source rows",source[4]);}
    private static byte[] Change(DeploymentCoverageEvidence e,Action<JsonObject> change)
    {var node=JsonNode.Parse(e.MetadataBytes)!.AsObject();change(node);node["integrityDigest"]="";node["integrityDigest"]=CanonicalJson.Sha256(node);return Encoding.UTF8.GetBytes(node.ToJsonString());}
    [Theory][InlineData("requestSha256")][InlineData("reportName")][InlineData("archiveFile")][InlineData("csvSha256")][InlineData("sourceRows")]
    public async Task An_unsigned_recomputed_digest_does_not_override_fixed_modern_source_binding(string field)
    {var e=DeploymentCoverageEvidence.Create(await Capture());var bytes=Change(e,n=>{var source=n["sources"]!.AsArray().Single(s=>s!["key"]!.GetValue<string>()=="service-results")!["modernExport"]!;source[field]=field=="sourceRows"?JsonValue.Create(0):JsonValue.Create("different");});Assert.ThrowsAny<ToolkitException>(()=>DeploymentCoverageEvidence.Read(bytes,e.OriginalPages,TestData.TenantA));}
    [Fact] public async Task A_signed_download_address_is_an_unsupported_metadata_field_not_persisted_or_echoed()
    {var e=DeploymentCoverageEvidence.Create(await Capture());var bytes=Change(e,n=>n["sources"]!.AsArray().Single(s=>s!["key"]!.GetValue<string>()=="service-results")!["modernExport"]!["url"]="https://synthetic.blob.core.windows.net/source.zip?sig=private-synthetic-only");var error=Assert.Throws<ConfigurationException>(()=>DeploymentCoverageEvidence.Read(bytes,e.OriginalPages,TestData.TenantA));Assert.DoesNotContain("private-synthetic-only",error.Message);}
    [Theory][InlineData("mode")][InlineData("scope")]
    public async Task Broader_source_access_cannot_be_relabelled_as_assessment_or_a_different_scope(string defect)
    {var e=DeploymentCoverageEvidence.Create(await Capture());var bytes=Change(e,n=>{if(defect=="mode")n["permissionMode"]="Assessment";else n["scopes"]=new JsonArray("DeviceManagementApps.Read.All");});Assert.ThrowsAny<ToolkitException>(()=>DeploymentCoverageEvidence.Read(bytes,e.OriginalPages,TestData.TenantA));}
    [Fact] public async Task Modern_originals_in_an_import_remain_historical_and_never_recreate_a_live_scope()
    {var e=DeploymentCoverageEvidence.Create(await Capture());var read=DeploymentCoverageBundle.Read(DeploymentCoverageBundle.Write(e),TestData.TenantA);Assert.True(read.Sources().Single(s=>s.Key=="service-results").ModernExport!.IsHistorical);}
    [Fact] public async Task A_valid_observation_created_for_another_tenant_cannot_be_attached_to_this_reports_parent()
    {var raw=await Capture();var f=IntuneExportObservationTests.Scope();f.Session.TenantId=TestData.TenantB;f.Snapshot.TenantId=TestData.TenantB;f.Mappings.TenantId=TestData.TenantB;f.Run.TenantId=TestData.TenantB;f.Run.IntegrityDigest=BuildStandard.Engine.Evidence.EvidenceIntegrity.Compute(f.Run);f.Seal();var selection=f.Prepare();var foreign=IntuneExportObservation.Create(selection,IntuneExportObservationTests.Receipt(selection));var changed=raw with{Sources=raw.Sources.Select(s=>s.Key=="service-results"?s with{ModernExport=foreign}:s).ToArray()};Assert.Throws<TenantMismatchException>(()=>DeploymentCoverageEvidence.Create(changed));}
}
