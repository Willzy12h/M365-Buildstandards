using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;

public sealed class ExternalCheckResultsTests
{
    internal static JsonObject Fixture() => new()
    {
        ["kind"] = "external-check-results", ["schemaVersion"] = 1, ["toolName"] = "Synthetic checks", ["toolVersion"] = "1.0",
        ["sourceRunId"] = "synthetic-run", ["tenantId"] = TestData.TenantA, ["performedAt"] = "2026-10-20T09:00:00Z",
        ["sourceState"] = "Partial", ["rows"] = new JsonArray(new JsonObject { ["testId"] = "TEST-1", ["outcome"] = "Passed", ["title"] = "A claimed result" }),
        ["integrityDigest"] = ""
    };
    internal static byte[] Bytes(JsonObject source)
    {
        source["integrityDigest"] = ""; source["integrityDigest"] = CanonicalJson.Sha256(source);
        return Encoding.UTF8.GetBytes(source.ToJsonString());
    }
    private static ExternalCheckImport Read(byte[] bytes, string? tenant = null) => ExternalCheckResultsSchema.Read(bytes, tenant ?? TestData.TenantA);
    [Theory][InlineData(false)][InlineData(true)]
    public void File_reader_preserves_optional_BOM_and_original_source(bool bom)
    {
        using var root = new TempRoot(); var bytes = Bytes(Fixture());
        if (bom) bytes = new byte[] { 0xef, 0xbb, 0xbf }.Concat(bytes).ToArray();
        var file = Path.Combine(root.Root, "external.json"); File.WriteAllBytes(file, bytes);
        var imported = ExternalCheckResultsSchema.ReadFile(file, TestData.TenantA);
        Assert.Equal(bytes, imported.GetOriginalBytes()); Assert.Equal(bytes, File.ReadAllBytes(file));
    }
    [Theory][InlineData("")][InlineData(" ")]
    public void Optional_plain_text_may_be_returned_empty_without_becoming_missing(string value)
    {
        var source = Fixture(); source["rows"]![0]!["reason"] = value; source["rows"]![0]!["reference"] = value;
        var admitted = Read(Bytes(source));
        Assert.Equal(value, admitted.Evidence.Rows.Single().Reason); Assert.Equal(value, admitted.Evidence.Rows.Single().Reference);
    }
    [Fact] public void Valid_source_preserves_original_bytes_and_partial_claim_without_native_assessment_authority()
    {
        var bytes = Bytes(Fixture()); var original = bytes.ToArray(); var admitted = Read(bytes);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)).ToLowerInvariant(), admitted.OriginalSha256);
        bytes[0] = 0;
        Assert.Equal(original, admitted.GetOriginalBytes());
        var copy = admitted.GetOriginalBytes(); copy[0] = 0;
        Assert.Equal(original, admitted.GetOriginalBytes());
        var evidence = ToolkitJson.ToNode(admitted.Evidence)!;
        Assert.Equal("Partial", evidence["sourceState"]!.GetValue<string>());
        Assert.Contains("untrusted", admitted.AssuranceNotice, StringComparison.OrdinalIgnoreCase);
        Assert.ThrowsAny<ToolkitException>(() => ReportEnvelopeDispatcher.Read(Encoding.UTF8.GetString(original), TestData.TenantA));
    }
    [Fact] public void Future_outcome_stays_raw_and_is_classified_unclassified()
    {
        var source = Fixture(); source["rows"]![0]!["outcome"] = "FutureSuccess";
        var admitted = Read(Bytes(source)); var evidence = ToolkitJson.ToNode(admitted.Evidence)!;
        Assert.Equal("FutureSuccess", evidence["rows"]![0]!["outcome"]!.GetValue<string>());
        Assert.Equal("Unclassified", admitted.Evidence.Rows.Single().DisplayOutcome);
    }
    [Theory]
    [InlineData("tenant")][InlineData("version")][InlineData("unknown-field")][InlineData("row-field")]
    [InlineData("null")][InlineData("timestamp")][InlineData("state")][InlineData("duplicate-id")]
    [InlineData("long-id")][InlineData("long-text")][InlineData("too-many")][InlineData("wrong-kind")]
    public void Invalid_source_claims_are_refused_before_admission(string fault)
    {
        var source = Fixture();
        switch (fault)
        {
            case "tenant": source["tenantId"] = TestData.TenantB; break;
            case "version": source["schemaVersion"] = 2; break;
            case "unknown-field": source["script"] = "Do-Not-Run"; break;
            case "row-field": source["rows"]![0]!["html"] = "<script>never</script>"; break;
            case "null": source["toolVersion"] = null; break;
            case "timestamp": source["performedAt"] = "2026-10-20T09:00:00+02:00"; break;
            case "state": source["sourceState"] = "SuccessProbably"; break;
            case "duplicate-id": source["rows"]!.AsArray().Add(source["rows"]![0]!.DeepClone()); break;
            case "long-id": source["rows"]![0]!["testId"] = new string('x', 201); break;
            case "long-text": source["rows"]![0]!["reason"] = new string('x', 4097); break;
            case "too-many": source["rows"] = new JsonArray(Enumerable.Range(0, 5001).Select(i => (JsonNode)new JsonObject { ["testId"] = "T-" + i, ["outcome"] = "Passed", ["title"] = "Synthetic" }).ToArray()); break;
            case "wrong-kind": source["kind"] = "Pester"; break;
        }
        Assert.ThrowsAny<ToolkitException>(() => Read(Bytes(source)));
    }
    [Theory][InlineData("digest")][InlineData("duplicate")][InlineData("encoding")][InlineData("depth")][InlineData("size")]
    public void Malformed_original_is_not_silently_normalised(string fault)
    {
        var bytes = Bytes(Fixture()); var json = Encoding.UTF8.GetString(bytes);
        bytes = fault switch
        {
            "digest" => Encoding.UTF8.GetBytes(json.Replace("A claimed result", "Tampered result", StringComparison.Ordinal)),
            "duplicate" => Encoding.UTF8.GetBytes(json.Replace("\"kind\":", "\"KIND\":\"external-check-results\",\"kind\":", StringComparison.Ordinal)),
            "encoding" => [0xff, 0xfe, 0],
            "depth" => Encoding.UTF8.GetBytes(new string('[', 17) + "0" + new string(']', 17)),
            _ => new byte[8 * 1024 * 1024 + 1]
        };
        Assert.ThrowsAny<ToolkitException>(() => Read(bytes));
    }
}
