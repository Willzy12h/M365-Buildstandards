using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class SecurityIncidentEvidenceTests
{
    internal const string Notice = "Incident context only, not proof of tenant safety or complete Defender coverage. Availability depends on licensed and connected workloads and delegated roles. Business Premium/P1 does not imply P2 Identity Protection. Retention is unavailable unless returned by Microsoft. Resolved is a source workflow state, not verified remediation. No alert content or response action is included.";
    internal static JsonObject Fixture(bool row = false)
    {
        var value = JsonSerializer.SerializeToNode(OperationalContextSchemaTests.Health(), RegisteredReportJson.Strict)!.AsObject();
        value["reportId"] = "security-incidents"; value["accessIntent"] = "security-incidents";
        value["incidentSelection"] = new JsonObject { ["startUtc"] = "2026-10-19T12:00:00Z", ["endUtc"] = "2026-10-20T12:00:00Z" };
        value["sources"] = new JsonArray(new JsonObject { ["api"] = "v1.0", ["registeredRoute"] = "/security/incidents", ["reference"] = "https://learn.microsoft.com/en-us/graph/api/security-list-incidents?view=graph-rest-1.0" });
        var rows = new JsonArray();
        if (row) rows.Add(new JsonObject
        {
            ["id"] = "123", ["name"] = "Synthetic incident", ["readStatus"] = "Collected", ["error"] = null,
            ["createdAt"] = "2026-10-20T09:00:00Z", ["modifiedAt"] = "2026-10-20T10:00:00Z",
            ["severity"] = "medium", ["status"] = "active", ["classification"] = "unknown", ["determination"] = "unknown",
            ["priorityScore"] = null, ["redirectIncidentId"] = null, ["incidentWebUrl"] = null
        });
        value["sections"] = new JsonArray(new JsonObject { ["id"] = "security-incidents", ["status"] = "Collected", ["error"] = null, ["limitations"] = new JsonArray(Notice), ["rows"] = rows });
        return value;
    }
    internal static string Seal(JsonObject value) { value["integrityDigest"] = EvidenceIntegrity.Compute(JsonSerializer.Deserialize<OperationalContextReport>(value.ToJsonString(), RegisteredReportJson.Strict)!); return value.ToJsonString(); }
    [Theory][InlineData(false)][InlineData(true)]
    public void Completed_exact_window_preserves_empty_or_nullable_priority_without_native_safety_claim(bool row)
    {
        var result = OperationalContextSchema.Read(Seal(Fixture(row)), TestData.TenantA);
        Assert.Equal("security-incidents", result.ReportId); Assert.Equal("Collected", result.Status);
        Assert.Contains(Notice, result.Sections.Single().Limitations);
        if (row) Assert.Null(result.Sections.Single().Rows.Single()["priorityScore"]); else Assert.Empty(result.Sections.Single().Rows);
    }
    [Fact] public void Updates_observed_during_capture_are_not_mistaken_for_future_data()
    {
        var value = Fixture(true); value["endedAt"] = "2026-10-20T12:01:00Z";
        value["sections"]![0]!["rows"]![0]!["modifiedAt"] = "2026-10-20T12:00:30Z";
        var result = OperationalContextSchema.Read(Seal(value), TestData.TenantA);
        Assert.Equal("Collected", result.Status);
    }
    [Theory][InlineData("future-value")][InlineData("duplicate")]
    public void Unsupported_values_and_duplicate_observations_remain_explicitly_partial(string fault)
    {
        var fixture = Fixture(true); fixture["status"] = "Partial"; var section = fixture["sections"]![0]!;
        section["status"] = "Partial"; section["error"] = "Synthetic incomplete source";
        var row = section["rows"]![0]!; row["readStatus"] = "Partial"; row["error"] = "Synthetic unknown observation";
        if (fault == "future-value") row["severity"] = "FutureSeverity";
        else section["rows"]!.AsArray().Add(row.DeepClone());
        var result = OperationalContextSchema.Read(Seal(fixture), TestData.TenantA);
        Assert.Equal("Partial", result.Status); Assert.Equal(fault == "duplicate" ? 2 : 1, result.Sections.Single().Rows.Count);
    }
    [Theory][InlineData("window")][InlineData("priority")][InlineData("private-content")][InlineData("source")][InlineData("missing-notice")][InlineData("unknown-as-complete")]
    public void Resealed_invalid_or_overclaiming_incident_evidence_is_refused(string fault)
    {
        var value = Fixture(true); var row = value["sections"]![0]!["rows"]![0]!;
        switch (fault)
        {
            case "window": value["incidentSelection"]!["endUtc"] = "2026-10-21T12:00:00Z"; break;
            case "priority": row["priorityScore"] = 101; break;
            case "private-content": row["comments"] = new JsonArray("must never be retained"); break;
            case "source": value["sources"]![0]!["registeredRoute"] = "/security/alerts_v2"; break;
            case "missing-notice": value["sections"]![0]!["limitations"] = new JsonArray(); break;
            case "unknown-as-complete": row["severity"] = "FutureSeverity"; break;
        }
        Assert.ThrowsAny<ToolkitException>(() => OperationalContextSchema.Read(Seal(value), TestData.TenantA));
    }
}
