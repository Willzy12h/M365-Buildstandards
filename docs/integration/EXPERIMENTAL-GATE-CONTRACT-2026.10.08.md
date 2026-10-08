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

## Required proof before merge of dependent source

- Default refusal through command and direct desktop execution paths, even when a write-capable connection and otherwise reviewable plan exist.
- Exact-context opt-in permits reaching the existing review boundary; it never substitutes for final approval or complete before-evidence.
- Tenant/account/client/mode/scope/catalogue/client-scope changes and reconnect/restart invalidate opt-in. Unconnected, historical and assessment contexts cannot grant it.
- Native renders at all three harness sizes retain readable gate/next-step text, accessible names and keyboard reach, with zero tenant calls.
- Existing safety suites remain unchanged and pass. Microsoft service, human accessibility and actual acceptance remain William's gates.

## Decision gate

This PR settles the cross-surface coordination contract only. Dependent implementation starts after independent review, exact-head checks and merge under HANDOVER's delegation. No permission grant, tenant action, version bump, promotion or publication occurs here. New read permissions remain subject to APPLICATION-SETUP and William's separate live consent review.
