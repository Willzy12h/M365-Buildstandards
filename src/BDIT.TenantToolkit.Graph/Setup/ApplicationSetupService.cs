using System.Text;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Graph.Auth;

namespace BDIT.TenantToolkit.Graph.Setup;

/// <summary>
/// Temporary, explicitly initiated setup session. Normal assessment/deployment clients never gain these routes.
/// Creation, consent and effective access are separate stages. No tenant operations run merely by constructing this service.
/// </summary>
public sealed partial class ApplicationSetupService : IAsyncDisposable
{
    public const string BootstrapClientId = "14d82eec-204b-4c2f-b7e8-296a70dab67e";
    public const string GraphApplicationId = "00000003-0000-0000-c000-000000000000";
    public static IReadOnlyList<string> SetupScopes { get; } = Array.AsReadOnly(new[] { "User.Read", "Application.ReadWrite.All", "Directory.Read.All", "AppRoleAssignment.ReadWrite.All" });
    private readonly ApplicationSetupGraphClient _graph;
    private readonly MsalAuthenticator? _authenticator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, string> _issuedPlans = new(StringComparer.Ordinal);
    private bool _disconnected;
    public SignInOutcome Identity { get; }

    /// <summary>Dependency-injected constructor also permits offline tests; every operation verifies tenant and operator against Graph.</summary>
    public ApplicationSetupService(HttpClient http, IAccessTokenProvider tokens, SignInOutcome identity, IToolkitLog log)
    {
        if (!ProfileValidator.IsGuid(identity.TenantId) || !ProfileValidator.IsGuid(identity.AccountObjectId))
            throw new ConfigurationException("Setup requires a verified tenant and delegated operator object ID.");
        Identity = identity;
        _authenticator = tokens as MsalAuthenticator;
        _graph = new ApplicationSetupGraphClient(http, tokens, log);
    }

