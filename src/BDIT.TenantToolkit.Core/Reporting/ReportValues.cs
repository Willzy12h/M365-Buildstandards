using System.Globalization;

namespace BDIT.TenantToolkit.Core.Reporting;

/// <summary>
/// The single identity and required-text rules shared by report collection and the strict report reader, so a value
/// the collector keeps as exact is always one the reader accepts, and anything else becomes an explicit partial result.
/// </summary>
public static class ReportValues
{
    /// <summary>Canonical lower-case D-format GUID text, never the zero GUID, with no surrounding or embedded whitespace.</summary>
    public static bool TryGuid(string? text, out string canonical)
    {
        canonical = "";
        if (text is not { Length: 36 } || text.Any(char.IsWhiteSpace)
            || !Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty) return false;
        canonical = id.ToString("D", CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>Accepts only GUID text already in the canonical form <see cref="TryGuid"/> produces.</summary>
    public static bool IsCanonicalGuid(string? text) => TryGuid(text, out var canonical) && string.Equals(text, canonical, StringComparison.Ordinal);

    /// <summary>A returned non-GUID identity: not empty, not blank and without surrounding whitespace.</summary>
    public static bool IsTextIdentity(string? text) => !string.IsNullOrWhiteSpace(text) && !char.IsWhiteSpace(text[0]) && !char.IsWhiteSpace(text[^1]);

    /// <summary>Required reported text: present and not empty. Blank text is kept as reported.</summary>
    public static bool HasText(string? text) => !string.IsNullOrEmpty(text);
}
