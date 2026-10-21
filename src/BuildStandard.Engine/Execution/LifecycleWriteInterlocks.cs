using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
namespace BuildStandard.Engine.Execution;

/// <summary>Shared local history checks before preparation, approval and intent. Never run after publishing the owned intent.</summary>
internal static class LifecycleWriteInterlocks
{
    internal static void Require(EvidenceStore evidence,string tenant,StandardCatalogue standard)
    {
        evidence.AssertNoUnresolvedLifecycle(tenant);
        var controls=standard.Controls.Select(c=>c.Id).Concat(evidence.RequireIntactRuns(tenant).SelectMany(run=>run.Results.Select(row=>row.ControlId)))
            .Concat(evidence.LoadRecoveryRuns(tenant).Select(run=>run.ControlId)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        evidence.AssertCompletionWritesResolved(tenant,controls);
        if(evidence.LoadPackageRuns(tenant).Any(run=>run.Steps.Any(step=>WriteAcceptance.IsUncertain(step.Acceptance))))throw new SafetyViolationException("An earlier package write has unknown acceptance. Reconcile before lifecycle actions.");
    }
}
