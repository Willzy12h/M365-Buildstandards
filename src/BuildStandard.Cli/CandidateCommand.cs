using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Standards.Authoring;
namespace BuildStandard.Cli;

/// <summary>Offline authoring consumer. No tenant profile, authentication, Graph or execution service.</summary>
internal static class CandidateCommand
{
    internal static int Run(IReadOnlyDictionary<string, string> options)
    {
        var action = Required("action");
        var allowed = action switch
        {
            "create" => new[] { "action", "root", "catalogue", "title" },
            "import" => ["action", "root", "input", "control", "source", "name", "map"],
            "validate" => ["action", "root", "input"],
            "export" => ["action", "root", "input", "reviewed-digest"],
            _ => throw new ConfigurationException("Candidate supports create, import, validate or export; it never installs or publishes a standard.")
        };
        if (options.Any(p => !allowed.Contains(p.Key, StringComparer.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(p.Value)))
            throw new ConfigurationException("Candidate options must be known, unique and have values for the selected action.");
        var paths = ToolkitPaths.Resolve(options.TryGetValue("root", out var root) ? root : null);
        var loader = new CandidateBaselineLoader(paths, NullLog.Instance);
        var authoring = new CandidateAuthoringService(loader);
        var outputs = new CandidateLocalOutputs(paths, authoring);
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (action == "create")
            {
                var draft = loader.Create(Required("catalogue"), Required("title"));
                var file = outputs.ExportDraft(draft, stop.Token);
                Console.WriteLine(ToolkitJson.Serialize(new { kind = "candidateDraftSaved", draft = file, draft.ContentDigest, limitation = "Saved draft, not a validated or published standard." }));
                return 0;
            }
            var source = CandidateDraftCodec.ReadFile(Required("input"));
            if (action == "import")
            {
                stop.Token.ThrowIfCancellationRequested();
                var mappings = options.TryGetValue("map", out var mapFile) ? CandidatePolicyImportService.ReadParameterMappings(mapFile) : null;
                var imported = new CandidatePolicyImportService(loader, authoring).ImportFile(source,
                    Required("control"), Required("source"), Required("name"), mappings);
                stop.Token.ThrowIfCancellationRequested();
                var file = outputs.ExportDraft(imported, stop.Token);
                Console.WriteLine(ToolkitJson.Serialize(new { kind = "localCandidateImport", draft = file,
                    draftDigest = imported.ContentDigest, status = imported.Unresolved.Count == 0 ? "ReadyForValidation" : "Unresolved",
                    imported.Unresolved, imported.AssignmentObservations, validationRequired = true,
                    limitation = "Saved local draft only. Assignments are not applied; review and validate before export. No installation or publication." }));
                return 0;
            }
            if (action == "export" && Required("reviewed-digest") != source.ContentDigest)
                throw new ConfigurationException("The explicitly reviewed digest does not match this draft. Validate and review the current draft first.");
            stop.Token.ThrowIfCancellationRequested();
            var ticket = CandidateValidationTicket.Create(authoring, source);
            stop.Token.ThrowIfCancellationRequested();
            if (action == "validate")
                Console.WriteLine(ToolkitJson.Serialize(new { kind = "localCandidateValidation", ticket.BaselineDigest, ticket.DraftDigest, ticket.CandidateDigest,
                    limitation = "Local validation only; review changes before export. No publication or Microsoft behaviour acceptance." }));
            else Console.WriteLine(ToolkitJson.Serialize(outputs.Export(source, ticket, stop.Token)));
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            Console.Error.WriteLine("Cancelled. No completed candidate export is claimed.");
            return 130;
        }
        finally { Console.CancelKeyPress -= cancel; }
        string Required(string name) => options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ConfigurationException("Candidate requires --" + name + ".");
    }
}
