using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Workflow;

namespace BuildStandard.Engine.Evidence;

/// <summary>Private, materialised reads only. Job builders use the same rules without touching live storage.</summary>
internal sealed class CapturedClientReviewStore : IJobReviewEvidence
{
    private readonly ClientReviewEvidence _evidence;
    private readonly IReadOnlyList<TenantJob> _jobs;
    private readonly IReadOnlyList<TenantObservation> _observations;
    private readonly IReadOnlyList<TenantDisposition> _dispositions;
    private readonly IReadOnlyList<CutoverRevision> _cutovers;
    private readonly IReadOnlyList<TenantSnapshot> _snapshots;
    private readonly IReadOnlyList<DeploymentRun> _runs;
    private readonly IReadOnlyList<StoredAssessment> _assessments;
    private readonly IReadOnlyList<Deviation> _deviations;
    private readonly ManualCheckRegister _manual;
    internal IReadOnlyList<ClientReviewWrite> Writes { get; }
    internal bool WriteHistoryKnown { get; }
    internal CapturedClientReviewStore(ClientReviewEvidence evidence)
    {
        _evidence = evidence;
        _jobs=evidence.Records<TenantJob>("jobs"); _observations=evidence.Records<TenantObservation>("observations");
        _dispositions=evidence.Records<TenantDisposition>("dispositions"); _cutovers=evidence.Records<CutoverRevision>("cutovers");
        _snapshots=evidence.Records<TenantSnapshot>("snapshots"); _runs=evidence.Runs; _assessments=evidence.Assessments;
        _deviations=evidence.Records<List<Deviation>>("deviations").SelectMany(d=>d).ToList().AsReadOnly();
        _manual=evidence.Records<ManualCheckRegister>("manual-checks").SingleOrDefault() ?? new() { TenantId=evidence.TenantId };
        WriteHistoryKnown=evidence.SourceSetComplete && !evidence.Diagnostics.Any(d => d.Section is "client" or "runs" or "recovery" or "reviewed-changes" or "entra-laps" or "verification");
        var verifications=evidence.Records<WriteVerification>("verification");
        var changes=evidence.Records<ReviewedChangeVerification>("reviewed-changes","verification-");
        var laps=evidence.Records<EntraLapsVerification>("entra-laps","verification-");
        var writes=new List<ClientReviewWrite>();
        foreach(var run in _runs)
            foreach(var row in run.Results)
            {
                var historicalAccepted=EvidenceStore.IsHistoricalCandidate(run,row) && verifications.Any(v => v.Verified && v.HistoricalAcknowledged
                    && v.SourceKind=="Deployment" && v.SourceRunId==run.Id && v.SourceDigest==run.IntegrityDigest && v.ControlId==row.ControlId);
                var unresolved=(WriteAcceptance.IsUncertain(row.WriteAcceptance) && !historicalAccepted)
                    || (row.WriteAcceptance==WriteAcceptance.Accepted && !ProfileValidator.IsGuid(row.ObjectId ?? ""));
                writes.Add(new("Deployment",run.Id,row.ControlId,row.WriteAcceptance,unresolved));
            }
        foreach(var run in evidence.Records<RecoveryRun>("recovery","run-"))
            writes.Add(new("Recovery",run.Id,run.ControlId,run.WriteAcceptance,WriteAcceptance.IsUncertain(run.WriteAcceptance)
                || (run.WriteAcceptance==WriteAcceptance.Accepted && (run.Verification!=ConfigurationVerification.Pass || run.Status!=RunStatus.Completed)
                    && !verifications.Any(v => v.Verified && v.SourceKind=="Recovery" && v.SourceRunId==run.Id && v.SourceDigest==run.IntegrityDigest && v.ControlId==run.ControlId))));
        foreach(var run in evidence.Records<ReviewedChangeRun>("reviewed-changes","run-"))
            writes.Add(new("Reviewed change",run.Id,run.ControlId,run.WriteAcceptance,WriteAcceptance.IsUncertain(run.WriteAcceptance)
                || (run.WriteAcceptance==WriteAcceptance.Accepted && run.Verification!=ConfigurationVerification.Pass
                    && !changes.Any(v => v.Verified && v.RunId==run.Id && v.RunDigest==run.IntegrityDigest))));
        foreach(var run in evidence.Records<EntraLapsRun>("entra-laps","run-"))
            writes.Add(new("Entra LAPS",run.Id,"Entra LAPS",run.WriteAcceptance,WriteAcceptance.IsUncertain(run.WriteAcceptance)
                || (run.WriteAcceptance==WriteAcceptance.Accepted && run.Verification!=ConfigurationVerification.Pass
                    && !laps.Any(v => v.Verification==ConfigurationVerification.Pass && v.RunId==run.Id && v.RunDigest==run.IntegrityDigest))));
        Writes=writes.AsReadOnly();
    }
    private void RequireTenant(string tenant)
    { if(!string.Equals(tenant,_evidence.TenantId,StringComparison.OrdinalIgnoreCase)) throw new ConfigurationException("Captured review belongs to another client."); }
    private IReadOnlyList<UnreadableRecord> Problems(string section) => _evidence.Diagnostics.Where(d=>d.Section==section)
        .Select(d=>new UnreadableRecord(d.File,d.Problem)).ToList();
    public TenantJob RequireJob(string tenant,string id)
    { RequireTenant(tenant); return _jobs.SingleOrDefault(j=>Same(j.Id,id)) ?? throw new ConfigurationException("Job was not captured."); }
    public (IReadOnlyList<TenantObservation> Observations,IReadOnlyList<UnreadableRecord> Unreadable) LoadObservations(string tenant)
    { RequireTenant(tenant); return (_observations,Problems("observations")); }
    public (IReadOnlyList<TenantDisposition> Dispositions,IReadOnlyList<UnreadableRecord> Unreadable) LoadDispositions(string tenant)
    { RequireTenant(tenant); return (_dispositions,Problems("dispositions")); }
    public (IReadOnlyList<CutoverRevision> Revisions,IReadOnlyList<UnreadableRecord> Unreadable) LoadCutovers(string tenant)
    { RequireTenant(tenant); return (_cutovers,Problems("cutovers")); }
    public CutoverRevision? LoadCutover(string tenant,string id)
    { RequireTenant(tenant); return _cutovers.SingleOrDefault(c=>Same(c.Id,id)); }
    public ManualCheckRegister LoadManualChecks(string tenant) { RequireTenant(tenant); return _manual; }
    public IReadOnlyList<Deviation> LoadDeviations(string tenant) { RequireTenant(tenant); return _deviations; }
    public TenantSnapshot? LoadSnapshot(string tenant,string id) { RequireTenant(tenant); return _snapshots.SingleOrDefault(s=>Same(s.Id,id)); }
    public DeploymentRun? LoadRun(string tenant,string id) { RequireTenant(tenant); return _runs.SingleOrDefault(r=>Same(r.Id,id)); }
    public StoredAssessment? LoadAssessment(string tenant,string id) { RequireTenant(tenant); return _assessments.SingleOrDefault(a=>Same(a.Result.Id,id)); }
    public bool SnapshotIntegrityIntact(TenantSnapshot snapshot) => EvidenceIntegrity.Verify(snapshot,snapshot.IntegrityDigest);
    public void AssertCompletionWritesResolved(string tenant,IEnumerable<string> controls)
    {
        RequireTenant(tenant);
        // A portfolio review is conservative for the whole client, including other controls. It is never a write gate.
        if(!WriteHistoryKnown) throw new ConfigurationException("Captured write history is incomplete or unreadable.");
        var unresolved=Writes.FirstOrDefault(w=>w.Unresolved);
        if(unresolved is not null) throw new ConfigurationException($"{unresolved.Kind} {unresolved.RunId} has an unresolved write outcome.");
    }
    private static bool Same(string a,string b) => string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
}
