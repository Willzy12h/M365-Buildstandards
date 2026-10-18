using System.Globalization;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;

namespace BuildStandard.Engine.Workflow;

public sealed record ClientReviewAssessment(string Id, DateTimeOffset At, string Release, string StandardDigest, bool Complete, int Findings, int Unknown);
public sealed record ClientReviewWrite(string Kind, string RunId, string ControlId, string Acceptance, bool Unresolved);
public sealed record ClientReviewDeviation(string Id, string ControlId, string Owner, string ReviewBy, string State);
public sealed record ClientReviewJob(string Id, string Owner, string Intention, string Release, bool Known, bool Complete,
    int Verified, int Outstanding, string Claim, IReadOnlyList<string> Reasons);
public sealed record ClientReviewResult(string TenantId, string ClientLabel, DateTimeOffset ReviewedAt,
    ClientReviewAssessment? LastAssessment, bool WriteHistoryKnown, IReadOnlyList<ClientReviewWrite> Writes,
    IReadOnlyList<ClientReviewDeviation> Deviations, IReadOnlyList<ClientReviewJob> Jobs, IReadOnlyList<ClientReviewDiagnostic> Diagnostics)
{
    public int UnresolvedWrites => Writes.Count(w => w.Unresolved);
}

/// <summary>Offline engineering summary only; never assessment, execution or recovery authority.</summary>
public static class ClientReviewProjection
{
    public static ClientReviewResult Project(TenantProfile profile, ClientReviewEvidence evidence, DateTimeOffset now)
    {
        if (!string.Equals(profile.TenantId, evidence.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("The review profile and captured client do not match.");
        var store = new CapturedClientReviewStore(evidence);
        var latest = evidence.Assessments.Select(a => a.Result).OrderByDescending(a => Instant(a.AssessedAt))
            .ThenBy(a => a.Id, StringComparer.Ordinal).FirstOrDefault();
        var assessment = latest is null ? null : new ClientReviewAssessment(latest.Id, Instant(latest.AssessedAt), latest.Release,
            latest.StandardDigest, latest.SnapshotComplete && latest.SnapshotIntegrity == SnapshotIntegrityState.Intact,
            latest.Findings.Count, latest.Findings.Count(f => f.Status == FindingStatus.UnableToAssess || !Enum.IsDefined(f.Status)));
        var deviations = store.LoadDeviations(evidence.TenantId).Select(d => new ClientReviewDeviation(d.Id, d.ControlId, d.Owner, d.ReviewBy,
            !DateOnly.TryParseExact(d.ReviewBy, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due)
                ? "Review date unknown" : due < DateOnly.FromDateTime(now.UtcDateTime) ? "Overdue" : "In date")).ToList().AsReadOnly();
        var catalogues = evidence.Catalogues;
        var capture = evidence.Records<TenantSnapshot>("snapshots").OrderByDescending(s => Instant(s.CapturedAt))
            .ThenBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
        var jobs = new List<ClientReviewJob>();
        foreach (var job in evidence.Records<TenantJob>("jobs").OrderByDescending(j => Instant(j.UpdatedAt)).ThenBy(j => j.Id, StringComparer.Ordinal))
        {
            var standard = catalogues.FirstOrDefault(c => c.Release == job.StandardRelease && c.IntegrityDigest == job.StandardDigest);
            var reasons = evidence.Diagnostics.Select(d => d.Section + ": " + d.Problem).ToList();
            if (standard is null)
            {
                reasons.Add("The exact catalogue for this job could not be captured.");
                jobs.Add(new(job.Id, job.Owner, job.Intention, job.StandardRelease, false, false, 0, 0,
                    "Completion unknown: restore the original catalogue and supporting evidence.", reasons.AsReadOnly()));
                continue;
            }
            var projection = JobProjection.BuildCaptured(store, evidence.TenantId, job.Id, standard, profile, now, capture);
            var completion = JobCompletion.BuildCaptured(store, projection, standard, profile, now);
            var missing = projection.Unreadable.Count > 0 || projection.JobReviewReasons.Contains(ReviewReason.EvidenceMissing)
                || projection.Subjects.Any(s => s.ReviewReasons.Contains(ReviewReason.EvidenceMissing))
                || projection.Dispositions.Any(s => s.ReviewReasons.Contains(ReviewReason.EvidenceMissing) || s.ReviewReasons.Contains(ReviewReason.DeviationMissing) || s.ReviewReasons.Contains(ReviewReason.CaseMissing))
                || projection.Cutovers.Any(s => s.ReviewReasons.Contains(ReviewReason.EvidenceMissing) || s.ReviewReasons.Contains(ReviewReason.DeviationMissing));
            var known = evidence.SourceSetComplete && reasons.Count == 0 && !missing;
            reasons.AddRange(completion.Blockers);
            reasons.AddRange(completion.Requirements.Where(r => r.Outstanding).SelectMany(r => r.Reasons));
            jobs.Add(new(job.Id, job.Owner, job.Intention, job.StandardRelease, known, known && completion.Complete,
                known ? completion.Verified : 0, known ? completion.Outstanding : completion.Requirements.Count,
                known ? completion.Claim : "Completion unknown: supporting history is incomplete or unreadable.",
                reasons.Distinct(StringComparer.Ordinal).ToList().AsReadOnly()));
        }
        return new(evidence.TenantId, profile.Company, now, assessment, store.WriteHistoryKnown,
            store.Writes, deviations, jobs.AsReadOnly(), evidence.Diagnostics);
    }

    private static DateTimeOffset Instant(string value) => Timestamps.TryParse(value, out var time)
        ? time : throw new ConfigurationException("Captured review contains an invalid timestamp.");
}
