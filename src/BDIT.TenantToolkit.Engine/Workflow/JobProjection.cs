using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;

namespace BDIT.TenantToolkit.Engine.Workflow;

/// <summary>The projected state of one requirement instance within a job.</summary>
public sealed class SubjectProjection
{
    public string SemanticId { get; init; } = "";
    public string ControlId { get; init; } = "";
    public string InstanceKey { get; init; } = "";
    /// <summary>The single valid head of the supersession graph, or null when there is none (fork or missing link).</summary>
    public TenantObservation? Current { get; init; }
    /// <summary>Every attached observation for this subject, oldest first. Originals are never removed.</summary>
    public IReadOnlyList<TenantObservation> History { get; init; } = Array.Empty<TenantObservation>();
    public IReadOnlyList<string> ReviewReasons { get; init; } = Array.Empty<string>();
    public bool NeedsReview => ReviewReasons.Count > 0;
    /// <summary>Only an in-date, unchallenged Pass stands. Pending, Fail, Unknown and NeedsReview never do.</summary>
    public bool Accepted => Current?.Status == ObservationStatus.Pass && !NeedsReview;
}

/// <summary>The projected state of one requirement instance's INT-050 decisions within a job.</summary>
public sealed class DispositionProjection
{
    public string SemanticId { get; init; } = "";
    public string ControlId { get; init; } = "";
    public string InstanceKey { get; init; } = "";
    /// <summary>The single valid head of the supersession graph, or null when there is none (fork or missing link).</summary>
    public TenantDisposition? Current { get; init; }
    /// <summary>Every attached decision for this subject, oldest first. Originals are never removed.</summary>
    public IReadOnlyList<TenantDisposition> History { get; init; } = Array.Empty<TenantDisposition>();
    public IReadOnlyList<string> ReviewReasons { get; init; } = Array.Empty<string>();
    public bool NeedsReview => ReviewReasons.Count > 0;
    /// <summary>
    /// Retention or an approved departure that still holds. It is an engineer decision, never a compliant finding,
    /// and it grants no ownership. Investigate, manual work, candidates and replacements always leave work outstanding.
    /// </summary>
    public bool Settled => Current is not null && DispositionDecision.Settles(Current.Decision) && !NeedsReview;
}

