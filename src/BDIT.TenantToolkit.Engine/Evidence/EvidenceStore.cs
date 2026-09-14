using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Evidence;

public sealed record SnapshotSummary(string Id, string CapturedAt, bool Complete, string StandardRelease, string CapturedBy, string SessionMode, string File);
public sealed record RunSummary(string Id, string StartedAt, string? EndedAt, string Status, string Release, string File);
public sealed record PlanSummary(string Id, string CreatedAt, string Release, int WriteRows, string File);

/// <summary>Tamper-evident digests for evidence records. Detects casual modification; it is not a signature.</summary>
public static class EvidenceIntegrity
{
    public const string PropertyName = "integrityDigest";

    public static string Compute<T>(T value)
    {
        var node = ToolkitJson.ToNode(value) as JsonObject ?? throw new ConfigurationException("Evidence records must serialise to JSON objects.");
        node[PropertyName] = "";
        return CanonicalJson.Sha256(node);
    }

    public static bool Verify<T>(T value, string recordedDigest)
    {
        if (string.IsNullOrEmpty(recordedDigest)) return false;
        return string.Equals(Compute(value), recordedDigest, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Local, tenant-partitioned evidence. Every record lives under data/tenants/&lt;tenantId&gt;/ and every load checks the
/// embedded tenant ID, so evidence from one client can never be substituted for another. Writes are atomic
/// (temp file + rename) and important records carry an integrity digest.
/// </summary>
public sealed partial class EvidenceStore
{
    private readonly ToolkitPaths _paths;
    private readonly IToolkitLog _log;
    private readonly object _gate = new();

    public EvidenceStore(ToolkitPaths paths, IToolkitLog log)
    {
        _paths = paths;
        _log = log;
    }

    public string RootDirectory => _paths.DataDirectory;

    public string TenantDirectory(string tenantId) => _paths.TenantDirectory(tenantId);

    // ---- generic helpers -------------------------------------------------------------------------------------

    public void WriteJsonAtomic<T>(string file, T value)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(ToolkitJson.Serialize(value));
                writer.Flush();
                stream.Flush(true);
            }
            try
            {
                for (var attempt = 0; ; attempt++)
                {
                    try { File.Move(temp, file, overwrite: true); break; }
                    catch (Exception ex) when (attempt < 4 && IsTemporaryReplacementLock(ex))
                    {
                        // Windows scanners/readers can briefly deny replacement after close. Retry only the local
                        // rename of already-flushed bytes; this never repeats a Graph request or changes its outcome.
                        Thread.Sleep(20 << attempt);
                    }
                }
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static bool IsTemporaryReplacementLock(Exception ex) =>
        (ex is IOException or UnauthorizedAccessException) && (ex.HResult & 0xffff) is 5 or 32 or 33;

    public T? ReadJson<T>(string file) where T : class
    {
        if (!File.Exists(file)) return null;
        try
        {
            return ToolkitJson.Deserialize<T>(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ConfigurationException)
        {
            throw new ConfigurationException($"Stored data is invalid: {Path.GetFileName(file)} ({ex.Message})", ex);
        }
    }

    public void AppendLine(string file, string line)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(line);
            writer.Flush();
            stream.Flush(true);
        }
    }

    private static string SafeId(string id)
    {
        if (!ProfileValidator.IsGuid(id)) throw new ConfigurationException("Evidence identifiers must be GUIDs.");
        return id.ToLowerInvariant();
    }

    private static string StampFor(string isoTimestamp) =>
        Timestamps.TryParse(isoTimestamp, out var t) ? t.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) : "00000000-000000";

    private void AssertTenant(string expected, string actual, string what)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException($"{what} belongs to tenant {actual}, not {expected}. Refusing to use it.");
    }

    // ---- profiles ----------------------------------------------------------------------------------------------

    public IReadOnlyList<TenantProfile> LoadProfiles() =>
        ReadJson<List<TenantProfile>>(_paths.ProfilesFile) ?? new List<TenantProfile>();

    public void SaveProfiles(IReadOnlyList<TenantProfile> profiles) => WriteJsonAtomic(_paths.ProfilesFile, profiles);

    // ---- snapshots ---------------------------------------------------------------------------------------------

    private string SnapshotsDirectory(string tenantId) => Path.Combine(TenantDirectory(tenantId), "snapshots");

    public string SaveSnapshot(TenantSnapshot snapshot)
    {
        snapshot.IntegrityDigest = EvidenceIntegrity.Compute(snapshot);
        var file = Path.Combine(SnapshotsDirectory(snapshot.TenantId), $"{StampFor(snapshot.CapturedAt)}-{SafeId(snapshot.Id)}.json");
        WriteJsonAtomic(file, snapshot);
        _log.Info("Evidence", $"Snapshot {snapshot.Id} saved ({(snapshot.Complete ? "complete" : "incomplete")}).", snapshot.TenantId);
        return file;
    }

