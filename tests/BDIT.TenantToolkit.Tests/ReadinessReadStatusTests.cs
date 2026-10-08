using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Prerequisites;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ReadinessReadStatusTests
{
    private const string Second = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Failed_reads_stay_unknown_and_explain_the_next_step_without_service_error_text(int status)
    {
        var graph = new ReadGraph { Error = new GraphRequestException(status, "GET", "/synthetic", "Synthetic", "secret-service-response") };
        var rows = await Check(graph);
        Assert.Equal(4, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal("Unknown", row.Status);
            Assert.Contains("HTTP " + status, row.Detail);
            Assert.DoesNotContain("secret-service-response", row.Detail + row.NextStep);
            Assert.Contains("failed read does not prove", row.NextStep);
        });
        Assert.Empty(graph.Writes);
    }

    [Theory]
    [InlineData("accountEnabled", "null")]
    [InlineData("accountEnabled", "\"true\"")]
    [InlineData("onPremisesSyncEnabled", "\"false\"")]
    [InlineData("onPremisesSyncEnabled", "{}")]
    [InlineData("userType", "\"futureMember\"")]
    [InlineData("userPrincipalName", "true")]
    [InlineData("id", "\"bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb\"")]
    public async Task Malformed_or_mismatched_emergency_identity_is_unknown(string field, string json)
    {
        var graph = new ReadGraph { ChangeUser = user => user[field] = JsonNode.Parse(json) };
        Assert.Equal("Unknown", (await Check(graph)).Single(r => r.ControlId == "ID-001").Status);
        Assert.Empty(graph.Writes);
    }

    [Fact]
    public async Task A_missing_sync_field_is_not_evidence_of_cloud_origin()
    {
        var graph = new ReadGraph { ChangeUser = user => user.Remove("onPremisesSyncEnabled") };
        Assert.Equal("Unknown", (await Check(graph)).Single(r => r.ControlId == "ID-001").Status);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("false")]
    public async Task Explicit_null_or_false_sync_supports_cloud_origin_but_not_operational_acceptance(string json)
    {
        var graph = new ReadGraph { ChangeUser = user => user["onPremisesSyncEnabled"] = JsonNode.Parse(json) };
        var row = (await Check(graph)).Single(r => r.ControlId == "ID-001");
        Assert.Equal("Configuration observed", row.Status);
        Assert.Contains("This read cannot prove", row.NextStep);
    }

    [Theory]
    [InlineData("accountEnabled", "false")]
    [InlineData("onPremisesSyncEnabled", "true")]
    [InlineData("userType", "\"Guest\"")]
    public async Task A_known_unsuitable_account_requires_action_instead_of_claiming_configuration(string field, string json)
    {
        var graph = new ReadGraph { ChangeUser = user => user[field] = JsonNode.Parse(json) };
        Assert.Equal("Action required", (await Check(graph)).Single(r => r.ControlId == "ID-001").Status);
    }

    [Fact]
    public async Task Duplicated_profile_ids_do_not_establish_two_independent_accounts()
    {
        var profile = Profile();
        profile.Parameters.EmergencyAccountIds = new() { TestData.Emergency, TestData.Emergency.ToUpperInvariant() };
        Assert.Equal("Action required", (await new ServiceReadinessService().CheckAsync(new ReadGraph(), profile, default)).Single(r => r.ControlId == "ID-001").Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("true")]
    [InlineData("\"futureStatus\"")]
    public async Task Missing_or_unrecognised_binding_state_is_unknown(string? json)
    {
        var graph = new ReadGraph { Binding = json is null ? new() : new() { ["bindStatus"] = JsonNode.Parse(json) } };
        Assert.Equal("Unknown", (await Check(graph)).Single(r => r.ControlId == "ENR-006").Status);
    }

    [Theory]
    [InlineData("notBound")]
    [InlineData("bound")]
    public async Task Known_unvalidated_binding_requires_action(string state)
    {
        var graph = new ReadGraph { Binding = new() { ["bindStatus"] = state } };
        Assert.Equal("Action required", (await Check(graph)).Single(r => r.ControlId == "ENR-006").Status);
    }

    [Fact]
    public async Task Malformed_role_rows_do_not_become_an_empty_count()
    {
        var graph = new ReadGraph { Roles = new List<JsonObject> { new() } };
        Assert.Equal("Unknown", (await Check(graph)).Single(r => r.ControlId == "ID-003").Status);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_converted_to_a_completed_readiness_result()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ServiceReadinessService().CheckAsync(new ReadGraph(), Profile(), cancel.Token));
    }

    [Fact]
    public async Task Cross_tenant_profile_is_refused_before_any_read()
    {
        var profile = Profile(); profile.TenantId = TestData.TenantB;
        var graph = new ReadGraph();
        await Assert.ThrowsAsync<TenantMismatchException>(() => new ServiceReadinessService().CheckAsync(graph, profile, default));
        Assert.Equal(0, graph.Reads);
    }

    private static TenantProfile Profile()
    {
        var profile = TestData.Profile(); profile.Parameters.EmergencyAccountIds.Add(Second); return profile;
    }
    private static Task<IReadOnlyList<ServiceReadiness>> Check(ReadGraph graph) => new ServiceReadinessService().CheckAsync(graph, Profile(), default);

    private sealed class ReadGraph : IGraphClient
    {
        public string TenantId => TestData.TenantA;
        public SessionMode Mode => SessionMode.Assessment;
        public Exception? Error { get; init; }
        public Action<JsonObject>? ChangeUser { get; init; }
        public JsonObject Binding { get; init; } = new() { ["bindStatus"] = "boundAndValidated" };
        public IReadOnlyList<JsonObject> Roles { get; init; } = new List<JsonObject>();
        public List<string> Writes { get; } = new();
        public int Reads { get; private set; }
        public Task<JsonObject> GetAsync(GraphApi api, string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Reads++;
            if (Error is not null) throw Error;
            if (path.StartsWith("/users/", StringComparison.Ordinal))
            {
                var id = path.Split('?')[0]["/users/".Length..];
                var user = new JsonObject { ["id"] = id, ["displayName"] = "Synthetic emergency account", ["userPrincipalName"] = "emergency@synthetic.onmicrosoft.com", ["accountEnabled"] = true, ["userType"] = "Member", ["onPremisesSyncEnabled"] = null };
                ChangeUser?.Invoke(user); return Task.FromResult(user);
            }
            if (path.Contains("applePush", StringComparison.Ordinal)) return Task.FromResult(new JsonObject { ["expirationDateTime"] = DateTimeOffset.UtcNow.AddYears(1).ToString("O") });
            return Task.FromResult((JsonObject)Binding.DeepClone());
        }
        public Task<IReadOnlyList<JsonObject>> GetAllAsync(GraphApi api, string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Reads++;
            if (Error is not null) throw Error;
            return Task.FromResult(Roles);
        }
        public Task<JsonObject> WriteAsync(GraphApi api, GraphWriteMethod method, string path, JsonObject payload, CancellationToken ct)
        {
            Writes.Add(path); throw new InvalidOperationException("Readiness must never write.");
        }
    }
}
