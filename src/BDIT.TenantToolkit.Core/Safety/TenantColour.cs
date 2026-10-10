using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BDIT.TenantToolkit.Core.Safety;

/// <summary>A tenant's identifying colour band: the band colour and the text drawn on it, as #RRGGBB.</summary>
public sealed record TenantColour(string Name, string Background, string Foreground);

/// <summary>
/// The colour band that marks which tenant a page or a copied script is for. It is a recognition cue that sits beside the
/// tenant's name and ID, never instead of them.
///
/// How a client's colour is chosen is not decided yet (see SCRIPT-LIBRARY-2026.10.08, "Pending decision: client
/// colours"). Until it is, the colour is derived from the tenant ID alone: the same tenant always gets the same colour on
/// every engineer's machine, nothing is stored, and the client profile schema is unchanged. This is the only place the
/// colour is chosen, so a colour set on the profile can replace <see cref="For"/> later without touching any page.
/// </summary>
public static class TenantColours
{
    /// <summary>Shown when no client is selected: the page's own panel colour, so nothing suggests a tenant.</summary>
    public static readonly TenantColour Neutral = new("Neutral", "#F7F9FC", "#193247");

    /// <summary>
    /// A small fixed palette of dark colours carrying white text. Every pair is at least 4.5:1 (WCAG 2.2 AA for normal
    /// text), and every band is at least 3:1 against the white and pale grey pages it sits on, so the band itself is
    /// visible. Red and amber are left out on purpose: they already mean danger and warning in this tool.
    /// </summary>
    public static IReadOnlyList<TenantColour> Palette { get; } = new[]
    {
        new TenantColour("Blue", "#1F5FA8", "#FFFFFF"),
        new TenantColour("Teal", "#0B6E6E", "#FFFFFF"),
        new TenantColour("Green", "#2D6A2F", "#FFFFFF"),
        new TenantColour("Purple", "#6B3FA0", "#FFFFFF"),
        new TenantColour("Plum", "#9A2A6A", "#FFFFFF"),
        new TenantColour("Brown", "#8A4A0B", "#FFFFFF"),
        new TenantColour("Indigo", "#3949AB", "#FFFFFF"),
        new TenantColour("Slate", "#44546A", "#FFFFFF")
    };

    /// <summary>
    /// The colour for a tenant ID. The ID is read as a GUID, so letter case, braces and surrounding spaces do not change
    /// the colour; anything that is not a GUID gets <see cref="Neutral"/>. SHA-256 spreads similar IDs across the palette.
    /// </summary>
    public static TenantColour For(string? tenantId)
    {
        if (!Guid.TryParse(tenantId?.Trim(), out var tenant) || tenant == Guid.Empty) return Neutral;
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(tenant.ToString("D")));
        var index = BinaryPrimitives.ReadUInt32BigEndian(digest) % (uint)Palette.Count;
        return Palette[(int)index];
    }

    /// <summary>The WCAG 2.2 contrast ratio between two #RRGGBB colours, from 1 to 21.</summary>
    public static double Contrast(string first, string second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string colour)
    {
        if (colour.Length != 7 || colour[0] != '#' || !int.TryParse(colour.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new ArgumentException("Use a colour written as #RRGGBB.", nameof(colour));
        static double Channel(int value)
        {
            var s = value / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel((rgb >> 16) & 0xFF) + 0.7152 * Channel((rgb >> 8) & 0xFF) + 0.0722 * Channel(rgb & 0xFF);
    }
}