    public static async Task<ApplicationSetupService> ConnectAsync(HttpClient http, string tenantId, IToolkitLog log, CancellationToken ct,
        IntPtr parentWindowHandle = default, bool useSystemBrowser = false, string loginHint = "")
    {
        var auth = await MsalAuthenticator.SignInAsync(new SignInRequest
        {
            TenantId = tenantId, ClientId = BootstrapClientId,
            ClientLabel = "Microsoft Graph Command Line Tools — temporary application setup",
            Purpose = "Application setup: registration writes and grant inspection", Scopes = SetupScopes,
            ParentWindowHandle = parentWindowHandle, UseSystemBrowser = useSystemBrowser, LoginHint = loginHint,
            ClientVersion = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(ApplicationSetupService).Assembly)?.InformationalVersion ?? "unknown",
            CacheFile = "" // Never attach the privileged setup identity to a persistent cache.
        }, log, ct).ConfigureAwait(false);
        try
        {
            if (SetupScopes.Except(auth.Outcome.Scopes, StringComparer.OrdinalIgnoreCase).Any())
                throw new AuthenticationRequiredException("Microsoft did not return all requested setup permissions. Review setup consent and sign in again.");
            var service = new ApplicationSetupService(http, auth, auth.Outcome, log);
            await service.VerifyIdentityAsync(ct).ConfigureAwait(false);
            return service;
        }
        catch { await auth.DisconnectAsync().ConfigureAwait(false); throw; }
    }

    public static List<string> RequiredScopes(StandardCatalogue standard, SessionMode mode)
    {
        var scopes = TenantConnectionService.DiagnosticScopes.Concat(standard.ReadScopes())
            .Concat(mode == SessionMode.Deployment ? standard.WriteScopes() : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        if (mode == SessionMode.Assessment && scopes.Any(s => s.Contains("ReadWrite", StringComparison.OrdinalIgnoreCase)
            || s.Contains(".Write", StringComparison.OrdinalIgnoreCase) || s.Contains("AccessAsUser", StringComparison.OrdinalIgnoreCase)))
            throw new ConfigurationException("The standard contains a write-capable assessment scope. Correct and review the standard before application setup.");
        return scopes;
    }

    public async Task<ApplicationSetupPlan> PreviewAsync(StandardCatalogue standard, string namePrefix, CancellationToken ct,
        string assessmentClientId = "", string deploymentClientId = "", bool assignOperator = false)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tenant = await VerifyIdentityAsync(ct).ConfigureAwait(false);
            namePrefix = namePrefix.Trim();
            if (namePrefix.Length is < 1 or > 100 || namePrefix.Any(char.IsControl)) throw new ConfigurationException("Enter an application naming prefix of 1–100 characters.");
            var resource = await GraphResourceAsync(ct).ConfigureAwait(false);
            var scopeMap = ScopeMap(resource);
            var plan = new ApplicationSetupPlan
            {
                TenantId = Identity.TenantId, TenantName = Text(tenant, "displayName"), OperatorId = Identity.AccountObjectId,
                OperatorName = Identity.Account, StandardHash = CanonicalJson.Sha256Value(standard), StandardRelease = standard.Release,
                GraphServicePrincipalId = RequireGuid(resource, "id"), CreatedAt = DateTimeOffset.UtcNow,
                AssignOperator = assignOperator, LogoDigest = ProductLogo.Digest
            };
            foreach (var mode in new[] { SessionMode.Assessment, SessionMode.Deployment })
            {
                var row = new ApplicationSetupRow { Mode = mode, DisplayName = namePrefix + (mode == SessionMode.Assessment ? " Assessment Tool" : " Deployment Tool") };
                foreach (var scope in RequiredScopes(standard, mode))
                {
                    if (!scopeMap.TryGetValue(scope, out var permission)) throw new ConfigurationException($"Microsoft Graph does not advertise the enabled delegated permission {scope}. Setup is blocked.");
                    row.Permissions.Add(permission);
                }
                row.ApplicationPayload = ApplicationPayload(row);
                var explicitId = mode == SessionMode.Assessment ? assessmentClientId.Trim() : deploymentClientId.Trim();
                if (explicitId.Length > 0)
                {
                    if (Same(assessmentClientId.Trim(), deploymentClientId.Trim())) throw new ConfigurationException("Use different IDs for assessment and deployment.");
                    await PrepareExistingAsync(row, explicitId, ct).ConfigureAwait(false);
                    plan.Rows.Add(row);
                    continue;
                }
                row.ExistingMatches = await FindNameMatchesAsync(row.DisplayName, ct).ConfigureAwait(false);
                if (row.ExistingMatches.Count > 0)
                {
                    row.Status = "Review existing";
                    row.Reason = "Matching registrations or enterprise applications already exist. Creation is blocked; explicitly choose and validate their client IDs. Names do not establish ownership.";
                }
                else row.Reason = "Create a single-tenant public-client registration and enterprise application. Delegated permissions are requested, not yet consented. Engineer assignment is required.";
                plan.Rows.Add(row);
            }
            plan.Before = new JsonObject
            {
                ["organisation"] = tenant.DeepClone(), ["graphServicePrincipal"] = resource.DeepClone(),
                ["matches"] = new JsonArray(plan.Rows.SelectMany(r => r.ExistingMatches).Select(m => (JsonNode)m.DeepClone()).ToArray())
            };
            plan.PlanHash = PlanHash(plan);
            _issuedPlans.Clear();
            _issuedPlans.Add(plan.Id, plan.PlanHash);
            return plan;
        }
        finally { _gate.Release(); }
    }

    public async Task<ApplicationSetupResult> ExecuteAsync(ApplicationSetupPlan plan, StandardCatalogue standard,
        string exactTenantConfirmation, bool permissionsApproved, string evidenceDirectory, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            AssertConnected();
            plan = ToolkitJson.Deserialize<ApplicationSetupPlan>(ToolkitJson.Serialize(plan));
            if (!permissionsApproved || !string.Equals(exactTenantConfirmation.Trim(), Identity.TenantId, StringComparison.OrdinalIgnoreCase))
                throw new PlanValidationException("Approve the displayed permissions and type the exact tenant ID before application creation.");
            if (!_issuedPlans.TryGetValue(plan.Id, out var issued) || issued != plan.PlanHash || PlanHash(plan) != issued)
                throw new PlanValidationException("The setup plan changed, was already used, or belongs to another session. Preview again.");
            if (!Same(plan.TenantId, Identity.TenantId) || !Same(plan.OperatorId, Identity.AccountObjectId)
                || plan.StandardHash != CanonicalJson.Sha256Value(standard))
                throw new PlanValidationException("Tenant, operator or standard changed. Preview application setup again.");
            if (plan.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(1) || DateTimeOffset.UtcNow - plan.CreatedAt > TimeSpan.FromMinutes(5))
                throw new PlanValidationException("The application setup plan expired. Preview again.");
            if (plan.LogoDigest != ProductLogo.Digest) throw new PlanValidationException("The product icon changed. Preview setup again.");
            var selected = plan.Rows.Where(r => r.Status is "Create" or "Configure existing").ToList();
            if (selected.Count == 0) throw new PlanValidationException("No missing application is eligible for creation.");
            AssertNoUnresolvedSetup(Path.GetFullPath(evidenceDirectory));
            await VerifyIdentityAsync(ct).ConfigureAwait(false);
            var currentGraph = await GraphResourceAsync(ct).ConfigureAwait(false);
            if (!Same(Text(currentGraph, "id"), plan.GraphServicePrincipalId)) throw new PlanValidationException("Graph resource identity changed. Preview again.");
            var currentScopes = ScopeMap(currentGraph);
            foreach (var row in selected)
            {
                if (row.Permissions.Any(p => !currentScopes.TryGetValue(p.Name, out var current) || !Same(p.Id, current.Id)))
                    throw new PlanValidationException("Graph permission definitions changed. Preview again.");
                if (row.Status == "Configure existing") { await AssertExistingUnchangedAsync(row, ct).ConfigureAwait(false); continue; }
                if ((await FindNameMatchesAsync(row.DisplayName, ct).ConfigureAwait(false)).Count > 0)
                    throw new PlanValidationException("An application with a planned name appeared after preview. Review existing client IDs before continuing.");
            }
            var result = new ApplicationSetupResult
            {
                TenantId = Identity.TenantId, OperatorId = Identity.AccountObjectId, PlanId = plan.Id, StartedAt = DateTimeOffset.UtcNow,
                Rows = plan.Rows.Select(r => new ApplicationSetupItemResult { Mode = r.Mode, DisplayName = r.DisplayName, ClientId = r.ClientId,
                    ApplicationObjectId = r.ApplicationObjectId, ServicePrincipalId = r.ServicePrincipalId,
                    Status = selected.Contains(r) ? "Not run" : "Review existing", Reason = r.Reason }).ToList()
            };
            result.EvidenceDirectory = Path.Combine(Path.GetFullPath(evidenceDirectory), "app-setup-" + result.Id);
            Directory.CreateDirectory(result.EvidenceDirectory);
            // Persist and verify the complete setup-specific pre-change metadata before any registration write.
            var before = ToolkitJson.Serialize(plan);
            DurableWrite(Path.Combine(result.EvidenceDirectory, "before.json"), before);
            if (CanonicalJson.Sha256Hex(File.ReadAllText(Path.Combine(result.EvidenceDirectory, "before.json"))) != CanonicalJson.Sha256Hex(before))
                throw new IntegrityException("Setup evidence could not be verified on disk. No application was created.");
            SaveResult(result);
            _issuedPlans.Remove(plan.Id); // A reviewed plan is single-use even if the first response is ambiguous.
            var after = new JsonArray();
            try
            {
                foreach (var row in selected)
                {
                    ct.ThrowIfCancellationRequested();
                    var item = result.Rows.Single(r => r.Mode == row.Mode);
                    try
                    {
                        // Fresh identity and name checks occur immediately before each candidate's first write.
                        await VerifyIdentityAsync(ct).ConfigureAwait(false);
                        if (row.Status == "Configure existing") await AssertExistingUnchangedAsync(row, ct).ConfigureAwait(false);
                        else if ((await FindNameMatchesAsync(row.DisplayName, ct).ConfigureAwait(false)).Count != 0)
                            throw new PlanValidationException("A matching application appeared. Creation stopped without adopting it.");
                        // Finish one application/enterprise-application pair at an action boundary. A UI stop does
                        // not cancel an in-flight registration write; timeouts still produce an ambiguous outcome.
                        using var action = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                        var actionToken = action.Token;
                        item.Status = "In progress";
                        if (row.Status == "Create")
                        {
                        item.ApplicationWrite = "Intent recorded";
                        SaveResult(result);
                        Journal(result, "Create application", row.Mode, CanonicalJson.Sha256(row.ApplicationPayload));
                        var app = await _graph.CreateAsync("/applications", row.ApplicationPayload, actionToken).ConfigureAwait(false);
                        item.ApplicationWrite = "Accepted";
                        item.ApplicationObjectId = RequireCreatedGuid(app, "id");
                        item.ClientId = RequireCreatedGuid(app, "appId");
                        SaveResult(result);
                        var spPayload = new JsonObject { ["appId"] = item.ClientId, ["appRoleAssignmentRequired"] = true };
                        item.ServicePrincipalWrite = "Intent recorded";
                        SaveResult(result);
                        Journal(result, "Create service principal", row.Mode, CanonicalJson.Sha256(spPayload));
                        var sp = await _graph.CreateAsync("/servicePrincipals", spPayload, actionToken).ConfigureAwait(false);
                        item.ServicePrincipalWrite = "Accepted";
                        item.ServicePrincipalId = RequireCreatedGuid(sp, "id");
                        SaveResult(result);
                        }
                        await CompleteRegistrationAsync(result, item, row, plan.AssignOperator, actionToken).ConfigureAwait(false);
                        var readApp = await _graph.GetAsync("/applications/" + item.ApplicationObjectId + AppSelect, actionToken).ConfigureAwait(false);
                        var readSp = await _graph.GetAsync("/servicePrincipals/" + item.ServicePrincipalId + SpSelect, actionToken).ConfigureAwait(false);
                        after.Add(new JsonObject { ["application"] = readApp.DeepClone(), ["servicePrincipal"] = readSp.DeepClone() });
                        var expectedApp = ApplicationPayload(row); expectedApp["publicClient"] = SetupRegistration.PublicClient(item.ClientId);
                        if (!CanonicalJson.IsSubset(readApp, expectedApp) || !Same(Text(readSp, "appId"), item.ClientId)
                            || readSp["appRoleAssignmentRequired"]?.GetValue<bool>() != true
                            || Text(readSp, "displayName") != row.DisplayName || Text(readSp, "homepage") != SetupRegistration.HomePage)
                            throw new ConfigurationException("Registration writes were accepted, but readback did not confirm the reviewed settings.");
                        item.ConfigurationVerification = "Passed";
                        item.Status = "Configured — consent pending";
                        item.Reason = "Registration and enterprise application readback passed. Continue with both permission approvals, then connect directly from setup.";
                        SaveResult(result);
                    }
                    catch (Exception ex)
                    {
                        if (item.AdditionalWrites.LastOrDefault() is { Acceptance: "Intent recorded" } extra)
                            extra.Acceptance = ex is WriteNotSentException or WriteDeniedException or SafetyViolationException or AuthenticationRequiredException
                                ? "Not attempted" : ex is GraphRequestException ? "Rejected" : "Unknown";
                        if (ex is AmbiguousWriteException)
                        {
                            if (item.ServicePrincipalWrite == "Intent recorded") item.ServicePrincipalWrite = "Unknown";
                            else if (item.ApplicationWrite == "Intent recorded") item.ApplicationWrite = "Unknown";
                            item.Status = "Outcome unknown — reconcile";
                        }
                        else
                        {
                            var acceptance = ex is WriteNotSentException or WriteDeniedException or SafetyViolationException or AuthenticationRequiredException
                                ? "Not attempted" : ex is GraphRequestException ? "Rejected" : "Not confirmed";
                            if (item.ServicePrincipalWrite == "Intent recorded") item.ServicePrincipalWrite = acceptance;
                            if (item.ApplicationWrite == "Intent recorded") item.ApplicationWrite = acceptance;
                            item.Status = item.ApplicationWrite == "Accepted" || item.AdditionalWrites.Any(w => w.Acceptance == "Accepted")
                                ? "Partially completed — review" : "Failed";
                        }
                        item.Reason = SafeError(ex);
                        item.ConfigurationVerification = "Incomplete";
                        result.Status = "Review required";
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { result.Status = "Stopped — review evidence"; }
            finally
            {
                // Re-read resulting objects even when a row failed. Names are evidence for reconciliation, never ownership.
                using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(100));
                try
                {
                    var observed = new JsonArray();
                    foreach (var row in plan.Rows)
                        foreach (var match in await FindNameMatchesAsync(row.DisplayName, finish.Token).ConfigureAwait(false)) observed.Add(match.DeepClone());
                    DurableWrite(Path.Combine(result.EvidenceDirectory, "after.json"), ToolkitJson.Serialize(new { capturedAt = DateTimeOffset.UtcNow, readback = after, observed }));
                    result.AfterComplete = true;
                }
                catch (Exception ex) { result.AfterError = SafeError(ex); result.Status = "Review required"; }
                if (result.Status == "Running") result.Status = "Setup complete — consent and access checks pending";
                result.EndedAt = DateTimeOffset.UtcNow;
                SaveResult(result);
            }
            return result;
        }
        finally { _gate.Release(); }
    }

    public async Task<ApplicationSetupValidation> ValidateAsync(StandardCatalogue standard, string assessmentClientId, string deploymentClientId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await VerifyIdentityAsync(ct).ConfigureAwait(false);
            var resource = await GraphResourceAsync(ct).ConfigureAwait(false);
            var scopeMap = ScopeMap(resource);
            var byId = scopeMap.Values.ToDictionary(p => p.Id, p => p.Name, StringComparer.OrdinalIgnoreCase);
            var validation = new ApplicationSetupValidation { TenantId = Identity.TenantId, CheckedAt = DateTimeOffset.UtcNow };
            foreach (var (mode, clientId) in new[] { (SessionMode.Assessment, assessmentClientId.Trim()), (SessionMode.Deployment, deploymentClientId.Trim()) })
            {
                var row = new ApplicationPermissionValidation { Mode = mode, ClientId = clientId, RequiredScopes = RequiredScopes(standard, mode) };
                validation.Rows.Add(row);
                try
                {
                    if (!ProfileValidator.IsGuid(clientId)) throw new ConfigurationException("Enter an explicit application/client ID to validate.");
                    if (Same(assessmentClientId, deploymentClientId)) throw new ConfigurationException("Assessment and deployment must use different application registrations.");
                    var apps = await _graph.GetAllAsync("/applications?$filter=" + Uri.EscapeDataString("appId eq '" + clientId + "'") + "&" + AppSelect[1..], ct).ConfigureAwait(false);
                    var principals = await _graph.GetAllAsync("/servicePrincipals?$filter=" + Uri.EscapeDataString("appId eq '" + clientId + "'") + "&" + SpSelect[1..], ct).ConfigureAwait(false);
                    if (apps.Count != 1 || principals.Count != 1) throw new ConfigurationException("Expected exactly one local registration and enterprise application for this client ID.");
                    var app = apps[0]; var sp = principals[0];
                    row.ApplicationObjectId = RequireGuid(app, "id"); row.ServicePrincipalId = RequireGuid(sp, "id");
                    if (!Same(Text(app, "appId"), clientId) || !Same(Text(sp, "appId"), clientId) || !Same(Text(sp, "appOwnerOrganizationId"), Identity.TenantId))
                        row.Issues.Add("Registration or enterprise application does not identify the requested tenant/client.");
                    if (Text(app, "signInAudience") != "AzureADMyOrg") row.Issues.Add("The application is not single-tenant.");
                    if (app["publicClient"]?["redirectUris"] is not JsonArray redirects || !redirects.Any(r => r?.GetValue<string>() == "http://localhost")
                        || !redirects.Any(r => Same(r?.GetValue<string>() ?? "", SetupRegistration.BrokerRedirect(clientId))))
                        row.Issues.Add("Windows pop-up or browser sign-in redirect is missing. Preview configuration for this existing ID.");
                    if (app["web"]?["redirectUris"] is not JsonArray webRedirects || !webRedirects.Any(r => r?.GetValue<string>() == SetupRegistration.ConsentRedirect))
                        row.Issues.Add("Administrator-consent web redirect is missing. Preview configuration for this existing ID before approving permissions.");
                    if (app["requiredResourceAccess"] is not JsonArray required) throw new ConfigurationException("Required permissions could not be read.");
                    foreach (var api in required.OfType<JsonObject>())
                    {
                        if (Text(api, "resourceAppId") != GraphApplicationId) { row.Issues.Add("Additional API permissions are configured; review before using this application."); continue; }
                        if (api["resourceAccess"] is not JsonArray permissions) throw new ConfigurationException("Configured Graph permissions are incomplete.");
                        foreach (var permission in permissions.OfType<JsonObject>())
                        {
                            if (Text(permission, "type") != "Scope") { row.Issues.Add("Application permissions are configured; this workflow requires delegated-only registrations."); continue; }
                            row.ConfiguredScopes.Add(byId.GetValueOrDefault(Text(permission, "id"), "Unknown permission " + Text(permission, "id")));
                        }
                    }
                    if (!new HashSet<string>(row.RequiredScopes, StringComparer.OrdinalIgnoreCase).SetEquals(row.ConfiguredScopes))
                        row.Issues.Add("Configured delegated permissions differ from the current standard: missing or extra scopes require review.");
                    row.AssignmentRequired = sp["appRoleAssignmentRequired"]?.GetValue<bool>();
                    if (row.AssignmentRequired != true) row.Issues.Add("Enterprise application does not require explicit engineer assignment.");
                    if (sp["accountEnabled"]?.GetValue<bool>() != true) row.Issues.Add("Enterprise application is disabled or its enabled state is unknown.");
                    var appRoles = await _graph.GetAllAsync("/servicePrincipals/" + row.ServicePrincipalId + "/appRoleAssignments?$select=id,resourceId,appRoleId", ct).ConfigureAwait(false);
                    if (appRoles.Count > 0) row.Issues.Add("Application permissions are granted to this service principal; review unexpected app-only access.");
                    row.ConfigurationValid = row.Issues.Count == 0;
                    var assignments = await _graph.GetAllAsync("/servicePrincipals/" + row.ServicePrincipalId + "/appRoleAssignedTo?$select=principalId,principalType,appRoleId", ct).ConfigureAwait(false);
                    row.EngineerAssignmentConfirmed = assignments.Any(a => Same(Text(a, "principalId"), Identity.AccountObjectId) && Text(a, "principalType") == "User");
                    row.EngineerAssignmentStatus = row.EngineerAssignmentConfirmed ? "Direct assignment of the current setup operator observed. Other engineers need their own assignment."
                        : "Current operator assignment not confirmed. Assign authorised engineers in Entra, then reconnect. Group-based assignment is not evaluated here.";
                    if (!row.EngineerAssignmentConfirmed) row.Issues.Add(row.EngineerAssignmentStatus);
                    var grants = await _graph.GetAllAsync("/oauth2PermissionGrants?$filter=" + Uri.EscapeDataString("clientId eq '" + row.ServicePrincipalId + "'"), ct).ConfigureAwait(false);
                    var graphId = Text(resource, "id");
                    foreach (var grant in grants.Where(g => Text(g, "resourceId") == graphId && Text(g, "consentType") == "AllPrincipals" && Same(Text(g, "clientId"), row.ServicePrincipalId)))
                        row.GrantedScopes.AddRange(Text(grant, "scope").Split(' ', StringSplitOptions.RemoveEmptyEntries));
                    row.GrantedScopes = row.GrantedScopes.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
                    row.ConsentComplete = !row.RequiredScopes.Except(row.GrantedScopes, StringComparer.OrdinalIgnoreCase).Any();
                    if (!row.ConsentComplete) row.Issues.Add("Tenant-wide administrator consent is missing or not yet visible. Recent grants can take time to replicate; validate again.");
                    var additional = grants.Any(g => Text(g, "resourceId") != graphId)
                        || grants.Where(g => Text(g, "resourceId") == graphId).SelectMany(g => Text(g, "scope").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            .Except(row.RequiredScopes.Concat(new[] { "openid", "profile", "email", "offline_access" }), StringComparer.OrdinalIgnoreCase).Any();
                    if (additional) { row.Issues.Add("Extra delegated permissions are granted, including possibly user-specific grants. Review and remove unnecessary access explicitly."); row.ConfigurationValid = false; }
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { row.ConfigurationValid = false; row.Issues.Add(SafeError(ex)); }
            }
            return validation;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Opens Microsoft's separate approval experience. Always validate grants afterwards; redirect success is not evidence.</summary>
    public static Uri AdminConsentUri(string tenantId, string clientId, IEnumerable<string>? scopes = null)
        => BuildAdminConsentUri(tenantId, clientId, scopes, SetupRegistration.ConsentRedirect, Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));

    internal static Uri BuildAdminConsentUri(string tenantId, string clientId, IEnumerable<string>? scopes, string redirectUri, string state)
    {
        if (!ProfileValidator.IsGuid(tenantId) || !ProfileValidator.IsGuid(clientId)) throw new ConfigurationException("Consent requires explicit tenant and application IDs.");
        var exactScopes = scopes?.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (exactScopes is null || exactScopes.Count == 0 || exactScopes.Any(s => s.Length == 0 || s == ".default" || s.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '.'))))
            throw new ConfigurationException("Review and supply the exact delegated permissions before opening administrator consent.");
        var requested = string.Join(' ', exactScopes.Select(s => "https://graph.microsoft.com/" + s));
        return new Uri($"https://login.microsoftonline.com/{tenantId}/v2.0/adminconsent?client_id={clientId}&scope={Uri.EscapeDataString(requested)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={state}");
    }

    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { _disconnected = true; _issuedPlans.Clear(); if (_authenticator is not null) await _authenticator.DisconnectAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);

    private void AssertConnected() { if (_disconnected) throw new AuthenticationRequiredException("Setup connection closed. Start a new setup sign-in."); }
    private async Task<JsonObject> VerifyIdentityAsync(CancellationToken ct)
    {
        AssertConnected();
        var orgs = await _graph.GetAllAsync("/organization?$select=id,displayName,verifiedDomains", ct).ConfigureAwait(false);
        if (orgs.Count != 1 || !Same(Text(orgs[0], "id"), Identity.TenantId)) throw new TenantMismatchException("Application setup tenant verification failed.");
        var me = await _graph.GetAsync("/me?$select=id,displayName,userPrincipalName", ct).ConfigureAwait(false);
        if (!Same(Text(me, "id"), Identity.AccountObjectId)) throw new AuthenticationRequiredException("Application setup operator changed. Disconnect and sign in again.");
        return orgs[0];
    }
    private async Task<JsonObject> GraphResourceAsync(CancellationToken ct)
    {
        var rows = await _graph.GetAllAsync("/servicePrincipals?$filter=" + Uri.EscapeDataString("appId eq '" + GraphApplicationId + "'") + "&$select=id,appId,displayName,oauth2PermissionScopes", ct).ConfigureAwait(false);
        if (rows.Count != 1 || Text(rows[0], "appId") != GraphApplicationId) throw new ConfigurationException("The Microsoft Graph resource service principal could not be identified uniquely.");
        return rows[0];
    }
    private async Task<List<JsonObject>> FindNameMatchesAsync(string name, CancellationToken ct)
    {
        var filter = Uri.EscapeDataString("displayName eq '" + name.Replace("'", "''", StringComparison.Ordinal) + "'");
        var apps = await _graph.GetAllAsync("/applications?$filter=" + filter + "&" + AppSelect[1..], ct).ConfigureAwait(false);
        var principals = await _graph.GetAllAsync("/servicePrincipals?$filter=" + filter + "&" + SpSelect[1..], ct).ConfigureAwait(false);
        return apps.Concat(principals).ToList();
    }
    private static Dictionary<string, SetupPermission> ScopeMap(JsonObject graph)
    {
        if (graph["oauth2PermissionScopes"] is not JsonArray scopes) throw new ConfigurationException("Graph delegated permission definitions are unavailable.");
        return scopes.OfType<JsonObject>().Where(s => s["isEnabled"]?.GetValue<bool>() == true)
            .Select(s => new SetupPermission { Id = RequireGuid(s, "id"), Name = Text(s, "value"), Description = Text(s, "adminConsentDescription"), AdminConsentRequired = Text(s, "type") == "Admin" })
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }
    private static JsonObject ApplicationPayload(ApplicationSetupRow row) => new()
    {
        ["displayName"] = row.DisplayName, ["signInAudience"] = "AzureADMyOrg",
        ["publicClient"] = SetupRegistration.PublicClient(row.ClientId.Length == 0 ? null : row.ClientId),
        ["web"] = SetupRegistration.Web(), ["isFallbackPublicClient"] = true,
        ["description"] = "M365 BuildStandard Tool: " + row.Mode + ". Delegated engineer access; no unattended credentials.",
        ["info"] = new JsonObject { ["marketingUrl"] = SetupRegistration.HomePage, ["supportUrl"] = SetupRegistration.HomePage + "/issues" },
        ["requiredResourceAccess"] = new JsonArray(new JsonObject
        {
            ["resourceAppId"] = GraphApplicationId,
            ["resourceAccess"] = new JsonArray(row.Permissions.Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["type"] = "Scope" }).ToArray())
        })
    };
    private static string PlanHash(ApplicationSetupPlan plan)
    {
        var node = ToolkitJson.ToNode(plan)!.AsObject(); node.Remove("planHash"); return CanonicalJson.Sha256(node);
    }
    private const string AppSelect = "?$select=id,appId,displayName,signInAudience,publicClient,requiredResourceAccess,web,info,description,isFallbackPublicClient,passwordCredentials,keyCredentials,appRoles";
    private const string SpSelect = "?$select=id,appId,displayName,appOwnerOrganizationId,appRoleAssignmentRequired,accountEnabled,homepage";
    private static string Text(JsonObject obj, string key) => obj[key]?.GetValue<string>() ?? "";
    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string RequireGuid(JsonObject obj, string key) => ProfileValidator.IsGuid(Text(obj, key)) ? Text(obj, key) : throw new ConfigurationException("Graph did not return a valid " + key + ".");
    private static string RequireCreatedGuid(JsonObject obj, string key) => ProfileValidator.IsGuid(Text(obj, key)) ? Text(obj, key) : throw new AmbiguousWriteException("Creation response did not identify " + key + ". Reconcile the accepted write before repeating it.", null);
    private static string SafeError(Exception ex) => SensitiveDataScrubber.Scrub(ex.Message);
    private static void SaveResult(ApplicationSetupResult result) => DurableWrite(Path.Combine(result.EvidenceDirectory, "result.json"), ToolkitJson.Serialize(result));
    private void AssertNoUnresolvedSetup(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var previousDirectory in Directory.EnumerateDirectories(directory, "app-setup-*"))
        {
            var beforeFile = Path.Combine(previousDirectory, "before.json");
            if (!File.Exists(beforeFile)) continue;
            var previousPlan = ToolkitJson.Deserialize<ApplicationSetupPlan>(File.ReadAllText(beforeFile));
            if (!Same(previousPlan.TenantId, Identity.TenantId)) continue;
            var resultFile = Path.Combine(previousDirectory, "result.json");
            if (!File.Exists(resultFile)) throw new PlanValidationException("A previous setup has no final result. Reconcile its local evidence before creating more applications.");
            var previous = ToolkitJson.Deserialize<ApplicationSetupResult>(File.ReadAllText(resultFile));
            if (previous.EndedAt is null || previous.Rows.Any(r => r.Status.StartsWith("Outcome unknown", StringComparison.Ordinal)
                || r.ApplicationWrite is "Unknown" or "Intent recorded" or "Not confirmed" || r.ServicePrincipalWrite is "Unknown" or "Intent recorded" or "Not confirmed"
                || r.AdditionalWrites.Any(w => w.Acceptance is "Unknown" or "Intent recorded")))
                throw new PlanValidationException("A previous application setup write is unresolved. Inspect its evidence and reconcile in Entra before another creation attempt; a fresh preview does not make retry safe.");
        }
    }
    private static void Journal(ApplicationSetupResult result, string action, SessionMode mode, string payloadHash)
    {
        var bytes = Encoding.UTF8.GetBytes(ToolkitJson.ToNode(new { at = DateTimeOffset.UtcNow, action, mode, payloadHash })!.ToJsonString(ToolkitJson.Compact) + Environment.NewLine);
        using var stream = new FileStream(Path.Combine(result.EvidenceDirectory, "journal.ndjson"), FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.Write(bytes); stream.Flush(flushToDisk: true);
    }
    private static void DurableWrite(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { stream.Write(Encoding.UTF8.GetBytes(content)); stream.Flush(flushToDisk: true); }
        File.Move(temporary, path, overwrite: true);
    }
}
