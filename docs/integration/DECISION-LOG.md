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

No decision in this PR authorises tenant writes, application registration, consent, subscriptions or a release to production. Source snapshots and digests are evidence, not signatures or automatic rollback.
