using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Standards;
using BuildStandard.Engine.Workflow;

namespace BuildStandard.Engine.Evidence;

public sealed record ClientReviewDiagnostic(string Section, string File, string Problem);
public sealed record ClientReviewSource(string Section, string RelativePath, string Sha256, long Length);

/// <summary>Immutable original-byte citations and private captured JSON; returned records are independent copies.
/// This diagnostic capture grants no deployment or reconciliation authority.</summary>
public sealed class ClientReviewEvidence
{
    private readonly IReadOnlyDictionary<string, string> _records;
    internal string Root { get; }
    public string TenantId { get; }
    public IReadOnlyList<ClientReviewSource> Sources { get; }
    public IReadOnlyList<ClientReviewDiagnostic> Diagnostics { get; }
    public bool SourceSetComplete { get; }
    internal ClientReviewEvidence(string root, string tenant, Dictionary<string, string> records,
        List<ClientReviewSource> sources, List<ClientReviewDiagnostic> diagnostics, bool complete)
    {
        Root = root; TenantId = tenant; _records = new Dictionary<string, string>(records, StringComparer.Ordinal);
        Sources = sources.AsReadOnly(); Diagnostics = diagnostics.AsReadOnly(); SourceSetComplete = complete;
    }
    internal IReadOnlyList<T> Records<T>(string section, string prefix = "") => _records.Where(p => p.Key.StartsWith(section + "/" + prefix, StringComparison.Ordinal))
        .Select(p => ToolkitJson.Deserialize<T>(p.Value)).ToList().AsReadOnly();
    internal IReadOnlyList<StandardCatalogue> Catalogues => _records.Where(p => p.Key.StartsWith("catalogue/", StringComparison.Ordinal))
        .Select(p => { var c = StandardsLoader.Parse(p.Value, Path.GetFileName(p.Key));
            c.IntegrityDigest = Sources.Single(s => s.RelativePath == p.Key).Sha256; return c; }).ToList().AsReadOnly();
    public IReadOnlyList<StoredAssessment> Assessments => _records.Where(p => p.Key.StartsWith("assessments/", StringComparison.Ordinal))
        .Select(p => new StoredAssessment(ToolkitJson.Deserialize<AssessmentResult>(p.Value), CanonicalJson.Sha256(ToolkitJson.ParseNode(p.Value))))
        .ToList().AsReadOnly();
    public IReadOnlyList<DeploymentRun> Runs => Records<DeploymentRun>("runs");
}

