namespace BDIT.TenantToolkit.Core.Models;

/// <summary>Presentation grouping; older catalogues keep their original bytes and digests.</summary>
public static class ControlAreas
{
    public static IReadOnlyList<string> Names { get; } = new[] { "Entra", "Intune", "Exchange", "Purview" };
    public static IReadOnlyList<string> Filters { get; } = new[] { "All areas" }.Concat(Names).ToArray();

    public static string For(ControlDefinition control)
    {
        if (control.Area is { } area && Names.Contains(area)) return area;
        var id = control.Id;
        if (id.StartsWith("EX-", StringComparison.Ordinal)) return "Exchange";
        if (id.StartsWith("PUR-", StringComparison.Ordinal)) return "Purview";
        return id.StartsWith("CA-", StringComparison.Ordinal) || id.StartsWith("ID-", StringComparison.Ordinal)
            || id.StartsWith("PRE-", StringComparison.Ordinal) ? "Entra" : "Intune";
    }
}
