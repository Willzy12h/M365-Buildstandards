using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;
public sealed class SecurityIncidentSelectionTests
{
    private static readonly DateTimeOffset End = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static SecurityIncidentReadSelection Selection(DateTimeOffset start, DateTimeOffset end, DateTimeOffset asOf) => new(start, end, asOf);
    [Fact] public void Fixed_query_uses_only_the_frozen_inclusive_start_exclusive_end_and_bounded_page()
    {
        var selected = Selection(End.AddDays(-31), End, End);
        var path = selected.RegisteredPath;
        Assert.StartsWith("/security/incidents?$filter=", path);
        Assert.Contains("createdDateTime ge 2026-09-19T12:00:00.0000000Z and createdDateTime lt 2026-10-20T12:00:00.0000000Z", Uri.UnescapeDataString(path));
        Assert.EndsWith("&$top=100", path); Assert.DoesNotContain("$select", path); Assert.DoesNotContain("$orderby", path); Assert.DoesNotContain("$expand", path);
    }
    [Theory][InlineData("zero")][InlineData("reverse")][InlineData("wide")][InlineData("future")][InlineData("offset-start")][InlineData("offset-end")][InlineData("offset-clock")]
    public void Invalid_or_unreviewable_time_windows_are_refused(string fault)
    {
        var start = End.AddDays(-1); var end = End; var clock = End;
        switch (fault)
        {
            case "zero": start = end; break;
            case "reverse": start = end.AddTicks(1); break;
            case "wide": start = end.AddDays(-31).AddTicks(-1); break;
            case "future": clock = end.AddTicks(-1); break;
            case "offset-start": start = start.ToOffset(TimeSpan.FromHours(1)); break;
            case "offset-end": end = end.ToOffset(TimeSpan.FromHours(1)); break;
            case "offset-clock": clock = clock.ToOffset(TimeSpan.FromHours(1)); break;
        }
        Assert.ThrowsAny<ToolkitException>(() => Selection(start, end, clock));
    }
}
