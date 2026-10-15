using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;
namespace BuildStandard.Engine.Planning;
public sealed record SelectionEligibility(bool Eligible, string Status, string Explanation);
/// <summary>Selection guidance only. Planning, approval and write admission remain independent.</summary>
public static class ControlSelectionEligibility
{
    public static SelectionEligibility For(ControlDefinition control, CollectionDefinition? collection, ControlFinding? finding)
    {
        if (!control.HasRecipe || collection is null || !collection.Writable)
            return new(false, "Manual", control.Assessment.ManualInstructions.Length > 0 ? control.Assessment.ManualInstructions : "No automated recipe; follow the build standard manually.");
        if (finding is null) return new(false, "Assess first", "Read the tenant configuration and review the assessment before planning.");
        if (finding.Status == FindingStatus.Missing || finding.Owned && finding.Status is FindingStatus.SettingsMatchNotEnforced or FindingStatus.PartialMatch or FindingStatus.Compliant)
            return new(true, "Candidate", finding.Status != FindingStatus.Missing ? "Toolkit-created object; an inactive update or no change will be proposed."
                : control.Category == "PRE" || control.Id.StartsWith("PRE-", StringComparison.OrdinalIgnoreCase)
                    ? "Tool creates this prerequisite after plan approval. Engineer verifies membership, scope and recovery access separately."
                    : control.Collection == "conditionalAccess" ? "Create a disabled candidate with exclusions; no activation." : "Create an unassigned candidate; no assignment.");
        return new(false, StatusLabels.For(finding.Status), finding.Reason + (finding.Status == FindingStatus.UnableToAssess
            ? " Next: resolve collection errors in Configuration, re-capture and assess."
            : finding.Owned ? " Next: review the finding and supporting evidence in Assessment."
            : " Next: review this existing object in Assessment. The tool does not adopt or change it by name."));
    }
}
