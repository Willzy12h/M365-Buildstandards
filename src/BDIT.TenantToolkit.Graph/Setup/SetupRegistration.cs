using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Graph.Setup;

public static class SetupRegistration
{
    public const string HomePage = "https://github.com/Willzy12h/M365-Buildstandards";
    public const string ConsentRedirect = "http://localhost:8400/m365-consent/";
    public static string BrokerRedirect(string clientId) => ProfileValidator.IsGuid(clientId)
        ? "ms-appx-web://microsoft.aad.brokerplugin/" + clientId.ToLowerInvariant()
        : throw new ConfigurationException("A valid client ID is required for Windows sign-in.");
    public static JsonObject PublicClient(string? clientId = null) => new()
    {
        ["redirectUris"] = string.IsNullOrEmpty(clientId) ? new JsonArray("http://localhost") : new JsonArray("http://localhost", BrokerRedirect(clientId))
    };
    public static JsonObject Web() => new() { ["homePageUrl"] = HomePage, ["redirectUris"] = new JsonArray(ConsentRedirect) };
}
