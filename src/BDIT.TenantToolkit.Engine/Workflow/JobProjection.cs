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
    /// <summary>Every attached observation for this subject, in revision order. Originals are never removed.</summary>
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
    /// <summary>Every attached decision for this subject, in revision order. Originals are never removed.</summary>
    public IReadOnlyList<TenantDisposition> History { get; init; } = Array.Empty<TenantDisposition>();
    public IReadOnlyList<string> ReviewReasons { get; init; } = Array.Empty<string>();
    public bool NeedsReview => ReviewReasons.Count > 0;
    /// <summary>
    /// Retention or an approved departure that still holds. It is an engineer decision, never a compliant finding,
    /// and it grants no ownership. Investigate, manual work, candidates and replacements always leave work outstanding.
    /// </summary>
    public bool Settled => Current is not null && DispositionDecision.Settles(Current.Decision) && !NeedsReview;
}

/// <summary>The projected state of one cutover case within a job.</summary>
public sealed class CutoverProjection
{
    public string CaseId { get; init; } = "";
    public string SemanticId { get; init; } = "";
    public string ControlId { get; init; } = "";
    public string InstanceKey { get; init; } = "";
    /// <summary>The single valid head of the case's revisions, or null when there is none (fork or missing link).</summary>
    public CutoverRevision? Current { get; init; }
    /// <summary>Every attached revision of the case, in revision order. Originals are never removed.</summary>
    public IReadOnlyList<CutoverRevision> History { get; init; } = Array.Empty<CutoverRevision>();
    public IReadOnlyList<string> ReviewReasons { get; init; } = Array.Empty<string>();
    public bool NeedsReview => ReviewReasons.Count > 0;
    /// <summary>Stopped on an unsupported scenario, with its owner and escalation path.</summary>
    public bool Escalated => Current?.Unsupported is not null;
    /// <summary>Only a closed case whose evidence still stands. An approved stage alone never closes a case.</summary>
    public bool Closed => Current?.Stage == CutoverStage.Closed && !NeedsReview;
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
    public IReadOnlyList<CutoverProjection> Cutovers { get; init; } = Array.Empty<CutoverProjection>();
    /// <summary>Observations written for this job but never attached (for example an interrupted save). Not counted.</summary>
    public IReadOnlyList<TenantObservation> Unattached { get; init; } = Array.Empty<TenantObservation>();
    /// <summary>Dispositions written for this job but never attached. Not counted.</summary>
    public IReadOnlyList<TenantDisposition> UnattachedDispositions { get; init; } = Array.Empty<TenantDisposition>();
    /// <summary>Cutover revisions written for this job but never attached. Not counted.</summary>
    public IReadOnlyList<CutoverRevision> UnattachedCutovers { get; init; } = Array.Empty<CutoverRevision>();
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
        var (cutovers, unattachedCutovers) = Partition(store.LoadCutovers(tenantId), job, job.CutoverIds ?? new(), "cutover revision", unreadable);

        var subjects = BySubject(observations)
            .Select(history =>
            {
                var (current, reasons) = Review(store, tenantId, history, standard, profile, now, currentCapture);
                if (observations.Any(o => string.Equals(o.InstanceKey, history[0].InstanceKey, StringComparison.OrdinalIgnoreCase)
                    && !JobWorkflow.SameSubject(o, history[0])))
                    reasons = reasons.Append("This requirement instance has conflicting outcome identities. Review all histories.").ToList();
                return new SubjectProjection
                {
                    SemanticId = history[0].SemanticId, ControlId = history[0].ControlId, InstanceKey = history[0].InstanceKey,
                    Current = current, History = InOrder(history), ReviewReasons = reasons
                };
            })
            .OrderBy(s => s.InstanceKey, StringComparer.OrdinalIgnoreCase).ToList();
        var decided = BySubject(dispositions)
            .Select(history =>
            {
                var (current, reasons) = Review(store, tenantId, history, standard, profile, now, currentCapture);
                if (dispositions.Any(d => string.Equals(d.InstanceKey, history[0].InstanceKey, StringComparison.OrdinalIgnoreCase)
                    && !JobWorkflow.SameSubject(d, history[0])))
                    reasons = reasons.Append("This requirement instance has conflicting decision identities. Review all histories.").ToList();
                return new DispositionProjection
                {
                    SemanticId = history[0].SemanticId, ControlId = history[0].ControlId, InstanceKey = history[0].InstanceKey,
                    Current = current, History = InOrder(history), ReviewReasons = reasons
                };
            })
            .OrderBy(s => s.InstanceKey, StringComparer.OrdinalIgnoreCase).ToList();