public sealed partial class EvidenceStore
{
    private const int ReviewRecordBytes = 8 * 1024 * 1024;
    private const long ReviewAggregateBytes = 64L * 1024 * 1024;
    private const int ReviewRecordLimit = 500;
    private static readonly string[] ReviewDirectories = ["assessments", "snapshots", "runs", "jobs", "observations",
        "dispositions", "cutovers", "recovery", "reviewed-changes", "entra-laps", "verification"];
    private static readonly string[] ReviewRegisters = ["deviations.json", "manual-checks.json"];
    private static readonly JsonSerializerOptions ReviewWorkflowOptions = new(ToolkitJson.Options)
    { RespectNullableAnnotations = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    private static readonly JsonSerializerOptions ReviewCatalogueOptions = new(ToolkitJson.Options) { RespectNullableAnnotations = true };

    /// <summary>Read only: missing evidence and failed reads are distinct, and every read is bounded/cancellable.</summary>
    public ClientReviewEvidence LoadClientReviewEvidence(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantId is not { Length: 36 } || !Guid.TryParseExact(tenantId, "D", out var id) || id == Guid.Empty)
            throw new ConfigurationException("Client review needs an exact tenant GUID.");
        tenantId = id.ToString("D");
        var tenantDirectory = TenantDirectory(tenantId);
        var records = new Dictionary<string, string>(StringComparer.Ordinal);
        var sources = new List<ClientReviewSource>();
        var diagnostics = new List<ClientReviewDiagnostic>();
        var complete = true; long total = 0;
        void Problem(string section, string file, string message, bool unread = false)
        { diagnostics.Add(new(section, file, message)); if (unread) complete = false; }
        void Read(string section, string path, string? catalogueKey = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = catalogueKey ?? Path.GetRelativePath(tenantDirectory, path).Replace('\\', '/');
            try
            {
                if (!ReviewPathExists(path, false)) return;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                var length = stream.Length;
                if (length > ReviewRecordBytes)
                { Problem(section, relative, "Record exceeds 8 MiB; it was not materialised.", true); return; }
                if (total + length > ReviewAggregateBytes)
                { Problem(section, relative, "Client history exceeds 64 MiB; collection is incomplete.", true); return; }
                var bytes = new byte[checked((int)length)];
                stream.ReadExactly(bytes); cancellationToken.ThrowIfCancellationRequested();
                if (stream.ReadByte() != -1) throw new IOException("Source changed during reading.");
                total += length;
                sources.Add(new(section, relative, CanonicalJson.Sha256Hex(bytes), length));
                string text;
                try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
                catch (DecoderFallbackException)
                { Problem(section, relative, "Record is not valid UTF-8; preserve the original."); return; }
                try
                {
                    ValidateReviewRecord(section, relative, text, tenantId);
                    records.Add(relative.Contains('/') ? relative : section + "/" + relative, text);
                }
                catch (Exception ex) when (ex is ToolkitException or JsonException or ArgumentException or InvalidOperationException)
                { Problem(section, relative, "Record cannot be used: " + ex.Message); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { Problem(section, relative, "Source could not be read safely. Check access and preserve the original evidence.", true); }
        }
        try
        {
            if (!ReviewPathExists(tenantDirectory, true))
                return new(_paths.Root, tenantId, records, sources, diagnostics, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Problem("client", ".", "Client history could not be opened safely; it is not an empty history.", true);
            return new(_paths.Root, tenantId, records, sources, diagnostics, false);
        }
        foreach (var section in ReviewDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(tenantDirectory, section);
            try
            {
                if (!ReviewPathExists(path, true)) continue;
                var files = Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => Path.GetExtension(f).Equals(".json", StringComparison.OrdinalIgnoreCase))
                    .Take(ReviewRecordLimit + 1).ToArray();
                if (files.Length > ReviewRecordLimit)
                    Problem(section, section, "More than 500 records; this section is incomplete.", true);
                foreach (var file in files.Take(ReviewRecordLimit).Order(StringComparer.Ordinal)) Read(section, file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { Problem(section, section, "History section could not be listed safely; absence cannot be inferred.", true); }
        }
        foreach (var name in ReviewRegisters) Read(Path.GetFileNameWithoutExtension(name), Path.Combine(tenantDirectory, name));
        // Never select an arbitrary record when two original files claim the same identity.
        foreach (var group in records.Where(p => ReviewDirectories.Contains(p.Key.Split('/')[0]))
            .GroupBy(p => p.Key.Split('/')[0] + "/" + ReviewRecordKind(p.Key) + "/" + ToolkitJson.ParseObject(p.Value)["id"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).ToList())
            foreach (var pair in group) { records.Remove(pair.Key); Problem(pair.Key.Split('/')[0], pair.Key, "Duplicate record identity; neither copy is used."); }
        var jobs = records.Where(p => p.Key.StartsWith("jobs/", StringComparison.Ordinal))
            .Select(p => ToolkitJson.Deserialize<TenantJob>(p.Value)).ToList();
        if (jobs.Count > 0)
        {
            const string manifestKey = "catalogue-manifest/manifest.json";
            Read("catalogue-manifest", Path.Combine(_paths.StandardsDirectory, StandardsManifest.FileName), manifestKey);
            if (!records.TryGetValue(manifestKey, out var manifestText))
                Problem("catalogue", manifestKey, "The catalogue manifest is missing or unreadable; job completion is unknown.");
            else
            {
                var manifest = ToolkitJson.Deserialize<StandardsManifest>(manifestText);
                foreach (var entry in manifest.Files.Where(p => jobs.Any(j => j.StandardDigest == p.Value)))
                {
                    var key = "catalogue/" + entry.Key;
                    Read("catalogue", Path.Combine(_paths.StandardsDirectory, entry.Key), key);
                    if (!records.ContainsKey(key)) continue;
                    if (sources.Single(s => s.RelativePath == key).Sha256 != entry.Value)
                    { records.Remove(key); Problem("catalogue", key, "Catalogue bytes do not match the captured manifest."); }
                }
                foreach (var job in jobs)
                    if (!records.Any(p => p.Key.StartsWith("catalogue/", StringComparison.Ordinal)
                        && sources.Any(s => s.RelativePath == p.Key && s.Sha256 == job.StandardDigest)
                        && ToolkitJson.Deserialize<StandardCatalogue>(p.Value).Release == job.StandardRelease))
                        Problem("catalogue", job.Id, "The exact catalogue for this job is missing or unreadable; completion is unknown.");
            }
        }
        return new(_paths.Root, tenantId, records, sources, diagnostics, complete);
    }

    /// <summary>Local export admission only; changed/additional/deleted files or unread byte ranges require a new review.</summary>
    public void RequireClientReviewCurrent(ClientReviewEvidence evidence)
    {
        if (!string.Equals(evidence.Root, _paths.Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || !evidence.SourceSetComplete)
            throw new ConfigurationException("The captured client source cannot be fully rechecked. Resolve its read limits and review again.");
        var current = LoadClientReviewEvidence(evidence.TenantId);
        if (!current.SourceSetComplete || !current.Sources.OrderBy(s => s.RelativePath, StringComparer.Ordinal)
            .SequenceEqual(evidence.Sources.OrderBy(s => s.RelativePath, StringComparer.Ordinal)))
            throw new ConfigurationException("Client evidence changed after the review. Review the current files before exporting.");
    }

    private static bool ReviewPathExists(string path, bool directory)
    {
        var full = Path.GetFullPath(path);
        var ancestors = new Stack<string>();
        for (var parent = Path.GetDirectoryName(full); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent)) ancestors.Push(parent);
        foreach (var parent in ancestors)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(parent); }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                throw new IOException("An ancestor is not a regular directory.");
        }
        FileAttributes own;
        try { own = File.GetAttributes(full); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        if ((own & FileAttributes.ReparsePoint) != 0 || ((own & FileAttributes.Directory) != 0) != directory)
            throw new IOException("Source is not a regular file or directory.");
        return true;
    }

    private static void ValidateReviewRecord(string section, string relative, string text, string tenant)
    {
        using (var json = JsonDocument.Parse(text, ToolkitJson.DocumentOptions)) RequireUnambiguousMembers(json.RootElement);
        if (section == "deviations")
        {
            var array = ToolkitJson.ParseNode(text) as JsonArray ?? throw new ConfigurationException("Expected a deviation array.");
            if (array.Count > ReviewRecordLimit) throw new ConfigurationException("More than 500 deviations; this section is incomplete.");
            var values = ToolkitJson.Deserialize<List<Deviation>>(text);
            foreach (var d in values)
            {
                if (d is null) throw new ConfigurationException("A deviation is null.");
                ReviewIdentity(d.Id, d.TenantId, tenant, d.Id + ".json");
                if (string.IsNullOrWhiteSpace(d.ControlId)) throw new ConfigurationException("A deviation has no control identity.");
            }
            if (values.Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
                throw new ConfigurationException("Duplicate deviation identity.");
            return;
        }
        // Force object construction to reject duplicate JSON members rather than accepting a last-value winner.
        var node = ToolkitJson.ParseObject(text); _ = node.ToJsonString();
        if (section == "manual-checks")
        {
            if (node["checks"] is not JsonObject checks || checks.Count > ReviewRecordLimit)
                throw new ConfigurationException("Manual checks are missing or exceed 500 records.");
            var register = ToolkitJson.Deserialize<ManualCheckRegister>(text);
            if (!string.Equals(register.TenantId, tenant, StringComparison.OrdinalIgnoreCase)
                || register.Checks.Any(p => p.Value is null || !string.Equals(p.Key, p.Value.ControlId, StringComparison.OrdinalIgnoreCase)))
                throw new ConfigurationException("Manual checks do not match their client or control identity.");
            return;
        }
        if (section == "catalogue-manifest")
        {
            if (node["files"] is not JsonObject files || files.Count > ReviewRecordLimit)
                throw new ConfigurationException("Catalogue manifest is missing its files or exceeds 500 entries.");
            var manifest = ToolkitJson.Deserialize<StandardsManifest>(text);
            if (!string.Equals(manifest.Algorithm, "SHA-256", StringComparison.OrdinalIgnoreCase)
                || manifest.Files.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count
                || manifest.Files.Any(p => !System.Text.RegularExpressions.Regex.IsMatch(p.Key, @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,60}\.json\z")
                    || p.Value is not { Length: 64 } || !p.Value.All(Uri.IsHexDigit)))
                throw new ConfigurationException("Catalogue manifest contains an invalid algorithm, file name or digest.");
            return;
        }
        if (section == "catalogue")
        {
            // Validate declared non-nullable members before the existing catalogue rules dereference them.
            var catalogue = JsonSerializer.Deserialize<StandardCatalogue>(text, ReviewCatalogueOptions)!;
            if (catalogue.Controls.Any(c => c is null) || catalogue.Collections.Values.Any(c => c is null)
                || catalogue.Parameters.Any(p => p is null))
                throw new ConfigurationException("Catalogue collections, controls and parameters cannot contain null records.");
            StandardsLoader.Parse(text, Path.GetFileName(relative)); return;
        }
        ReviewIdentity(node["id"]?.GetValue<string>(), node["tenantId"]?.GetValue<string>(), tenant, relative);
        if (section == "assessments")
        {
            var value = ToolkitJson.Deserialize<AssessmentResult>(text);
            ReviewIdentity(value.Id, value.TenantId, tenant, relative);
            if (!Timestamps.TryParse(value.AssessedAt, out _) || value.Findings is null || value.Findings.Any(f => f is null)
                || value.Limitations is null || value.Limitations.Any(l => l is null)
                || value.CollectionStatus is null || value.CollectionStatus.Any(c => c.Value is null))
                throw new ConfigurationException("Assessment timestamp or findings are invalid.");
        }
        else if (section == "runs")
        {
            var value = ToolkitJson.Deserialize<DeploymentRun>(text);
            ReviewIdentity(value.Id, value.TenantId, tenant, relative);
            if (!Timestamps.TryParse(value.StartedAt, out _) || value.Results is null || value.Results.Any(r => r is null))
                throw new ConfigurationException("Run timestamp or results are invalid.");
            if (!EvidenceIntegrity.Verify(value, value.IntegrityDigest)) throw new IntegrityException("Run digest does not match.");
        }
        else if (section == "snapshots")
        {
            var value = ToolkitJson.Deserialize<TenantSnapshot>(text);
            if (!Timestamps.TryParse(value.CapturedAt, out _) || value.Collections is null
                || value.Collections.Any(p => p.Value is null || p.Value.Items is null || p.Value.Items.Any(i => i is null)))
                throw new ConfigurationException("Capture timestamp or collections are invalid.");
            if (!EvidenceIntegrity.Verify(value, value.IntegrityDigest)) throw new IntegrityException("Capture digest does not match.");
        }
        else if (section is "jobs" or "observations" or "dispositions" or "cutovers")
        {
            if (Path.GetFileNameWithoutExtension(relative) != node["id"]!.GetValue<string>() || node["schemaVersion"]?.GetValue<int>() != 1)
                throw new ConfigurationException("Workflow file identity or schema is unsupported.");
            if (node["evidence"] is not JsonArray evidence || evidence.Any(e => e is not JsonObject))
                throw new ConfigurationException("Workflow evidence references are invalid.");
            switch (section)
            {
                case "jobs":
                    if (node["observationIds"] is not JsonArray) throw new ConfigurationException("Job attachments are missing.");
                    CheckWorkflow<TenantJob>(WorkflowRecordRules.ValidateJob); break;
                case "observations":
                    if (node["observedObjectIds"] is not JsonArray) throw new ConfigurationException("Observed objects are missing.");
                    CheckWorkflow<TenantObservation>(WorkflowRecordRules.ValidateObservation); break;
                case "dispositions":
                    if (node["observedObjectIds"] is not JsonArray) throw new ConfigurationException("Observed objects are missing.");
                    CheckWorkflow<TenantDisposition>(WorkflowRecordRules.ValidateDisposition); break;
                case "cutovers": CheckWorkflow<CutoverRevision>(WorkflowRecordRules.ValidateCutover); break;
            }
        }
        else
        {
            // These histories have different digest conventions from deployment captures/runs.
            var kind = ReviewRecordKind(relative);
            object value = (section, kind) switch
            {
                ("recovery", "run") => ToolkitJson.Deserialize<RecoveryRun>(text),
                ("recovery", "plan") => ToolkitJson.Deserialize<RecoveryPlan>(text),
                ("reviewed-changes", "run") => ToolkitJson.Deserialize<ReviewedChangeRun>(text),
                ("reviewed-changes", "plan") => ToolkitJson.Deserialize<ReviewedChangePlan>(text),
                ("reviewed-changes", "verification") => ToolkitJson.Deserialize<ReviewedChangeVerification>(text),
                ("entra-laps", "run") => ToolkitJson.Deserialize<EntraLapsRun>(text),
                ("entra-laps", "plan") => ToolkitJson.Deserialize<EntraLapsPlan>(text),
                ("entra-laps", "verification") => ToolkitJson.Deserialize<EntraLapsVerification>(text),
                ("verification", _) => ToolkitJson.Deserialize<WriteVerification>(text),
                _ => throw new ConfigurationException("Unrecognised history record kind.")
            };
            if (node["integrityDigest"]?.GetValue<string>() != RecoveryDigest(value))
                throw new IntegrityException("History digest does not match.");
            if (kind == "run" && !Timestamps.TryParse(node["startedAt"]?.GetValue<string>(), out _))
                throw new ConfigurationException("History timestamp is invalid.");
        }
        void CheckWorkflow<T>(Action<T> validate)
        {
            var value = JsonSerializer.Deserialize<T>(text, ReviewWorkflowOptions) ?? throw new ConfigurationException("Empty workflow record.");
            if (node["integrityDigest"]?.GetValue<string>() != RecoveryDigest(value)) throw new IntegrityException("Workflow digest does not match.");
            validate(value);
        }
    }

    private static void RequireUnambiguousMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ConfigurationException("Duplicate or case-aliased JSON member; its meaning is ambiguous.");
                RequireUnambiguousMembers(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RequireUnambiguousMembers(child);
    }

    private static string ReviewRecordKind(string relative)
    {
        var name = Path.GetFileName(relative);
        return name.StartsWith("run-", StringComparison.Ordinal) ? "run" : name.StartsWith("plan-", StringComparison.Ordinal) ? "plan"
            : name.StartsWith("verification-", StringComparison.Ordinal) ? "verification" : "record";
    }

    private static void ReviewIdentity(string? id, string? actualTenant, string tenant, string relative)
    {
        if (id is not { Length: 36 } || !Guid.TryParseExact(id, "D", out var parsed) || parsed == Guid.Empty
            || !string.Equals(actualTenant, tenant, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("Record identity or client binding is invalid.");
        var name = Path.GetFileNameWithoutExtension(relative);
        if (!name.EndsWith("-" + id, StringComparison.OrdinalIgnoreCase) && !string.Equals(name, id, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("Record identity does not match its file name.");
    }
}