    public TenantSnapshot? LoadSnapshot(string tenantId, string snapshotId)
    {
        var id = SafeId(snapshotId);
        var dir = SnapshotsDirectory(tenantId);
        if (!Directory.Exists(dir)) return null;
        var matches = Directory.EnumerateFiles(dir, $"*-{id}.json").Take(2).ToList();
        if (matches.Count > 1) throw new ConfigurationException("Multiple files use this snapshot ID. Reconcile the evidence before deployment.");
        var file = matches.SingleOrDefault();
        if (file is null) return null;
        var snapshot = ReadJson<TenantSnapshot>(file);
        if (snapshot is null) return null;
        AssertTenant(tenantId, snapshot.TenantId, "Snapshot");
        if (!string.Equals(snapshot.Id, snapshotId, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("The stored snapshot ID does not match the requested evidence.");
        if (!EvidenceIntegrity.Verify(snapshot, snapshot.IntegrityDigest))
            _log.Warn("Evidence", $"Snapshot {snapshot.Id} failed its integrity digest check; treat it as modified.", tenantId);
        return snapshot;
    }

    public bool SnapshotIntegrityIntact(TenantSnapshot snapshot) => EvidenceIntegrity.Verify(snapshot, snapshot.IntegrityDigest);

    public TenantSnapshot RequireDeploymentSnapshot(TenantSnapshot expected, StandardCatalogue standard)
    {
        var stored = LoadSnapshot(expected.TenantId, expected.Id)
            ?? throw new PlanValidationException("The acknowledged before-change snapshot is not saved. Capture and save it before deploying.");
        if (!SnapshotIntegrityIntact(stored) || !SnapshotIntegrityIntact(expected))
            throw new PlanValidationException("The before-change snapshot failed its integrity check. Capture and review a new snapshot.");
        if (EvidenceIntegrity.Compute(stored) != EvidenceIntegrity.Compute(expected))
            throw new PlanValidationException("The saved before-change snapshot differs from the reviewed snapshot. Rebuild the plan.");
        SnapshotRequirements.AssertComplete(stored, standard);
        return stored;
    }

    public IReadOnlyList<SnapshotSummary> ListSnapshots(string tenantId)
    {
        var dir = SnapshotsDirectory(tenantId);
        var list = new List<SnapshotSummary>();
        if (!Directory.Exists(dir)) return list;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var s = ReadJson<TenantSnapshot>(file);
                if (s is null || !string.Equals(s.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new SnapshotSummary(s.Id, s.CapturedAt, s.Complete, s.StandardRelease, s.CapturedBy, s.SessionMode, file));
            }
            catch (ConfigurationException ex)
            {
                _log.Warn("Evidence", ex.Message, tenantId);
            }
        }
        return list.OrderByDescending(s => s.CapturedAt, StringComparer.Ordinal).ToList();
    }

    // ---- plans -------------------------------------------------------------------------------------------------

    private string PlansDirectory(string tenantId) => Path.Combine(TenantDirectory(tenantId), "plans");

    public string SavePlan(DeploymentPlan plan)
    {
        var file = Path.Combine(PlansDirectory(plan.TenantId), $"{StampFor(plan.CreatedAt)}-{SafeId(plan.Id)}.json");
        WriteJsonAtomic(file, plan);
        return file;
    }

    public DeploymentPlan? LoadPlan(string tenantId, string planId)
    {
        var dir = PlansDirectory(tenantId);
        if (!Directory.Exists(dir)) return null;
        var file = Directory.EnumerateFiles(dir, $"*-{SafeId(planId)}.json").FirstOrDefault();
        if (file is null) return null;
        var plan = ReadJson<DeploymentPlan>(file);
        if (plan is null) return null;
        AssertTenant(tenantId, plan.TenantId, "Plan");
        return plan;
    }

    public IReadOnlyList<PlanSummary> ListPlans(string tenantId)
    {
        var dir = PlansDirectory(tenantId);
        var list = new List<PlanSummary>();
        if (!Directory.Exists(dir)) return list;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var p = ReadJson<DeploymentPlan>(file);
                if (p is null || !string.Equals(p.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new PlanSummary(p.Id, p.CreatedAt, p.Release, p.Rows.Count(r => r.IsWrite), file));
            }
            catch (ConfigurationException ex) { _log.Warn("Evidence", ex.Message, tenantId); }
        }
        return list.OrderByDescending(p => p.CreatedAt, StringComparer.Ordinal).ToList();
    }

