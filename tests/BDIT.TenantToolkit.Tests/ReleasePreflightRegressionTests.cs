using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public class ReleasePreflightRegressionTests
{
    [Fact]
    public void Engineer_html_collection_status_contains_each_actual_collection_outcome()
    {
        var result = new AssessmentResult
        {
            CollectionStatus = new()
            {
                ["Synthetic collected collection"] = "Collected",
                ["Synthetic failed collection"] = "Not collected: synthetic read refusal"
            }
        };

        var html = HtmlReports.Engineer(result);
        var section = html[html.IndexOf("<h2>Collection status</h2>", StringComparison.Ordinal)..];
        Assert.Contains("<th>Collection</th><th>Status</th>", section, StringComparison.Ordinal);
        Assert.Contains("Synthetic collected collection", section, StringComparison.Ordinal);
        Assert.Contains("Synthetic failed collection", section, StringComparison.Ordinal);
        Assert.Contains("Not collected: synthetic read refusal", section, StringComparison.Ordinal);
        Assert.DoesNotContain("<th>Covers control</th>", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Numbered_releases_sort_numerically_so_latest_fallback_does_not_select_point_nine()
    {
        using var root = new TempRoot();
        var releases = new[] { "2026.09.9", "2026.09.10", "2026.09.11", "2026.09.12", "2026.10.1" };
        foreach (var release in releases)
        {
            var standard = TestData.Standard();
            standard.Release = release;
            root.WriteStandard(release + ".json", ToolkitJson.Serialize(standard));
        }

        var listed = new StandardsLoader(root.Paths, NullLog.Instance).ListReleases();
        Assert.Equal(new[] { "2026.10.1", "2026.09.12", "2026.09.11", "2026.09.10", "2026.09.9" },
            listed.Select(r => r.Release));
    }
}
