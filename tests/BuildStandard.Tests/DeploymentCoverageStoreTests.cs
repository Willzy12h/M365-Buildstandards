using BuildStandard.Core;
using BuildStandard.Engine.Evidence;
using Xunit;
namespace BuildStandard.Tests;
public sealed class DeploymentCoverageStoreTests
{
    [Fact] public async Task Original_report_is_durable_tenant_bound_historical_and_separate_from_deployment_evidence()
    {using var root=new TempRoot();var store=new EvidenceStore(root.Paths,BuildStandard.Core.Diagnostics.NullLog.Instance);var e=await DeploymentCoverageEvidenceTests.Evidence();var file=store.SaveDeploymentCoverage(e);Assert.True(File.Exists(file));Assert.Contains("coverage-reports",file);var read=store.LoadDeploymentCoverage(TestData.TenantA,e.Metadata.Id)!;Assert.True(read.IsHistorical);Assert.Equal(e.MetadataBytes,read.MetadataBytes);Assert.Empty(store.ListSnapshots(TestData.TenantA));Assert.Null(store.LoadDeploymentCoverage(TestData.TenantB,e.Metadata.Id));Assert.DoesNotContain(Directory.EnumerateFiles(root.Paths.TenantsDirectory,"*",SearchOption.AllDirectories),f=>f.EndsWith(".tmp",StringComparison.Ordinal));}
    [Fact] public async Task Existing_original_is_never_overwritten_even_with_the_same_report()
    {using var root=new TempRoot();var store=new EvidenceStore(root.Paths,BuildStandard.Core.Diagnostics.NullLog.Instance);var e=await DeploymentCoverageEvidenceTests.Evidence();var file=store.SaveDeploymentCoverage(e);var original=File.ReadAllBytes(file);Assert.Throws<SafetyViolationException>(()=>store.SaveDeploymentCoverage(e));Assert.Equal(original,File.ReadAllBytes(file));}
    [Fact] public async Task A_moved_report_cannot_impersonate_a_different_record_ID()
    {using var root=new TempRoot();var store=new EvidenceStore(root.Paths,BuildStandard.Core.Diagnostics.NullLog.Instance);var e=await DeploymentCoverageEvidenceTests.Evidence();var file=store.SaveDeploymentCoverage(e);File.Move(file,Path.Combine(Path.GetDirectoryName(file)!,TestData.Office+".json.zip"));Assert.Throws<IntegrityException>(()=>store.LoadDeploymentCoverage(TestData.TenantA,TestData.Office));}
    [Theory][InlineData("../other")][InlineData("")][InlineData("00000000-0000-0000-0000-000000000000")]
    public void Invalid_record_paths_are_refused_before_filesystem_use(string id)
    {using var root=new TempRoot();var store=new EvidenceStore(root.Paths,BuildStandard.Core.Diagnostics.NullLog.Instance);Assert.Throws<ConfigurationException>(()=>store.LoadDeploymentCoverage(TestData.TenantA,id));}
    [Fact] public async Task Handover_preserves_the_registered_original_bundle_as_opaque_bytes()
    {using var root=new TempRoot();var store=new EvidenceStore(root.Paths,BuildStandard.Core.Diagnostics.NullLog.Instance);var e=await DeploymentCoverageEvidenceTests.Evidence();var file=store.SaveDeploymentCoverage(e);var bytes=File.ReadAllBytes(file);var backup=new WorkspaceBackup(root.Paths);var archive=backup.Create();var destination=Path.Combine(root.Root,"restored");backup.RestoreSeparate(archive,destination,WorkspaceBackup.ArchiveDigest(archive));Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(destination,"data",Path.GetRelativePath(root.Paths.DataDirectory,file))));Assert.True(WorkspaceBackup.VerifyRestored(destination)>0);}
    [Theory][InlineData("other.zip")][InlineData("tenants/unknown/coverage-reports/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa.json.zip")][InlineData("tenants/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa/coverage-reports/../other.zip")][InlineData("tenants/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa/coverage-reports/00000000-0000-0000-0000-000000000000.json.zip")]
    public void Registered_coverage_bundles_do_not_enable_arbitrary_archives_in_handover(string relative)
    {using var root=new TempRoot();var file=Path.Combine(root.Paths.DataDirectory,relative.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllBytes(file,[1,2,3]);Assert.Throws<ConfigurationException>(()=>new WorkspaceBackup(root.Paths).Create());}
}
