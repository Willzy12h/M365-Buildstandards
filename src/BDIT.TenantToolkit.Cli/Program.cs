using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Engine.Standards;
using BDIT.TenantToolkit.Engine.Workflow;

namespace BDIT.TenantToolkit.Cli;

/// <summary>
/// A headless consumer of the engine, and the proof that the engine is one.
///
/// The desktop application was the only consumer, so the claim that business logic is free of interface concerns was
/// asserted rather than demonstrated. This runner exercises standards loading, assessment and reporting with no WPF,
/// no window and no interaction. If it ever needs an engine change to do its job, that is a seam defect worth fixing
/// in the engine rather than working around here.
///
/// It is read-only by construction rather than by policy. It never authenticates, so there is no token, no Graph
/// client and no write path reachable from this assembly. It reads evidence the application already captured, through
/// the same <see cref="EvidenceStore"/> the application reads it with, so a headless report cannot disagree with the
/// application's for the same snapshot. Conformance tests assert the absence of every write-capable type and of every
/// evidence mutator, so neither guarantee can be lost by accident.
/// </summary>
public static class Program
{
    /// <summary>Types that must never appear in this assembly. Kept here so the intent is readable beside the code.</summary>
    public static readonly string[] ForbiddenTypes =
    {
        "DeploymentExecutor", "ReviewedChangeService", "ApplicationPackageService",
        "RecoveryService", "EntraLapsService", "GraphClient", "TenantConnectionService", "JobWorkflow"
    };

    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (ToolkitException ex)
        {
            // An expected refusal: a missing file, a tenant mismatch, an unreadable standard. The message is written
            // for the operator, so it is printed without a stack trace.
            Console.Error.WriteLine("Refused: " + ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Failed: " + ex.GetType().Name + ": " + ex.Message);
            return 3;
        }
    }

    private static int Run(string[] args)
    {
        var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
        var options = ParseOptions(args.Skip(1));

        return command switch
        {
            "releases" => Releases(options),
            "report" => Report(options),
            "document" => Document(options),
            "standard" => StandardDefinition(options),
            "verify-restore" => VerifyRestore(options),
            "upgrade-impact" => UpgradeImpact(options),
            "jobs" => Jobs(options),
            "job" => Job(options),
            "help" or "--help" or "-h" => Help(),
            _ => Unknown(command)
        };
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        Help();
        return 64;
    }

    private static int Help()
    {
        Console.WriteLine("""
            bdit — headless reporting over captured tenant evidence. Never connects to a tenant.

              bdit releases
                  List the Build Standard releases available to this installation.

              bdit report --snapshot <file> [--exchange-snapshot <file>] [--release <r>] [--format <f>] [--root <dir>]
                  Assess a captured snapshot against a standard and write the engineer report.
                  Requires this installation's client record for the snapshot's tenant, because the
                  client inputs, ownership records and accepted deviations change the result.
                  Formats: html, markdown, json, csv, xlsx. Default html.
                  Optional Exchange evidence uses the existing raw capture or exported snapshot format.
                  It must belong to the same tenant; missing or invalid supplied evidence is refused.

              bdit document --client "<name>" [--release <r>] [--format <f>] [--root <dir>]
                  Write the client-facing build standard document.
                  Formats: html, markdown. Default html.

              bdit standard [--release <r>] [--format <f>] [--root <dir>]
                  Export all standard defaults/settings, with no client data.
                  No format: full ZIP set with exact catalogue JSON/manifest, HTML and Markdown.
                  Individual formats: html, markdown, json. JSON retains original placeholders.

              bdit upgrade-impact --snapshot <file> --from <r> --to <r> [--format <f>] [--root <dir>]
                  Assess one stored capture under two releases and show how each requirement changed,
                  traced through the shipped release lineage. Describes the standard, not the tenant.
                  Needs this installation's client record for the snapshot's tenant.
                  Formats: html, markdown, json, csv, xlsx. Default html.

              bdit jobs --tenant <id> [--snapshot <file>] [--release <r>] [--root <dir>]
                  List the tenant's jobs with their completion claim. Read-only: never records anything.

              bdit job --tenant <id> --job <id> [--snapshot <file>] [--release <r>] [--root <dir>]
                  Show one job: each requirement's state and reasons, decisions, cutover cases and any
                  unattached or unreadable records. A complete job grants no authority to change a tenant.
                  --snapshot names the current capture the recorded objects are checked against;
                  without it that check is skipped and the output says so.

              bdit verify-restore --folder <dir>
                  Re-check a separately restored evidence folder against its recorded checksums.
                  Read-only: refuses changed, missing or unlisted files and writes nothing.

            Common options:
              --root <dir>     Toolkit root holding standards/, config/ and reports/.
                               Defaults to BDIT_TOOLKIT_ROOT or the installation directory.
              --release <r>    Standard release, for example 2026.09.11. Defaults to the configured release.

            Exit codes: 0 success, 2 refused, 3 failed, 64 usage.
            """);
        return 0;
    }