    // ---- runs and journals ------------------------------------------------------------------------------------

    private string RunsDirectory(string tenantId) => Path.Combine(TenantDirectory(tenantId), "runs");

    public void AssertPlanHasNotRun(DeploymentPlan plan)
    {
        AssertNoUnresolvedRecovery(plan.TenantId, plan.WriteRows.Select(r => r.ControlId));
        var directory = RunsDirectory(plan.TenantId);
        if (!Directory.Exists(directory)) return;
        var controls = plan.WriteRows.Select(r => r.ControlId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            // Do not silently skip unreadable history: it may describe the previous ambiguous attempt.
            var run = ReadJson<DeploymentRun>(file) ?? throw new ConfigurationException("Previous run evidence is empty. Reconcile it before deploying.");
            AssertTenant(plan.TenantId, run.TenantId, "Previous run");
            if (!EvidenceIntegrity.Verify(run, run.IntegrityDigest))
                throw new PlanValidationException("Previous run evidence failed its integrity check. Reconcile it before deploying.");
            if (string.Equals(run.PlanId, plan.Id, StringComparison.OrdinalIgnoreCase))
                throw new PlanValidationException("This plan already has a deployment attempt. Reconcile its results, capture fresh evidence and review a new plan; existing plans are never replayed.");
            foreach (var result in run.Results.Where(r => controls.Contains(r.ControlId)))
            {
                // Fresh snapshots cannot prove an ambiguous create did not succeed: Graph may be eventually consistent.
                var couldHaveWritten = result.Status is ResultStatus.InProgress or ResultStatus.Completed or ResultStatus.Error
                    || result.PlannedAction is nameof(PlanAction.Create) or nameof(PlanAction.Update);
                var unresolved = result.Status != ResultStatus.NotRun && couldHaveWritten
                    && ((result.WriteAcceptance == WriteAcceptance.Unknown && !HasAcceptedWrite(run, result))
                        || (result.WriteAcceptance == WriteAcceptance.Accepted && !ProfileValidator.IsGuid(result.ObjectId ?? "")));
                if (unresolved)
                    throw new PlanValidationException($"{result.ControlId} has an unresolved write in run {run.Id}. A fresh plan or missing search result cannot clear this uncertainty. Preserve the evidence and reconcile manually; this release has no automatic reconciliation override.");
            }
        }
    }

    public string RunFile(DeploymentRun run) => Path.Combine(RunsDirectory(run.TenantId), $"{StampFor(run.StartedAt)}-{SafeId(run.Id)}.json");

    public void SaveRun(DeploymentRun run)
    {
        run.IntegrityDigest = EvidenceIntegrity.Compute(run);
        WriteJsonAtomic(RunFile(run), run);
    }

    public DeploymentRun? LoadRun(string tenantId, string runId)
    {
        var dir = RunsDirectory(tenantId);
        if (!Directory.Exists(dir)) return null;
        var file = Directory.EnumerateFiles(dir, $"*-{SafeId(runId)}.json").FirstOrDefault();
        if (file is null) return null;
        var run = ReadJson<DeploymentRun>(file);
        if (run is null) return null;
        AssertTenant(tenantId, run.TenantId, "Run");
        if (!EvidenceIntegrity.Verify(run, run.IntegrityDigest))
            _log.Warn("Evidence", $"Run {run.Id} failed its integrity digest check; treat it as modified.", tenantId);
        return run;
    }

    public IReadOnlyList<DeploymentRun> LoadRuns(string tenantId)
    {
        var dir = RunsDirectory(tenantId);
        var list = new List<DeploymentRun>();
        if (!Directory.Exists(dir)) return list;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var r = ReadJson<DeploymentRun>(file);
                if (r is null || !string.Equals(r.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(r);
            }
            catch (ConfigurationException ex) { _log.Warn("Evidence", ex.Message, tenantId); }
        }
        return list.OrderByDescending(r => r.StartedAt, StringComparer.Ordinal).ToList();
    }

