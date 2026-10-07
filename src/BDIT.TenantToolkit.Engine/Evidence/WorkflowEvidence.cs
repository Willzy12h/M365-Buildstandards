using System.Text.Json;
using System.Text.Json.Serialization;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Workflow;

namespace BDIT.TenantToolkit.Engine.Evidence;

/// <summary>A stored workflow record that could not be used, and why. Listed so it is visible rather than skipped.</summary>
public sealed record UnreadableRecord(string File, string Problem);

/// <summary>
/// INT-049 persistence: tenant-partitioned jobs/&lt;id&gt;.json and immutable observations/&lt;id&gt;.json. Readers refuse
/// unknown schema versions and unknown members, check the embedded tenant and ID against the file, and verify the
/// integrity digest. Observations are created once and never replaced; only a job's attachment list is replaced.
/// </summary>
public sealed partial class EvidenceStore
{
    // New record readers refuse members they do not know; adding one needs a deliberate schema version.
    private static readonly JsonSerializerOptions StrictRecords = new(ToolkitJson.Options) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    private string JobsDirectory(string tenant) => Path.Combine(TenantDirectory(tenant), "jobs");
    private string ObservationsDirectory(string tenant) => Path.Combine(TenantDirectory(tenant), "observations");
    private string JobFile(string tenant, string id) => Path.Combine(JobsDirectory(tenant), SafeId(id) + ".json");
    private string ObservationFile(string tenant, string id) => Path.Combine(ObservationsDirectory(tenant), SafeId(id) + ".json");

    // ---- jobs ------------------------------------------------------------------------------------------------

    public void CreateJob(TenantJob job)
    {
        WorkflowRecordRules.ValidateJob(job);
        job.IntegrityDigest = RecoveryDigest(job);
        CreateRecordFile(JobFile(job.TenantId, job.Id), job, "job");
        _log.Info("Evidence", $"Job {job.Id} opened ({job.Intention}).", job.TenantId);
    }

    /// <summary>Replaces a job after checking that only its mutable fields changed.</summary>
    public void ReplaceJob(TenantJob job)
    {
        WorkflowRecordRules.ValidateJob(job);
        lock (_gate)
        {
            var stored = RequireJob(job.TenantId, job.Id);
            if (stored.ProfileId != job.ProfileId || stored.Actor != job.Actor || stored.CreatedAt != job.CreatedAt
                || stored.Intention != job.Intention || stored.StandardRelease != job.StandardRelease
                || stored.StandardDigest != job.StandardDigest || stored.ClientScopeDigest != job.ClientScopeDigest)
                throw new SafetyViolationException("A job's identity, intention, standard and client inputs cannot be changed. Open a new job instead.");
            if (stored.ObservationIds.Except(job.ObservationIds, StringComparer.OrdinalIgnoreCase).Any())
                throw new SafetyViolationException("Attached observations cannot be detached from a job.");
            job.IntegrityDigest = RecoveryDigest(job);
            WriteJsonAtomic(JobFile(job.TenantId, job.Id), job);
        }
    }

    public TenantJob RequireJob(string tenant, string id) =>
        ReadWorkflowRecord<TenantJob>(JobFile(tenant, id), tenant, id, "Job", j => (j.SchemaVersion, j.TenantId, j.Id, j.IntegrityDigest))
        ?? throw new ConfigurationException("The job is not saved for this tenant.");

    public bool JobExists(string tenant, string id) => File.Exists(JobFile(tenant, id));

    public (IReadOnlyList<TenantJob> Jobs, IReadOnlyList<UnreadableRecord> Unreadable) LoadJobs(string tenant) =>
        LoadWorkflowRecords<TenantJob>(JobsDirectory(tenant), tenant, "Job", j => (j.SchemaVersion, j.TenantId, j.Id, j.IntegrityDigest));

    // ---- observations ----------------------------------------------------------------------------------------

    /// <summary>Writes a new immutable observation. It is unattached until its job is replaced to list it.</summary>
    public void CreateObservation(TenantObservation observation)
    {
        WorkflowRecordRules.ValidateObservation(observation);
        observation.IntegrityDigest = RecoveryDigest(observation);
        CreateRecordFile(ObservationFile(observation.TenantId, observation.Id), observation, "observation");
    }

    public TenantObservation? LoadObservation(string tenant, string id) =>
        ReadWorkflowRecord<TenantObservation>(ObservationFile(tenant, id), tenant, id, "Observation", o => (o.SchemaVersion, o.TenantId, o.Id, o.IntegrityDigest));

    public (IReadOnlyList<TenantObservation> Observations, IReadOnlyList<UnreadableRecord> Unreadable) LoadObservations(string tenant) =>
        LoadWorkflowRecords<TenantObservation>(ObservationsDirectory(tenant), tenant, "Observation", o => (o.SchemaVersion, o.TenantId, o.Id, o.IntegrityDigest));

    // ---- shared ----------------------------------------------------------------------------------------------

    /// <summary>Atomic create: the record appears whole or not at all, and an existing ID is never replaced.</summary>
    private void CreateRecordFile<T>(string file, T value, string what)
    {
        lock (_gate)
        {
            if (File.Exists(file)) throw new SafetyViolationException($"A {what} with this ID already exists. Records are never replaced.");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(ToolkitJson.Serialize(value));
                    writer.Flush();
                    stream.Flush(true);
                }
                try { File.Move(temp, file, overwrite: false); }
                catch (IOException) when (File.Exists(file))
                {
                    throw new SafetyViolationException($"A {what} with this ID already exists. Records are never replaced.");
                }
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private T? ReadWorkflowRecord<T>(string file, string tenant, string id, string what, Func<T, (int Schema, string Tenant, string Id, string Digest)> identity) where T : class
    {
        if (!File.Exists(file)) return null;
        T record;
        try { record = JsonSerializer.Deserialize<T>(File.ReadAllText(file), StrictRecords) ?? throw new ConfigurationException($"{what} record is empty."); }
        catch (JsonException ex) { throw new ConfigurationException($"{what} record {Path.GetFileName(file)} is not a schema {TenantJob.CurrentSchemaVersion} record: {ex.Message}", ex); }
        var (schema, recordTenant, recordId, digest) = identity(record);
        if (schema != 1) throw new ConfigurationException($"{what} record {Path.GetFileName(file)} uses schema version {schema}; this build reads version 1 only.");
        AssertTenant(tenant, recordTenant, what);
        if (!string.Equals(recordId, id, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException($"{what} record {Path.GetFileName(file)} does not carry the ID its file name claims.");
        if (string.IsNullOrEmpty(digest) || digest != RecoveryDigest(record))
            throw new IntegrityException($"{what} {recordId} failed its integrity check. Treat it as modified; it is not used.");
        return record;
    }

    private (IReadOnlyList<T>, IReadOnlyList<UnreadableRecord>) LoadWorkflowRecords<T>(string directory, string tenant, string what,
        Func<T, (int Schema, string Tenant, string Id, string Digest)> identity) where T : class
    {
        var records = new List<T>();
        var unreadable = new List<UnreadableRecord>();
        if (!Directory.Exists(directory)) return (records, unreadable);
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            try
            {
                if (!Core.Models.ProfileValidator.IsGuid(id)) throw new ConfigurationException($"{what} file name is not a record ID.");
                records.Add(ReadWorkflowRecord(file, tenant, id, what, identity)!);
            }
            catch (ToolkitException ex)
            {
                unreadable.Add(new UnreadableRecord(file, ex.Message));
                _log.Warn("Evidence", ex.Message, tenant);
            }
        }
        return (records, unreadable);
    }
}