    // ---- commands -------------------------------------------------------------------------------------------------

    private static int Releases(IReadOnlyDictionary<string, string> options)
    {
        var context = Context.Open(options);
        foreach (var release in context.Loader.ListReleases())
            Console.WriteLine($"{release.Release,-14} {release.FileName,-22} {release.Status}");
        return 0;
    }

    private static int Report(IReadOnlyDictionary<string, string> options)
    {
        var file = Require(options, "snapshot");
        if (!File.Exists(file)) throw new ConfigurationException($"Snapshot file not found: {file}");

        var context = Context.Open(options);
        var standard = context.Standard();
        // Refuses a snapshot changed after capture, as the supplemental Exchange evidence below is refused.
        var snapshot = AssessmentContext.ReadPrimary(file);
        if (!ProfileValidator.IsGuid(snapshot.TenantId))
            throw new ConfigurationException("The snapshot does not name a tenant, so it cannot be assessed.");

        // Assessment reads three stored records for the snapshot's tenant, and each one changes its result: the
        // profile carries the client inputs that resolve a standard's parameters, the mappings say which objects this
        // toolkit created, and the deviations say what an engineer has already accepted. Substituting a default for
        // any of them produces a report that disagrees with the application's for the same snapshot, which is worse
        // than no report at all, so a missing client record is refused rather than filled in.
        var profile = context.Profile(snapshot.TenantId)
            ?? throw new ConfigurationException(
                $"No client record for tenant {snapshot.TenantId} was found under {context.Paths.DataDirectory}. "
                + "Assessment reads that client's inputs, ownership records and accepted deviations, so a report "
                + "written without them would not match the application's. Run this on the installation that captured "
                + "the snapshot, or add the client in the application first.");
        ExchangeCapture? supplemental = null;
        if (options.TryGetValue("exchange-snapshot", out var exchangeFile))
        {
            if (string.IsNullOrWhiteSpace(exchangeFile) || !File.Exists(exchangeFile))
                throw new ConfigurationException("Supplemental Exchange evidence file not found.");
            supplemental = AssessmentContext.ReadSupplement(exchangeFile, profile.TenantId, DateTimeOffset.UtcNow);
        }
        var result = AssessmentContext.Assess(new AssessmentEngine(SystemClock.Instance, ToolkitVersion.Current),
            context.Evidence, snapshot, standard, profile, "bdit (headless)", supplemental,
            // A headless report always reads stored evidence, so freshness is judged as of that evidence.
            Timestamps.TryParse(snapshot.CapturedAt, out var capturedAt) ? capturedAt : null);

        var format = Format(options, ExportFormat.Html);
        var written = context.Exporter.ExportAssessment(result, format);

        var s = result.Summary;
        Console.WriteLine($"{result.TenantName} · standard {result.Release} · snapshot {(result.SnapshotComplete ? "complete" : "INCOMPLETE")} · integrity {MarkdownReports.IntegrityText(result.SnapshotIntegrity)}");
        Console.WriteLine($"Compliant {s.Compliant} · match not enforced {s.SettingsMatchNotEnforced} · partial {s.PartialMatch} · missing {s.Missing} · manual {s.RequiresManualReview} · unable {s.UnableToAssess}");
        Console.WriteLine("Report: " + written);
        return 0;
    }

