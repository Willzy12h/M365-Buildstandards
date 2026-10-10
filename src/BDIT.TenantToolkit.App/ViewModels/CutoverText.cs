using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.App.ViewModels;

/// <summary>
/// The plain-text forms the Jobs page uses for a cutover's lists, so an engineer can type them and read them back.
/// Parsing refuses rather than guesses: an unrecognised criterion result is an error, not "not run".
/// </summary>
public static class CutoverText
{
    private static readonly char[] Separators = { '\n', '\r', ',', ';', ' ', '\t' };

    public static List<string> Ids(string text) =>
        text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static string Ids(IEnumerable<string> ids) => string.Join(Environment.NewLine, ids);

    private static IEnumerable<string> Lines(string text) =>
        text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>One prerequisite per line; "[x]" at the start marks it met, "[ ]" or nothing leaves it unmet.</summary>
    public static List<CutoverPrerequisite> Prerequisites(string text) => Lines(text).Select(line =>
    {
        var met = line.StartsWith("[x]", StringComparison.OrdinalIgnoreCase);
        var description = met || line.StartsWith("[ ]", StringComparison.Ordinal) ? line[3..].Trim() : line;
        return new CutoverPrerequisite { Description = description, Met = met };
    }).ToList();

    public static string Prerequisites(IEnumerable<CutoverPrerequisite> items) =>
        string.Join(Environment.NewLine, items.Select(p => (p.Met ? "[x] " : "[ ] ") + p.Description));

    /// <summary>One criterion per line: "result | description | what was observed", the result Passed, Failed or NotRun.</summary>
    public static List<CutoverCriterion> Criteria(string text) => Lines(text).Select(line =>
    {
        var parts = line.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Length > 3)
            throw new ConfigurationException($"Write each criterion as \"result | description | what was observed\": {line}");
        var result = CriterionResult.All.FirstOrDefault(r => string.Equals(r, parts[0].Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            ?? throw new ConfigurationException($"A criterion result is Passed, Failed or NotRun, not \"{parts[0]}\".");
        return new CutoverCriterion { Result = result, Description = parts[1], Detail = parts.Length == 3 ? parts[2] : "" };
    }).ToList();

    public static string Criteria(IEnumerable<CutoverCriterion> items) =>
        string.Join(Environment.NewLine, items.Select(c => $"{c.Result} | {c.Description} | {c.Detail}"));

    /// <summary>Null when neither who approved nor where it is recorded is given.</summary>
    public static CutoverApproval? Approval(string by, string reference) =>
        by.Trim().Length == 0 && reference.Trim().Length == 0 ? null : new CutoverApproval { ApprovedBy = by.Trim(), Reference = reference.Trim() };

    /// <summary>Null when no reason is given: an escalation without a reason is no escalation.</summary>
    public static CutoverEscalation? Escalation(string reason, string owner, string path) =>
        reason.Trim().Length == 0 ? null : new CutoverEscalation { Reason = reason.Trim(), Owner = owner.Trim(), Path = path.Trim() };
}
