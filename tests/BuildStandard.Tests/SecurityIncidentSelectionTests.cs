using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;
public sealed class SecurityIncidentSelectionTests
{
    private static readonly DateTimeOffset End = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static SecurityIncidentReadSelection Selection(DateTimeOffset start, DateTimeOffset end, DateTimeOffset asOf) => new(start, end, asOf);
    private static SecurityIncidentReadSelection Parse(string start, string end) => SecurityIncidentReadSelection.Parse(start, end, End);
    [Fact] public void Explicit_UTC_text_is_parsed_without_local_time_or_locale_inference()
    {
        var selection = Parse("2026-10-19T12:00:00Z", "2026-10-20T12:00:00.000Z");
        Assert.Equal(End.AddDays(-1), selection.StartUtc); Assert.Equal(End, selection.EndUtc);
    }
    [Theory][InlineData("")][InlineData("19/10/2026")][InlineData("2026-10-19T12:00:00")]
    [InlineData("2026-10-19T13:00:00+01:00")][InlineData("2026-10-19T12:00:00Z or true")]
    public void Ambiguous_non_UTC_or_query_like_date_input_is_refused(string input)
    { Assert.ThrowsAny<ToolkitException>(() => Parse(input, "2026-10-20T12:00:00Z")); }
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
