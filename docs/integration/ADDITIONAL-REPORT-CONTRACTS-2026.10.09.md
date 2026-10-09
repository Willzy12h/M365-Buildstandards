# Additional report contracts and integration plan — proposed, 9 October 2026

Decision PR #69, INT-094. William authorised additional reliability, optimisation and reporting work. This document proposes the shared registrations before dependent implementation. Base: integration `59f1d02`, after Claude's #66 merge. The library has 29 Copy-only Exchange items. This decision contains no collector, new registered row, live read, permission grant or script execution.

## First delivery and dependencies

| Slice | Reviewable result | Dependency / next gate |
|---|---|---|
| #65 | Repeatable scale measurement and four correctness tests, existing caps unchanged | Independent review, current-head Windows checks; use measurements to select a later optimisation |
| #67 | Five synthetic actual-transport interruption cases | Independent review and current-head Windows checks |
| #68 | Eight end-to-end export fidelity/integrity cases | Independent review and current-head Windows checks |
| This decision | Application-expiry/consent contracts below | Merge this decision before registry/row/collector changes |
| Offline doctor | Proposed `bdit doctor`: package integrity, writable folders and local prerequisite findings | #53 readiness and #62 CLI claims settle first; reuse their services, no authentication/tenant/DNS reads; deterministic success/warning/failure exit contract reviewed before implementation |
| Servicing review | Resolved dependencies, runtime/MSAL/module compatibility and EOL references | Reuse RELEASE-AND-SERVICING and existing package inventory; named support/security owners remain human gates |
| Script maintenance | Extend existing parser/manifest/output/negative-case checks | Coordinate with Claude after its script-library slices settle; no competing library or runner |

These are successive small PRs. They do not hold an earlier preview milestone open for later features. #55 experimental gates and #61 read-only runner remain separate decisions; this report work does not create an Exchange runner or write route.

## Application credential-expiry report

Stable proposed ID: `application-expiry`. Read `/applications` and `/servicePrincipals`, using bounded pagination and a documented `$select` of application/object identity, display name, app ID, passwordCredentials and keyCredentials. Optional `/applications/{id}/owners` and `/servicePrincipals/{id}/owners` are independently bounded reads; missing owner names/limited-information objects remain explicitly unresolved. Ownership never grants access or proves that an engineer may rotate a credential.

One row per directory object, with exact canonical object ID, object kind (application/servicePrincipal), application/client ID, readable name, credential-read state, owner-read state and credential metadata entries. Application registration and enterprise application are distinct objects even when app IDs match; do not merge them by name or duplicate-count a single directory object.

Each credential entry has kind, key ID, display name, type/usage where applicable, start/end dates as reported and a classification derived at the fixed collection end time. Valid future/past intervals distinguish not-yet-valid, expiring-within-the-selected-horizon and expired. Missing/malformed dates/IDs are Unknown with a reason, never "no expiry" or an absent credential. The typed horizon is visible and pinned in report parameters; shared parameter schema changes are additive only for this new report and must not relax existing log-date validation. A returned empty list is distinct from a missing/unreadable list. Matching a key ID never establishes possession or trust.

Whitelist metadata only. Never persist/export/log secretText, hint, password values, certificate/key bytes, customKeyIdentifier, access tokens or raw Graph response objects. No creation, rotation, renewal, deletion or upload is offered. No notification is sent to an owner automatically.

## Application consent report

Stable proposed ID: `application-consent`. Read bounded `/servicePrincipals`, `/oauth2PermissionGrants` and `/servicePrincipals/{id}/appRoleAssignments`. These are observed delegated grants and observed application-role assignments, not an application's declared requested permissions. An assignment does not establish successful use or effective access to every resource.

Keep separate sections for principals, delegated grants and application-role assignments. Preserve exact grant/assignment IDs, client/principal service-principal IDs, resource service-principal IDs, delegated scope text, consent type, subject ID where returned and app-role ID. Resolve role names only through the exact resource object ID and exact role ID in its returned appRoles; names alone never join records. Zero/unknown/ambiguous identities remain explicit unresolved/partial records rather than a guessed principal or an empty-success result. Delegated scope text is observed text; it is not proof of present token contents or permission use.

Grant/assignment row identities follow the actual endpoint's documented ID format, not an assumption that every assignment ID is a GUID. Register those row rules explicitly and do not relax existing GUID-identity reports. Requested API permissions may be added as a separate labelled section later; never label them Granted. Risk wording is factual (for example an observed write-scope grant), not an unsupported automatic verdict that an app is malicious or should be removed. Revocation, consent and app deletion remain absent.

## Shared evidence and access rules

Extend the existing package-owned Graph report registry with these two new IDs and typed row/nested metadata schemas only after this decision merges. Keep schema 1 historical report bytes and meanings intact. Give the new adapter its own recorded version; do not rewrite old records or broaden old row contracts. Reuse strict parsing, integrity, exact tenant/account pinning, bounded reads, cancellation, immutable report storage and existing exports. No raw API rows, second authentication cache or interactive mid-operation retry.

Every source has an access result and collection interval. Mixed success, missing access, cancelled pagination, malformed rows and cap saturation remain Partial/Failed/Cancelled/NotAttempted as appropriate. Existing 5,000-row section, 32 MiB and five-minute bounds remain; child lists and per-object read fan-out receive explicit limits, truncation reasons and tests before implementation. Reports never become complete deployment before-evidence. Never import a truncated report as a full configuration capture.

Official Graph documentation checked on 9 October (HTTP 200, public documentation only):

| Read | Delegated permission | Current effect |
|---|---|---|
| [Applications](https://learn.microsoft.com/en-us/graph/api/application-list?view=graph-rest-1.0), [application owners](https://learn.microsoft.com/en-us/graph/api/application-list-owners?view=graph-rest-1.0), [service principals](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-list?view=graph-rest-1.0), [app-role assignments](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-list-approleassignments?view=graph-rest-1.0) | `Application.Read.All`; documented higher `Directory.Read.All` accepted only on reviewed routes | Application.Read.All is already documented for assessment/deployment; reuse it when actually authorised. Supported Entra roles and owner limited-information behaviour remain live-unverified |
| [Delegated grants](https://learn.microsoft.com/en-us/graph/api/oauth2permissiongrant-list?view=graph-rest-1.0) | `Directory.Read.All` | Use only if present in the verified assessment/report context. The temporary setup bootstrap's access does not authorise a report. If absent, this section is NotAttempted with an access reason; any new read-consent request is separately previewed for William |

No write permission is requested for these reports. Document registrations, reasons, routes and consent impact in APPLICATION-SETUP before implementation readiness; source development grants nothing. Role listings and Microsoft returned-property behaviour require William's live acceptance.

## Acceptance and integration

Synthetic tests must cover exact identities and same names, separate application/SP objects, known/unknown/empty credential lists, dates and fixed-clock horizon boundaries, secret-bearing input that never reaches evidence/logs/exports, owner limited information, missing permissions, partial pages, every list bound, cancellation, hostile text, immutable import/export and refusal as deployment evidence. Desktop/CLI use shared engine collectors after their current surface claims settle. New items remain experimental/manual until applicable live acceptance is recorded.

Claude reviews #65, #67, #68 and this decision independently. Refresh each branch from the final integration base, preserve both ledger histories, rerun exact-head required checks, merge with merge commits under HANDOVER and record each actual merge SHA/run. The decision merges before dependent report source. No automatic merge is scheduled merely because Claude has finished another PR. New live consent, tenant actions, promotion, tags and publication keep William's separate approval gates.
