# Integration decisions

| ID | Date | Decision | Reason / authority | Status |
|---|---|---|---|---|
| INT-001 | Original | Preserve source repositories; develop shared product in master through reviewed PRs | Existing accepted decision and current user instruction | Accepted |
| INT-002 | 2026-09-14 | Claude C#/.NET 337c8e6 is primary; Asta 020c69b is a selective reference | Explicit current user direction; evidence in comparison. Replaces the pending proposal on Claude's unmerged coordination branch | Accepted |
| INT-003 | 2026-09-14 | First implementation imports baseline, recovers reports and repairs build/package with focused tests | Restore a reviewable baseline before expanding functionality | Accepted |
| INT-004 | 2026-09-14 | Preserve CA disabled state and stored targeting/exclusions in this import | Targeting and enforcement differ. Historical "unassigned" semantics must be agreed before a targeting change or live test | Accepted for baseline preservation; final rollout semantics unresolved |
| INT-005 | 2026-09-14 | Complete durable evidence, dependency checks, plan binding, ownership/drift checks and truthful terminal states remain requirements | Current user safety requirements override incomplete implementation; see handover gaps | Accepted requirement, implementation incomplete |
| INT-006 | 2026-09-14 | Defer app-only, app provisioning, cloud browser UI and extra workloads | Stabilise delegated Windows scope first | Accepted |

No decision in this PR authorises tenant writes, application registration, consent, subscriptions or a release to production. Source snapshots and digests are evidence, not signatures or automatic rollback.
