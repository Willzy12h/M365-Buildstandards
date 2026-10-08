using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Standards;

namespace BDIT.TenantToolkit.Engine.Workflow;

/// <summary>Where one requirement instance stands in the completion projection.</summary>
public static class RequirementState
{
    /// <summary>A current, in-date, unchallenged Pass relying on intact stored evidence, with no open decision or cutover.</summary>
    public const string Verified = "Verified";
    /// <summary>A current approved departure that still relies on an in-date approved deviation. Never a verification.</summary>
    public const string Departure = "ApprovedDeparture";
    /// <summary>An in-date not-applicable entry in the deviation register. Not counted as a requirement of this job.</summary>
    public const string NotApplicable = "NotApplicable";
    public const string Outstanding = "Outstanding";
}

/// <summary>One applicable requirement instance and why it does or does not stand.</summary>
public sealed class RequirementCompletion
{
    public string InstanceKey { get; init; } = "";
    public string ControlId { get; init; } = "";
    public string Name { get; init; } = "";
    public string State { get; init; } = RequirementState.Outstanding;
    /// <summary>The current observation's status, or empty when none is recorded.</summary>
    public string Outcome { get; init; } = "";
    /// <summary>The current disposition's decision, or empty when none is recorded.</summary>
    public string Decision { get; init; } = "";
    /// <summary>The stage of each cutover case for this instance, or empty when there is none.</summary>
    public string Cutover { get; init; } = "";
    /// <summary>Why the requirement is outstanding, or what it relies on when it is not. Written for engineers.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public bool Outstanding => State == RequirementState.Outstanding;
}

/// <summary>
/// INT-049 completion projection over a <see cref="JobProjection"/>: every applicable requirement needs a current
/// accepted outcome, or an approved, same-scope, in-date departure. Pending, Fail, Unknown, NeedsReview, an open
/// decision and an open cutover case never satisfy a requirement, and a job never completes because candidates exist
/// or a cutover stage was approved. Computing it writes nothing, and a complete job grants no authority.
/// </summary>
public sealed class JobCompletion
{
    public IReadOnlyList<RequirementCompletion> Requirements { get; init; } = Array.Empty<RequirementCompletion>();
    /// <summary>Problems with the job as a whole. Any one of them prevents a completion claim.</summary>
    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();

    public int Verified => Requirements.Count(r => r.State == RequirementState.Verified);
    public int Departures => Requirements.Count(r => r.State == RequirementState.Departure);
    public int NotApplicable => Requirements.Count(r => r.State == RequirementState.NotApplicable);
    public int Outstanding => Requirements.Count(r => r.Outstanding);
    public bool Complete => Blockers.Count == 0 && Outstanding == 0 && Requirements.Any(r => r.State != RequirementState.NotApplicable);

    /// <summary>The claim in one line. Approved departures are named as such, never as verification.</summary>
    public string Claim => !Complete
        ? $"Not complete: {Outstanding} of {Requirements.Count - NotApplicable} requirement(s) outstanding" + (Blockers.Count > 0 ? $", {Blockers.Count} job problem(s)." : ".")
        : Departures == 0
            ? $"Complete: all {Verified} requirement(s) verified."
            : $"Complete with approved departures: {Verified} verified, {Departures} relying on an approved deviation (not verified).";