    private static int Document(IReadOnlyDictionary<string, string> options)
    {
        var context = Context.Open(options);
        var standard = context.Standard();
        var client = Require(options, "client");
        var format = Format(options, ExportFormat.Html);
        if (format is not (ExportFormat.Html or ExportFormat.Markdown))
            throw new ConfigurationException("The build standard document is written as html or markdown.");

        Console.WriteLine("Document: " + context.Exporter.ExportBuildStandard(standard, client, DateTimeOffset.UtcNow, format));
        return 0;
    }

    private static int StandardDefinition(IReadOnlyDictionary<string, string> options)
    {
        var context = Context.Open(options);
        var exporter = new StandardDefinitionExporter(context.Paths);
        var standard = context.Standard();
        var now = DateTimeOffset.UtcNow;
        var file = options.ContainsKey("format") ? exporter.Export(standard, Format(options, ExportFormat.Html), now) : exporter.ExportSet(standard, now);
        Console.WriteLine("Standard definition: " + file);
        return 0;
    }

    private static int UpgradeImpact(IReadOnlyDictionary<string, string> options)
    {
        var file = Require(options, "snapshot");
        if (!File.Exists(file)) throw new ConfigurationException($"Snapshot file not found: {file}");
        var context = Context.Open(options);
        var source = context.Release(Require(options, "from"));
        var target = context.Release(Require(options, "to"));
        var snapshot = AssessmentContext.ReadPrimary(file);
        var profile = (ProfileValidator.IsGuid(snapshot.TenantId) ? context.Profile(snapshot.TenantId) : null)
            ?? throw new ConfigurationException($"No client record for the snapshot's tenant was found under {context.Paths.DataDirectory}. The comparison uses that client's inputs, ownership records and deviations.");
        var lineage = ReleaseLineage.Load(context.Paths.StandardsDirectory, StandardsManifest.Load(context.Paths.StandardsDirectory), target.Release);
        var report = UpgradeImpactAnalyser.Analyse(new AssessmentEngine(SystemClock.Instance, ToolkitVersion.Current), snapshot, source, target, lineage,
            profile, context.Evidence.LoadMappings(profile.TenantId), context.Evidence.LoadDeviations(profile.TenantId), DateTimeOffset.UtcNow);
        var written = context.Exporter.ExportUpgradeImpact(report, Format(options, ExportFormat.Html));
        Console.WriteLine($"{report.TenantName} · {report.SourceRelease} → {report.TargetRelease} · lineage {(report.LineageRecorded ? "recorded" : "NOT recorded")}");
        Console.WriteLine($"Status changes {report.StatusChanges} · " + string.Join(" · ", UpgradeImpactChange.Order.Select(c => $"{c.ToLowerInvariant()} {report.Count(c)}")));
        Console.WriteLine("Report: " + written);
        return 0;
    }

    private static int Jobs(IReadOnlyDictionary<string, string> options)
    {
        var (context, standard, profile, capture) = WorkflowContext(options);
        var (jobs, unreadable) = context.Evidence.LoadJobs(profile.TenantId);
        Console.WriteLine($"{profile.Company} · standard {standard.Release} · {jobs.Count} job(s)" + CaptureNote(capture));
        foreach (var job in jobs.OrderBy(j => j.CreatedAt, StringComparer.Ordinal))
        {
            var projection = JobProjection.Build(context.Evidence, profile.TenantId, job.Id, standard, profile, DateTimeOffset.UtcNow, capture);
            var completion = JobCompletion.Build(context.Evidence, projection, standard, profile, DateTimeOffset.UtcNow);
            Console.WriteLine($"{job.Id}  {job.CreatedAt}  {JobCompletion.Words(job.Intention)} · owner {job.Owner} · {completion.Claim}");
        }
        foreach (var u in unreadable) Console.WriteLine($"UNREADABLE {u.File}: {u.Problem}");
        return 0;
    }

