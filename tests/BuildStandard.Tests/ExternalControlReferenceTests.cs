using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;
using BuildStandard.Engine.Standards;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ExternalControlReferenceTests
{
    private static StandardCatalogue Standard(TempRoot root)
    {
        root.WriteStandard("test.json", TestData.StandardJson); root.WriteManifest();
        return new StandardsLoader(root.Paths, NullLog.Instance).Load("test.json");
    }
    [Fact] public void Explicit_reference_keeps_exact_source_and_standard_identity_without_changing_either()
    {
        using var root = new TempRoot(); var standard = Standard(root);
        var bytes = ExternalCheckResultsTests.Bytes(ExternalCheckResultsTests.Fixture()); var source = ExternalCheckResultsSchema.Read(bytes, TestData.TenantA);
        var link = ExternalControlReferences.Create(root.Paths, source, "TEST-1", standard, "CA-001");
        Assert.Equal(source.OriginalSha256, link.SourceSha256);
        Assert.Equal(standard.IntegrityDigest, link.StandardSha256);
        Assert.Equal(standard.Release, link.StandardRelease);
        var result = ExternalControlReferences.Resolve(root.Paths, link, source, standard);
        Assert.True(result.Resolved);
        Assert.Equal(bytes, source.GetOriginalBytes()); Assert.Equal("Partial", source.Evidence.SourceState);
    }
    [Theory][InlineData("test")][InlineData("control")][InlineData("modified-standard")]
    public void Creation_refuses_unknown_identity_or_modified_catalogue(string fault)
    {
        using var root = new TempRoot(); var standard = Standard(root);
        var source = ExternalCheckResultsSchema.Read(ExternalCheckResultsTests.Bytes(ExternalCheckResultsTests.Fixture()), TestData.TenantA);
        if (fault == "modified-standard") standard.Controls[0].Name = "Changed in memory";
        Assert.ThrowsAny<ToolkitException>(() => ExternalControlReferences.Create(root.Paths, source, fault == "test" ? "missing" : "TEST-1", standard, fault == "control" ? "missing" : "CA-001"));
    }
    [Theory][InlineData("source")][InlineData("missing-standard")][InlineData("changed-standard")]
    public void Resolution_never_rebinds_a_reference_to_different_source_or_catalogue(string fault)
    {
        using var root = new TempRoot(); var standard = Standard(root); var fixture = ExternalCheckResultsTests.Fixture();
        var source = ExternalCheckResultsSchema.Read(ExternalCheckResultsTests.Bytes(fixture), TestData.TenantA);
        var link = ExternalControlReferences.Create(root.Paths, source, "TEST-1", standard, "CA-001");
        if (fault == "source") { fixture["sourceRunId"] = "different-run"; source = ExternalCheckResultsSchema.Read(ExternalCheckResultsTests.Bytes(fixture), TestData.TenantA); }
        if (fault == "changed-standard") standard.Controls[0].Name = "Changed";
        var result = ExternalControlReferences.Resolve(root.Paths, link, source, fault == "missing-standard" ? null : standard);
        Assert.False(result.Resolved);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }
}
