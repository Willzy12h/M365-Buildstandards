# Application setup

## Quick Connect and the next step

For a client's own account, select **Quick Connect with Microsoft** on Connect. Windows sign-in discovers the organisation, domain, tenant ID and account. Review the displayed identity, then select **Connect to this tenant read-only**. The initial shared-app sign-in requests assessment read permissions and is retained in memory for five minutes. Confirmation rechecks the same tenant/account without another interactive sign-in. A configured different dedicated application needs its own token, so Microsoft may require app-specific consent or authentication. It does not copy another saved client's policy inputs, save a profile automatically, or enable deployment. An installation that disables the shared assessment fallback must use its dedicated application details instead.

**Partner / GDAP sign-in** uses an explicitly selected customer: choose its saved connection or enter its label and tenant ID, then use the separate partner button. Choose the partner account in Microsoft's account chooser. Existing GDAP, roles, assignment rules and application consent are still enforced by Microsoft. This is not automatic customer discovery or a promise of access to every endpoint.

Navigating pages does not authenticate. An unchanged verified connection can be reused; an explicit reconnect first tries a matching cached account. Different applications/modes and Microsoft's MFA or consent requirements can still require interaction. Ordinary reads renew silently and stop with reconnect guidance when Microsoft requires interaction mid-operation.

**No deployment application configured** means the current connection has no dedicated deployment client ID. Select **Configure deployment application** on Deploy to open setup for that client. If the application already exists, enter its Application (client) ID; do not recreate it or substitute an assessment ID. After configuration, consent and assignment checks, **Continue to deployment** requests the separate write-capable identity. Capture and plan again before policy deployment.

### Identify, approve and use an existing enterprise application

Default names are **BuildStandard Assessment Tool** and **BuildStandard Deployment Tool** (the name prefix is configurable). The page shows application IDs, observed creation names or clearly labelled configured names, and verified enterprise-app object IDs when available. Copy the identities and next steps for a local ticket.

1. In Entra, switch to the verified client tenant. Open **Entra ID → Enterprise applications → All applications**, search by the **Application ID**, and compare it with the tool's Application (client) ID. An enterprise application's **Object ID** is different.
2. Use **Approve assessment/deployment permissions in browser**. Check Microsoft's application name and permission list, select **Accept** as an authorised administrator, and return to the tool. WAM handles desktop sign-in; the browser handles this tenant-wide administrator-consent endpoint. They are different steps.
3. If assignment is missing, open that enterprise application's **Users and groups → Add user/group** and assign the intended engineer. Keep assignment required. For the current setup operator, a new reviewed preview with **Assign me** enabled can perform the explicitly approved assignment. Group/GDAP access is a separate check.
4. Select **Check again**. Configuration, consent and direct assignment are checked independently. Browser success and application creation alone do not establish readiness. Allow for consent propagation; do not recreate an app because approval timed out.
5. Select **Connect read-only now**, then inspect actual access-check results. These are desktop sign-in applications, not websites to launch from My Apps. Roles, licences and service behaviour still matter.

## Read-only capture and troubleshooting

**Capture configuration (read-only)** issues reads and saves evidence locally, even if the session is deployment-capable. It creates, changes, enables and assigns no tenant object. The object table's **Reported state** is the resource's state field, not compliance: **Not reported** means no state field; **Unknown** means a null, empty or unexpected value. Neither implies enabled, disabled or compliant.

For an incomplete capture, open **Collection status**. New captures include the exact assignment/settings/relationship failure, not just a summary. HTTP 400/404 can indicate an unsupported route; do not assume that increasing permissions resolves it. HTTP 401/403 calls for checking the reported permissions/roles and sign-in. Correct the cause and recapture; an old failed capture is not rewritten. Missing Exchange/Purview observations use the separate read-only capture button or offline import; they do not replace Graph evidence.

Preview.16 reads authentication method configurations from the policy GET and passkey profiles from the FIDO2 GET, whose documented responses contain the arrays. It reads compliance scheduled-action configurations explicitly. Missing/truncated arrays or any nested read failure still leave evidence incomplete. These changes use existing read permissions and preserve published catalogue bytes.

