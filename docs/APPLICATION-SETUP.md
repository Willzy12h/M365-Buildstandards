# Application setup

The wizard provisions the toolkit's own delegated assessment and deployment identities. It does not deploy arbitrary enterprise applications, create secrets, grant directory roles or implement app-only authentication.

1. On Connect, enter the tenant ID and choose **Set up tenant applications**. Alternatively open Application setup from the workflow rail.
2. Review the separate privileged sign-in. It requests `User.Read`, `Application.ReadWrite.All` and `Directory.Read.All` through Microsoft Graph Command Line Tools. Use a suitably authorised application administrator. Creating a service principal and granting consent require appropriate Entra roles as well as OAuth permissions; these are separate checks.
3. Preview both registrations. Permission names and IDs are resolved from the tenant's Graph service principal and the loaded standard. Review both rows, descriptions and exact payload. A name collision blocks creation of that row; enter an existing client ID explicitly for validation.
4. Approve the listed permissions and type the tenant ID. The five-minute, single-use plan is bound to tenant, operator and standard. Before evidence is written and re-read before any request. Both applications are single-tenant public clients with engineer assignment required. Creation requests permissions; it does not itself grant consent.
5. Review Microsoft's administrator-consent screen for each app, then assign authorised engineers in Entra. A temporary loopback listener presents a local completion page and verifies the response state and tenant. The browser response is not proof of granted permissions: the toolkit then reads actual configuration and grants. Select **Validate setup** again after any propagation delay. The listener expires after five minutes and requires no local administrator rights.
6. Validation separates exact configured scopes, tenant-wide consent, direct current-engineer assignment and effective access. Group-based assignment is not confirmed by this check. Use the IDs on Connect, then sign in with the application and run the access checks. Roles, licensing, PIM, group membership, Intune RBAC and successful writes are not proven by a consent grant.

Closing the setup session removes its in-memory tokens; it does not revoke consent already granted in Entra. Tenant configuration and setup evidence remain local. Do not commit these records to Git.

Stop waits for the current application/enterprise-application pair and after evidence before ending. If a write outcome is unknown, creation is blocked until manual reconciliation. Do not delete evidence to force a retry. Use the recorded client/object IDs and timestamps to inspect Entra; explicit-ID validation is read-only. There is no automatic deletion, rollback or repair of existing registrations.

## Microsoft references checked 14 September 2026

- [Create application](https://learn.microsoft.com/en-us/graph/api/application-post-applications?view=graph-rest-1.0) and [application permission configuration](https://learn.microsoft.com/en-us/graph/tutorial-applications-basics).
- [Create service principal and supported administrator roles](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-post-serviceprincipals?view=graph-rest-1.0).
- [Read delegated consent grants](https://learn.microsoft.com/en-us/graph/api/oauth2permissiongrant-list?view=graph-rest-1.0).
- [Administrator consent endpoint](https://learn.microsoft.com/en-us/entra/identity-platform/v2-admin-consent).
- [Engineer assignments](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-list-approleassignedto?view=graph-rest-1.0) and [application permission assignments](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-list-approleassignments?view=graph-rest-1.0).
- [Microsoft first-party application identity](https://learn.microsoft.com/en-us/troubleshoot/entra/entra-id/governance/verify-first-party-apps-sign-in).

Live bootstrap sign-in, consent propagation, creation and engineer access remain unverified until authorised tenant testing.
