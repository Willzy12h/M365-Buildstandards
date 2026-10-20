using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ExchangeFamilyResultTests
{
    private static readonly ReadRunContext Context = new(TestData.TenantA, TestData.Operator, "operator@synthetic.example");
    private static readonly DateTimeOffset Started = DateTimeOffset.Parse("2026-10-20T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private static IReadOnlyList<ReadRunParameter> Parameters =>
    [
        new("MailboxGuids", "Text", TestData.Office, true, JsonValue.Create(Convert.ToBase64String(Encoding.UTF8.GetBytes(TestData.Office)))),
        new("IncludeHidden", "Boolean", "false", true, JsonValue.Create(false))
    ];
    private static ScriptEntry Entry() => Assert.Single(ScriptCatalogue.Load(ScriptLibraryReport2Tests.Library()).Entries);
    private static JsonObject Envelope(ScriptEntry entry)
    {
        var report = InboxRuleEvidenceV2Tests.Report();
        foreach (var row in report.Sections.SelectMany(section => section.Rows)) row["id"] = null;
        return new JsonObject
        {
            ["schemaVersion"] = 1, ["kind"] = "scriptReadResult", ["runId"] = report.RunId,
            ["scriptId"] = entry.Manifest.Id, ["manifestSha256"] = entry.ManifestSha256,
            ["scriptSha256"] = entry.Manifest.ScriptSha256, ["runnerTemplateSha256"] = ReadRunnerTemplate.Sha256,
            ["adapter"] = "exchangeOnline", ["reportId"] = report.ReportId, ["reportSchemaVersion"] = 2,
            ["tenantId"] = Context.TenantId, ["observedAccountUpn"] = Context.AccountUpn,
            ["startedAt"] = report.StartedAt, ["endedAt"] = report.EndedAt, ["runtimeVersion"] = "7.6.7", ["moduleVersion"] = "99.0.0",
            ["parameters"] = new JsonArray(Parameters.Select(ScriptReadRequest.Parameter).ToArray()),
            ["sections"] = JsonSerializer.SerializeToNode(report.Sections, ToolkitJson.Options)
        };
    }
    private static ScriptReadResult Read(JsonObject envelope, ScriptEntry entry) => ScriptReadResult.Read(
        Encoding.UTF8.GetBytes(envelope.ToJsonString()), envelope["runId"]!.GetValue<string>(), entry, Context, Parameters,
        Started, Started.AddMinutes(2));

    [Fact]
    public void Lower_manifest_limit_counts_target_and_rule_rows_together()
    {
        var entry = Assert.Single(ScriptCatalogue.Load(ScriptLibraryReport2Tests.Library(m => m["limits"]!["maximumRows"] = 1)).Entries);
        var envelope = Envelope(entry);
        Assert.ThrowsAny<ToolkitException>(() => Read(envelope, entry));
    }

    [Theory]
    [InlineData("184467440737095516150000")]
    [InlineData("opaque-é-🍵-\"-\\-<&")]
    public void Parent_admits_exact_version_and_computes_one_authoritative_key_without_rewriting_source_values(string identity)
    {
        var entry = Entry(); var envelope = Envelope(entry);
        var raw = envelope["sections"]![1]!["rows"]![0]!.AsObject();
        raw["ruleIdentity"] = identity;
        var before = Encoding.UTF8.GetBytes(envelope.ToJsonString());
        var result = Read(envelope, entry);
        Assert.Equal(2, result.ReportSchemaVersion);
        Assert.Equal(ExchangeObservationKey.TargetRead("exo-inbox-rules", TestData.Office), result.Sections[0].Rows[0]["id"]!.GetValue<string>());
        Assert.Equal(ExchangeObservationKey.InboxRule(TestData.Office, identity), result.Sections[1].Rows[0]["id"]!.GetValue<string>());
        var expected = (JsonObject)raw.DeepClone(); expected["id"] = ExchangeObservationKey.InboxRule(TestData.Office, identity);
        Assert.True(JsonNode.DeepEquals(expected, result.Sections[1].Rows[0]));
        Assert.Equal(before, Encoding.UTF8.GetBytes(envelope.ToJsonString()));
        Assert.False(result.Sections[1].Rows[0]["enabled"]!["value"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("child-key")][InlineData("missing-id")][InlineData("wrong-version")]
    [InlineData("duplicate-source")][InlineData("unknown-source-field")][InlineData("missing-source-id")]
    [InlineData("foreign-parent")][InlineData("unknown-to-false")]
    public void Child_keys_unknown_shapes_and_mismatched_source_claims_are_refused(string fault)
    {
        var entry = Entry(); var envelope = Envelope(entry);
        var sections = envelope["sections"]!.AsArray(); var rows = sections[1]!["rows"]!.AsArray(); var row = rows[0]!.AsObject();
        switch (fault)
        {
            case "child-key": row["id"] = ExchangeObservationKey.InboxRule(TestData.Office, row["ruleIdentity"]!.GetValue<string>()); break;
            case "missing-id": row.Remove("id"); break;
            case "wrong-version": envelope["reportSchemaVersion"] = 1; break;
            case "duplicate-source": rows.Add(row.DeepClone()); break;
            case "unknown-source-field": row["messageContents"] = "never admitted"; break;
            case "missing-source-id": row["ruleIdentity"] = null; break;
            case "foreign-parent": row["mailboxExchangeGuid"] = TestData.TenantB; break;
            case "unknown-to-false": row["enabled"]!["state"] = "Missing"; row["enabled"]!["reason"] = "Synthetic missing value"; break;
        }
        Assert.ThrowsAny<ToolkitException>(() => Read(envelope, entry));
    }
}