## Standard 2026.09.12 permission changes

Update the configured permission lists and grant administrator consent again to **both applications** before using .12. No consent or tenant check was performed during development. Assessment remains delegated and read-only.

| New delegated scope | Application | Purpose and Microsoft reference |
|---|---|---|
| `Application.Read.All` | Assessment and deployment | Resolve the Intune Provisioning Client by its documented Microsoft application ID, never by name. [Service-principal read](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-list?view=graph-rest-1.0). |
| `Policy.ReadWrite.Authorization` | Deployment only | Remove default-user self-consent grants while preserving other authorisation settings. [Authorisation policy update](https://learn.microsoft.com/en-us/graph/api/authorizationpolicy-update?view=graph-rest-1.0). |
| `Policy.ReadWrite.ConsentRequest` | Deployment only | Enable the admin consent workflow with mandatory client-selected reviewers. [Workflow update](https://learn.microsoft.com/en-us/graph/api/adminconsentrequestpolicy-update?view=graph-rest-1.0). |

Existing `Policy.Read.All` covers the consent-policy reads; no invented consent read scope is requested. The provisioning-group owner action uses existing `Group.ReadWrite.All`. Passkey profiles and system-preferred MFA use the declared authentication policy scopes. Exchange/Purview use a separate integrated, delegated read-only PowerShell capture (manual export/import remains available) with appropriate Exchange/Purview RBAC; they add no Exchange credentials or write scopes to these Graph applications.

The local [engineer document exports](ENGINEER-DOCUMENTS.md) need no sign-in, application registration or consent. [Exchange/Purview observations and proposals](EXCHANGE-PURVIEW.md) use a separate delegated read-only connection with appropriate service RBAC. The toolkit can collect observations from its embedded template; it never executes service writes or selected manual proposals. Exported references do not grant access.

## Guided setup

The **Application setup** page is one guided sequence: sign in, approve once, connect. Everything below it on the page is for existing applications and manual repair.

1. Open **Application setup** after connecting. The verified tenant is filled in automatically. Select **Quick setup · check existing applications**. If there is no verified connection, select the target tenant explicitly. Existing configured IDs are checked; otherwise select exact client IDs from the discovered matches and choose **Check selected application**. Names alone cannot establish ownership. If deployment has no configured app, **Connect for deployment** opens the same page.
2. **Sign in as administrator.** Windows Web Account Manager (WAM) opens Microsoft's own sign-in window; tick the browser option first if needed. Microsoft asks the account to accept the four temporary setup permissions listed on the page. *Assign me to both applications* is ticked by default so the engineer who runs setup can connect straight away; untick it when that account will not use the tool (partner/GDAP engineers, for example). The toolkit writes nothing at sign-in; accepting Microsoft's prompt can leave a consent grant for Microsoft Graph Command Line Tools in Entra, as described below. Setup reuses a connected setup session for the same tenant.
3. **Review and approve.** Signing in previews the plan automatically: the tenant name and ID, both applications, every permission each will request and the exact registration payload. Review the verified tenant, tick approval and select **Approve and create/configure applications**. Previewing alone creates nothing. You do not need to type the tenant ID again for setup: approval binds the exact verified tenant, operator and fresh plan. A changed identity/input invalidates approval. Policy deployment now has an explicit reviewed-tenant approval tick and deliberate deploy button, with complete fresh Graph evidence and unchanged engine checks. Separate activation/recovery workflows retain their confirmations.
4. The toolkit then, in order and stopping at the first stage that does not finish cleanly:
   - saves before-change evidence and each write outcome, creates or configures the reviewed registrations, registers the sign-in and consent redirects, uploads the tool icon, sets the project URLs and applies the approved engineer assignment. Client IDs fill in automatically;
   - reads both applications back from Microsoft Graph, and only if every write was confirmed goes on;
   - opens Microsoft's administrator-consent page in the browser for the **assessment** application and waits for it, then does the same for the **deployment** application. Microsoft's consent covers one application per page, so there are two approvals; the second opens without another click. Each lists the operational scopes, not the temporary setup scopes. An application whose grants are already complete is skipped;
   - reads the actual grants (browser success is not evidence), reading once more after 20 seconds if a grant has not appeared yet.
5. **Connect.** When the assessment application is ready, select **Connect read-only now**; **Continue to deployment** is enabled when the deployment application is. The selected application's sign-in and read-only access checks follow directly. Microsoft may reuse Windows sign-in, but app-specific token acquisition and any required consent/MFA remain necessary.

If a stage does not finish, the page says which and why, and nothing is retried automatically. A partial or uncertain creation stops before any consent is requested. A declined, unsuccessful or timed-out approval, or a stop, leaves the created applications in place: use **Approve assessment permissions** / **Approve deployment permissions** under *Existing applications and manual steps*, then **Check again**. Do not recreate the applications.

**Existing applications.** Tool applications found by name block creation, because names never establish ownership. Enter their exact client IDs under *Existing applications and manual steps* and select **Preview again**; approval of the displayed verified tenant and reviewed plan can then repair them.

Two registrations separate assessment and deployment requests: **BuildStandard Assessment Tool** has read scopes; **BuildStandard Deployment Tool** also has the required write scopes. The normal client never grants its own access. Both are delegated, single-tenant public clients, with assignment required and no secrets. Admin consent does not itself prove directory roles, Intune RBAC, licences or successful deployment.

## Setup permissions and delegated administrators

The temporary Microsoft Graph Command Line Tools sign-in requests `User.Read`, `Directory.Read.All`, `Application.ReadWrite.All` and `AppRoleAssignment.ReadWrite.All`. The last permission is used only for the explicitly approved current-engineer default app assignment. The toolkit never grants directory roles, writes consent grants directly or stores credentials. Microsoft may display its own first-party branding for that bootstrap identity; the two tool apps use the custom icon and project URLs.

Use an identity with appropriate rights in the target tenant. For partner/GDAP or group access, direct assignment may not be visible. After configuration and consent are verified, **Check delegated-admin deployment access** permits an actual target-tenant sign-in and read-only access check. It does not bypass Microsoft's assignment/role enforcement or promise GDAP compatibility for every endpoint. Tenant and returned operator identity are still verified. Group expansion, PIM activation, custom roles and partner-specific endpoint behaviour require live testing.

## Redirects and existing-app repair

- Desktop sign-in: WAM uses `ms-appx-web://microsoft.aad.brokerplugin/{client-id}`; browser fallback uses the native `http://localhost` registration.
- Administrator consent: exact web redirect `http://localhost:8400/m365-consent/`. It is separate from the native redirect; the code no longer assumes an ephemeral-port exemption for admin consent.
- The wizard verifies this registered callback before opening consent. An older registration must be explicitly previewed and repaired first. If port 8400 is occupied, close the other consent window. Windows can also hold the port briefly after the first approval's browser connections close, so the second approval waits for it for up to four minutes, saying so, rather than stopping the sequence; waiting is local and has no tenant effect. No alternate unregistered port is silently used.
- The five-minute listener checks state, tenant, Host and request shape. Browser success is not permission evidence. Stop or timeout leaves **Check again** available to inspect actual grants.

Existing-ID repair is restricted to tenant-owned, single-tenant, delegated Graph tool apps without credentials, exposed roles or application-permission grants. It replaces only displayed supported registration fields, logo and enterprise-app properties. Preview and execution check object identity and drift. It never adopts an app by name or silently revokes existing consent; unexpected grants block readiness and need explicit administrative review.

## Evidence and limits

Plans are single-use and expire after five minutes. Intent is durably recorded before each request; accepted registration, logo, property and assignment writes are separately recorded. Configuration readback, consent and access checks remain distinct. Logo upload acceptance is recorded; portal replication is not independently verified. No write is automatically retried after an uncertain response.

Stop finishes the current registration sequence (five-minute action budget) and attempts after evidence (100-second budget). This is separate from policy deployment's stop behaviour. Unknown setup writes block further setup in the evidence root; preserve the journal and reconcile by exact IDs. There is no automatic app deletion, consent revocation, engineer-assignment removal or registration rollback. Closing setup clears local in-memory tokens, not persistent Entra consent or the operating system's signed-in account.

## Microsoft references checked 14 September 2026

- [WAM desktop integration](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam).
- [Administrator consent endpoint](https://learn.microsoft.com/en-us/entra/identity-platform/v2-admin-consent).
- [Application settings and logo update](https://learn.microsoft.com/en-us/graph/api/application-update?view=graph-rest-1.0).
- [Enterprise-app properties](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-update?view=graph-rest-1.0).
- [Engineer app assignment](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-post-approleassignedto?view=graph-rest-1.0).

Source and synthetic tests do not establish live WAM, bootstrap consent, the two-approval sequence, the local approval port's reuse between approvals, GDAP, grant propagation or portal branding acceptance. Test the complete flow in the authorised disposable tenant first.

## Registered report access — source implementation, live-unverified (PR #44)

Reports reuse the verified account, tenant, permission mode, Graph audience and existing token provider. They do not create another registration, acquire interactive consent, add application permissions or retry interactively. The assessment registration remains read-only; the deployment registration does not gain writes from a report. Existing consent and role restrictions still apply.

| Report | Delegated access checked | Impact and limits |
| --- | --- | --- |
| Users and assigned licences | Existing `User.Read.All` and `Organization.Read.All`, or documented higher `Directory.Read.All` | Reuses current inventory access, then reads `/users/{id}/licenseDetails` by exact identity. Microsoft lists `LicenseAssignment.Read.All` as least privilege for licence details/subscriptions; no new grant is introduced to replace the already authorised scopes. Supported directory/licence-read roles may be required. |
| Intune devices | Existing `DeviceManagementManagedDevices.Read.All` | Managed-device inventory only; tenant Intune licensing and account access are required. No device actions. |
| MFA registration | Proposed `AuditLog.Read.All` | A genuinely new delegated report scope if absent. Microsoft documents Reports Reader, Security Reader, Security Administrator or Global Reader. Registration does not establish enforcement or successful use; contact values and recovery codes are excluded. |
| Sign-in logs | Proposed `AuditLog.Read.All` | Supported reader/security roles and applicable Entra licensing/retention are required. The adapter does not collect Conditional Access policy details, which can require further permissions. No additional policy permission is requested. |
| Directory audit | Existing `Directory.Read.All` when present; otherwise proposed `AuditLog.Read.All` | Microsoft's endpoint documents Directory.Read.All as a higher permission. Supported report/security reader roles remain required. Directory audit is separate from Exchange unified audit. |

`AuditLog.Read.All` is **proposed access, not granted**. Before enabling it live, William must review the report purpose, delegated scope, affected assessment/deployment app name and application ID, current grants and consent change. The registration/setup scope declarations are unchanged by PR #44. Missing access produces **Not attempted** with a next step; existing authorised contexts are reused. No source development performs consent or live calls.

Log queries accept an explicit UTC range of no more than 31 days; this is a local read bound, not a retention guarantee. Each section is bounded to 5,000 rows and each run to five minutes. Missing, failed, cancelled, truncated and malformed reads retain their status; a successful empty response is distinct. Available retention remains unknown even after successful pagination. Licensing, roles, retention and Microsoft behaviour need William's separately approved live acceptance. These reports are separate evidence and cannot satisfy deployment before-evidence.

Official references inspected on 8 October 2026: [licence details](https://learn.microsoft.com/en-us/graph/api/user-list-licensedetails?view=graph-rest-1.0), [subscriptions](https://learn.microsoft.com/en-us/graph/api/subscribedsku-list?view=graph-rest-1.0), [managed devices](https://learn.microsoft.com/en-us/graph/api/intune-devices-manageddevice-list?view=graph-rest-1.0), [registration](https://learn.microsoft.com/en-us/graph/api/authenticationmethodsroot-list-userregistrationdetails?view=graph-rest-1.0), [sign-ins](https://learn.microsoft.com/en-us/graph/api/signin-list?view=graph-rest-1.0) and [directory audit](https://learn.microsoft.com/en-us/graph/api/directoryaudit-list?view=graph-rest-1.0).

## Application metadata report access — PR #87 engine slice

The application credential-expiry and observed-consent engine is implemented, live-unverified. Desktop/CLI report selection and schema-2 browser wiring are separate continuation work; this section does not describe buttons that are already available. William acknowledged reuse of the existing Application.Read.All permission for this tenant-wide metadata purpose. No permission is granted by source development.

| Read purpose | Actual verified report-context permission | Registration/consent impact |
|---|---|---|
| Application/service-principal credential-expiry metadata, limited-information owners and observed app-role assignments | Existing Application.Read.All; documented higher Directory.Read.All only when already present | Assessment/deployment report contexts reuse their actual authorised scopes and registration. No added permission or navigation sign-in; credential values, hints, certificate/key bytes and writes are excluded. |
| Observed delegated permission grants | Directory.Read.All, only if already present in that report context | It is not added/requested here. Without it, this section is NotAttempted with an explicit next step. Temporary setup bootstrap access does not authorise a report; any genuinely new read consent remains separately previewed for William. |

Observed grants differ from requested permissions and do not prove token contents, effective use or immediate replication. Owner names do not grant rotation authority. Collection reuses the guarded provider with exact tenant/account/scope pins and silent-only renewal; failures/limits stay explicit. Full requirements and bounds are in [the implementation note](https://github.com/Willzy12h/M365-Buildstandards/pull/87) and the reviewed [INT-094 contract](https://github.com/Willzy12h/M365-Buildstandards/blob/integration/docs/integration/ADDITIONAL-REPORT-CONTRACTS-2026.10.09.md). These repository links describe source, not a completed live or deployment flow.

## Returned Graph token capabilities

The native interface separates **Assessment mode — toolkit changes blocked** from **Token: read-only capabilities verified / broader permissions present / not fully classified**. No active connection shows **No live connection**. Classification uses an explicit supported Graph read list and the protocol scopes `openid`, `profile`, `offline_access` and `email`. Protocol-only, empty, blank, unknown-resource and unknown-permission observations are not verified read-only. Only the existing `https://graph.microsoft.com/` qualified representation is recognised; arbitrary resource prefixes and names containing “Read” or “Write” do not determine access.

A dedicated assessment connection rejects broader or unclassified returned capabilities. The documented shared Graph PowerShell fallback remains assessment-only with an explicit token warning; the guarded Graph client continues refusing every assessment write independently. An experimental tenant-change opt-in requires a verified deployment context and classified broader capabilities with no unknown scope; it still cannot grant permissions or replace exact operation approval. Registered Exchange Run has its own opt-in and resource/account authentication. Page navigation does not acquire a token.

This change requests no scopes and adds no service permissions or consent. Recognising existing report permissions (including `AuditLog.Read.All` and `Directory.Read.All`) does not grant or add them. Unknown capabilities remain visible and must be reviewed rather than guessed. Token classification alone proves neither a directory role nor that a collection succeeds.

Microsoft's [permissions reference](https://learn.microsoft.com/en-us/graph/permissions-reference#directoryaccessasuserall), consulted 12 October 2026, confirms that delegated `Directory.AccessAsUser.All`, [User.Invite.All](https://learn.microsoft.com/en-us/graph/permissions-reference#userinviteall) and [Mail.Send](https://learn.microsoft.com/en-us/graph/permissions-reference#mailsend) can confer broader authority despite lacking the old write spelling. This is a conservative scope classification, not a JWT validator or general permissions catalogue.

The same classifier also checks assessment permission requests before explicit connection acquisition and application registration preparation. Unsupported or broader proposed read scopes are refused before authentication/creation rather than being discovered only after consent. Existing shipped catalogue request sets are unchanged; the existing `WindowsUpdates.ReadWrite.All` deployment capability is explicitly classified as broader, not mistaken for an unknown scope. This recognition adds no permission, consent or Windows Update activation.

### INT-121 registered Exchange settings reports

The protocol/forwarding/hold reports add no Graph permissions and require no registration/consent update. They reuse separate Exchange Online authentication and approved installed module access. Recipient reads use the existing Exchange read roles; protocol reporting additionally needs permission for the read-only organisation transport configuration. Missing RBAC/module properties remain explicit unknown/partial reads. Do not grant broader roles automatically; the listed role requirements remain live-unverified. See [Exchange/Purview](EXCHANGE-PURVIEW.md#registered-mailbox-setting-reports-int-121).

### Controlled existing-user lifecycle purpose (INT-137; experimental, live-unverified)

Lifecycle preparation reuses returned `User.Read.All`, `Group.Read.All`, `Organization.Read.All` and `RoleManagement.Read.Directory` only for its registered dependency reads. A current and eligible directory-role read is required; unavailable eligibility or any role-assignable group blocks readiness. No `Directory.Read.All` or PIM-for-Groups permission is added as a fallback.

The selected action's approved development purpose is `GroupMember.ReadWrite.All` for one direct membership, `LicenseAssignment.ReadWrite.All` for one direct SKU, `User.EnableDisableAccount.All` with `User.Read.All` for accountEnabled, or `User.RevokeSessions.All` for one session-revocation request. These delegated scopes are write-capable. They belong only to deliberately selected deployment access, not the assessment registration or a reporting-only token. Microsoft may also require corresponding administrator RBAC. A missing returned scope blocks preparation; source development neither grants consent nor establishes live RBAC.

Client and connection and Application setup expose a separate, default-off existing-user action selector. Assessment requests only its fixed readiness reads; deployment requests its one write purpose alongside the required baseline reads, without unrelated standard-policy write scopes. Quick Connect remains initial read-only discovery. Review access transfers the selected purpose to the connection form without signing in or granting anything. The explicit connection/setup preview names the selected registration, exact access and purpose. Previously configured approved purposes are retained in a deployment registration rather than silently revoked; an assessment registration never retains write scopes. Reusing a current verified connection requires the same exact requested scope set, account, tenant, registration and permission mode. Group add/remove and account enable/disable may reuse an equivalent current context; a different write purpose requires deliberate access review. The native Existing-user actions page delegates readiness, preview, single-use approval, durable evidence, dispatch and reconciliation to shared services. See [the operator guide](OPERATOR-START.md#existing-user-actions-experimental). No scripts, account creation, passwords, purchases, Exchange changes or additional permission purposes are included. Named execution/authentication/evidence acceptance and separately approved live consent/actions remain outstanding.

### Optional usage-report access (INT-129/133)

Connect and Application setup offer one fixed, default-off usage-report choice. It adds only delegated `Reports.Read.All` to the selected existing assessment/deployment registration's reviewed permission list; historical standards remain unchanged. Purpose: the four registered Office365/email/Teams/OneDrive usage CSV reads, not report privacy settings, licence changes, directory joins or application-only access. Review the affected application names/IDs, current and proposed permissions and consent impact before any separately approved setup/consent action. Reporting roles/workload licences can still refuse access.

The access intent is frozen for the explicit Connect/Quick Connect/setup preview. Changing it invalidates reuse or setup approval; actual returned access is checked independently. Selecting off requests baseline access and does not silently remove previously configured usage permissions or grants. No report page silently reconnects, adds consent or falls back to a different app. Existing broker, protected cache, cancellation and account/tenant/mode separation remain. Source development does not grant permissions or perform setup writes.

## Optional operational read purposes — experimental G development

Service-health context uses only **ServiceHealth.Read.All** for the fixed v1.0 health-overview/issue GETs. Partner relationship context uses only **DelegatedAdminRelationship.Read.All** in the deliberately connected partner source tenant for relationships and their admitted assignment reads. These are the exact read purposes William approved on18October; source implementation grants no permission and performs no tenant read. They do not establish customer access or entitlement. Live reporting role/API behaviour and named runtime acceptance are unverified.

The existing Connect and application-setup controls expose separate default-off choices alongside optional usage access. Only chosen purposes enter the existing requested-scope/preview flow; no generic scope entry or new cache is added. Changes invalidate retained discovery/reuse/setup validation; actual returned scopes and exact tenant/account/app/mode binding remain independently required. Navigation does not authenticate or consent. Mode and read purpose are operation boundaries, not proof that a returned token has no additional rights.

Changing purpose or selecting off does not revoke existing optional configuration/consent. Preview preserves already configured approved usage/health/partner read definitions. Disabled, ambiguous or unreturned configured IDs block preview rather than being silently removed. Unconfigured extra grants still require explicit review; no automatic privilege fallback, higher service permission or customer-account switch is offered. Application and enterprise-app IDs, exact permission payload, consent impact and final setup approval remain visible and guarded through the existing workflow.

This wires the native service-health journey in source, with Windows/native/package validation pending. Partner collection and the whole expansion remain incomplete. No MessageCenter.Read.All, ReportSettings.Read.All or new Directory.Read.All request is added to assessment/deployment. Existing privileged setup authorisation remains its separate established registration-only boundary.

## Collaboration inventory purpose — E/INT-135 source foundation

The fixed optional purposes `CollaborationSites` and `CollaborationTeams` add only **Sites.Read.All** and **TeamMember.Read.All**, respectively, to the existing explicit connection/setup scope calculation. Both remain off by default. **Sites.Read.All permits content access**, although the approved adapter is limited to metadata; this warning must remain visible in the native consent-purpose preview. Existing **Group.Read.All** is reused for selected group metadata/direct owners. No Directory.Read.All or Sites.FullControl.All fallback is added. Recognising returned scopes does not grant them, prove current roles or authorise live reads.

Existing configured approved optional definitions are preserved when another purpose is selected; unavailable definitions still block a preview rather than silently stripping access. This is the shared scope/registry foundation only: the E native choices, collector and evidence surfaces are not yet complete. Do not infer usability or consent from these definitions. Current exact-source Windows evidence and named authentication/Graph/evidence acceptance remain required.

### Security incident context (J / INT-138, source development)

The fixed read transport uses the separately approved `SecurityIncident.Read.All` reporting purpose for `GET /security/incidents`. No write/response scope, application-only fallback or `Directory.Read.All` is added. Connect and Application setup expose an explicit, default-off incident read choice through the existing optional-access flow. No existing connection is silently upgraded and no permission is granted by this code. Review this purpose for the selected assessment/shared registration; Microsoft's applicable consent remains separate.

Microsoft also requires a suitable delegated role (Security Reader, Global Reader, Security Operator, Security Administrator, or a suitable custom role) and available licensed/connected Defender workloads. Returned scope alone does not prove RBAC or coverage. Business Premium/P1 does not imply P2 Identity Protection access. Unavailable/licence-limited reads must remain unable to check. The bounded transport requests an exact reviewed UTC created-date interval, with no alert expansion, response action, automatic portal navigation or content download. Named Graph/access/evidence acceptance and live validation remain outstanding.

INT-138 incident context implementation now registers `SecurityIncident.Read.All` as an optional, default-off read purpose in the existing connection/setup policy and exact token classifier. No consent is granted by this source change. Connect and Application setup expose the optional incident selection; it starts off and reuses the existing deliberate preview/reconnect flow. Existing connection/setup planning retains deliberately granted optional scopes; deselecting a purpose does not revoke previous consent. Security Reader, Global Reader, Security Operator or Security Administrator (or suitable custom RBAC) and the relevant connected/licensed workloads remain necessary; consent alone does not prove access. No write scope, application-only fallback or Directory.Read.All is added.


### Domain-readiness access

The experimental Domain readiness page adds **no Microsoft Graph or Exchange permission**, consent or authentication. An explicit Run asks the workstation's configured DNS resolver for the reviewed MX/TXT/CNAME names through the fixed Windows PowerShell/DnsClient helper. It never changes DNS, installs a module or changes execution/application-control policy. Existing verified connection identity may be recorded as the initiating context; it does not prove ownership of the domain. Offline profiles do not create a verified binding. Historical reports retain their original unverified claims. The separate report cannot satisfy tenant deployment before-evidence. Actual public DNS checks still need the applicable owner authorisation; development uses synthetic responses only.

### INT-127 hypothetical Conditional Access evaluation — implementation and acceptance

The dedicated evaluator reuses the current verified Graph account and returned Policy.Read.All; it requests no new scope, sign-in or consent and cannot switch permission mode. Its one fixed POST is Microsoft's read-only `/identity/conditionalAccess/evaluate` action, not a generic assessment-write exception. Its owned transport refuses redirects/cookies/default credentials and has a120-second total acquisition/renewal/read budget. Only one silent renewal after401 is allowed; no timeout/throttling/server-error retry. Existing generic POST/PATCH/DELETE guards remain.

This evaluates the supplied hypothetical conditions against enabled/report-only policies. It neither evaluates disabled candidates nor proves actual sign-in, MFA or enforcement, and does not expand service dependencies. Unknown/invalid/incomplete responses remain explicit. Native CA selection, original evidence/recovery and shared exports are implemented in #147 and have synthetic Windows validation at99434f6/39383074250. Connected selected-control coverage remains in progress; neither source implementation nor synthetic validation is live acceptance. Microsoft role/licence access can still refuse the read.

The in-progress CA scenario adapter also reuses existing `User.Read.All` for a fresh exact-ID member lookup (`/users`, fixed selected metadata, top2). It refuses guest/external/ambiguous or incomplete user origin metadata rather than replacing the selected user. `Policy.Read.All` remains required for evaluation; this source change requests no consent and adds no `Directory.Read.All` fallback. The shared 120-second budget includes this lookup. The native CA workflow is available in the implementation branch; named safety acceptance and live Microsoft behaviour remain pending.


### INT-127/130 modern Intune reports — existing broader scopes, reporting purpose only

The two fixed modern export requests use the existing delegated `DeviceManagementApps.ReadWrite.All` (selected app install-status export) and `DeviceManagementConfiguration.ReadWrite.All` (selected classic configuration status export). Microsoft requires these write-capable scopes for `/deviceManagement/reports/exportJobs`, even though these fixed operations produce reports. William acknowledged this reporting-only purpose in the20October contract acceptance; no new scope, registration or consent is granted by developing it. `Directory.Read.All` is not added.

The dedicated transport requires deliberately selected verified **deployment access**, the appropriate existing scope and an explicit broader-token reporting acknowledgement before token acquisition. Assessment mode refuses job creation, and there is no silent permission upgrade or generic write exception. Keep the write-capable access warning visible. Missing consent/RBAC/licensing remains unavailable, with no automatic request or fallback. Actual sign-in, consent and tenant reads remain separately approved.

One fixed CSV request can create one job, with at most one silent401 renewal across its Graph requests; no throttling/server-error/timeout replay. Opaque job IDs are bound to the report/filter/raw column set. Status polls wait at least10seconds (honouring a larger Retry-After) within the same five-minute budget. A timed-out/stopped create may leave a **report job** running; this is not evidence of a configuration write and never authorises automatic recreation. Completed-job download uses a separate token-free/cookie-free/default-credential-free client restricted to the approved public Blob host shape; redirects and authenticated fallback are refused. Signed URLs and response headers are transient and excluded from evidence/logs.

The fixed transport, strict CSV/archive admission and offline original coverage path are source implementations under #147, not completed connected native integration or named/live acceptance. Existing assessment/deployment guards, actual tenant permissions and manual fallback retain their meaning.
