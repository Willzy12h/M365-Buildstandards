# Application setup

1. Enter the client label and tenant ID on Connect. Select **Set up or validate applications**. If deployment has no configured app, **Connect for deployment** opens the same wizard.
2. Authorise the temporary setup sign-in. Windows Web Account Manager (WAM) opens Microsoft's own sign-in window; select the browser fallback if needed. Setup reuses a connected setup session for the same tenant.
3. Leave both IDs blank for new registrations, or enter the exact IDs of existing dedicated tool registrations to repair them. Preview the full name, permissions and payload for each. Optionally include assignment of the current setup engineer to both enterprise apps; this is an application assignment, not a directory role.
4. Review and approve setup, then type the tenant ID. The wizard saves before evidence and each write outcome. It creates or configures the reviewed registrations, registers sign-in/consent redirects, uploads the original tool icon, sets GitHub URLs and applies the approved engineer assignment. Client IDs fill automatically.
5. Approve **assessment consent** and **deployment consent** separately on Microsoft's page. These grant the listed operational scopes, not the temporary setup scopes. The UI then reads actual grants and displays required, configured, granted and missing counts. Revalidate after propagation delays.
6. Choose **Continue read-only** or **Continue to deployment** when ready. The selected application's sign-in and read-only access checks follow directly. Microsoft may reuse Windows sign-in, but app-specific token acquisition and any required consent/MFA remain necessary.

Two registrations preserve a token-level read-only boundary: **M365 BuildStandard Assessment Tool** has read scopes; **M365 BuildStandard Deployment Tool** also has the required write scopes. The normal client never grants its own access. Both are delegated, single-tenant public clients, with assignment required and no secrets. Admin consent does not itself prove directory roles, Intune RBAC, licences or successful deployment.

## Setup permissions and delegated administrators

The temporary Microsoft Graph Command Line Tools sign-in requests `User.Read`, `Directory.Read.All`, `Application.ReadWrite.All` and `AppRoleAssignment.ReadWrite.All`. The last permission is used only for the explicitly approved current-engineer default app assignment. The toolkit never grants directory roles, writes consent grants directly or stores credentials. Microsoft may display its own first-party branding for that bootstrap identity; the two tool apps use the custom icon and project URLs.

Use an identity with appropriate rights in the target tenant. For partner/GDAP or group access, direct assignment may not be visible. After configuration and consent are verified, **Check delegated-admin deployment access** permits an actual target-tenant sign-in and read-only access check. It does not bypass Microsoft's assignment/role enforcement or promise GDAP compatibility for every endpoint. Tenant and returned operator identity are still verified. Group expansion, PIM activation, custom roles and partner-specific endpoint behaviour require live testing.

## Redirects and existing-app repair

- Desktop sign-in: WAM uses `ms-appx-web://microsoft.aad.brokerplugin/{client-id}`; browser fallback uses the native `http://localhost` registration.
- Administrator consent: exact web redirect `http://localhost:8400/m365-consent/`. It is separate from the native redirect; the code no longer assumes an ephemeral-port exemption for admin consent.
- The wizard verifies this registered callback before opening consent. An older registration must be explicitly previewed and repaired first. If port 8400 is occupied, close the other consent window. No alternate unregistered port is silently used.
- The five-minute listener checks state, tenant, Host and request shape. Browser success is not permission evidence. Stop or timeout leaves **Validate setup** available to inspect actual grants.

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

Source and synthetic tests do not establish live WAM, bootstrap consent, GDAP, grant propagation or portal branding acceptance. Test the complete flow in the authorised disposable tenant first.