/// <summary>
/// INT-049 and INT-050 read-only projection of a job against current evidence. Computing it never writes: stale records
/// are reported as needing review, not rewritten, and nothing here grants authority to execute.
/// </summary>
public sealed class JobProjection
{
    public TenantJob Job { get; init; } = new();
    /// <summary>Reasons the job as a whole was opened against something that has since changed.</summary>
    public IReadOnlyList<string> JobReviewReasons { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SubjectProjection> Subjects { get; init; } = Array.Empty<SubjectProjection>();
    public IReadOnlyList<DispositionProjection> Dispositions { get; init; } = Array.Empty<DispositionProjection>();
    /// <summary>Observations written for this job but never attached (for example an interrupted save). Not counted.</summary>
    public IReadOnlyList<TenantObservation> Unattached { get; init; } = Array.Empty<TenantObservation>();
    /// <summary>Dispositions written for this job but never attached. Not counted.</summary>
    public IReadOnlyList<TenantDisposition> UnattachedDispositions { get; init; } = Array.Empty<TenantDisposition>();
    /// <summary>Attached IDs whose record is missing, unreadable or failed its integrity check.</summary>
    public IReadOnlyList<UnreadableRecord> Unreadable { get; init; } = Array.Empty<UnreadableRecord>();
    /// <summary>Old manual-check entries: shown as legacy, unbound attestations, never as passes of this job.</summary>
    public IReadOnlyList<ManualCheck> LegacyAttestations { get; init; } = Array.Empty<ManualCheck>();

    public int Outstanding => Subjects.Count(s => !s.Accepted);

    public static JobProjection Build(EvidenceStore store, string tenantId, string jobId, StandardCatalogue standard, TenantProfile profile,
        DateTimeOffset now, TenantSnapshot? currentCapture = null)
    {
        var job = store.RequireJob(tenantId, jobId);
        var jobReasons = new List<string>();
        if (!SubjectReview.SameStandard(job.StandardRelease, job.StandardDigest, standard)) jobReasons.Add(ReviewReason.StandardChanged);
        if (!SubjectReview.SameClient(job.ProfileId, job.ClientScopeDigest, profile)) jobReasons.Add(ReviewReason.ClientInputsChanged);
        if (job.Evidence.Any(r => !SubjectReview.EvidenceIntact(store, tenantId, r))) jobReasons.Add(ReviewReason.EvidenceMissing);

        var unreadable = new List<UnreadableRecord>();
        var (observations, unattached) = Partition(store.LoadObservations(tenantId), job, job.ObservationIds, "observation", unreadable);
        var (dispositions, unattachedDispositions) = Partition(store.LoadDispositions(tenantId), job, job.DispositionIds ?? new(), "disposition", unreadable);

        var subjects = BySubject(observations)
            .Select(history =>
            {
                var (current, reasons) = Review(store, tenantId, history, standard, profile, now, currentCapture);
                return new SubjectProjection
                {
                    SemanticId = history[0].SemanticId, ControlId = history[0].ControlId, InstanceKey = history[0].InstanceKey,
                    Current = current, History = Oldest(history), ReviewReasons = reasons
                };
            })
            .OrderBy(s => s.InstanceKey, StringComparer.OrdinalIgnoreCase).ToList();
        var decided = BySubject(dispositions)
            .Select(history =>
            {
                var (current, reasons) = Review(store, tenantId, history, standard, profile, now, currentCapture);
                return new DispositionProjection
                {
                    SemanticId = history[0].SemanticId, ControlId = history[0].ControlId, InstanceKey = history[0].InstanceKey,
                    Current = current, History = Oldest(history), ReviewReasons = reasons
                };
            })
            .OrderBy(s => s.InstanceKey, StringComparer.OrdinalIgnoreCase).ToList();

        return new JobProjection
        {
            Job = job,
            JobReviewReasons = jobReasons,
            Subjects = subjects,
            Dispositions = decided,
            Unattached = unattached,
            UnattachedDispositions = unattachedDispositions,
            Unreadable = unreadable,
            LegacyAttestations = store.LoadManualChecks(tenantId).Checks.Values.OrderBy(c => c.ControlId, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    /// <summary>Splits a tenant's records into those attached to the job and those written for it but never attached.</summary>
    private static (List<T> Attached, List<T> Unattached) Partition<T>((IReadOnlyList<T> Records, IReadOnlyList<UnreadableRecord> Unreadable) loaded,
        TenantJob job, IReadOnlyList<string> attachedIds, string what, List<UnreadableRecord> unreadable) where T : ISubjectRecord
    {
        var ids = attachedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var forJob = loaded.Records.Where(r => string.Equals(r.JobId, job.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        var attached = forJob.Where(r => ids.Contains(r.Id)).ToList();
        var found = attached.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        unreadable.AddRange(attachedIds.Where(id => !found.Contains(id)).Select(id => new UnreadableRecord(id,
            loaded.Unreadable.FirstOrDefault(u => Path.GetFileNameWithoutExtension(u.File).Equals(id, StringComparison.OrdinalIgnoreCase))?.Problem
            ?? $"The attached {what} is missing or belongs to another job.")));
        return (attached, Oldest(forJob.Where(r => !ids.Contains(r.Id)).ToList()));
    }

    private static IEnumerable<List<T>> BySubject<T>(List<T> attached) where T : ISubjectRecord =>
        attached.GroupBy(o => (o.SemanticId, ControlId: o.ControlId.ToUpperInvariant(), InstanceKey: o.InstanceKey.ToUpperInvariant())).Select(g => g.ToList());

    private static List<T> Oldest<T>(List<T> records) where T : ISubjectRecord => records.OrderBy(o => o.RecordedAt, StringComparer.Ordinal).ToList();

    /// <summary>Selects the current record by the supersession graph, never by time, and lists why it does not stand.</summary>
    private static (T? Current, IReadOnlyList<string> Reasons) Review<T>(EvidenceStore store, string tenantId, List<T> history,
        StandardCatalogue standard, TenantProfile profile, DateTimeOffset now, TenantSnapshot? capture) where T : class, ISubjectRecord
    {
        var reasons = new List<string>();
        var ids = history.Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // A link must point to an attached record of the same subject; anything else breaks the history.
        if (history.Any(o => o.SupersedesId is not null && !ids.Contains(o.SupersedesId))) reasons.Add(ReviewReason.PredecessorMissing);
        var superseded = history.Where(o => o.SupersedesId is not null).Select(o => o.SupersedesId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var heads = history.Where(o => !superseded.Contains(o.Id)).ToList();
        var forked = heads.Count != 1 || history.GroupBy(o => o.SupersedesId ?? "", StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)
            || !Acyclic(history);
        if (forked) reasons.Add(ReviewReason.Forked);
        var current = forked ? null : heads[0];
        if (current is not null) reasons.AddRange(SubjectReview.Reasons(store, tenantId, current, standard, profile, now, capture));
        return (current, reasons.Distinct().ToList());
    }

    private static bool Acyclic<T>(List<T> history) where T : ISubjectRecord
    {
        var byId = history.ToDictionary(o => o.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var start in history)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (ISubjectRecord? node = start; node is not null; node = node.SupersedesId is null ? null : byId.GetValueOrDefault(node.SupersedesId))
                if (!seen.Add(node.Id)) return false;
        }
        return true;
    }
}

/// <summary>Whether one current record still stands against the standard, client inputs and evidence now in use.</summary>
internal static class SubjectReview
{
    public static List<string> Reasons(EvidenceStore store, string tenantId, ISubjectRecord current, StandardCatalogue standard, TenantProfile profile,
        DateTimeOffset now, TenantSnapshot? capture)
    {
        var reasons = new List<string>();
        if (!SameStandard(current.StandardRelease, current.StandardDigest, standard)) reasons.Add(ReviewReason.StandardChanged);
        if (!SameClient(current.ProfileId, current.ClientScopeDigest, profile)
            || ControlInstances.Find(standard, profile, current.InstanceKey) is null) reasons.Add(ReviewReason.ClientInputsChanged);
        if (current.Evidence.Any(r => !EvidenceIntact(store, tenantId, r))) reasons.Add(ReviewReason.EvidenceMissing);
        if (current.ObservedObjectIds.Count > 0 && capture is not null
            && ObservedMaterial.Digest(capture, current.ObservedObjectIds) != current.MaterialDigest) reasons.Add(ReviewReason.MaterialChanged);
        if (!Timestamps.TryParse(current.ReviewDueAt, out var due) || due <= now) reasons.Add(ReviewReason.ReviewOverdue);
        if (current is TenantDisposition { DeviationId: not null } disposition && DeviationProblem(store, tenantId, disposition, now))
            reasons.Add(ReviewReason.DeviationMissing);
        return reasons;
    }

    /// <summary>True unless the referenced deviation exists in this tenant, is an approved deviation for the same control and is in date.</summary>
    public static bool DeviationProblem(EvidenceStore store, string tenantId, TenantDisposition disposition, DateTimeOffset now)
    {
        try
        {
            var deviation = store.LoadDeviations(tenantId).FirstOrDefault(d => string.Equals(d.Id, disposition.DeviationId, StringComparison.OrdinalIgnoreCase));
            return deviation is null || deviation.Kind != DeviationKind.ApprovedDeviation
                || !string.Equals(deviation.ControlId, disposition.ControlId, StringComparison.OrdinalIgnoreCase) || deviation.IsReviewOverdue(now);
        }
        catch (ToolkitException) { return true; }
    }

    public static bool SameStandard(string release, string digest, StandardCatalogue standard) =>
        release == standard.Release && !string.IsNullOrEmpty(standard.IntegrityDigest) && string.Equals(digest, standard.IntegrityDigest, StringComparison.OrdinalIgnoreCase);

    public static bool SameClient(string profileId, string scopeDigest, TenantProfile profile) =>
        string.Equals(profileId, profile.Id, StringComparison.OrdinalIgnoreCase) && scopeDigest == ReviewedClientScope.Digest(profile);

    public static bool EvidenceIntact(EvidenceStore store, string tenantId, EvidenceReference reference)
    {
        if (reference.Kind != EvidenceKind.Snapshot) return false;
        try
        {
            var snapshot = store.LoadSnapshot(tenantId, reference.Id);
            return snapshot is not null && store.SnapshotIntegrityIntact(snapshot) && snapshot.IntegrityDigest == reference.Sha256;
        }
        catch (ToolkitException) { return false; }
    }
}
