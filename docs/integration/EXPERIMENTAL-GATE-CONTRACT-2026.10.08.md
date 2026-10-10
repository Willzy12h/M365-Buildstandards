# Experimental tenant-change gate — proposed INT-082

Decision-only Astra slice, branched from integration `b755dbb3b8e1b958c12825ec296fee4e319e9ae4` on 8 October 2026 after fresh coordination pre-flight. William's current goal requires unaccepted capabilities to be gated in the app, not merely described in a document. Existing source has exact operation approval and write-capable-session checks, but no explicit experimental opt-in shared by all desktop tenant-change surfaces.

## Proposed contract

1. Keep read-only discovery, collection, assessments, scoped checks, reports and plan/recovery previews available under existing access checks. Label service behaviour live-unverified; a failed read never becomes empty success.
2. Unaccepted tenant changes are **experimental and off by default**. Provide one clear, unticked, session-scoped opt-in for an authorised acceptance/pilot context. The engineer sees the verified tenant/account, purpose and next step. Changing pages or reviewing a plan adds no authentication prompt.
3. Bind opt-in to the actual verified deployment connection, tenant, account object ID, client application, permission mode/scopes, current catalogue digest and reviewed client scope. A reconnect, disconnect, context/profile/standard change, sign-out or restart removes it. Do not persist it or include it in exports, backups, support bundles or jobs. An expired opt-in cannot be restored by silently reconnecting a new context.
4. Enforce the gate both in command availability and at the desktop execution boundary, including direct calls: candidate deployment, reviewed activation/assignment/retirement actions, Entra LAPS, package publication and recovery. Explain every disabled action and how to proceed. Gate closure does not interrupt an already sent write; existing stop/reconciliation rules still apply.
5. Isolated application setup/consent keeps its own identity/resource and exact permission preview. Its consequential create/repair and consent actions are manual/experimental, require visible deliberate approval in that verified bootstrap context, and cannot borrow a deployment-session opt-in. Validation and application previews stay read-only. Existing unticked exact setup approval remains required.
6. This opt-in permits review/testing only; it does not approve an exact operation or prove production support. Preserve every complete before-evidence, final exact approval, single-use plan, unresolved-write, tenant/account and after-readback safeguard. Retain CA disabled targeting/exclusions, Intune unassigned state, no write replay and immutable historical evidence/catalogues.
7. Keep the gate in the desktop coordination layer. Collection, assessment and mutation logic stays in the existing engine. No historical evidence, profile, token cache, manifest or engine request schema changes; offline CLI stays read-only. Exchange changes remain copy-only, with no Run or alternative write host.

## Review refinement — 9 October 2026

Claude reviewed `6534c49` and identified CLA-20261008-20–24. The following requirements refine the proposal before dependent implementation; they do not claim runtime enforcement already exists.

### Permission acquisition is a deliberate boundary

Interactive explicit-scope requests can initiate consent. Enumerate deployment-mode sign-in, Quick Connect, setup/bootstrap sign-in, create/repair followed by consent, and each standalone consent command. Before any request that can acquire new write/setup authority or grant consent, show the exact registration/application ID, resource, requested scopes, purpose and known tenant/account; require the applicable deliberate permission-request approval. An existing read session can be reused to verify identity first where authorised. If Microsoft must select the tenant/account, label them unverified until the returned identity is validated; that pre-sign-in approval permits the displayed request only and never permits a tenant operation. Do not request write scopes automatically on a page change, assessment sign-in or reconnect.

Read-only sign-in remains available without the tenant-write opt-in. It discloses its read scopes and possible Microsoft consent boundary; it cannot request bootstrap or deployment write scopes. New consent/grant changes require the separate exact permission preview and approval, even if the resource request is read-only. Prefer existing authorised/silent acquisition, but do not claim the token cache proves current grants or suppress Microsoft CA/MFA/consent. A cancelled/changed request invalidates its approval. The final verified-context experimental opt-in is granted only after actual connection verification; it does not inherit approval from a pre-sign-in request.

### One guard and explicit invalidation

Use one desktop `ExperimentalOperationGuard` for command availability and direct coordination/service entry points. Its immutable authorisation snapshot binds verified tenant/account object ID, deployment client ID, resource/mode, exact scope digest, catalogue digest and the canonical `ReviewedClientScope` digest (INT-056). Bind the validated client profile values through that canonical digest; do not use display names or an undefined second digest as authority. Operation inputs still require their existing exact review. An entry-point enumeration test covers Workspace deployment, reviewed changes, LAPS, application package publication, recovery, explicit scope/consent acquisition and isolated application setup routes, including view models that call services directly. Read-only previews and local exports are excluded explicitly, not by an assumed deployment-page route.

Retain the existing typed tenant-ID confirmation, complete before-evidence, exact approval, single-use plan, unresolved-write blocking, per-tenant write lease and after-readback. The guard adds no consent/grant, bypass or replay. A closed guard cannot unsend an accepted write; use existing Stop/reconciliation behaviour.

Reconnect, sign-out/disconnect, session/profile/catalogue/client-scope change, expiry and actual scope drift during silent renewal invalidate the snapshot. Revalidate the current authorisation at every operation boundary; a token refresh is not a new opt-in. A setup session is not a deployment session and cannot share its authorisation. Setup/registration changes or consent affecting that deployment registration invalidate any coexisting deployment opt-in, even when the deployment connection itself was not replaced.

### Setup and consent approvals are explicit

Isolated bootstrap approval is bound to its own verified identity/resource and exact app IDs, grants and proposed operations. Create/repair and subsequent consent are separate consequential steps. The app may offer one reviewed sequence only if its preview and unticked approval enumerate both steps and exact requested permissions; a create-only approval cannot silently approve consent. Standalone assessment/deployment consent commands require the same visible permission preview and deliberate approval. Success of read validation, an existing enterprise app, or a setup connection never implies permission to grant access or deploy changes.

Required regressions include refusal before any unauthorised interactive write-scope request, standalone/chained consent coverage, coexisting setup/deployment invalidation, renewal scope drift, exact client-profile digest changes and every direct entry point. Native checks retain readable purpose/next-step wording without claiming universal zero prompts. The own claim is rendered with a table header. Historical schemas, existing authorisation and blocked Exchange writes remain unchanged.

## Required proof before merge of dependent source

- Default refusal through command and direct desktop execution paths, even when a write-capable connection and otherwise reviewable plan exist.
- Exact-context opt-in permits reaching the existing review boundary; it never substitutes for final approval or complete before-evidence.
- Tenant/account/client/mode/scope/catalogue/client-scope changes and reconnect/restart invalidate opt-in. Unconnected, historical and assessment contexts cannot grant it.
- Native renders at all three harness sizes retain readable gate/next-step text, accessible names and keyboard reach, with zero tenant calls.
- Existing safety suites remain unchanged and pass. Microsoft service, human accessibility and actual acceptance remain William's gates.

## Decision gate

This PR settles the cross-surface coordination contract only. Dependent implementation starts after independent review, exact-head checks and merge under HANDOVER's delegation. No permission grant, tenant action, version bump, promotion or publication occurs here. New read permissions remain subject to APPLICATION-SETUP and William's separate live consent review.