        var cases = cutovers.GroupBy(c => c.CaseId, StringComparer.OrdinalIgnoreCase).Select(g => g.ToList())
            .Select(history =>
            {
                var (current, reasons) = Graph(history);
                if (current is not null) reasons.AddRange(CutoverReasons(store, tenantId, current, standard, profile, now, currentCapture));
                // Later stage records rely on the immutable review/candidate/pilot evidence in their predecessors.
                // Inspect those pinned references too; a new stage cannot make lost or modified stage evidence disappear.
                if (history.SelectMany(r => r.Evidence).Any(r => !SubjectReview.EvidenceIntact(store, tenantId, r)))
                    reasons.Add(ReviewReason.EvidenceMissing);
                return new CutoverProjection
                {
                    CaseId = history[0].CaseId, SemanticId = history[0].SemanticId, ControlId = history[0].ControlId, InstanceKey = history[0].InstanceKey,
                    Current = current, History = InOrder(history), ReviewReasons = reasons.Distinct().ToList()
                };
            })
            .OrderBy(c => c.InstanceKey, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.CaseId, StringComparer.Ordinal).ToList();

        return new JobProjection
        {
            Job = job,
            JobReviewReasons = jobReasons,
            Subjects = subjects,
            Dispositions = decided,
            Cutovers = cases,
            Unattached = unattached,
            UnattachedDispositions = unattachedDispositions,
            UnattachedCutovers = unattachedCutovers,
            Unreadable = unreadable,
            LegacyAttestations = store.LoadManualChecks(tenantId).Checks.Values.OrderBy(c => c.ControlId, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    /// <summary>Splits a tenant's records into those attached to the job and those written for it but never attached.</summary>
    private static (List<T> Attached, List<T> Unattached) Partition<T>((IReadOnlyList<T> Records, IReadOnlyList<UnreadableRecord> Unreadable) loaded,
        TenantJob job, IReadOnlyList<string> attachedIds, string what, List<UnreadableRecord> unreadable) where T : IJobRecord
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

    private static List<T> Oldest<T>(List<T> records) where T : IJobRecord => records.OrderBy(o => o.RecordedAt, StringComparer.Ordinal).ToList();

    /// <summary>History in supersession order (each record after the one it revises), then by time; cycles stop at the history's length.</summary>
    private static List<T> InOrder<T>(List<T> history) where T : IJobRecord
    {
        var byId = history.ToDictionary(o => o.Id, StringComparer.OrdinalIgnoreCase);
        int Depth(T record)
        {
            var depth = 0;
            for (var node = record; node.SupersedesId is not null && byId.TryGetValue(node.SupersedesId, out var previous) && depth < history.Count; node = previous) depth++;
            return depth;
        }
        return history.OrderBy(Depth).ThenBy(o => o.RecordedAt, StringComparer.Ordinal).ToList();
    }

    private static (T? Current, IReadOnlyList<string> Reasons) Review<T>(EvidenceStore store, string tenantId, List<T> history,
        StandardCatalogue standard, TenantProfile profile, DateTimeOffset now, TenantSnapshot? capture) where T : class, ISubjectRecord
    {
        var (current, reasons) = Graph(history);
        if (current is not null) reasons.AddRange(SubjectReview.Reasons(store, tenantId, current, standard, profile, now, capture));
        return (current, reasons.Distinct().ToList());
    }

    /// <summary>Selects the current record by the supersession graph, never by time, and lists why there is none.</summary>
    private static (T? Current, List<string> Reasons) Graph<T>(List<T> history) where T : class, IJobRecord
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
        return (forked ? null : heads[0], reasons);
    }

    /// <summary>Why a cutover revision no longer stands. A stage recorded once is never assumed to still hold.</summary>
    private static List<string> CutoverReasons(EvidenceStore store, string tenantId, CutoverRevision current, StandardCatalogue standard, TenantProfile profile,
        DateTimeOffset now, TenantSnapshot? capture)
    {
        var reasons = new List<string>();
        if (!SubjectReview.SameStandard(current.StandardRelease, current.StandardDigest, standard)) reasons.Add(ReviewReason.StandardChanged);
        if (!SubjectReview.SameClient(current.ProfileId, current.ClientScopeDigest, profile)
            || ControlInstances.Find(standard, profile, current.InstanceKey) is null) reasons.Add(ReviewReason.ClientInputsChanged);
        if (current.Evidence.Any(r => !SubjectReview.EvidenceIntact(store, tenantId, r))) reasons.Add(ReviewReason.EvidenceMissing);
        if (capture is not null)
        {
            // Before closure the old protection must be as reviewed; after closure it must be gone or, when coexistence was kept, unchanged.
            var retired = current.Stage == CutoverStage.Closed && current.Retirement == RetirementDecision.Retire;
            if (retired ? current.OldObjectIds.Any(id => ObservedMaterial.Contains(capture, id))
                    : ObservedMaterial.Digest(capture, current.OldObjectIds) != current.OldMaterialDigest)
                reasons.Add(ReviewReason.MaterialChanged);
            if (current.NewObjectIds.Any(id => !ObservedMaterial.Contains(capture, id))) reasons.Add(ReviewReason.MaterialChanged);
        }
        if (!Timestamps.TryParse(current.ReviewDueAt, out var due) || due <= now) reasons.Add(ReviewReason.ReviewOverdue);
        if (current.ResidualDeviationIds.Any(id => SubjectReview.MissingDeviation(store, tenantId, id, current.ControlId, now))) reasons.Add(ReviewReason.DeviationMissing);
        return reasons;
    }

    private static bool Acyclic<T>(List<T> history) where T : IJobRecord
    {
        var byId = history.ToDictionary(o => o.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var start in history)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (IJobRecord? node = start; node is not null; node = node.SupersedesId is null ? null : byId.GetValueOrDefault(node.SupersedesId))
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
        if (current is TenantDisposition disposition)
        {
            if (disposition.DeviationId is not null && MissingDeviation(store, tenantId, disposition.DeviationId, disposition.ControlId, now))
                reasons.Add(ReviewReason.DeviationMissing);
            if (disposition.CaseId is not null && MissingCase(store, tenantId, disposition)) reasons.Add(ReviewReason.CaseMissing);
        }
        return reasons;
    }

    /// <summary>True unless the deviation exists in this tenant, is an approved deviation for the control and is in date.</summary>
    public static bool MissingDeviation(EvidenceStore store, string tenantId, string? deviationId, string controlId, DateTimeOffset now)
    {
        try
        {
            var deviation = store.LoadDeviations(tenantId).FirstOrDefault(d => string.Equals(d.Id, deviationId, StringComparison.OrdinalIgnoreCase));
            return deviation is null || deviation.Kind != DeviationKind.ApprovedDeviation
                || !string.Equals(deviation.ControlId, controlId, StringComparison.OrdinalIgnoreCase) || deviation.IsReviewOverdue(now);
        }
        catch (ToolkitException) { return true; }
    }

    /// <summary>True unless the disposition's cutover case is attached to its job for the same requirement instance.</summary>
    public static bool MissingCase(EvidenceStore store, string tenantId, TenantDisposition disposition)
    {
        try
        {
            var job = store.RequireJob(tenantId, disposition.JobId);
            return !(job.CutoverIds ?? new()).Select(id => store.LoadCutover(tenantId, id)).Any(c => c is not null
                && string.Equals(c.CaseId, disposition.CaseId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.SemanticId, disposition.SemanticId, StringComparison.Ordinal)
                && string.Equals(c.ControlId, disposition.ControlId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.InstanceKey, disposition.InstanceKey, StringComparison.OrdinalIgnoreCase));
        }
        catch (ToolkitException) { return true; }
    }

    public static bool SameStandard(string release, string digest, StandardCatalogue standard) =>
        release == standard.Release && !string.IsNullOrEmpty(standard.IntegrityDigest) && string.Equals(digest, standard.IntegrityDigest, StringComparison.OrdinalIgnoreCase);

    public static bool SameClient(string profileId, string scopeDigest, TenantProfile profile) =>
        string.Equals(profileId, profile.Id, StringComparison.OrdinalIgnoreCase) && scopeDigest == ReviewedClientScope.Digest(profile);

    public static bool EvidenceIntact(EvidenceStore store, string tenantId, EvidenceReference reference)
    {
        try
        {
            switch (reference.Kind)
            {
                case EvidenceKind.Snapshot:
                    var snapshot = store.LoadSnapshot(tenantId, reference.Id);
                    return snapshot is not null && store.SnapshotIntegrityIntact(snapshot) && snapshot.IntegrityDigest == reference.Sha256;
                case EvidenceKind.Run:
                    var run = store.LoadRun(tenantId, reference.Id);
                    return run is not null && EvidenceIntegrity.Verify(run, run.IntegrityDigest) && run.IntegrityDigest == reference.Sha256;
                case EvidenceKind.Assessment:
                    return store.LoadAssessment(tenantId, reference.Id)?.Sha256 == reference.Sha256;
                default:
                    return false;
            }
        }
        catch (ToolkitException) { return false; }
    }
}
