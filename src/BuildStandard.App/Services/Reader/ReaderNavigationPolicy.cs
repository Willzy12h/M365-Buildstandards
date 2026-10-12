namespace BuildStandard.App.Services.Reader;

/// <summary>Document-only admission, never an execution or external-browser authority.</summary>
public sealed class ReaderNavigationPolicy(IEnumerable<string> anchors, IEnumerable<string> references)
{
    public const string Origin = "https://reader.buildstandard.example";
    public const string DocumentUrl = Origin + "/catalogue.html";
    private readonly HashSet<string> _anchors = new(anchors, StringComparer.Ordinal);
    private readonly HashSet<string> _references = new(references, StringComparer.Ordinal);
    public bool AllowsDocument(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host != "reader.buildstandard.example" || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.AbsolutePath != "/catalogue.html") return false;
        // Reject encoded, dot-segment and otherwise normalised path disguises, not only the parsed path.
        var fragmentAt = value.IndexOf('#');
        var originalWithoutFragment = fragmentAt < 0 ? value : value[..fragmentAt];
        if (!string.Equals(originalWithoutFragment, DocumentUrl, StringComparison.Ordinal)) return false;
        if (uri.Fragment.Length == 0) return true;
        try { return _anchors.Contains(Uri.UnescapeDataString(uri.Fragment[1..])); }
        catch (UriFormatException) { return false; }
    }
    public bool AllowsResource(string uri) => string.Equals(uri, DocumentUrl, StringComparison.Ordinal);
    public bool AllowsReference(string value, bool userInitiated, bool current) => current && userInitiated
        && _references.Contains(value) && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "learn.microsoft.com" && uri.IsDefaultPort
        && uri.UserInfo.Length == 0;
}
