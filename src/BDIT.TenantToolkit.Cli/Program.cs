using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Engine.Standards;

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
/// client and no write path reachable from this assembly. It reads evidence the application already captured. A
/// conformance test asserts the absence of every write-capable type, so the guarantee cannot be lost by accident.
/// </summary>
public static class Program
{
    /// <summary>Types that must never appear in this assembly. Kept here so the intent is readable beside the code.</summary>
    public static readonly string[] ForbiddenTypes =
    {
        "DeploymentExecutor", "ReviewedChangeService", "ApplicationPackageService",
        "RecoveryService", "EntraLapsService", "GraphClient", "TenantConnectionService"
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

              bdit report --snapshot <file> [--release <r>] [--format <f>] [--root <dir>]
                  Assess a captured snapshot against a standard and write the engineer report.
                  Formats: html, markdown, json, csv, xlsx. Default html.

              bdit document --client "<name>" [--release <r>] [--format <f>] [--root <dir>]
                  Write the client-facing build standard document.
                  Formats: html, markdown. Default html.

            Common options:
              --root <dir>     Toolkit root holding standards/, config/ and reports/.
                               Defaults to BDIT_TOOLKIT_ROOT or the installation directory.
              --release <r>    Standard release, for example 2026.09.10. Defaults to the configured release.

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
        var snapshot = ToolkitJson.Deserialize<TenantSnapshot>(File.ReadAllText(file))
            ?? throw new ConfigurationException("The snapshot file did not contain a capture.");
        if (!ProfileValidator.IsGuid(snapshot.TenantId))
            throw new ConfigurationException("The snapshot does not name a tenant, so it cannot be assessed.");

        // Assessment binds snapshot, profile and mappings to one tenant. A stored profile is used when the
        // installation has one; otherwise a minimal profile carries the tenant identity and nothing else. Client
        // inputs are irrelevant here because nothing is planned or written.
        var profile = context.Profile(snapshot.TenantId) ?? new TenantProfile
        {
            Id = snapshot.TenantId, TenantId = snapshot.TenantId,
            Company = snapshot.TenantName.Length > 0 ? snapshot.TenantName : snapshot.TenantId,
            Domain = snapshot.PrimaryDomain
        };
        var mappings = context.Mappings(snapshot.TenantId);

        var result = new AssessmentEngine(SystemClock.Instance, ToolkitVersion.Current)
            .Assess(snapshot, standard, profile, mappings, Array.Empty<Deviation>(), "bdit (headless)");

        var format = Format(options, ExportFormat.Html);
        var written = context.Exporter.ExportAssessment(result, format);

        var s = result.Summary;
        Console.WriteLine($"{result.TenantName} · standard {result.Release} · snapshot {(result.SnapshotComplete ? "complete" : "INCOMPLETE")}");
        Console.WriteLine($"Compliant {s.Compliant} · match not enforced {s.SettingsMatchNotEnforced} · partial {s.PartialMatch} · missing {s.Missing} · manual {s.RequiresManualReview} · unable {s.UnableToAssess}");
        Console.WriteLine("Report: " + written);
        return 0;
    }

    private static int Document(IReadOnlyDictionary<string, string> options)
    {
        var context = Context.Open(options);
        var standard = context.Standard();
        var client = Require(options, "client");
        var format = Format(options, ExportFormat.ClientHtml);
        if (format is not (ExportFormat.ClientHtml or ExportFormat.Html or ExportFormat.Markdown))
            throw new ConfigurationException("The build standard document is written as html or markdown.");

        Console.WriteLine("Document: " + context.Exporter.ExportBuildStandard(standard, client, DateTimeOffset.UtcNow, format));
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
        }

        public ToolkitPaths Paths { get; }
        public ToolkitSettings Settings { get; }
        public StandardsLoader Loader { get; }
        public ReportExporter Exporter { get; }

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

        public TenantProfile? Profile(string tenantId)
        {
            if (!File.Exists(Paths.ProfilesFile)) return null;
            var profiles = ToolkitJson.Deserialize<List<TenantProfile>>(File.ReadAllText(Paths.ProfilesFile)) ?? new();
            return profiles.FirstOrDefault(p => string.Equals(p.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Ownership records if this installation holds them; an empty set otherwise, never a guess.</summary>
        public ManagedObjectMappings Mappings(string tenantId)
        {
            var file = Path.Combine(Paths.TenantDirectory(tenantId), "managed-objects.json");
            if (File.Exists(file))
            {
                var stored = ToolkitJson.Deserialize<ManagedObjectMappings>(File.ReadAllText(file));
                if (stored is not null && string.Equals(stored.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)) return stored;
            }
            return new ManagedObjectMappings { TenantId = tenantId };
        }
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
