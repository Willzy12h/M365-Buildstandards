# Authentication flow and interaction budget

Inspected against integration `8f0285a` (.NET 10, Preview.19) on 8 October 2026; PR #37 measures the existing explicit-connect policy and improves its explanations. Source/synthetic evidence is separate from Microsoft behaviour. No live sign-in, consent or service calls were performed.

## What engineers should expect

Changing pages, reading a saved assessment and reviewing a plan do not sign in. Quick Connect's confirmation rechecks the organisation and account using its retained sign-in when the shared assessment registration is selected. Repeating a connection for an unchanged, verified profile/mode/application reuses the session and runs read-only access checks. A deliberate switch to deployment requests write access through the deployment registration, then preserves the verified context through plan review and deployment. Final exact approval remains required.

Microsoft may require MFA, Conditional Access, consent or account selection. A Graph token does not grant Exchange/Purview access. No universal zero-prompt flow is promised.

## Contexts and boundaries

| Journey | Code path and identity | Registration / resource / scopes | Lifetime and interaction reason |
|---|---|---|---|
| Quick Connect discovery | `TenantDiscoveryService.DiscoverRetainedAsync` → `MsalAuthenticator.DiscoverAsync`; engineer chooses the client's account, then Graph verifies tenant/operator | Shared Microsoft Graph PowerShell assessment registration; Graph; diagnostic/catalogue read scopes, with write requests refused | Explicit chooser. Pending verified discovery is retained for five minutes; cancellation, future/expired context or tenant/standard/account mismatch rejects confirmation |
| Confirm discovery | `Workspace.ConnectAsync` → `TenantConnectionService.ConfirmDiscoveryAsync` | Shared registration reuses the pending authenticator. A dedicated assessment registration requires its own token and tries its matching cached account first | Same fresh confirmed tenant/account; no second prompt on the shared path. Different registration may need interaction; tokens are never relabelled |
| Assessment / reconnect | `Workspace.ConnectAsync` → `TenantConnectionService.ConnectAsync` → MSAL explicit-connect acquisition; known account hint from the current verified session or setup identity | Dedicated assessment client or authorised shared fallback; Graph; `DiagnosticScopes` plus catalogue `ReadScopes()` | DPAPI-protected `msal-assessment.cache` in this tenant's local data directory. Silent first only for one matching known account; otherwise explicit interaction |
| Partner/GDAP chooser | `ConnectViewModel.ConnectAsync(chooseAccount: true)` supplies an empty hint; explicit target tenant | Selected assessment registration; Graph delegated permissions | Explicit account choice; no inferred client from the partner's home tenant. Current identity/access checks remain required |
| Read-to-write switch | `DeployViewModel.EnableDeploymentAsync` or Connect deployment action → `Workspace.ConnectAsync(Deployment)` | Dedicated deployment registration; Graph; read and catalogue write scopes | Visible deliberate switch, known-account hint, separate `msal-deployment.cache`. New scopes/consent or application context may require interaction. It invalidates prior plan/acknowledgement |
| Plan review / deploy | `PlanViewModel`, `DeployViewModel` and `Workspace` use the existing verified connection | Same pinned deployment registration, tenant/account and scopes | No new sign-in solely to review or approve a plan. Exact reviewed plan, complete durable before evidence and single-use execution checks remain independent |
| Graph token expiry / GET 401 | `MsalAuthenticator.GetAccessTokenAsync` and `GraphClient` | Same client, tenant, cached account and requested scopes | Access token reused until within five minutes of expiry. Silent renewal validates tenant and home-account identifier. One silent forced renewal after GET 401; no interactive retry during an operation and no write replay |
| Application setup | `Workspace.ConnectApplicationSetupAsync` → `ApplicationSetupService.ConnectAsync` | Temporary Microsoft Graph Command Line Tools bootstrap registration; Graph; `User.Read`, `Application.ReadWrite.All`, `Directory.Read.All`, `AppRoleAssignment.ReadWrite.All` | Same verified setup session reused for the same tenant; account hint where available. No toolkit persistent privileged cache. Consent grants use Microsoft's browser flow and remain distinct from WAM authentication |
| Exchange / Purview capture | `ExchangeCaptureRunner` → toolkit-owned `ReadCapture.ps1`; verified output identity and evidence imports | ExchangeOnlineManagement delegated sessions, Exchange/Purview audiences and RBAC; no reuse of Graph bearer tokens | Separate owned bounded PowerShell process and service authentication. Each new capture process may require service prompts; no assumption that Graph consent or silent access satisfies these resources |
| Reopening the application | Existing protected MSAL files may remain after ordinary session release; profiles load without authentication | Selected tenant/mode/registration only when explicitly connecting | Current code does not restore a live session or infer a known account from a saved client profile. Without a confirmed hint, account selection remains explicit; do not claim automatic silent reopening |
| Disconnect / sign-out | `ConnectedTenant.DisposeAsync` → `MsalAuthenticator.DisconnectAsync`; pending discovery and setup are cleared | Selected owned sessions only | Removes the owned MSAL account/cache and memory token. `ReleaseAsync` clears owned session memory while preserving protected cache for reuse; it does not sign other toolkit sessions out of the broker |

