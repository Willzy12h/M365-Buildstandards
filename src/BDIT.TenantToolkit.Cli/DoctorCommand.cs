using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Engine.Diagnostics;

namespace BDIT.TenantToolkit.Cli;

/// <summary>Owns option refusal and error sanitisation before the general CLI's raw exception catch.</summary>
public static class DoctorCommand
{
    /// <param name="args">Only arguments following doctor; Program must route before its general option parser.</param>
    /// <param name="defaultRoot">The host's resolved portable root, when --root is absent. It is never printed.</param>
    public static int Run(string[] args, string? defaultRoot, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default)
    {
        try { return Run(args, defaultRoot, output, error, cancellationToken, new OfflinePrerequisiteService()); }
        catch (Exception) { return Unexpected(error); }
    }

    internal static int Run(string[] args, string? defaultRoot, TextWriter output, TextWriter error,
        CancellationToken cancellationToken, OfflinePrerequisiteService service)
    {
        try
        {
            var root = defaultRoot;
            var format = "text";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index++)
            {
                var option = args[index];
                if (option is not ("--root" or "--format") || !seen.Add(option) || ++index >= args.Length
                    || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
                    return Refuse("Use doctor [--root <folder>] [--format text|json]; options must be known, unique and have a value.");
                if (option == "--root") root = args[index];
                else format = args[index];
            }
            if (format is not ("text" or "json")) return Refuse("Doctor supports text or json output.");
            if (root is null)
            {
                try { root = ToolkitPaths.Resolve().Root; }
                catch (ToolkitException) { return Refuse("Choose a portable root with --root, or start from the extracted portable application."); }
            }
            var result = service.Check(root, cancellationToken);
            if (format == "json") output.WriteLine(ToolkitJson.Serialize(result));
            else WriteText(result, output);
            return result.Status switch
            {
                OfflinePrerequisiteStatus.Ready => 0,
                OfflinePrerequisiteStatus.NeedsAttention => 1,
                OfflinePrerequisiteStatus.Cancelled => 130,
                _ => 3
            };
        }
        catch (OfflineDiagnosticInputException) { return Refuse("Choose an existing, ordinary portable root folder without links or reparse points."); }
        catch (Exception)
        {
            return Unexpected(error);
        }

        int Refuse(string message) { error.WriteLine("Refused: " + message); return 2; }
    }

    private static int Unexpected(TextWriter error)
    {
        try { error.WriteLine("Doctor failed unexpectedly. No readiness was certified. Ask the toolkit maintainer to review this offline failure. If a probe started, review temporary .bdit-doctor- files in the portable folder."); }
        catch (Exception) { /* An unusable output stream must not send its private error to the general CLI catch. */ }
        return 3;
    }

    public static void WriteText(OfflinePrerequisiteResult result, TextWriter output)
    {
        output.WriteLine("Offline prerequisites: " + result.Status + " (toolkit " + result.ToolkitVersion + ")");
        output.WriteLine("Local observations only; tenant readiness, write approval and script execution are not certified.");
        foreach (var finding in result.Findings)
        {
            output.WriteLine($"[{finding.Disposition}] {finding.Id} ({finding.Applicability}): {finding.Explanation}");
            output.WriteLine("  Next: " + finding.NextStep);
        }
    }
}
