using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;

namespace BDIT.TenantToolkit.Engine.Exchange;

public static class ExchangeEvidenceImporter
{
    public static TenantSnapshot Import(string json, TenantProfile profile, StandardCatalogue standard, string enteredDomain, DateTimeOffset now)
    {
        var domain = MailDomain.Validate(enteredDomain);
        var capture = ExchangeCaptureSchema.Parse(json, profile.TenantId, now);
        if (capture.Domain != domain) throw new ConfigurationException("The imported domain differs from the explicitly entered mail domain.");
        return Snapshot(capture, profile, standard);
    }

    public static TenantSnapshot Snapshot(ExchangeCapture capture, TenantProfile profile, StandardCatalogue standard) => new()
    {
        Id = Guid.NewGuid().ToString(), TenantId = profile.TenantId, TenantName = profile.Company, ClientLabel = profile.Company,
        PrimaryDomain = capture.Domain, CapturedAt = capture.CapturedAt, CapturedBy = "Engineer-imported observations (source not independently authenticated)",
        IdentitySource = "Imported delegated Exchange/Purview capture; offline review only", SessionMode = "Offline",
        StandardRelease = standard.Release, Complete = false, ExchangeCapture = capture
    };

    public static async Task<TenantSnapshot> CheckDnsAsync(TenantSnapshot snapshot, TenantProfile profile, StandardCatalogue standard, IDnsLookup dns, DateTimeOffset now, CancellationToken ct)
    {
        var capture = Core.Json.ToolkitJson.Deserialize<ExchangeCapture>(Core.Json.ToolkitJson.Serialize(snapshot.ExchangeCapture
            ?? throw new ConfigurationException("Import an Exchange capture before checking DNS.")));
        ExchangeCaptureSchema.Validate(capture, profile.TenantId, now);
        if (!ExchangeCaptureSchema.Complete(capture, "acceptedDomains", out var domains)
            || !domains.Any(d => string.Equals(d["DomainName"]?.GetValue<string>(), capture.Domain, StringComparison.OrdinalIgnoreCase)))
            throw new ConfigurationException("The capture must establish this exact accepted mail domain before a DNS check.");
        capture.Dns.Clear();
        foreach (var (name, kind) in new[] { ("selector1._domainkey." + capture.Domain, DnsRecordKind.Cname),
                     ("selector2._domainkey." + capture.Domain, DnsRecordKind.Cname), ("_dmarc." + capture.Domain, DnsRecordKind.Txt) })
        {
            ct.ThrowIfCancellationRequested();
            var answer = await dns.QueryAsync(name, kind, ct).ConfigureAwait(false);
            if (answer.Name != name || answer.Kind != kind) throw new ConfigurationException("The DNS resolver returned a different question.");
            capture.Dns.Add(answer);
        }
        ExchangeCaptureSchema.Validate(capture, profile.TenantId, now);
        return Snapshot(capture, profile, standard);
    }
}
