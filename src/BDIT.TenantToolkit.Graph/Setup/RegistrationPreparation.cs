using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Graph.Setup;

public sealed partial class ApplicationSetupService
{
    private async Task PrepareExistingAsync(ApplicationSetupRow row, string clientId, CancellationToken ct)
    {
        if (!ProfileValidator.IsGuid(clientId) || Same(clientId, BootstrapClientId) || Same(clientId, GraphApplicationId))
            throw new ConfigurationException("Enter an explicit dedicated tool client ID. Microsoft applications cannot be configured by this wizard.");
        var filter = Uri.EscapeDataString("appId eq '" + clientId + "'");
        var apps = await _graph.GetAllAsync("/applications?$filter=" + filter + "&" + AppSelect[1..], ct).ConfigureAwait(false);
        var principals = await _graph.GetAllAsync("/servicePrincipals?$filter=" + filter + "&" + SpSelect[1..], ct).ConfigureAwait(false);
        if (apps.Count != 1 || principals.Count != 1 || !Same(Text(principals[0], "appOwnerOrganizationId"), Identity.TenantId)
            || !Same(Text(apps[0], "appId"), clientId) || !Same(Text(principals[0], "appId"), clientId))
            throw new ConfigurationException("Expected one local registration and enterprise app owned by this tenant for the explicit client ID.");
        var app = apps[0];
        if (Text(app, "signInAudience") != "AzureADMyOrg" || new[] { "passwordCredentials", "keyCredentials", "appRoles" }.Any(k => app[k] is not JsonArray { Count: 0 })
            || app["requiredResourceAccess"] is not JsonArray access
            || access.Any(entry => entry is not JsonObject a || Text(a, "resourceAppId") != GraphApplicationId
                || a["resourceAccess"] is not JsonArray scopes || scopes.Any(s => s is not JsonObject o || Text(o, "type") != "Scope")))
            throw new ConfigurationException("Existing-ID configuration is limited to dedicated single-tenant delegated Graph tools without credentials or exposed app roles. Review this application separately.");
        row.ClientId = clientId; row.ApplicationObjectId = RequireGuid(app, "id"); row.ServicePrincipalId = RequireGuid(principals[0], "id");
        if ((await _graph.GetAllAsync("/servicePrincipals/" + row.ServicePrincipalId + "/appRoleAssignments", ct).ConfigureAwait(false)).Count != 0)
            throw new ConfigurationException("This enterprise application has application permissions. Existing-ID setup supports delegated-only tools.");
        row.ExistingApplication = app; row.ExistingPrincipal = principals[0];
        row.ApplicationPayload = ApplicationPayload(row); row.Status = "Configure existing";
        row.Reason = "Explicit ID selected: review replacement name, delegated permission configuration, Windows/browser/consent redirects, GitHub URLs and custom logo. Consent grants are not silently changed.";
    }

    public async Task ValidateConsentRedirectAsync(string clientId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            AssertConnected();
            if (!ProfileValidator.IsGuid(clientId)) throw new ConfigurationException("Create or enter the tool application ID first.");
            await VerifyIdentityAsync(ct).ConfigureAwait(false);
            var apps = await _graph.GetAllAsync("/applications?$filter=" + Uri.EscapeDataString("appId eq '" + clientId + "'") + "&" + AppSelect[1..], ct).ConfigureAwait(false);
            if (apps.Count != 1 || !CanonicalJson.IsSubset(apps[0]["web"], SetupRegistration.Web()))
                throw new ConfigurationException("The tool consent callback is not registered. Enter the existing client ID above, then preview and approve setup to repair it before granting permissions.");
        }
        finally { _gate.Release(); }
    }

    private async Task AssertExistingUnchangedAsync(ApplicationSetupRow row, CancellationToken ct)
    {
        var app = await _graph.GetAsync("/applications/" + row.ApplicationObjectId + AppSelect, ct).ConfigureAwait(false);
        var principal = await _graph.GetAsync("/servicePrincipals/" + row.ServicePrincipalId + SpSelect, ct).ConfigureAwait(false);
        if (CanonicalJson.Sha256(app) != CanonicalJson.Sha256(row.ExistingApplication!)
            || CanonicalJson.Sha256(principal) != CanonicalJson.Sha256(row.ExistingPrincipal!))
            throw new PlanValidationException("Existing application changed after preview. No configuration write was sent; preview again.");
    }

    private async Task CompleteRegistrationAsync(ApplicationSetupResult result, ApplicationSetupItemResult item,
        ApplicationSetupRow row, bool assignOperator, CancellationToken ct)
    {
        var appPayload = row.Status == "Create" ? new JsonObject { ["publicClient"] = SetupRegistration.PublicClient(item.ClientId) } : ApplicationPayload(row);
        await AdditionalWriteAsync(result, item, "Configure registration", appPayload,
            () => _graph.ConfigureApplicationAsync(item.ApplicationObjectId, item.ClientId, appPayload, ct));
        await AdditionalWriteAsync(result, item, "Set original tool icon", new JsonObject { ["sha256"] = ProductLogo.Digest },
            () => _graph.UploadLogoAsync(item.ApplicationObjectId, ProductLogo.Read(), ct));
        var spPayload = new JsonObject { ["displayName"] = row.DisplayName, ["homepage"] = SetupRegistration.HomePage, ["appRoleAssignmentRequired"] = true };
        await AdditionalWriteAsync(result, item, "Configure enterprise app", spPayload,
            () => _graph.ConfigurePrincipalAsync(item.ServicePrincipalId, spPayload, ct));
        if (assignOperator)
        {
            var assigned = await _graph.GetAllAsync("/servicePrincipals/" + item.ServicePrincipalId + "/appRoleAssignedTo?$select=principalId,principalType,appRoleId", ct);
            if (!assigned.Any(a => Same(Text(a, "principalId"), Identity.AccountObjectId) && Text(a, "principalType") == "User"))
            {
                var payload = new JsonObject { ["principalId"] = Identity.AccountObjectId, ["resourceId"] = item.ServicePrincipalId, ["appRoleId"] = Guid.Empty.ToString() };
                await AdditionalWriteAsync(result, item, "Assign current engineer (no directory role)", payload,
                    () => _graph.AssignEngineerAsync(item.ServicePrincipalId, Identity.AccountObjectId, payload, ct));
            }
        }
    }

    private static async Task AdditionalWriteAsync(ApplicationSetupResult result, ApplicationSetupItemResult item, string action, JsonObject payload, Func<Task> send)
    {
        var step = new SetupWriteRecord { Action = action, PayloadDigest = CanonicalJson.Sha256(payload), Acceptance = "Intent recorded" };
        item.AdditionalWrites.Add(step); SaveResult(result); Journal(result, action, item.Mode, step.PayloadDigest);
        await send().ConfigureAwait(false);
        step.Acceptance = "Accepted"; SaveResult(result);
    }
}
