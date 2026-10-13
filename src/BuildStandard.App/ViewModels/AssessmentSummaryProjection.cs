using BuildStandard.Core.Models;
namespace BuildStandard.App.ViewModels;

public sealed record AssessmentStatusCount(string Label, int Count, string Meaning);
/// <summary>Presentation partition only. Never changes findings, source timing or deployment authority.</summary>
public sealed record AssessmentSummaryProjection(int Total, int Compliant, int NeedsReview, int SettingsMatchNotEnforced,
    int UnableToAssess, int CompliantWithDeviation, int NotApplicable, int Unclassified,
    IReadOnlyList<AssessmentStatusCount> Groups, IReadOnlyList<AssessmentStatusCount> ReviewBreakdown)
{
    public string Description => Total == 0 ? "No findings are available. This does not establish a successful empty check."
        : $"{Total} findings · source completeness, original timing and qualifications remain separate.";
    public static AssessmentSummaryProjection Create(IEnumerable<FindingStatus> findings)
    {
        var statuses = findings.ToArray(); int Count(FindingStatus status) => statuses.Count(s => s == status);
        var review = new[]
        {
            new AssessmentStatusCount("Partial match", Count(FindingStatus.PartialMatch), "Inspect exact settings and equivalent coverage."),
            new AssessmentStatusCount("Missing", Count(FindingStatus.Missing), "No matching object found in the returned source; inspect coverage before proposing changes."),
            new AssessmentStatusCount("Manual review", Count(FindingStatus.RequiresManualReview), "Engineer review is required; it is not an automated pass."),
            new AssessmentStatusCount("Licence unavailable", Count(FindingStatus.LicenceUnavailable), "Review the reported entitlement limitation; no licence is bought or assigned.")
        };
        var needsReview = review.Sum(g => g.Count);
        var unknown = statuses.Count(s => !Enum.IsDefined(s));
        var groups = new List<AssessmentStatusCount>
        {
            new("Compliant", Count(FindingStatus.Compliant), "Recorded settings and expected production state match."),
            new("Needs review", needsReview, "Partial, missing, manual and licence results remain distinct."),
            new("Settings match · not enforced", Count(FindingStatus.SettingsMatchNotEnforced), "Disabled, report-only or unassigned. Protection is not established."),
            new("Unable to assess", Count(FindingStatus.UnableToAssess), "Unread or unsupported settings remain unknown, not absent."),
            new("Recorded deviation", Count(FindingStatus.CompliantWithDeviation), "Original assessment qualification; current approval/expiry checks remain independent."),
            new("Not applicable", Count(FindingStatus.NotApplicable), "Recorded client scope, not compliance.")
        };
        if (unknown > 0) groups.Add(new("Unclassified results", unknown, "This status could not be classified; inspect the original record."));
        return new(statuses.Length, Count(FindingStatus.Compliant), needsReview,
            Count(FindingStatus.SettingsMatchNotEnforced), Count(FindingStatus.UnableToAssess),
            Count(FindingStatus.CompliantWithDeviation), Count(FindingStatus.NotApplicable), unknown,
            groups.AsReadOnly(), Array.AsReadOnly(review));
    }
}
