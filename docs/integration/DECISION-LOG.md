# Integration decisions

| ID | Date | Decision | Reason / authority | Status |
|---|---|---|---|---|
| INT-001 | Original | Preserve source repositories; develop shared product in master through reviewed PRs | Existing accepted decision and current user instruction | Accepted |
| INT-002 | 2026-09-14 | Claude C#/.NET 337c8e6 is primary; Asta 020c69b is a selective reference | Explicit current user direction; evidence in comparison. Replaces the pending proposal on Claude's unmerged coordination branch | Accepted |
| INT-003 | 2026-09-14 | First implementation imports baseline, recovers reports and repairs build/package with focused tests | Restore a reviewable baseline before expanding functionality | Accepted |
| INT-004 | 2026-09-14 | Preserve CA disabled state and stored targeting/exclusions in this import | Targeting and enforcement differ. Historical "unassigned" semantics must be agreed before a targeting change or live test | Accepted for baseline preservation; final rollout semantics unresolved |
| INT-005 | 2026-09-14 | Complete durable evidence, dependency checks, plan binding, ownership/drift checks and truthful terminal states remain requirements | Current user safety requirements override incomplete implementation; see testing evidence and handover | Implemented with synthetic regression tests; live validation pending |
| INT-006 | 2026-09-14 | Defer app-only, cloud browser UI and extra workloads; app provisioning now handled by INT-007 | Current user explicitly requested integrated enterprise application setup | Partially superseded |
| INT-007 | 2026-09-14 | Add isolated delegated setup for two single-tenant public-client applications and enterprise apps; explicit creation preview, separate browser consent and Graph validation | Current user instruction; toolkit app setup is the stated assumption. No app-only credentials or automatic directory-role assignment | Implemented; live validation pending |
| INT-008 | 2026-09-14 | Resolve and explicitly select tenant-bound exclusions with purpose/reason; never infer an exemption from admin-style names | Current user account-lookup request and existing emergency/creator safeguards | Implemented; synthetic tests |
| INT-009 | 2026-09-14 | One-time connection is default; saving a profile is optional, but local audit evidence remains durable | Selective Asta workflow integration | Implemented |
| INT-010 | 2026-09-14 | Green means verified/pass; amber marks write access or warnings; blue indicates collection/planning; grey means unknown/pending | Current user requested coherent design for engineering decisions | Implemented; see DESIGN-SYSTEM |

## Recovery and licensing decisions — 14 September 2026

- **INT-011:** user-approved selective recovery for recorded policy creations and supported inactive updates, plus state-only CA disablement. Durable ownership and before/after evidence, fresh preview, typed tenant and explicit drift review are mandatory. Unknown outcomes, assigned Intune objects and unsupported cleanup remain blocked or manual. Implemented with synthetic tests; live acceptance pending.
- **INT-012:** user-approved licence dashboard and on-demand assigned-user search. Direct-user scope checks use captured service-plan assignments; unresolved group/role/guest/device scopes stay unknown. Do not infer entitlement from seat totals or assign licences automatically. Implemented with synthetic tests; tenant-specific acceptance pending.
No decision in this PR authorises tenant writes, application registration, consent, subscriptions or a release to production. Source snapshots and digests are evidence, not signatures or automatic rollback.

## Independent review decisions — preview.3

- **INT-013:** explicit transport boundary: only confirmed pre-send failures become NotAttempted. Unknown writes are not resolved through name searches or retries. Accepted outcomes are retained even when follow-up reads fail.
- **INT-014:** accepted-unverified writes can be re-verified using reads only. Separate source-linked evidence records and safe local mapping finalisation preserve the original audit trail. Read-only assessment access is sufficient.
- **INT-015:** do not assume no real 1.0.0 evidence exists. Require acknowledged reconciliation of completed, absent-acceptance records against the latest exact ownership/payload and current safe settings. Preserve historical bytes; insufficient evidence remains blocked.
- **INT-016:** Stop cancels deployment reads immediately but lets an in-flight policy write reach its configured timeout/result. Readback and after-capture each have a 60-second budget; truncated evidence remains explicitly incomplete. Storage/OS hangs are outside these network budgets.
- **INT-017:** preserve the localhost registration pending a real admin-consent redirect test. UI and documentation explain Stop waiting / Validate setup fallback; no automatic application recreation or consent inference.

## Preview.4: authentication and device automation (14 September 2026)

User testing reported localhost redirect mismatch and confusing setup permissions. Keep separate assessment/deployment tokens, replace random-port admin consent with an exact registered web callback, and prefer WAM for desktop sign-in. Existing-ID repair requires explicit preview/approval and rejects unrelated application configurations. Engineer app assignment may be included explicitly; directory roles and consent grants are never written. Generalise visible product branding while retaining internal namespaces and original historical standard bytes. Add only user-specified BitLocker settings and optional long paths in 2026.09.4. The beta device-configuration route is isolated and receives the same recovery gates; no other beta recovery route is enabled. User clarified unspecified recovery choices remain not configured. Live server defaults and device effects are acceptance work.