The WAM broker is used on Windows unless **Use system browser** is selected. Setup consent is an authorisation website, not an account-authentication popup that this tool can replace. A missing parent window stops broker authentication with browser guidance instead of silently changing identity providers.

## Measured prompt decisions

The existing explicit-connect policy is now directly exercised with fake acquisition delegates in `ExplicitConnectAcquisitionTests`; it owns no second cache/session. “Prompt” means an interactive acquisition requested by the toolkit, not the number of Microsoft screens.

| Scenario | Before source policy: silent / interactive | After tested policy: silent / interactive |
|---|---|---|
| Known tenant/account, one matching cached account, usable access | 1 / 0 | 1 / 0 |
| Known account absent from cache | 0 / 1 | 0 / 1 |
| More than one matching cached account | 0 / 1 | 0 / 1; explicit account chooser retains the hint |
| Discovery/partner chooser or no confirmed hint | 0 / 1 | 0 / 1 |
| Silent acquisition needs Microsoft interaction | 1 / 1 | 1 / 1, explicit reason recorded |
| Cancellation after the silent attempt | 1 / 0 | 1 / 0 |
| Silent service failure other than interaction-required | 1 / 0 | 1 / 0; failure propagated |
| Interactive cancellation | 0 / 1 | 0 / 1; no retry |

The before column is source inspection, not a historical fake-auth test run. The after column is executed synthetic testing. This slice claims improved diagnostics and enforced ambiguous-account choice, not a newly achieved reduction in prompts that the baseline already avoided. Existing `Preview17AuthAndProcessTests` exercise discovery expiry, tenant/standard/operator mismatch and write-scope refusal. `GraphClientTests` exercise bounded GET renewal and write non-replay. Windows broker, CA/MFA/consent and Exchange session behaviour remain live-unverified.

## Retention and acceptance

Caches are protected local authentication material, never included in reports, evidence transfer, support bundles or script outputs. A handoff never restores approval or sign-in. Reuse respects the existing application/tenant/account/mode boundaries; no cache migration or new retention contract is added here. Current application setup and permission requirements remain in [APPLICATION-SETUP](APPLICATION-SETUP.md); this change requests no new scopes or consent.

William's live run should record toolkit-requested interaction counts separately for discovery/confirmation, repeat assessment, deliberate deployment switch, plan review/deploy, Exchange/Purview capture, setup and reopening. Record only scenario, package/version, resource, count, outcome and whether Microsoft requested consent/MFA; omit tenant/account identifiers, tokens and exports. Cancellation and interaction-required outcomes must stop safely without mid-operation authentication or write replay.
