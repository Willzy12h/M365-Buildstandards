using System.Globalization;
namespace BuildStandard.Core.Reporting;

/// <summary>Frozen read-only incident window; never a caller-supplied Graph query or response action.</summary>
public sealed class SecurityIncidentReadSelection
{
    public DateTimeOffset StartUtc { get; }
    public DateTimeOffset EndUtc { get; }
    public DateTimeOffset AsOfUtc { get; }
    public string RegisteredPath { get; }
    public SecurityIncidentReadSelection(DateTimeOffset startUtc, DateTimeOffset endUtc, DateTimeOffset asOfUtc)
    {
        if (startUtc.Offset != TimeSpan.Zero || endUtc.Offset != TimeSpan.Zero || asOfUtc.Offset != TimeSpan.Zero
            || endUtc <= startUtc || endUtc - startUtc > TimeSpan.FromDays(31) || endUtc > asOfUtc)
            throw new ConfigurationException("Choose an explicit UTC incident window, start inclusive and end exclusive, no more than 31 days and not beyond the capture clock.");
        StartUtc = startUtc; EndUtc = endUtc; AsOfUtc = asOfUtc;
        var filter = "createdDateTime ge " + Format(startUtc) + " and createdDateTime lt " + Format(endUtc);
        RegisteredPath = "/security/incidents?$filter=" + Uri.EscapeDataString(filter) + "&$top=100";
    }
    private static string Format(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
