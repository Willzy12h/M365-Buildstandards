using System.Text;
using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine;
using BuildStandard.Engine.Assessment;
using BuildStandard.Engine.Checks;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Identity;
using BuildStandard.Engine.Reports;
using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Standards;
using BuildStandard.Engine.Workflow;

namespace BuildStandard.Cli;

/// <summary>
/// A headless consumer of the engine, and the proof that the engine is one.
///
/// The desktop application was the only consumer, so the claim that business logic is free of interface concerns was
/// asserted rather than demonstrated. This runner exercises standards loading, assessment and reporting with no WPF,
/// no window and no interaction. If it ever needs an engine change to do its job, that is a seam defect worth fixing
/// in the engine rather than working around here.
///
/// It is read-only by construction rather than by policy. It never authenticates, so there is no token, no Graph
/// client and no tenant-write path reachable from this assembly. Its offline doctor uses the shared engine's owned
/// temporary local probes. It reads evidence the application already captured, through
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
        // Doctor owns strict options, default-root refusal and console failures before the general raw catch.
        if (args.FirstOrDefault()?.Equals("doctor", StringComparison.OrdinalIgnoreCase) == true)
            return DoctorConsoleCancellation.RunCommand(args, Console.Out, Console.Error);

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
        var options = ParseOptions(args.Skip(1), command is "report-evidence" or "customers");

        return command switch
        {
            "releases" => Releases(options),
            "customers" => Customers(options),
            "report" => Report(options),
            "report-evidence" => ReportEvidenceExport(options),
            "inventory" => Inventory(options),
            "check" => Check(options),
            "document" => Document(options),
            "standard" => StandardDefinition(options),
            "verify-restore" => VerifyRestore(options),
            "upgrade-impact" => UpgradeImpact(options),
            "jobs" => Jobs(options),
            "job" => Job(options),
            "scripts" => Scripts(options),
            "script" => Script(options),
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
            buildstandard — offline reports and local prerequisite diagnostics. Never connects to a tenant.

              buildstandard doctor [--root <folder>] [--format text|json]
                  Check local portable prerequisites; text is the default, JSON is a separate diagnostic result.
                  Checks package/catalogue/settings and uses temporary owned write/delete probes.
                  No repair, installation, sign-in or script execution. Optional limitations stay visible.
                  Ctrl+C requests cancellation and owned probe cleanup; remaining required checks are Unknown.
                  Local readiness does not certify publisher trust, tenant readiness or write approval.

              buildstandard check --snapshot <file> (--area <area> | --control <id>) [--exchange-snapshot <file>] [--release <r>] [--root <dir>]
                  Review selected controls from stored evidence, using the saved client inputs.
                  --exchange-snapshot adds separate Exchange/Purview evidence, as report does.
                  Writes a separate partial scoped-check JSON wrapper to standard output.
                  Historical filtering only: no new collection, sign-in or complete before-evidence.

              buildstandard customers --csv <file> [--root <dir>]
                  Preview a Partner Center customer CSV against saved local clients as JSON.
                  Invalid/conflicting/existing clients remain visible. No profiles are saved,
                  no tenant is contacted and relationship/tags do not prove access.
                  UTF-8; maximum 5 MiB and 5,000 data rows. Native import/save is separate.

              buildstandard releases
                  List the Build Standard releases available to this installation.

              buildstandard report --snapshot <file> [--exchange-snapshot <file>] [--release <r>] [--format <f>] [--root <dir>]
                  Assess a captured snapshot against a standard and write the engineer report.
                  Requires this installation's client record for the snapshot's tenant, because the
                  client inputs, ownership records and accepted deviations change the result.
                  Formats: html, markdown, json, csv, xlsx. Default html.
                  Optional Exchange evidence uses the existing raw capture or exported snapshot format.
                  It must belong to the same tenant; missing or invalid supplied evidence is refused.

              buildstandard report-evidence --input <file> --tenant <id> [--format <f>] [--root <dir>] [--historical]
                  Export a registered Graph report already captured by the tool; no live reads.
                  Validates its recorded tenant, schema and integrity before writing an export.
                  Formats: html, json, csv (ZIP of sections), xlsx. Default html.
                  No client record or standard is required. Failed/partial reads retain their status;
                  a successful export does not mean the original collection succeeded.

              buildstandard document --client "<name>" [--release <r>] [--format <f>] [--root <dir>]
                  Write the client-facing build standard document.
                  Formats: html, markdown. Default html.

              buildstandard inventory --snapshot <file> [--format <f>] [--root <dir>]
                  Export all captured configuration and collection states; no assessment or live reads.
                  Formats: html, json, csv, xlsx. Default html. No client record is required.
                  Uses the capture's recorded release; --release is not an override for observed data.

              buildstandard standard [--release <r>] [--format <f>] [--root <dir>]
                  Export all standard defaults/settings, with no client data.
                  No format: full ZIP set with exact catalogue JSON/manifest, HTML and Markdown.
                  Individual formats: html, markdown, json. JSON retains original placeholders.

              buildstandard upgrade-impact --snapshot <file> --from <r> --to <r> [--format <f>] [--root <dir>]
                  Assess one stored capture under two releases and show how each requirement changed,
                  traced through the shipped release lineage. Describes the standard, not the tenant.
                  Needs this installation's client record for the snapshot's tenant.
                  Formats: html, markdown, json, csv, xlsx. Default html.

              buildstandard jobs --tenant <id> [--snapshot <file>] [--release <r>] [--root <dir>]
                  List the tenant's jobs with their completion claim. Read-only: never records anything.

              buildstandard job --tenant <id> --job <id> [--snapshot <file>] [--release <r>] [--root <dir>]
                  Show one job: each requirement's state and reasons, decisions, cutover cases and any
                  unattached or unreadable records. A complete job grants no authority to change a tenant.
                  --snapshot names the current capture the recorded objects are checked against;
                  without it that check is skipped and the output says so.

              buildstandard scripts [--search "<words>"]
                  List the reviewed script library by area, or the items matching every word.

              buildstandard script --id <id> [--copy] [--tenant <id>] [--tenant-name "<name>"] [--account <upn>]
                          [--out <file>] [--<Field> <value> ...]
                  Without --copy, show the item's purpose, needs and form fields.
                  With --copy, check the fields and print the standalone Copy script, or save it with --out
                  (never over an existing file). Blank optional fields are ignored; separate several values
                  with commas. The script signs in to Exchange Online itself and refuses another tenant.
                  Nothing is run here.

              buildstandard verify-restore --folder <dir>
                  Re-check a separately restored evidence folder against its recorded checksums.
                  Read-only: refuses changed, missing or unlisted files and writes nothing.

            bdit.cmd remains a compatible alias to the same application host.

            Common options:
              --root <dir>     Toolkit root holding standards/, config/ and reports/.
                               Defaults to BUILDSTANDARD_ROOT (legacy BDIT_TOOLKIT_ROOT also supported)
                               or the installation directory. Conflicting aliases refuse.
              --release <r>    Standard release, for example 2026.09.11. Defaults to the configured release.

            Exit codes: 0 success, 2 refused, 3 failed, 64 usage.
            Doctor: 0 required local checks pass, 1 needs attention, 2 invocation refused,
                    3 sanitised unexpected failure, 130 cancelled. Unknown command remains 64.
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

    private static int Check(IReadOnlyDictionary<string, string> options)
    {
        var allowed = new[] { "snapshot", "area", "control", "exchange-snapshot", "release", "root" };
        if (options.Keys.Any(k => !allowed.Contains(k, StringComparer.OrdinalIgnoreCase)))
            throw new ConfigurationException("Check accepts --snapshot, exactly one --area or --control, and optional --exchange-snapshot/--release/--root. It never connects to a tenant.");
        if (options.ContainsKey("area") == options.ContainsKey("control"))
            throw new ConfigurationException("Choose exactly one --area or --control for this partial check.");
        var file = Require(options, "snapshot");
        if (!File.Exists(file)) throw new ConfigurationException($"Snapshot file not found: {file}");
        var source = AssessmentContext.ReadPrimary(file);
        var context = Context.Open(options);
        var catalogue = context.Standard();
        var profile = context.Profile(source.TenantId)
            ?? throw new ConfigurationException("No client record for tenant " + source.TenantId + ". Open the installation with that saved client before checking stored evidence.");
        var selection = options.ContainsKey("area")
            ? CheckSelection.ForArea(catalogue, profile, Require(options, "area"))
            : CheckSelection.ForControl(catalogue, profile, Require(options, "control"));
        // The same separate Exchange/Purview evidence, read the same way, as report uses.
        ExchangeCapture? supplemental = null;
        if (options.TryGetValue("exchange-snapshot", out var exchangeFile))
        {
            if (string.IsNullOrWhiteSpace(exchangeFile) || !File.Exists(exchangeFile))
                throw new ConfigurationException("Supplemental Exchange evidence file not found.");
            supplemental = AssessmentContext.ReadSupplement(exchangeFile, profile.TenantId, DateTimeOffset.UtcNow);
        }
        var result = new ScopedCheckService(SystemClock.Instance, ToolkitVersion.Current, NullLog.Instance, context.Paths.StandardsDirectory)
            .ReviewHistorical(source, catalogue, profile, selection, context.Evidence.LoadMappings(profile.TenantId),
                context.Evidence.LoadDeviations(profile.TenantId), "offline scoped review", supplemental);
        Console.WriteLine(ToolkitJson.Serialize(result));
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
            context.Evidence, snapshot, standard, profile, "BuildStandard CLI (headless)", supplemental,
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
                r.Cutover.Length > 0 ? "cutover " + r.Cutover : "", Cited(projection, r.InstanceKey) }.Where(t => t.Length > 0));
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

    /// <summary>"cites assessment …" when the current outcome for this instance cites a stored assessment.</summary>
    private static string Cited(JobProjection projection, string instanceKey)
    {
        var current = projection.Subjects.FirstOrDefault(s => string.Equals(s.InstanceKey, instanceKey, StringComparison.OrdinalIgnoreCase))?.Current;
        var cited = current?.Evidence.FirstOrDefault(e => e.Kind == EvidenceKind.Assessment);
        return cited is null ? "" : "cites assessment " + cited.Id;
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

    private static int Inventory(IReadOnlyDictionary<string, string> options)
    {
        var file = Require(options, "snapshot");
        if (!File.Exists(file)) throw new ConfigurationException($"Snapshot file not found: {file}");
        if (options.ContainsKey("release")) throw new ConfigurationException("Inventory uses the capture's recorded release. Omit --release; use upgrade-impact to compare standards.");
        var format = Format(options, ExportFormat.Html);
        if (format is not (ExportFormat.Html or ExportFormat.Json or ExportFormat.Csv or ExportFormat.Xlsx))
            throw new ConfigurationException("Configuration inventory exports as html, json, csv or xlsx.");
        var capture = AssessmentContext.ReadPrimary(file);
        if (!ProfileValidator.IsGuid(capture.TenantId)) throw new ConfigurationException("The capture has no valid tenant ID; configuration inventory cannot identify its source.");
        var context = Context.Open(options);
        Console.WriteLine(context.Exporter.ExportSnapshot(capture, null, format));
        Console.WriteLine($"Captured configuration · {capture.CapturedAt} · {(capture.Complete ? "Review collection status" : "INCOMPLETE — unknown reads remain explicit")} · no live reads or writes.");
        return 0;
    }

    private static int ReportEvidenceExport(IReadOnlyDictionary<string, string> options)
    {
        var allowed = new[] { "input", "tenant", "format", "root", "historical" };
        if (options.Keys.Any(k => !allowed.Contains(k, StringComparer.OrdinalIgnoreCase)))
            throw new ConfigurationException("Report evidence exports accept only --input, --tenant, --format, --root and --historical. No collection or execution is supported.");
        if (options.TryGetValue("historical", out var historicalValue) && historicalValue.Length != 0)
            throw new ConfigurationException("--historical is an explicit flag without a value, for limited original expiry revision-1 inspection only.");
        var tenant = Require(options, "tenant");
        if (!ProfileValidator.IsGuid(tenant)) throw new ConfigurationException("--tenant must name the expected tenant GUID.");
        var file = Require(options, "input");
        if (!File.Exists(file)) throw new ConfigurationException($"Report evidence file not found: {file}");
        var format = Format(options, ExportFormat.Html);
        if (format is not (ExportFormat.Html or ExportFormat.Json or ExportFormat.Csv or ExportFormat.Xlsx))
            throw new ConfigurationException("Report evidence exports as html, json, csv or xlsx.");
        var report = options.ContainsKey("historical") ? ReportEnvelopeDispatcher.ReadHistoricalFile(file, tenant) : ReportEnvelopeDispatcher.ReadFile(file, tenant);
        var context = Context.Open(options);
        if (report.AssuranceNotice.Length > 0)
            Console.WriteLine(report.AssuranceNotice + ". Historical JSON exports bundle original bytes and a warning; current owner validation is unavailable for this revision.");
        Console.WriteLine(report.Export(context.Exporter, format, suppliedFile: true));
        var state = report.Schema1?.Status ?? report.Schema2!.Status;
        var started = report.Schema1?.StartedAt ?? report.Schema2!.StartedAt;
        var ended = report.Schema1?.EndedAt ?? report.Schema2!.EndedAt;
        Console.WriteLine($"Offline export · read status {state} · recorded {started} to {ended} · no live reads or writes. Export success does not prove collection success.");
        return 0;
    }

    private static int VerifyRestore(IReadOnlyDictionary<string, string> options)
    {
        var folder = Require(options, "folder");
        if (!Directory.Exists(folder)) throw new ConfigurationException($"Restored folder not found: {folder}");
        Console.WriteLine($"Restored folder verified: {WorkspaceBackup.VerifyRestored(folder)} evidence file(s) match their recorded SHA-256.");
        return 0;
    }

    // ---- shared context -------------------------------------------------------------------------------------------

    private static int Customers(IReadOnlyDictionary<string, string> options)
    {
        foreach (var key in options.Keys)
            if (!key.Equals("csv", StringComparison.OrdinalIgnoreCase) && !key.Equals("root", StringComparison.OrdinalIgnoreCase)) throw new ConfigurationException("Unsupported customer-preview option --" + key + ". No save or connection is supported.");
        var file = Require(options, "csv");
        var context = Context.Open(options);
        var profiles = CustomerPreviewProfiles(context.Paths);
        var bytes = ReadPreviewInput(file, PartnerCustomerImport.MaximumBytes, "Customer CSV exceeds 5 MiB.");
        var preview = PartnerCustomerImport.Preview(bytes, profiles);
        Console.WriteLine(ToolkitJson.Serialize(new
        {
            Kind = "customerImportPreview", Saved = false, Root = context.Paths.Root,
            Notice = "Offline preview only. No clients saved or connected. Domain, relationship and access remain unverified.",
            preview.DataRows, preview.Rows
        }));
        return 0;
    }

    private static IReadOnlyList<TenantProfile> CustomerPreviewProfiles(ToolkitPaths paths)
    {
        // File.Exists swallows access/path errors. Do not turn blocked storage into 'no existing clients'.
        FileAttributes dataAttributes;
        try { dataAttributes = File.GetAttributes(paths.DataDirectory); }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        if ((dataAttributes & FileAttributes.Directory) == 0)
            throw new ConfigurationException("The saved-profile data path is not a directory. Resolve local storage before previewing customers.");
        FileAttributes profileAttributes;
        try { profileAttributes = File.GetAttributes(paths.ProfilesFile); }
        catch (FileNotFoundException) { return []; }
        if ((profileAttributes & FileAttributes.Directory) != 0)
            throw new ConfigurationException("The saved-profile file path is occupied by a directory. Resolve it before previewing customers.");
        var bytes = ReadPreviewInput(paths.ProfilesFile, 8 * 1024 * 1024, "Saved profiles exceed the 8 MiB preview limit; no preview was produced.");
        var json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        // Bound the object count before constructing profiles and their nested default parameters.
        using (var document = JsonDocument.Parse(json, ToolkitJson.DocumentOptions))
            if (document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() > 5000)
                throw new ConfigurationException("Saved profiles exceed the 5,000-client preview limit; no preview was produced.");
        return ToolkitJson.Deserialize<List<TenantProfile>>(json);
    }

    private static byte[] ReadPreviewInput(string file, int maximumBytes, string limitMessage)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes) throw new ConfigurationException(limitMessage);
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new ConfigurationException("The preview input changed while reading it. Open a new preview.");
        return bytes;
    }

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

    private static int Scripts(IReadOnlyDictionary<string, string> options)
    {
        var catalogue = ScriptCatalogue.Shipped;
        var entries = options.TryGetValue("search", out var search) ? catalogue.Search(search) : catalogue.Entries;
        Console.WriteLine($"{entries.Count} of {catalogue.Entries.Count} library item(s). None has been tested in a tenant yet.");
        foreach (var area in entries.GroupBy(e => e.Manifest.Area))
        {
            Console.WriteLine();
            Console.WriteLine(area.Key);
            foreach (var entry in area)
                Console.WriteLine($"  {entry.Manifest.Id,-28} {(entry.Manifest.Mode == ScriptMode.ReadOnly ? "Read  " : "Change")} {entry.Manifest.Name}"
                    + (entry.Manifest.SchemaVersion == 2 ? " (desktop experimental Run only; this CLI has no Run command or Copy form)" : ""));
        }
        return 0;
    }

    private static readonly HashSet<string> ScriptOptions = new(StringComparer.OrdinalIgnoreCase) { "id", "copy", "tenant", "tenant-name", "account", "out", "root" };

    private static int Script(IReadOnlyDictionary<string, string> options)
    {
        var entry = ScriptCatalogue.Shipped.Find(Require(options, "id"));
        var m = entry.Manifest;
        if (!options.ContainsKey("copy"))
        {
            Console.WriteLine($"{m.Name} ({m.Id}) · {(m.Mode == ScriptMode.ReadOnly ? "read only" : "change, copy only")} · {m.Area}");
            Console.WriteLine(m.Description);
            Console.WriteLine("Needs: " + m.Prerequisites);
            if (m.Roles.Count > 0) Console.WriteLine("Roles: " + string.Join(" or ", m.Roles));
            Console.WriteLine("PowerShell: " + string.Join(" or ", m.SupportedRuntimes) + " · live status: not yet tested in a tenant");
            Console.WriteLine();
            foreach (var p in m.Parameters)
            {
                var kind = p.Type switch
                {
                    ScriptParameterType.Boolean => "tick box",
                    ScriptParameterType.Enum => "one of " + string.Join(", ", p.Allowed!),
                    ScriptParameterType.Integer => $"number {p.Minimum}-{p.Maximum}",
                    ScriptParameterType.Date => "date YYYY-MM-DD",
                    _ => (p.Format ?? ScriptValueFormat.Text).ToString().ToLowerInvariant()
                };
                var many = p.Array ? $", up to {p.MaxItems} values" : "";
                var fallback = p.Default is null ? "" : $" [default {p.Default}]";
                Console.WriteLine($"  --{p.Name}{(p.Required ? " *" : "")}  {p.Label}: {kind}{many}{fallback}");
                Console.WriteLine($"      {p.Help}");
            }
            foreach (var rule in m.Rules)
                Console.WriteLine(rule.Kind == ScriptRuleKind.AtLeastOne
                    ? "  Rule: enter at least one of " + string.Join(", ", rule.Parameters!.Select(n => "--" + n))
                    : $"  Rule: --{rule.Start} to --{rule.End} covers at most {rule.MaximumDays} days" + (rule.MaximumAgeDays is { } age ? $", starting no more than {age} days ago" : ""));
            foreach (var limitation in m.Limitations) Console.WriteLine("  Limitation: " + limitation);
            return 0;
        }

        var fields = options.Where(o => !ScriptOptions.Contains(o.Key)).ToDictionary(o => o.Key, o => (string?)o.Value, StringComparer.OrdinalIgnoreCase);
        var binding = ScriptInputs.Bind(m, fields, DateTimeOffset.UtcNow);
        if (!binding.IsValid)
        {
            foreach (var problem in binding.Problems) Console.Error.WriteLine("  " + problem);
            throw new ConfigurationException($"The fields for {m.Id} are not complete; nothing was produced.");
        }
        options.TryGetValue("tenant", out var tenant);
        options.TryGetValue("tenant-name", out var tenantName);
        options.TryGetValue("account", out var account);
        var script = ScriptCopy.Generate(entry, binding, new ScriptCopyTarget(tenant, tenantName, account), DateTimeOffset.UtcNow);
        if (!options.TryGetValue("out", out var file) || file.Length == 0)
        {
            Console.Write(script);
            return 0;
        }
        // A byte order mark keeps Windows PowerShell 5.1 reading any non-ASCII value correctly; an existing file is never replaced.
        using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(true)))
            writer.Write(script);
        Console.WriteLine("Copy script: " + Path.GetFullPath(file));
        return 0;
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(IEnumerable<string> args, bool rejectDuplicates = false)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? pending = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (pending is not null) options[pending] = "";
                pending = arg[2..];
                if (rejectDuplicates && !seen.Add(pending)) throw new ConfigurationException($"Duplicate --{pending.ToLowerInvariant()} option; supply each option once.");
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