    private static int Job(IReadOnlyDictionary<string, string> options)
    {
        var (context, standard, profile, capture) = WorkflowContext(options);
        var jobId = Require(options, "job");
        var now = DateTimeOffset.UtcNow;
        var projection = JobProjection.Build(context.Evidence, profile.TenantId, jobId, standard, profile, now, capture);
        var completion = JobCompletion.Build(context.Evidence, projection, standard, profile, now);
        var job = projection.Job;

        Console.WriteLine($"{profile.Company} · job {job.Id} · {JobCompletion.Words(job.Intention)} · owner {job.Owner} · opened {job.CreatedAt} by {job.Actor}");
        Console.WriteLine($"Standard {job.StandardRelease} (now {standard.Release})" + CaptureNote(capture));
        Console.WriteLine(completion.Claim);
        foreach (var blocker in completion.Blockers) Console.WriteLine("  JOB: " + blocker);
        Console.WriteLine();
        foreach (var r in completion.Requirements)
        {
            var detail = string.Join(" · ", new[] { r.Outcome.Length > 0 ? "outcome " + r.Outcome : "", r.Decision.Length > 0 ? "decision " + JobCompletion.Words(r.Decision) : "",
                r.Cutover.Length > 0 ? "cutover " + r.Cutover : "" }.Where(t => t.Length > 0));
            Console.WriteLine($"{r.InstanceKey,-22} {JobCompletion.Words(r.State),-20} {detail}");
            foreach (var reason in r.Reasons) Console.WriteLine("    " + reason);
        }
        foreach (var o in projection.Unattached) Console.WriteLine($"UNATTACHED observation {o.Id} for {o.InstanceKey} (not counted)");
        foreach (var d in projection.UnattachedDispositions) Console.WriteLine($"UNATTACHED decision {d.Id} for {d.InstanceKey} (not counted)");
        foreach (var c in projection.UnattachedCutovers) Console.WriteLine($"UNATTACHED cutover revision {c.Id} for {c.InstanceKey} (not counted)");
        if (projection.LegacyAttestations.Count > 0)
            Console.WriteLine($"{projection.LegacyAttestations.Count} legacy manual check(s) are unbound attestations and count for nothing here.");
        return 0;
    }

    /// <summary>The installation, standard, stored client record and optional current capture a job is projected against.</summary>
    private static (Context Context, StandardCatalogue Standard, TenantProfile Profile, TenantSnapshot? Capture) WorkflowContext(IReadOnlyDictionary<string, string> options)
    {
        var tenant = Require(options, "tenant");
        if (!ProfileValidator.IsGuid(tenant)) throw new ConfigurationException("--tenant is the tenant ID, a GUID.");
        var context = Context.Open(options);
        var standard = context.Standard();
        var profile = context.Profile(tenant)
            ?? throw new ConfigurationException($"No client record for tenant {tenant} was found under {context.Paths.DataDirectory}. Jobs are projected against that client's inputs.");
        TenantSnapshot? capture = null;
        if (options.TryGetValue("snapshot", out var file))
        {
            if (!File.Exists(file)) throw new ConfigurationException($"Snapshot file not found: {file}");
            capture = AssessmentContext.ReadPrimary(file);
            if (!string.Equals(capture.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase))
                throw new TenantMismatchException("The snapshot belongs to another tenant.");
        }
        return (context, standard, profile, capture);
    }

    private static string CaptureNote(TenantSnapshot? capture) => capture is null
        ? " · no current capture given, so recorded objects were not checked against one"
        : $" · checked against capture {capture.Id} ({capture.CapturedAt})";

    private static int VerifyRestore(IReadOnlyDictionary<string, string> options)
    {
        var folder = Require(options, "folder");
        if (!Directory.Exists(folder)) throw new ConfigurationException($"Restored folder not found: {folder}");
        Console.WriteLine($"Restored folder verified: {WorkspaceBackup.VerifyRestored(folder)} evidence file(s) match their recorded SHA-256.");
        return 0;
    }

    // ---- shared context -------------------------------------------------------------------------------------------

