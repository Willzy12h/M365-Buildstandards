using BuildStandard.Core;
namespace BuildStandard.Graph.Reports;

/// <summary>Fixed INT-129 location policy. Signed paths/queries are request memory only, never evidence or diagnostics.</summary>
internal static class UsageDownloadAdmission
{
    internal const string Host = "reports.office.com";
    internal static Uri Require(Uri? location)
    {
        if (location is null || !location.IsAbsoluteUri || location.Scheme != Uri.UriSchemeHttps
            || !string.Equals(location.IdnHost, Host, StringComparison.OrdinalIgnoreCase) || location.HostNameType != UriHostNameType.Dns
            || location.Port != 443 || location.UserInfo.Length != 0 || location.Fragment.Length != 0)
            throw Refuse();
        var original = location.OriginalString;
        if (original.Length > 8192 || original.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\')) throw Refuse();
        // Inspect original path bytes: Uri normalises literal and encoded dot segments before AbsolutePath is exposed.
        var authorityEnd = original.IndexOf('/', original.IndexOf("://", StringComparison.Ordinal) + 3);
        if (authorityEnd < 0) throw Refuse();
        var queryStart = original.IndexOf('?', authorityEnd);
        var path = queryStart < 0 ? original[authorityEnd..] : original[authorityEnd..queryStart];
        const string prefix = "/data/download/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.Length == prefix.Length) throw Refuse();
        foreach (var segment in path[prefix.Length..].Split('/'))
        {
            string decoded;
            try { decoded = Uri.UnescapeDataString(segment); }
            catch (UriFormatException) { throw Refuse(); }
            if (decoded.Length == 0 || decoded is "." or ".."
                || decoded.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '/' or '\\' or '%' or '?' or '#')) throw Refuse();
        }
        if (!location.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) throw Refuse();
        return location;
    }
    private static ConfigurationException Refuse() => new("The usage response did not provide an admissible HTTPS download location. No download was requested; no signed URL was retained.");
}
