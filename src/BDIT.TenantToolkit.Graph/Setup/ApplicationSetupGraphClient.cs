using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Graph.Auth;

namespace BDIT.TenantToolkit.Graph.Setup;

/// <summary>Separate setup transport. It cannot write grants, credentials, roles, policies or existing objects.</summary>
internal sealed class ApplicationSetupGraphClient(HttpClient http, IAccessTokenProvider tokens, IToolkitLog log)
{
    private const string Root = "https://graph.microsoft.com/v1.0";
    private const string GuidPart = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";

    public Task<JsonObject> GetAsync(string path, CancellationToken ct) => SendAsync(HttpMethod.Get, path, null, ct);

    public async Task<List<JsonObject>> GetAllAsync(string path, CancellationToken ct)
    {
        var items = new List<JsonObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var initialBase = path.Split('?')[0];
        while (true)
        {
            if (!seen.Add(path) || seen.Count > 100) throw new ConfigurationException("Setup discovery pagination did not complete.");
            var page = await GetAsync(path, ct);
            if (page["value"] is not JsonArray values || values.Any(v => v is not JsonObject))
                throw new ConfigurationException("Setup discovery returned an incomplete collection.");
            items.AddRange(values.Cast<JsonObject>().Select(v => (JsonObject)v.DeepClone()));
            if (items.Count > 10000) throw new ConfigurationException("Setup discovery exceeded the collection limit.");
            var next = page["@odata.nextLink"]?.GetValue<string>();
            if (string.IsNullOrEmpty(next)) return items;
            if (!Uri.TryCreate(next, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "graph.microsoft.com"
                || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
                || uri.AbsolutePath != "/v1.0" + initialBase)
                throw new ConfigurationException("Setup discovery returned an unexpected pagination target.");
            path = uri.PathAndQuery["/v1.0".Length..];
        }
    }

    public Task<JsonObject> CreateAsync(string path, JsonObject payload, CancellationToken ct)
    {
        if (path == "/applications")
        {
            var allowed = new[] { "displayName", "signInAudience", "publicClient", "requiredResourceAccess" };
            if (payload.Any(p => !allowed.Contains(p.Key)) || payload["signInAudience"]?.GetValue<string>() != "AzureADMyOrg"
                || string.IsNullOrWhiteSpace(payload["displayName"]?.GetValue<string>())
                || payload["publicClient"] is not JsonObject pc || pc.Count != 1
                || pc["redirectUris"] is not JsonArray redirects || redirects.Count != 1 || redirects[0]?.GetValue<string>() != "http://localhost"
                || payload["requiredResourceAccess"] is not JsonArray resources || resources.Count != 1
                || resources[0] is not JsonObject resource || resource.Count != 2
                || resource["resourceAppId"]?.GetValue<string>() != ApplicationSetupService.GraphApplicationId
                || resource["resourceAccess"] is not JsonArray access || access.Count == 0
                || access.Any(a => a is not JsonObject o || o.Count != 2 || o["type"]?.GetValue<string>() != "Scope" || !ProfileValidator.IsGuid(o["id"]?.GetValue<string>())))
                throw new WriteDeniedException("Application setup payload must be a single-tenant public client with reviewed delegated Graph permissions only.");
        }
        else if (path == "/servicePrincipals")
        {
            if (payload.Count != 2 || !ProfileValidator.IsGuid(payload["appId"]?.GetValue<string>()) || payload["appRoleAssignmentRequired"]?.GetValue<bool>() != true)
                throw new WriteDeniedException("Enterprise application creation requires a client ID and explicit user assignment.");
        }
        else throw new WriteDeniedException("This setup connection can only create reviewed applications and service principals.");
        return SendAsync(HttpMethod.Post, path, payload, ct);
    }

    private async Task<JsonObject> SendAsync(HttpMethod method, string path, JsonObject? payload, CancellationToken ct)
    {
        GraphRouteAllowList.ValidatePathSyntax(path);
        var basePath = path.Split('?')[0];
        if (!Regex.IsMatch(basePath, "^/(organization|me|applications(?:/" + GuidPart + ")?|servicePrincipals(?:/" + GuidPart + "(?:/appRoleAssignments|/appRoleAssignedTo)?)?|oauth2PermissionGrants)$", RegexOptions.CultureInvariant))
            throw new WriteDeniedException("Graph route is outside application setup.");
        var read = method == HttpMethod.Get;
        var forceRefresh = false;
        while (true)
        {
            var token = await tokens.GetAccessTokenAsync(forceRefresh, ct);
            using var request = new HttpRequestMessage(method, Root + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (payload is not null) request.Content = new StringContent(payload.ToJsonString(ToolkitJson.Compact), Encoding.UTF8, "application/json");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(100));
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token); }
            catch (Exception ex) when (!read && ex is HttpRequestException or OperationCanceledException)
            {
                throw new AmbiguousWriteException("Application setup write outcome is unknown. Do not repeat creation; inspect the saved journal and reconcile by object/client ID first.", ex);
            }
            using (response)
            {
                var status = (int)response.StatusCode;
                log.Info("Application setup", $"{method} {basePath} -> {status}");
                if (read && status == 401 && !forceRefresh) { forceRefresh = true; continue; }
                if (!read && (status == 408 || status >= 500)) throw new AmbiguousWriteException($"Application setup received HTTP {status}. Reconcile the outcome before creating again.", null);
                if (!response.IsSuccessStatusCode)
                    throw new GraphRequestException(status, method.Method, basePath, null, $"Application setup {method} {basePath} returned HTTP {status}. Check setup permissions and directory roles; no write was retried.");
                try
                {
                    var body = await response.Content.ReadAsStringAsync(timeout.Token);
                    return ToolkitJson.ParseNode(body) as JsonObject ?? throw new JsonException("Expected an object.");
                }
                catch (Exception ex) when (ex is JsonException or OperationCanceledException or HttpRequestException)
                {
                    if (!read) throw new AmbiguousWriteException("Application creation was accepted but its response could not be read. Reconcile before repeating.", ex);
                    throw new GraphRequestException(status, "GET", basePath, "response", "Application setup read did not return complete JSON.");
                }
            }
        }
    }
}
