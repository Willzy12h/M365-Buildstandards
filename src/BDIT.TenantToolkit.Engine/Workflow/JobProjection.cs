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

/// <summary>
/// INT-049 read-only projection of a job against current evidence. Computing it never writes: stale records are
/// reported as needing review, not rewritten, and nothing here grants authority to execute.
/// </summary>
public sealed class JobProjection
{
    public TenantJob Job { get; init; } = new();
    /// <summary>Reasons the job as a whole was opened against something that has since changed.</summary>
    public IReadOnlyList<string> JobReviewReasons { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SubjectProjection> Subjects { get; init; } = Array.Empty<SubjectProjection>();
    /// <summary>Observations written for this job but never attached (for example an interrupted save). Not counted.</summary>
    public IReadOnlyList<TenantObservation> Unattached { get; init; } = Array.Empty<TenantObservation>();
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
        if (!SameStandard(job.StandardRelease, job.StandardDigest, standard)) jobReasons.Add(ReviewReason.StandardChanged);
        if (!SameClient(job.ProfileId, job.ClientScopeDigest, profile)) jobReasons.Add(ReviewReason.ClientInputsChanged);
        if (job.Evidence.Any(r => !EvidenceIntact(store, tenantId, r))) jobReasons.Add(ReviewReason.EvidenceMissing);

        var (all, unreadableFiles) = store.LoadObservations(tenantId);
        var attachedIds = job.ObservationIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var attached = all.Where(o => attachedIds.Contains(o.Id) && string.Equals(o.JobId, job.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        var foundIds = attached.Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unreadable = job.ObservationIds.Where(id => !foundIds.Contains(id)).Select(id => new UnreadableRecord(id,
            unreadableFiles.FirstOrDefault(u => Path.GetFileNameWithoutExtension(u.File).Equals(id, StringComparison.OrdinalIgnoreCase))?.Problem
            ?? "The attached observation is missing or belongs to another job.")).ToList();
        var unattached = all.Where(o => string.Equals(o.JobId, job.Id, StringComparison.OrdinalIgnoreCase) && !attachedIds.Contains(o.Id))
            .OrderBy(o => o.RecordedAt, StringComparer.Ordinal).ToList();

        var subjects = attached
            .GroupBy(o => (o.SemanticId, ControlId: o.ControlId.ToUpperInvariant(), InstanceKey: o.InstanceKey.ToUpperInvariant()))
            .Select(g => Subject(store, tenantId, g.ToList(), attached, standard, profile, now, currentCapture))
            .OrderBy(s => s.InstanceKey, StringComparer.OrdinalIgnoreCase).ToList();

        return new JobProjection
        {
            Job = job,
            JobReviewReasons = jobReasons,
            Subjects = subjects,
            Unattached = unattached,
            Unreadable = unreadable,
            LegacyAttestations = store.LoadManualChecks(tenantId).Checks.Values.OrderBy(c => c.ControlId, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static SubjectProjection Subject(EvidenceStore store, string tenantId, List<TenantObservation> history, List<TenantObservation> attached,
        StandardCatalogue standard, TenantProfile profile, DateTimeOffset now, TenantSnapshot? capture)
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

        if (current is not null)
        {
            if (!SameStandard(current.StandardRelease, current.StandardDigest, standard)) reasons.Add(ReviewReason.StandardChanged);
            if (!SameClient(current.ProfileId, current.ClientScopeDigest, profile)
                || ControlInstances.Find(standard, profile, current.InstanceKey) is null) reasons.Add(ReviewReason.ClientInputsChanged);
            if (current.Evidence.Any(r => !EvidenceIntact(store, tenantId, r))) reasons.Add(ReviewReason.EvidenceMissing);
            if (current.ObservedObjectIds.Count > 0 && capture is not null
                && ObservedMaterial.Digest(capture, current.ObservedObjectIds) != current.MaterialDigest) reasons.Add(ReviewReason.MaterialChanged);
            if (!Timestamps.TryParse(current.ReviewDueAt, out var due) || due <= now) reasons.Add(ReviewReason.ReviewOverdue);
        }

        var first = history[0];
        return new SubjectProjection
        {
            SemanticId = first.SemanticId,
            ControlId = first.ControlId,
            InstanceKey = first.InstanceKey,
            Current = current,
            History = history.OrderBy(o => o.RecordedAt, StringComparer.Ordinal).ToList(),
            ReviewReasons = reasons.Distinct().ToList()
        };
    }

    private static bool Acyclic(List<TenantObservation> history)
    {
        var byId = history.ToDictionary(o => o.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var start in history)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var node = start; node is not null; node = node.SupersedesId is null ? null : byId.GetValueOrDefault(node.SupersedesId))
                if (!seen.Add(node.Id)) return false;
        }
        return true;
    }

    private static bool SameStandard(string release, string digest, StandardCatalogue standard) =>
        release == standard.Release && !string.IsNullOrEmpty(standard.IntegrityDigest) && string.Equals(digest, standard.IntegrityDigest, StringComparison.OrdinalIgnoreCase);

    private static bool SameClient(string profileId, string scopeDigest, TenantProfile profile) =>
        string.Equals(profileId, profile.Id, StringComparison.OrdinalIgnoreCase) && scopeDigest == ReviewedClientScope.Digest(profile);

    private static bool EvidenceIntact(EvidenceStore store, string tenantId, EvidenceReference reference)
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
