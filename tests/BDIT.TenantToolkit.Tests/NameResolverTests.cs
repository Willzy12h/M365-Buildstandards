using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// Names are display only, but every page and report that shows tenant objects builds a resolver from the whole
/// capture. A captured value of an unexpected type used to throw from inside that, taking the page with it.
/// </summary>
public class NameResolverTests
{
    private static TenantSnapshot Capture(params JsonObject[] groups)
    {
        var snapshot = new TenantSnapshot { TenantId = "11111111-1111-1111-1111-111111111111" };
        snapshot.Collections["groups"] = new CollectionCapture { Status = CaptureStatus.Collected, Items = groups.ToList(), Count = groups.Length };
        return snapshot;
    }

    [Fact]
    public void An_object_whose_name_is_not_text_is_skipped_rather_than_stopping_resolution()
    {
        var resolver = NameResolver.FromSnapshot(Capture(
            new JsonObject { ["id"] = "aaaaaaaa-aaaa-aaaa-aaaa-000000000001", ["displayName"] = 42 },
            new JsonObject { ["id"] = 7, ["displayName"] = "Numeric id" },
            new JsonObject { ["id"] = "aaaaaaaa-aaaa-aaaa-aaaa-000000000002", ["displayName"] = "Pilot devices" }));

        Assert.Equal("Pilot devices [aaaaaaaa-aaaa-aaaa-aaaa-000000000002]", resolver.Display("aaaaaaaa-aaaa-aaaa-aaaa-000000000002"));
        Assert.Equal("Unresolved [aaaaaaaa-aaaa-aaaa-aaaa-000000000001]", resolver.Display("aaaaaaaa-aaaa-aaaa-aaaa-000000000001"));
    }

    [Fact]
    public void An_assignment_target_with_an_unexpected_type_is_summarised_rather_than_thrown()
    {
        var resolver = NameResolver.FromSnapshot(Capture());
        var item = new JsonObject
        {
            ["_assignments"] = new JsonArray(new JsonObject { ["target"] = new JsonObject { ["@odata.type"] = 3, ["groupId"] = true } })
        };

        var summary = resolver.AssignmentSummary(item);

        Assert.Equal("Assigned (targets unreadable)", summary);
    }
}
