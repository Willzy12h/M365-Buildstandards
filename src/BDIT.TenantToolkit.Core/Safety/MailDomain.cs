using System.Net;

namespace BDIT.TenantToolkit.Core.Safety;

public static class MailDomain
{
    public static string Validate(string? input)
    {
        var domain = (input ?? "").Trim().ToLowerInvariant();
        if (domain.Length is < 3 or > 253 || !domain.Contains('.') || IPAddress.TryParse(domain, out _)
            || domain.Split('.').Any(p => p.Length is < 1 or > 63 || p[0] == '-' || p[^1] == '-'
                || p.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new ConfigurationException("Enter the client's mail domain, for example example.invalid: a full DNS domain without a URL, wildcard, IP address or trailing dot.");
        return domain;
    }

    public static string ValidateQuery(string name)
    {
        var plain = name.StartsWith("_dmarc.", StringComparison.Ordinal) ? name[7..]
            : name.StartsWith("selector1._domainkey.", StringComparison.Ordinal) || name.StartsWith("selector2._domainkey.", StringComparison.Ordinal) ? name[21..] : name;
        Validate(plain);
        if (name.Length > 253) throw new ConfigurationException("The DNS query name is too long.");
        return name;
    }
}