    /// <summary>Everything a command needs from the installation, resolved once and failing early with a clear reason.</summary>
    private sealed class Context
    {
        private readonly IReadOnlyDictionary<string, string> _options;
        private Context(ToolkitPaths paths, ToolkitSettings settings, IReadOnlyDictionary<string, string> options)
        {
            Paths = paths;
            Settings = settings;
            _options = options;
            Loader = new StandardsLoader(paths, NullLog.Instance);
            Exporter = new ReportExporter(paths, settings.CompanyName);
            Evidence = new EvidenceStore(paths, NullLog.Instance);
        }

        public ToolkitPaths Paths { get; }
        public ToolkitSettings Settings { get; }
        public StandardsLoader Loader { get; }
        public ReportExporter Exporter { get; }

        /// <summary>
        /// The application's own evidence reader. Only its read methods are called from here, and a conformance test
        /// holds that line. Using it rather than a local copy is the point: its tenant checks, its handling of a
        /// missing or unreadable file and its error messages are then the same ones the application applies.
        /// </summary>
        public EvidenceStore Evidence { get; }

        public static Context Open(IReadOnlyDictionary<string, string> options)
        {
            var paths = ToolkitPaths.Resolve(options.TryGetValue("root", out var root) ? root : null);
            var settings = File.Exists(paths.SettingsFile) ? ToolkitSettings.Load(paths.SettingsFile) : new ToolkitSettings();
            return new Context(paths, settings, options);
        }

        public StandardCatalogue Standard()
        {
            var releases = Loader.ListReleases();
            if (releases.Count == 0) throw new ConfigurationException("No Build Standard releases were found under " + Paths.StandardsDirectory);

            var wanted = _options.TryGetValue("release", out var r) && r.Length > 0 ? r : Settings.DefaultStandardRelease;
            var match = releases.FirstOrDefault(x => string.Equals(x.Release, wanted, StringComparison.OrdinalIgnoreCase))
                        ?? releases.FirstOrDefault(x => string.Equals(x.FileName, wanted, StringComparison.OrdinalIgnoreCase));
            if (match is null && !string.IsNullOrWhiteSpace(wanted))
                throw new ConfigurationException($"Release '{wanted}' was not found. Available: {string.Join(", ", releases.Select(x => x.Release))}");

            return Loader.Load((match ?? releases[0]).FileName);
        }

        /// <summary>A named, manifest-verified release.</summary>
        public StandardCatalogue Release(string release)
        {
            var releases = Loader.ListReleases();
            var match = releases.FirstOrDefault(x => string.Equals(x.Release, release, StringComparison.OrdinalIgnoreCase))
                ?? throw new ConfigurationException($"Release '{release}' was not found. Available: {string.Join(", ", releases.Select(x => x.Release))}");
            return Loader.Load(match.FileName);
        }

        /// <summary>The stored client record for this tenant, or null. Never a record invented to keep going.</summary>
        public TenantProfile? Profile(string tenantId) =>
            Evidence.LoadProfiles().FirstOrDefault(p => string.Equals(p.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
    }

    // ---- argument handling ----------------------------------------------------------------------------------------

    private static IReadOnlyDictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? pending = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (pending is not null) options[pending] = "";
                pending = arg[2..];
                continue;
            }
            if (pending is null) throw new ConfigurationException($"Unexpected value '{arg}'. Options are given as --name value.");
            options[pending] = arg;
            pending = null;
        }
        if (pending is not null) options[pending] = "";
        return options;
    }

    private static string Require(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && value.Length > 0
            ? value
            : throw new ConfigurationException($"--{name} is required.");

    private static ExportFormat Format(IReadOnlyDictionary<string, string> options, ExportFormat fallback)
    {
        if (!options.TryGetValue("format", out var text) || text.Length == 0) return fallback;
        return Enum.TryParse<ExportFormat>(text, ignoreCase: true, out var format)
            ? format
            : throw new ConfigurationException($"Unknown format '{text}'. Use one of: {string.Join(", ", Enum.GetNames<ExportFormat>())}.");
    }
}