    public static JobCompletion Build(EvidenceStore store, JobProjection projection, StandardCatalogue standard, TenantProfile profile, DateTimeOffset now)
    {
        var blockers = new List<string>(projection.JobReviewReasons.Select(r => "The job needs review. " + r));
        blockers.AddRange(projection.Unreadable.Select(u => $"An attached record ({u.File}) cannot be read: {u.Problem}"));

        IReadOnlyList<Deviation> deviations;
        try { deviations = store.LoadDeviations(projection.Job.TenantId); }
        catch (ToolkitException ex) { deviations = Array.Empty<Deviation>(); blockers.Add("The deviation register cannot be read: " + ex.Message); }

        var instances = ControlInstances.All(standard, profile);
        var writeHistoryBlocked = false;
        try { store.AssertCompletionWritesResolved(projection.Job.TenantId, instances.Select(c => c.Id)); }
        catch (ToolkitException ex) { writeHistoryBlocked = true; blockers.Add("Write history needs reconciliation: " + ex.Message); }
        foreach (var duplicate in projection.Subjects.Select(s => s.InstanceKey)
                     .GroupBy(k => k, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            blockers.Add($"{duplicate.Key} has conflicting outcome identities. Review its complete history before claiming completion.");
        foreach (var duplicate in projection.Dispositions.Select(d => d.InstanceKey)
                     .GroupBy(k => k, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            blockers.Add($"{duplicate.Key} has conflicting decision identities. Review its complete history before claiming completion.");
        var keys = instances.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stray in projection.Subjects.Select(s => s.InstanceKey)
                     .Concat(projection.Dispositions.Select(d => d.InstanceKey))
                     .Concat(projection.Cutovers.Select(c => c.InstanceKey))
                     .Where(k => !keys.Contains(k)).Distinct(StringComparer.OrdinalIgnoreCase))
            blockers.Add($"Records exist for {stray}, which is not a requirement under the current standard and client inputs.");

        var requirements = instances.Select(control =>
        {
            var subject = projection.Subjects.FirstOrDefault(s => Same(s.InstanceKey, control.Id));
            var decided = projection.Dispositions.FirstOrDefault(d => Same(d.InstanceKey, control.Id));
            var cases = projection.Cutovers.Where(c => Same(c.InstanceKey, control.Id)).ToList();
            var reasons = new List<string>();
            var outcomeConflict = projection.Subjects.Count(s => Same(s.InstanceKey, control.Id)) > 1;
            var decisionConflict = projection.Dispositions.Count(d => Same(d.InstanceKey, control.Id)) > 1;

            var notApplicable = deviations.FirstOrDefault(d => d.Kind == DeviationKind.NotApplicable && Same(d.ControlId, control.Id) && !d.IsReviewOverdue(now))
                ?? deviations.FirstOrDefault(d => d.Kind == DeviationKind.NotApplicable && Same(d.ControlId, BaseControl(control, standard)) && !d.IsReviewOverdue(now));
            var departure = decided is { Settled: true, Current.Decision: DispositionDecision.ApprovedDeparture };

            if (subject is null) reasons.Add("No outcome is recorded in this job.");
            else if (subject.Current is null) reasons.Add("Its outcome history needs review before a current outcome can be read.");
            else if (subject.Current.Status != ObservationStatus.Pass) reasons.Add($"The current outcome is {subject.Current.Status}.");
            else if (subject.Current.Evidence.Count == 0) reasons.Add("The current pass references no stored evidence. Record it against a saved capture.");
            if (subject is not null) reasons.AddRange(subject.ReviewReasons.Select(r => "Outcome: " + r));

            if (decided is not null)
            {
                if (decided.Current is null) reasons.Add("Its decision history needs review before a current decision can be read.");
                else if (!DispositionDecision.Settles(decided.Current.Decision))
                    reasons.Add($"The current decision, {Words(decided.Current.Decision)}, leaves work outstanding.");
                reasons.AddRange(decided.ReviewReasons.Select(r => "Decision: " + r));
            }
            foreach (var c in cases.Where(c => !c.Closed))
                reasons.Add(c.Escalated ? $"Cutover case {Short(c.CaseId)} is stopped on an unsupported scenario: {c.Current!.Unsupported!.Reason}"
                    : c.Current is null ? $"Cutover case {Short(c.CaseId)} needs review before its stage can be read."
                    : c.NeedsReview ? $"Cutover case {Short(c.CaseId)} needs review: {string.Join(" ", c.ReviewReasons)}"
                    : $"Cutover case {Short(c.CaseId)} is open at {Words(c.Current.Stage)}.");

            var openCase = cases.Any(c => !c.Closed);
            var openDecision = decided is not null && !decided.Settled;
            string state;
            if (outcomeConflict || decisionConflict || writeHistoryBlocked)
            {
                state = RequirementState.Outstanding;
                if (outcomeConflict || decisionConflict) reasons.Add("Conflicting requirement identities need review; no one history is selected as authoritative.");
                if (writeHistoryBlocked) reasons.Add("Tenant write history must be reconciled before this job's completion can be verified.");
            }
            else if (notApplicable is not null && subject is null && decided is null && cases.Count == 0)
            {
                state = RequirementState.NotApplicable;
                reasons.Clear();
                reasons.Add($"Not applicable in the deviation register, approved by {notApplicable.ApprovedBy}, review by {notApplicable.ReviewBy}.");
            }
            else if (departure && !openCase)
            {
                state = RequirementState.Departure;
                reasons.Clear();
                reasons.Add($"Relies on approved deviation {Short(decided!.Current!.DeviationId ?? "")}; this is an exception, not a verified outcome.");
            }
            else if (subject is { Accepted: true, Current.Evidence.Count: > 0 } && !openDecision && !openCase)
            {
                state = RequirementState.Verified;
                reasons.Clear();
            }
            else
            {
                state = RequirementState.Outstanding;
                if (reasons.Count == 0) reasons.Add("Not yet verified.");
            }

            return new RequirementCompletion
            {
                InstanceKey = control.Id,
                ControlId = BaseControl(control, standard),
                Name = control.Name,
                State = state,
                Outcome = outcomeConflict ? "Needs review" : subject?.Current?.Status ?? (subject is null ? "" : "Needs review"),
                Decision = decisionConflict ? "Needs review" : decided?.Current?.Decision ?? (decided is null ? "" : "Needs review"),
                Cutover = string.Join(", ", cases.Select(c => c.Current is null ? "Needs review" : Words(c.Current.Stage))),
                Reasons = reasons.Distinct().ToList()
            };
        }).ToList();

        return new JobCompletion { Requirements = requirements, Blockers = blockers };
    }

    /// <summary>The catalogue control an instance comes from; office instances share their control.</summary>
    private static string BaseControl(ControlDefinition instance, StandardCatalogue standard) =>
        standard.Controls.FirstOrDefault(c => Same(c.Id, instance.Id))?.Id
        ?? standard.Controls.Where(c => instance.Id.StartsWith(c.Id + "-", StringComparison.OrdinalIgnoreCase)).OrderByDescending(c => c.Id.Length).FirstOrDefault()?.Id
        ?? instance.Id;

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string Short(string id) => id.Length > 8 ? id[..8] : id;

    /// <summary>"RetainExternalCoverage" as "retain external coverage".</summary>
    public static string Words(string value) =>
        string.Concat(value.Select((ch, i) => i > 0 && char.IsUpper(ch) ? " " + char.ToLowerInvariant(ch) : (i == 0 ? char.ToLowerInvariant(ch) : ch).ToString()));
}

/// <summary>The stable requirement identity a new record is written under.</summary>
public static class SemanticIdentity
{
    /// <summary>
    /// An existing line of history keeps its own identity. Otherwise the identity is the one the shipped lineage into
    /// this release declares for the control, or the control ID when lineage declares none (or disagrees with itself).
    /// </summary>
    public static string Resolve(string controlId, ReleaseLineage? lineage, string? existing = null)
    {
        if (!string.IsNullOrWhiteSpace(existing)) return existing;
        var declared = (lineage?.Sources ?? new()).SelectMany(s => s.Relations)
            .Where(r => r.SemanticId.Length > 0 && r.TargetControls.Any(t => string.Equals(t, controlId, StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.SemanticId).Distinct(StringComparer.Ordinal).ToList();
        return declared.Count == 1 ? declared[0] : controlId;
    }
}
