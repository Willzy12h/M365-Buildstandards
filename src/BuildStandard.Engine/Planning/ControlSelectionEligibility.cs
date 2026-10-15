using BuildStandard.Core.Models;
namespace BuildStandard.Engine.Planning;
public sealed record SelectionEligibility(bool Eligible, string Status, string Explanation);
/// <summary>Selection guidance only. Planning, approval and write admission remain independent.</summary>
public static class ControlSelectionEligibility
{
    public static SelectionEligibility For(ControlDefinition control, CollectionDefinition? collection, ControlFinding? finding) =>
        throw new NotSupportedException("Shared selection guidance not implemented.");
}
