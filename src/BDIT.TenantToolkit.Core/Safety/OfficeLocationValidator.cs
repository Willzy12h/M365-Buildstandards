using System.Net;
using System.Net.Sockets;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Core.Safety;

public static class PublicIpRange
{
    private static readonly string[] ReservedV4 = { "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8",
        "169.254.0.0/16", "172.16.0.0/12", "192.0.0.0/24", "192.0.2.0/24", "192.88.99.0/24", "192.168.0.0/16",
        "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4" };
    private static readonly string[] ReservedV6 = { "2001::/23", "2001:db8::/32", "2002::/16", "3fff::/20" };

    // Test the whole subnet, not just its first address: a broad public-looking range can contain private space.
    public static bool IsPublic(string cidr)
    {
        if (!TryParse(cidr, out var address, out var prefix) || prefix == 0) return false;
        var ipv6 = address.AddressFamily == AddressFamily.InterNetworkV6;
        if (ipv6 && (prefix < 3 || (address.GetAddressBytes()[0] & 0xe0) != 0x20)) return false;
        foreach (var reserved in ipv6 ? ReservedV6 : ReservedV4)
        {
            if (!TryParse(reserved, out var other, out var otherPrefix)) return false;
            if (SamePrefix(address.GetAddressBytes(), other.GetAddressBytes(), Math.Min(prefix, otherPrefix))) return false;
        }
        return true;
    }

    private static bool TryParse(string cidr, out IPAddress address, out int prefix)
    {
        address = IPAddress.None; prefix = -1;
        var parts = cidr.Split('/');
        if (parts.Length != 2 || parts[0].Contains('%') || parts.Any(p => p.Length == 0 || p != p.Trim())
            || !parts[1].All(char.IsAsciiDigit) || !int.TryParse(parts[1], out prefix)
            || !IPAddress.TryParse(parts[0], out var parsed)) return false;
        address = parsed;
        if (address.IsIPv4MappedToIPv6) return false;
        if (address.AddressFamily == AddressFamily.InterNetwork && parts[0] != address.ToString()) return false;
        return prefix >= 0 && prefix <= (address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32);
    }

    private static bool SamePrefix(byte[] a, byte[] b, int bits)
    {
        for (var i = 0; i < bits; i++)
            if ((a[i / 8] & (128 >> (i % 8))) != (b[i / 8] & (128 >> (i % 8)))) return false;
        return true;
    }
}

public static class OfficeLocationValidator
{
    public static List<OfficeLocation>? Validate(IReadOnlyList<OfficeLocation>? locations)
    {
        if (locations is null) return null;
        if (locations.Count > 100) throw new ConfigurationException("At most 100 office locations can be recorded.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<OfficeLocation>();
        foreach (var location in locations)
        {
            var key = location.Key.Trim().ToUpperInvariant(); var name = location.Name.Trim();
            if (key.Length is < 1 or > 24 || !key.All(char.IsAsciiLetterOrDigit) || !keys.Add(key))
                throw new ConfigurationException("Each office needs a unique stable key of 1–24 letters or digits. Keep that key when renaming an office.");
            if (name.Length is < 1 or > 120 || name.Any(char.IsControl) || !names.Add(name))
                throw new ConfigurationException("Each office needs a unique, non-empty name of at most 120 characters.");
            var ranges = location.IpRanges.Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (ranges.Count is < 1 or > 100 || ranges.Any(r => !PublicIpRange.IsPublic(r)))
                throw new ConfigurationException($"Office '{name}' needs 1–100 public CIDR ranges. Private, reserved, documentation, loopback, link-local, carrier-grade NAT, multicast and default routes are refused.");
            result.Add(new OfficeLocation { Key = key, Name = name, IpRanges = ranges });
        }
        return result;
    }

    public static List<OfficeLocation> ParseLines(string text)
    {
        var rows = new List<OfficeLocation>();
        foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('|');
            if (parts.Length != 3) throw new ConfigurationException("Enter each office as stable key | name | public CIDR ranges separated by commas.");
            rows.Add(new OfficeLocation { Key = parts[0], Name = parts[1], IpRanges = parts[2].Split(',', StringSplitOptions.TrimEntries).ToList() });
        }
        return Validate(rows)!;
    }

    public static string Render(IEnumerable<OfficeLocation>? locations) => string.Join(Environment.NewLine,
        (locations ?? Enumerable.Empty<OfficeLocation>()).Select(l => l.Key + " | " + l.Name + " | " + string.Join(", ", l.IpRanges)));
}
