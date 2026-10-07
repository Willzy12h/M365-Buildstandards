# Application setup

## Quick Connect and the next step

For a client's own account, select **Quick Connect with Microsoft** on Connect. Windows sign-in discovers the organisation, domain, tenant ID and account. Review the displayed identity, then select **Connect to this tenant read-only**. The initial shared-app sign-in requests assessment read permissions and is retained in memory for five minutes. Confirmation rechecks the same tenant/account without another interactive sign-in. A configured different dedicated application needs its own token, so Microsoft may require app-specific consent or authentication. It does not copy another saved client's policy inputs, save a profile automatically, or enable deployment. An installation that disables the shared assessment fallback must use its dedicated application details instead.

**Partner / GDAP sign-in** uses an explicitly selected customer: choose its saved connection or enter its label and tenant ID, then use the separate partner button. Choose the partner account in Microsoft's account chooser. Existing GDAP, roles, assignment rules and application consent are still enforced by Microsoft. This is not automatic customer discovery or a promise of access to every endpoint.

Navigating pages does not authenticate. An unchanged verified connection can be reused; an explicit reconnect first tries a matching cached account. Different applications/modes and Microsoft's MFA or consent requirements can still require interaction. Ordinary reads renew silently and stop with reconnect guidance when Microsoft requires interaction mid-operation.

**No deployment application configured** means the current connection has no dedicated deployment client ID. Select **Configure deployment application** on Deploy to open setup for that client. If the application already exists, enter its Application (client) ID; do not recreate it or substitute an assessment ID. After configuration, consent and assignment checks, **Continue to deployment** requests the separate write-capable identity. Capture and plan again before policy deployment.

### Identify, approve and use an existing enterprise application

Default names are **M365 BuildStandard Assessment Tool** and **M365 BuildStandard Deployment Tool** (the name prefix is configurable). The page shows application IDs, observed creation names or clearly labelled configured names, and verified enterprise-app object IDs when available. Copy the identities and next steps for a local ticket.

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

Two registrations preserve a token-level read-only boundary: **M365 BuildStandard Assessment Tool** has read scopes; **M365 BuildStandard Deployment Tool** also has the required write scopes. The normal client never grants its own access. Both are delegated, single-tenant public clients, with assignment required and no secrets. Admin consent does not itself prove directory roles, Intune RBAC, licences or successful deployment.

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
