using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ExternalControlReferenceDocumentTests
{
    private static ExternalCheckImport Source() => ExternalCheckResultsSchema.Read(ExternalCheckResultsTests.Bytes(ExternalCheckResultsTests.Fixture()), TestData.TenantA);
    private static JsonObject Document(ExternalCheckImport source) => new()
    {
        ["kind"] = "external-control-references", ["schemaVersion"] = 1, ["sourceSha256"] = source.OriginalSha256,
        ["references"] = new JsonArray(new JsonObject { ["sourceSha256"] = source.OriginalSha256, ["sourceTestId"] = "TEST-1",
            ["standardRelease"] = "historical-unavailable", ["standardSha256"] = new string('a', 64), ["controlId"] = "CA-001" })
    };
    private static IReadOnlyList<ExternalControlReference> Read(JsonObject document, ExternalCheckImport source) =>
        ExternalControlReferenceDocument.Read(Encoding.UTF8.GetBytes(document.ToJsonString()), source);
    [Fact] public void Historical_reference_remains_exact_and_unresolved_without_rebinding_to_a_current_standard()
    {
        using var root = new TempRoot(); var source = Source(); var original = source.GetOriginalBytes();
        var link = Assert.Single(Read(Document(source), source));
        Assert.Equal("historical-unavailable", link.StandardRelease);
        Assert.False(ExternalControlReferences.Resolve(root.Paths, link, source, null).Resolved);
        Assert.Equal(original, source.GetOriginalBytes());
        Assert.Equal(link, Assert.Single(ExternalControlReferenceDocument.Read(ExternalControlReferenceDocument.Write(source, [link]), source)));
    }
    [Theory][InlineData("source")][InlineData("test")][InlineData("digest")][InlineData("unknown-field")][InlineData("conflict")][InlineData("duplicate")]
    public void Changed_source_invalid_shape_and_conflicting_links_are_refused(string fault)
    {
        var source = Source(); var document = Document(source); var row = document["references"]![0]!;
        switch (fault)
        {
            case "source": document["sourceSha256"] = new string('b', 64); break;
            case "test": row["sourceTestId"] = "not-in-this-source"; break;
            case "digest": row["standardSha256"] = "not-a-digest"; break;
            case "unknown-field": row["compliant"] = true; break;
            case "conflict": var other = row.DeepClone(); other["controlId"] = "CA-002"; document["references"]!.AsArray().Add(other); break;
            case "duplicate": document["references"]!.AsArray().Add(row.DeepClone()); break;
        }
        Assert.ThrowsAny<ToolkitException>(() => Read(document, source));
    }
}
