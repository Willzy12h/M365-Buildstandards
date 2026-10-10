# Current source and next work — 10 October 2026

This is a navigation index, not a release approval or substitute for fresh repository checks. Update the snapshot after material merges; use commit-bound evidence, not older green runs, for changed bytes.

## Verified baseline

- Integration `463c0c84e396aac4d3dcf4dfc1c8f3e4d9eae2a8`, source **1.1.0-preview.19, unpublished**.
- Latest actual integration Windows run [38044346614](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/38044346614) succeeded. #61 runner contract and #71 enrolment naming decision are merged. #70/#72 contain the post-merge Exchange script corrections; the current library remains Copy/Save only.
- Reviewed #58 mailbox-capacity evaluation, #62 offline report exports, #65 scale and #68 export robustness merged. [Exact source/merge/push evidence](MERGE-EVIDENCE-2026.10.10.md) distinguishes integration runs from promotion PR checks.
- Existing engine workflows, historical standards, evidence, reviewed approvals and protected publisher remain authoritative. No live tenant action or publication is authorised by this index.

## Active work

| PR | Owner | Remaining action |
|---|---|---|
| #57 | Astra | ENR correction implemented at `665fa2d`, full Linux/Windows/native/package checks green; independent delta re-review pending |
| #67 | Astra | Dispatch timing assumption fixed at `381deb0`; cancellation mutation still fails and restored suite/Windows checks pass; narrow review pending |
| #69 | Astra | William’s permission-purpose acknowledgement recorded at `8e8257b`; no Directory.Read.All or live consent added; narrow documentation review pending |
| #73 | Astra | Full validation passed at `198cd9d`; independent safeguard/authentication source review before integration |
| #74 / #75 | Claude | Astra review complete: AST-20261010-01–03 require fixes, fresh Windows proof and re-review; then adapters/UI integration |
| #76 | Astra | Current index, own trackers, exact merge evidence, safe branch-cleanup audit and review recommendations; independent review pending |
| #77 | Claude / William | Draft main-promotion placeholder; AST-20261010-04 accuracy corrections and final candidate still required; separate promotion approval |

PR links use https://github.com/Willzy12h/M365-Buildstandards/pull/ followed by the number. This table is a dated snapshot: check the actual current heads and states before action.

The #73 header regression was a wrapped write-access badge increasing the persistent header height. `3ea8485` keeps write access and experimental state visible in a compact label. Its full Windows runs 38011061374 / 38011064887 passed 1,414 engine/CLI and 162 App tests, 48 layouts/89 command presses, zero binding issues/tenant calls, PowerShell 5.1 checks and fresh extracted startup/context-menu/CLI verification. The refreshed combined-source head `198cd9d` also passed Linux 1,414 tests with no skips and strict cross-builds. Exact-head Windows push/PR runs 38039989331 / 38039991349 both passed the full suite: 1,414 engine/CLI, 162 App, 48 layouts/89 commands, zero binding issues/tenant calls, PowerShell 5.1 and fresh 302-file package checks. The original ZIP SHA-256 for push run 38039989331 is `c9ffce4642362ee39f5fe75bba1497f90838489670936972304e286bcc7002b1`. Independent review is still required before merge.

The confirmed findings and token-efficient review recommendation are in [REVIEW-STRATEGY](release-readiness/REVIEW-STRATEGY-2026.10.10.md). This is source review and a dated snapshot, not a declaration that the master programme or final candidate is complete.

## Continue without rereading every historical checkpoint

1. Follow [AGENT-COORDINATION](AGENT-COORDINATION.md): fetch integration, check open PRs in all three repositories, read changed files/overlaps and current claims before each branch. Claim through a draft PR; never overwrite the other agent's branch.
2. Start with this index and the current goal in [release-readiness](release-readiness/astra-goal-prompt.md). The dated checkpoints in [HANDOVER](HANDOVER.md) and the decision/feedback/completion registers remain available for affected contracts and evidence. Read the relevant changes since the last inspected revision; a stale main README is not current capability evidence.
3. Keep verification bound to the exact head and actual merge. Once applicable checks pass, repeat only after source/base changes, failures or unresolved concerns. Do not replace required checks with an index or cached summary.
4. Record engineer-visible changes, exact source/checks and next blocker concisely. Retain historical proof and attribution rather than duplicating the entire timeline in each continuation.

## Cleanup and release boundaries

William authorised removing unnecessary merged branch references. 28 remote and 25 local Astra branches were deleted (initial 24/21 plus four newly merged slices) only after proving their heads are reachable from retained integration and checking there is no open PR. Active branches, Claude remote branches, tags, releases and evidence were retained. [The audit](archive/BRANCH-CLEANUP-2026.10.10.json) records original heads for recovery; deleting a reference did not delete integration history.

William acknowledged reusing **Application.Read.All** for tenant-wide application/service-principal credential-expiry metadata and app-role assignments on 10 October. Exclude secret values and all writes; add no Directory.Read.All consent. Dependent source waits for #69's merged contract. Live reads, human acceptance, security-owner distribution decisions, main promotion, tags and publication remain separate approval gates.

Remaining product source includes report/navigation integration, bounded read-only runner integration, the agreed application reports and offline prerequisite diagnostics, followed by exact-source candidate/package and promotion preparation. Preserve existing features; do not rebuild the script library. Live-unverified features remain gated and Exchange changes remain copy-only.
