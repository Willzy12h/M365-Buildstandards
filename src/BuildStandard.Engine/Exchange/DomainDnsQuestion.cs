using BuildStandard.Core;

namespace BuildStandard.Engine.Exchange;

public enum DomainDnsType { TXT, MX, CNAME }
public sealed record DomainDnsReference(string Original, string Canonical);
public sealed record DomainDnsQuestion
{
    public string Name { get; }
    public DomainDnsType Type { get; }
    public DomainDnsQuestion(string name, DomainDnsType type)
    {
        if (!Enum.IsDefined(type)) throw new ConfigurationException("Only TXT, MX and CNAME readiness questions are supported.");
        Name=DomainDnsName.Reference(name).Canonical; Type=type;
    }
}
public static class DomainDnsName
{
    public static DomainDnsReference Reference(string value)
    {
        // SPF utility-label references are not mail domains; never loosen the older MailDomain rules.
        if (string.IsNullOrEmpty(value)) throw Bad();
        var canonical=value.EndsWith(".",StringComparison.Ordinal) ? value[..^1] : value;
        if (canonical.Length is < 3 or > 253 || !canonical.Contains('.') || System.Net.IPAddress.TryParse(canonical,out _)) throw Bad();
        foreach (var label in canonical.Split('.'))
            if (label.Length is < 1 or > 63 || label[0]=='-' || label[^1]=='-'
                || label.Any(c=>!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) throw Bad();
        return new(value,canonical.ToLowerInvariant());
    }
    private static ConfigurationException Bad() => new("Use a literal ASCII DNS name within DNS length limits, without a URL, IP address, wildcard, whitespace or SPF macro.");
}
