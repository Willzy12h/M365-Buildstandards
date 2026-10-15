using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Engine.Identity;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ObjectResolverTests
{
    [Fact]
    public async Task Wrong_tenant_is_refused_before_a_read()
    {
        var graph = new FakeGraphClient(TestData.Standard());
        await Assert.ThrowsAsync<TenantMismatchException>(() => ObjectResolver.SearchAsync(graph, TestData.TenantB, ObjectLookupKind.Group, "Pilot", default));
        Assert.Empty(graph.Reads);
    }
    [Fact]
    public async Task Duplicate_names_remain_distinct_explicit_choices_and_filter_values_are_escaped()
    {
        var graph = new FakeGraphClient(TestData.Standard());
        graph.Add("/groups", new JsonObject { ["id"] = TestData.Operator, ["displayName"] = "O'Brien" });
        graph.Add("/groups", new JsonObject { ["id"] = TestData.Emergency, ["displayName"] = "O'Brien" });
        var result = await ObjectResolver.SearchAsync(graph, TestData.TenantA, ObjectLookupKind.Group, "O'Brien", default);
        Assert.Equal(2, result.Count); Assert.NotEqual(result[0].ObjectId, result[1].ObjectId);
        Assert.All(result, x => Assert.Contains(x.ObjectId, x.Label));
        Assert.Contains("O''Brien", Uri.UnescapeDataString(Assert.Single(graph.Reads)));
        Assert.Empty(graph.Writes);
    }
    [Fact]
    public async Task Failure_is_not_an_empty_success()
    {
        var graph = new FakeGraphClient(TestData.Standard()) { BeforeRead = (_, _) => throw new IOException("synthetic unavailable lookup") };
        await Assert.ThrowsAsync<IOException>(() => ObjectResolver.SearchAsync(graph, TestData.TenantA, ObjectLookupKind.NamedLocation, "Office", default));
    }
}
