namespace BuildStandard.Core.Reporting;

/// <summary>
/// INT-128 exact explicitly selected Exchange targets. The original order and identities are retained; this is never
/// discovery, name resolution, an implicit estate scan or permission to act on a target.
/// </summary>
public static class ExchangeTargetSelection
{
    public const int MaximumTargets = 50;
    public const int MaximumTextLength = MaximumTargets * 37 - 1;

    public static IReadOnlyList<string> Parse(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumTextLength)
            throw Invalid();
        var targets = value.Split(',');
        if (targets.Length is < 1 or > MaximumTargets
            || targets.Any(id => !ReportValues.IsCanonicalGuid(id))
            || targets.Distinct(StringComparer.Ordinal).Count() != targets.Length)
            throw Invalid();
        return Array.AsReadOnly(targets);
    }

    private static ConfigurationException Invalid() => new(
        "Choose 1–50 unique exact Exchange GUIDs separated by commas, without names, addresses or an implicit estate scan.");
}
