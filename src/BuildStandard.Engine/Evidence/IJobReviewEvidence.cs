using BuildStandard.Core.Models;

namespace BuildStandard.Engine.Evidence;

/// <summary>Only the reads needed by job projections. Captured reviews cannot write or reload live files.</summary>
internal interface IJobReviewEvidence
{
    TenantJob RequireJob(string tenant, string id);
    (IReadOnlyList<TenantObservation> Observations, IReadOnlyList<UnreadableRecord> Unreadable) LoadObservations(string tenant);
    (IReadOnlyList<TenantDisposition> Dispositions, IReadOnlyList<UnreadableRecord> Unreadable) LoadDispositions(string tenant);
    (IReadOnlyList<CutoverRevision> Revisions, IReadOnlyList<UnreadableRecord> Unreadable) LoadCutovers(string tenant);
    CutoverRevision? LoadCutover(string tenant, string id);
    ManualCheckRegister LoadManualChecks(string tenant);
    IReadOnlyList<Deviation> LoadDeviations(string tenant);
    TenantSnapshot? LoadSnapshot(string tenant, string id);
    DeploymentRun? LoadRun(string tenant, string id);
    StoredAssessment? LoadAssessment(string tenant, string id);
    bool SnapshotIntegrityIntact(TenantSnapshot snapshot);
    void AssertCompletionWritesResolved(string tenant, IEnumerable<string> controls);
}

public sealed partial class EvidenceStore : IJobReviewEvidence { }
