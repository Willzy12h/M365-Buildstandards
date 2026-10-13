using System.Text.Json.Nodes;
using BuildStandard.Core.Models;
namespace BuildStandard.App.ViewModels;

/// <summary>Independent recorded facts, not an execution verdict or functional acceptance.</summary>
public sealed record OutcomePresentation(string Lifecycle, string Acceptance, string Readback, string Protection, string Functional)
{
    public static OutcomePresentation ForResult(RunResult result)
    {
        var protection = "Protection state not verified. Intended payload is not readback.";
        if (result.Configuration == ConfigurationVerification.Pass && result.AfterObject is { } after)
        {
            if (after["state"] is JsonValue value && value.TryGetValue<string>(out var state) && state == "disabled")
                protection = "Disabled candidate observed · not protecting users. Activation is a separate reviewed action.";
            else if (after["assignments"] is JsonArray assignments && assignments.Count == 0)
                protection = "Unassigned candidate observed · not protecting users. Assignment is a separate reviewed action.";
            else protection = "Readback recorded · protection/assignment requires its own evidence and functional checks.";
        }
        return new(result.Status, result.WriteAcceptance, result.Configuration, protection,
            "Functional acceptance remains separate. " + result.Verification);
    }
    public static string ForRun(DeploymentRun run, bool? saved)
    {
        var accepted = run.Results.Count(r => r.WriteAcceptance == WriteAcceptance.Accepted);
        var uncertain = run.Results.Count(r => r.WriteAcceptance == WriteAcceptance.Unknown);
        var rejected = run.Results.Count(r => r.WriteAcceptance == WriteAcceptance.Rejected);
        var unattempted = run.Results.Count(r => r.WriteAcceptance == WriteAcceptance.NotAttempted);
        var sent = run.Results.Count == 0 ? "No recorded write outcomes; nothing can be inferred about transport."
            : accepted > 0 || uncertain > 0 || rejected > 0
                ? $"{accepted} accepted · {uncertain} uncertain · {rejected} rejected · {unattempted} not attempted. Earlier sent/uncertain rows must not be replayed."
                : "All recorded writes were not attempted. This does not erase other historical runs.";
        var save = saved is true ? "Saved record verified locally." : saved is false ? "Saving could not be confirmed; preserve any original files." : "Save status unknown; inspect the durable record.";
        return $"Recorded lifecycle: {run.Status}. {sent}\n{save}\nConfiguration readback, candidate state and functional acceptance remain separate.";
    }
}