    /// <summary>Marks runs that were still "Running" when a previous host ended. Called once at start-up per tenant.</summary>
    public int MarkInterruptedRuns(string tenantId)
    {
        var count = 0;
        foreach (var run in LoadRuns(tenantId))
        {
            if (run.Status != RunStatus.Running) continue;
            run.Status = RunStatus.Interrupted;
            run.Error = "The previous toolkit session ended before final evidence was captured. Reassess the tenant and reconcile before further writes.";
            run.EndedAt ??= Timestamps.Format(DateTimeOffset.UtcNow);
            foreach (var r in run.Results)
            {
                if (r.Status == ResultStatus.InProgress)
                {
                    r.Status = ResultStatus.Error;
                    r.WriteAcceptance = WriteAcceptance.Unknown;
                    r.Reason = "The previous session ended during a write. Reconcile its outcome before retrying.";
                }
                else if (r.Status == ResultStatus.Pending)
                {
                    r.Status = ResultStatus.NotRun;
                    r.WriteAcceptance = WriteAcceptance.NotAttempted;
                }
                if (r.Configuration == ConfigurationVerification.Pending)
                    r.Configuration = r.Status == ResultStatus.NotRun ? ConfigurationVerification.NotRun : ConfigurationVerification.Unknown;
            }
            SaveRun(run);
            count++;
        }
        if (count > 0) _log.Warn("Evidence", $"{count} interrupted run(s) marked for review.", tenantId);
        return count;
    }

    public string JournalFile(string tenantId, string runId) => Path.Combine(RunsDirectory(tenantId), $"journal-{SafeId(runId)}.jsonl");

    public void AppendJournal(string tenantId, string runId, JournalEntry entry) =>
        AppendLine(JournalFile(tenantId, runId), System.Text.Json.JsonSerializer.Serialize(entry, ToolkitJson.Compact));

    public IReadOnlyList<JournalEntry> ReadJournal(string tenantId, string runId)
    {
        var file = JournalFile(tenantId, runId);
        var list = new List<JournalEntry>();
        if (!File.Exists(file)) return list;
        foreach (var line in File.ReadLines(file))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { list.Add(ToolkitJson.Deserialize<JournalEntry>(line)); } catch (Exception ex) when (ex is System.Text.Json.JsonException or ConfigurationException) { }
        }
        return list;
    }

    // ---- mappings, deviations, manual checks ---------------------------------------------------------------------

    private string MappingsFile(string tenantId) => Path.Combine(TenantDirectory(tenantId), "managed-objects.json");

    public ManagedObjectMappings LoadMappings(string tenantId)
    {
        var m = ReadJson<ManagedObjectMappings>(MappingsFile(tenantId)) ?? new ManagedObjectMappings { TenantId = tenantId.ToLowerInvariant() };
        if (string.IsNullOrEmpty(m.TenantId)) m.TenantId = tenantId.ToLowerInvariant();
        AssertTenant(tenantId, m.TenantId, "Managed object mapping");
        return m;
    }

    public void SaveMappings(ManagedObjectMappings mappings)
    {
        mappings.UpdatedAt = Timestamps.Format(DateTimeOffset.UtcNow);
        WriteJsonAtomic(MappingsFile(mappings.TenantId), mappings);
    }

    private string DeviationsFile(string tenantId) => Path.Combine(TenantDirectory(tenantId), "deviations.json");

    public IReadOnlyList<Deviation> LoadDeviations(string tenantId)
    {
        var list = ReadJson<List<Deviation>>(DeviationsFile(tenantId)) ?? new List<Deviation>();
        foreach (var d in list) AssertTenant(tenantId, d.TenantId, $"Deviation {d.Id}");
        return list;
    }

    public void SaveDeviations(string tenantId, IReadOnlyList<Deviation> deviations)
    {
        foreach (var d in deviations) AssertTenant(tenantId, d.TenantId, $"Deviation {d.Id}");
        WriteJsonAtomic(DeviationsFile(tenantId), deviations);
    }

    private string ChecksFile(string tenantId) => Path.Combine(TenantDirectory(tenantId), "manual-checks.json");

    public ManualCheckRegister LoadManualChecks(string tenantId)
    {
        var r = ReadJson<ManualCheckRegister>(ChecksFile(tenantId)) ?? new ManualCheckRegister { TenantId = tenantId.ToLowerInvariant() };
        if (string.IsNullOrEmpty(r.TenantId)) r.TenantId = tenantId.ToLowerInvariant();
        AssertTenant(tenantId, r.TenantId, "Manual check register");
        return r;
    }

    public void SaveManualChecks(ManualCheckRegister register) => WriteJsonAtomic(ChecksFile(register.TenantId), register);

    // ---- assessments -------------------------------------------------------------------------------------------

    private string AssessmentsDirectory(string tenantId) => Path.Combine(TenantDirectory(tenantId), "assessments");

    public string SaveAssessment(AssessmentResult result)
    {
        var file = Path.Combine(AssessmentsDirectory(result.TenantId), $"{StampFor(result.AssessedAt)}-{SafeId(result.Id)}.json");
        WriteJsonAtomic(file, result);
        return file;
    }
}
