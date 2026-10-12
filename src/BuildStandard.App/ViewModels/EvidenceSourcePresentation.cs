using BuildStandard.Core.Models;

namespace BuildStandard.App.ViewModels;

/// <summary>Read-only source labels shared by the pinned header and assessment. Never establishes write eligibility.</summary>
public sealed record EvidenceSourcePresentation(string Summary, string OriginalCaptureTime, string Details)
{
    public static EvidenceSourcePresentation Create(TenantSnapshot? graph, bool graphIsLive, TenantSnapshot? exchange,
        bool exchangeCapturedByTool, string? selectedTenant, string displayedTenant)
    {
        var primary = graph ?? exchange;
        if (primary is null) return new(selectedTenant is null ? "No client evidence selected"
            : "No full capture open · choose saved evidence or capture read-only", "", "");
        bool Matches(TenantSnapshot capture) => !string.IsNullOrWhiteSpace(selectedTenant)
            && string.Equals(capture.TenantId, selectedTenant, StringComparison.OrdinalIgnoreCase)
            && string.Equals(capture.TenantId, displayedTenant, StringComparison.OrdinalIgnoreCase);
        if (!Matches(primary)) return new("Capture belongs to another tenant · reopen evidence for the current client", "", "");
        var source = graph is not null ? (graphIsLive ? "Live full capture" : "Historical evidence · offline")
            : exchangeCapturedByTool ? "Supplemental Exchange capture" : "Historical Exchange evidence · offline";
        var at = DateTimeOffset.TryParse(primary.CapturedAt, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var time)
            ? time.ToUniversalTime().ToString("dd MMM yyyy HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture) : primary.CapturedAt;
        var summary = $"{source} · {at} · {(primary.Complete ? "complete" : "INCOMPLETE")}";
        var details = $"Evidence source: {source}\nCapture ID: {primary.Id}\nCaptured at: {primary.CapturedAt}";
        if (graph is not null && exchange is not null)
        {
            if (!Matches(exchange))
            {
                summary += " · Exchange: different tenant";
                details += "\nSeparate Exchange evidence belongs to another tenant. Reopen matching observations.";
            }
            else
            {
                summary += exchangeCapturedByTool ? " · Exchange: tool capture" : " · Exchange: historical";
                details += $"\nSeparate Exchange source: {(exchangeCapturedByTool ? "collected by the tool" : "historical/offline")}"
                    + $"\nExchange capture ID: {exchange.Id}\nExchange captured at: {exchange.CapturedAt}"
                    + $"\nExchange completeness: {(exchange.Complete ? "complete" : "INCOMPLETE")}";
            }
        }
        return new(summary, primary.CapturedAt, details);
    }
}
