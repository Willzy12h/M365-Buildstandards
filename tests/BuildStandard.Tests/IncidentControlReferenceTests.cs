using System.Reflection;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Engine.Reports;
using BuildStandard.Engine.Standards;
using Xunit;
namespace BuildStandard.Tests;
public sealed class IncidentControlReferenceTests
{
    private static Type Api() { var type = typeof(ExternalControlReferences).Assembly.GetType("BuildStandard.Engine.Reports.IncidentControlReferences"); Assert.NotNull(type); return type; }
    private static object Invoke(string method, params object?[] args)
    {
        try { return Api().GetMethod(method)!.Invoke(null, args)!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    [Fact] public void Reference_uses_original_incident_bytes_and_exact_verified_catalogue_without_native_verdict()
    {
        using var root = new TempRoot(); root.WriteStandard("test.json", TestData.StandardJson); root.WriteManifest();
        var standard = new StandardsLoader(root.Paths, NullLog.Instance).Load("test.json");
        var bytes = Encoding.UTF8.GetBytes(SecurityIncidentEvidenceTests.Seal(SecurityIncidentEvidenceTests.Fixture(true)));
        var source = OperationalContextFile.Read(bytes, TestData.TenantA);
        var link = Invoke("Create", root.Paths, source, "123", standard, "CA-001");
        Assert.True((bool)Invoke("Resolve", root.Paths, link, source, standard).GetType().GetProperty("Resolved")!.GetValue(Invoke("Resolve", root.Paths, link, source, standard))!);
        Assert.Equal(bytes, source.CopyOriginal());
        Assert.False((bool)Invoke("Resolve", root.Paths, link, source, null).GetType().GetProperty("Resolved")!.GetValue(Invoke("Resolve", root.Paths, link, source, null))!);
    }
    [Theory][InlineData("missing")][InlineData("duplicate")][InlineData("changed-catalogue")]
    public void Ambiguous_incidents_and_unverified_targets_cannot_be_linked(string fault)
    {
        using var root = new TempRoot(); root.WriteStandard("test.json", TestData.StandardJson); root.WriteManifest();
        var standard = new StandardsLoader(root.Paths, NullLog.Instance).Load("test.json"); var fixture = SecurityIncidentEvidenceTests.Fixture(true);
        if (fault == "duplicate")
        {
            fixture["status"] = "Partial"; var section = fixture["sections"]![0]!; section["status"] = "Partial"; section["error"] = "Duplicate";
            var row = section["rows"]![0]!; row["readStatus"] = "Partial"; row["error"] = "Duplicate"; section["rows"]!.AsArray().Add(row.DeepClone());
        }
        var source = OperationalContextFile.Read(Encoding.UTF8.GetBytes(SecurityIncidentEvidenceTests.Seal(fixture)), TestData.TenantA);
        if (fault == "changed-catalogue") standard.Controls[0].Name = "Changed";
        Assert.ThrowsAny<ToolkitException>(() => Invoke("Create", root.Paths, source, fault == "missing" ? "not-returned" : "123", standard, "CA-001"));
    }
    [Fact] public void Reference_document_round_trips_exact_source_and_rejects_duplicate_or_changed_identity()
    {
        using var root = new TempRoot(); root.WriteStandard("test.json", TestData.StandardJson); root.WriteManifest();
        var standard = new StandardsLoader(root.Paths, NullLog.Instance).Load("test.json");
        var source = OperationalContextFile.Read(Encoding.UTF8.GetBytes(SecurityIncidentEvidenceTests.Seal(SecurityIncidentEvidenceTests.Fixture(true))), TestData.TenantA);
        var link = IncidentControlReferences.Create(root.Paths, source, "123", standard, "CA-001");
        var codec = typeof(IncidentControlReferences).Assembly.GetType("BuildStandard.Engine.Reports.IncidentControlReferenceDocument"); Assert.NotNull(codec);
        var write = codec.GetMethod("Write")!; var read = codec.GetMethod("Read")!;
        var bytes = (byte[])write.Invoke(null, new object[] { source, new[] { link } })!;
        var links = (IReadOnlyList<IncidentControlReference>)read.Invoke(null, new object[] { bytes, source })!;
        Assert.Equal(link, Assert.Single(links));
        var duplicate = Assert.Throws<TargetInvocationException>(() => write.Invoke(null, new object[] { source, new[] { link, link } }));
        Assert.IsAssignableFrom<ToolkitException>(duplicate.InnerException);
        var fixture = SecurityIncidentEvidenceTests.Fixture(true); fixture["sections"]![0]!["rows"]![0]!["name"] = "Different source bytes";
        var changed = OperationalContextFile.Read(Encoding.UTF8.GetBytes(SecurityIncidentEvidenceTests.Seal(fixture)), TestData.TenantA);
        var mismatch = Assert.Throws<TargetInvocationException>(() => read.Invoke(null, new object[] { bytes, changed }));
        Assert.IsAssignableFrom<ToolkitException>(mismatch.InnerException);
    }
}
