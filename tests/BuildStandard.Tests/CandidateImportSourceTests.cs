using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Standards.Authoring;
using Xunit;
namespace BuildStandard.Tests;

public sealed class CandidateImportSourceTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void Digest_cites_original_bytes_including_optional_BOM(bool bom)
    {
        using var root = new TempRoot(); var path = Path.Combine(root.Paths.ReportsDirectory, "source.json");
        var json = "{\"displayName\":\"CFG - Café\"}\r\n";
        var bytes = (bom ? Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json)) : Encoding.UTF8.GetBytes(json)).ToArray();
        File.WriteAllBytes(path, bytes); var source = CandidateImportSource.ReadFile(path);
        Assert.Equal(json, source.Json); Assert.Equal(CanonicalJson.Sha256Hex(bytes), source.Sha256);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }
    [Theory][InlineData("[]")][InlineData("null")][InlineData("{bad")]
    [InlineData("{\"id\":1,\"ID\":2}")][InlineData("{\"settings\":[{\"value\":1,\"Value\":2}]}")]
    public void Malformed_nonobject_or_duplicate_source_is_refused(string json)
    {
        using var root = new TempRoot(); var path = Path.Combine(root.Paths.ReportsDirectory, "source.json");
        File.WriteAllText(path, json); Assert.ThrowsAny<ToolkitException>(() => CandidateImportSource.ReadFile(path));
    }
    [Theory][InlineData("size")][InlineData("depth")][InlineData("encoding")]
    public void Unbounded_or_invalid_encoding_is_not_admitted(string fault)
    {
        using var root = new TempRoot(); var path = Path.Combine(root.Paths.ReportsDirectory, "source.json");
        var bytes = fault switch {
            "size" => Encoding.UTF8.GetBytes("{\"value\":\"" + new string('x', CandidateImportSource.MaximumBytes) + "\"}"),
            "depth" => Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("{\"x\":", 50)) + "0" + new string('}', 50)),
            _ => new byte[] { 123, 34, 120, 34, 58, 34, 0xff, 34, 125 }
        };
        File.WriteAllBytes(path, bytes); Assert.ThrowsAny<ToolkitException>(() => CandidateImportSource.ReadFile(path));
    }
}
