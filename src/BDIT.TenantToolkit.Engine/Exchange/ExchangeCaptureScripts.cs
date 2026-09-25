using System.Text.Json;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Safety;

namespace BDIT.TenantToolkit.Engine.Exchange;

public static class ExchangeCaptureScripts
{
    public static string ReadOnlyCapture(string tenantId, string domain)
    {
        if (!Guid.TryParse(tenantId, out var tenant)) throw new ConfigurationException("Select a client with a valid tenant ID before exporting.");
        domain = MailDomain.Validate(domain);
        using var stream = typeof(ExchangeCaptureScripts).Assembly.GetManifestResourceStream("Exchange.ReadCapture.ps1")
            ?? throw new ConfigurationException("The read-only Exchange capture template is missing.");
        using var reader = new StreamReader(stream);
        var definitions = JsonSerializer.Serialize(ExchangeCaptureSchema.Definitions, ToolkitJson.Options);
        return reader.ReadToEnd().Replace("__TENANT__", tenant.ToString(), StringComparison.Ordinal)
            .Replace("__DOMAIN__", domain, StringComparison.Ordinal).Replace("__SOURCE__", ExchangeCaptureSchema.Source, StringComparison.Ordinal)
            .Replace("__DEFINITIONS__", definitions, StringComparison.Ordinal);
    }
}
