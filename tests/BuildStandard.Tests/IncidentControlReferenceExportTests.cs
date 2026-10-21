using System.Reflection;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class IncidentControlReferenceExportTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void Export_is_source_bound_and_never_overwrites_or_admits_another_tenant(bool wrongTenant)
    {
        using var root = new TempRoot(); var original = Encoding.UTF8.GetBytes(SecurityIncidentEvidenceTests.Seal(SecurityIncidentEvidenceTests.Fixture(true)));
        var source = OperationalContextFile.Read(original,TestData.TenantA);
        var links = new[] { new IncidentControlReference(CanonicalJson.Sha256Hex(original),"123","historical-unavailable",new string('a',64),"CA-001") };
        var method = typeof(ReportExporter).GetMethod("ExportIncidentControlReferences"); Assert.NotNull(method);
        var exporter = new ReportExporter(root.Paths,"Synthetic");
        if (wrongTenant)
        {
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(exporter,new object[] {source,links,TestData.TenantB}));
            Assert.IsAssignableFrom<ToolkitException>(error.InnerException); Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory)); return;
        }
        var file = (string)method.Invoke(exporter,new object[] {source,links,TestData.TenantA})!;
        var bytes = File.ReadAllBytes(file); Assert.Equal(links,IncidentControlReferenceDocument.Read(bytes,source));
        var next = (string)method.Invoke(exporter,new object[] {source,links,TestData.TenantA})!;
        Assert.NotEqual(file,next); Assert.Equal(bytes,File.ReadAllBytes(file)); Assert.Equal(original,source.CopyOriginal());
    }
}
