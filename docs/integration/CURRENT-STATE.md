# Current source and next work — 10 October 2026

This is a navigation index, not a release approval or substitute for fresh repository checks. Update the snapshot after material merges; use commit-bound evidence, not older green runs, for changed bytes.

## Verified baseline

- Integration `5ef5470e1d6d28653261d88f40e854ebe9d09d02`, source **1.1.0-preview.19, unpublished**.
- Latest actual integration Windows run [38057154787](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/38057154787) succeeded. #61 runner contract and #71 enrolment naming decision are merged. #70/#72 contain the post-merge Exchange script corrections; the current library remains Copy/Save only.
- Reviewed #58 mailbox-capacity evaluation, #62 offline report exports, #65 scale and #68 export robustness merged. #65 retained an open test gap despite conflicting ready wording; #78 corrects it and remains unmerged. #74/#75 strict Exchange report evidence and the owned read-only runner engine are now independently re-reviewed, merged and Windows-verified. No shipped runnable adapter or Run UI is present. [Exact source/merge/push evidence](MERGE-EVIDENCE-2026.10.10.md) distinguishes integration runs from promotion PR checks.
- Existing engine workflows, historical standards, evidence, reviewed approvals and protected publisher remain authoritative. No live tenant action or publication is authorised by this index.

## Active work

| PR | Owner | Remaining action |
|---|---|---|
| #57 | Astra | Merged `5ef5470` after Claude resolved CLA-34, compatibility note and exact-head Windows green; actual integration push 38057154787 green |
| #67 | Astra | Seven-case transport/report-service corrections at `5ee67b3`; page-two/renewal/back-off mutations fail; exact Windows push/PR 38056199787 / 38056202609 green; delta review pending |
| #69 | Astra | Ownership, strict schema dispatch and replication limits corrected at `2cd5289`; existing permission acknowledgement retained; exact Windows push/PR 38056291887 / 38056294751 green; delta review pending |
| #73 | Astra | Production-path renewal/setup/consent regressions and truthful account-choice preview at `28a30a0`; targeted mutations fail; full push 38057034112 green (1,737 engine/CLI, 170 App, native/portable); PR check 38057036961 green; delta review pending |
| #76 | Astra | Current index, own trackers, exact merge evidence and cleanup snapshot corrections for CLA-50–53; fresh review/checks pending |
| #77 | Claude / William | Draft main-promotion placeholder; inaccurate description corrected, final candidate/approval still required |
| #78 | Astra | #65 truncation and #68 BOM/line-break gaps corrected at `ea17fa7`; full Windows push/PR 38055393043 / 38055397201 green; independent review/merge pending |

PR links use https://github.com/Willzy12h/M365-Buildstandards/pull/ followed by the number. This table is a dated snapshot: check the actual current heads and states before action.

The #73 header regression was a wrapped write-access badge increasing the persistent header height. `3ea8485` keeps write access and experimental state visible in a compact label. Its full Windows runs 38011061374 / 38011064887 passed 1,414 engine/CLI and 162 App tests, 48 layouts/89 command presses, zero binding issues/tenant calls, PowerShell 5.1 checks and fresh extracted startup/context-menu/CLI verification. The refreshed combined-source head `198cd9d` also passed Linux 1,414 tests with no skips and strict cross-builds. Exact-head Windows push/PR runs 38039989331 / 38039991349 both passed the full suite: 1,414 engine/CLI, 162 App, 48 layouts/89 commands, zero binding issues/tenant calls, PowerShell 5.1 and fresh 302-file package checks. The original ZIP SHA-256 for push run 38039989331 is `c9ffce4642362ee39f5fe75bba1497f90838489670936972304e286bcc7002b1`. Claude has now independently reviewed this head; that review revealed production authentication/setup coverage gaps. These are implemented at 28a30a0, whose full push run passed 1,737 engine/CLI and 170 App tests, 48 native layouts/89 commands, PowerShell 5.1 and fresh 302-file package checks with zero tenant calls. The new production-path mutations fail as intended; independent delta review remains before merge. Original push ZIP SHA-256: `e54f932e6aa4b7b637c8baad8282cc803d2d83ee42e50637ae4207cc4e503174`.

The confirmed findings and token-efficient review recommendation are in [REVIEW-STRATEGY](release-readiness/REVIEW-STRATEGY-2026.10.10.md). This is source review and a dated snapshot, not a declaration that the master programme or final candidate is complete.

## Continue without rereading every historical checkpoint

1. Follow [AGENT-COORDINATION](AGENT-COORDINATION.md): fetch integration, check open PRs in all three repositories, read changed files/overlaps and current claims before each branch. Claim through a draft PR; never overwrite the other agent's branch.
2. Start with this index and the current goal in [release-readiness](release-readiness/astra-goal-prompt.md). The dated checkpoints in [HANDOVER](HANDOVER.md) and the decision/feedback/completion registers remain available for affected contracts and evidence. Read the relevant changes since the last inspected revision; a stale main README is not current capability evidence.
3. Keep verification bound to the exact head and actual merge. Once applicable checks pass, repeat only after source/base changes, failures or unresolved concerns. Do not replace required checks with an index or cached summary.
4. Record engineer-visible changes, exact source/checks and next blocker concisely. Retain historical proof and attribution rather than duplicating the entire timeline in each continuation.

## Cleanup and release boundaries

William authorised removing unnecessary merged branch references. 29 remote and 26 local Astra branches were deleted (initial 24/21, four newly merged slices, then reviewed #57) only after proving their heads are reachable from retained integration and checking there is no open PR. Active branches, Claude remote branches, tags, releases and evidence were retained. [The audit](archive/BRANCH-CLEANUP-2026.10.10.json) records original heads for recovery; deleting a reference did not delete integration history.

William acknowledged reusing **Application.Read.All** for tenant-wide application/service-principal credential-expiry metadata and app-role assignments on 10 October. Exclude secret values and all writes; add no Directory.Read.All consent. Dependent source waits for #69's merged contract. Live reads, human acceptance, security-owner distribution decisions, main promotion, tags and publication remain separate approval gates.

Remaining product source includes report/navigation integration, bounded read-only runner integration, the agreed application reports and offline prerequisite diagnostics, followed by exact-source candidate/package and promotion preparation. Preserve existing features; do not rebuild the script library. Live-unverified features remain gated and Exchange changes remain copy-only.

## Other-agent work without an open PR

Claude's preserved `claude/records-after-46-2026-10-08` (`61eee2e`) and `claude/scripts-run-2026-10-08` (`5a003fa`) were earlier paused records/runner work. Check their actual heads and Claude's current claim before reuse; the old runner must conform to merged INT-088 and #74/#75. Neither is an Astra cleanup candidate. Claude's #74/#75 claim rows remain stale Open in integration; a reconciliation note was posted on each merged PR. Astra does not overwrite them.

Latest source tip is `5ef5470` (#57); actual integration push 38057154787 is independently read and green, including exact package/source identity. The compact [Claude delta-review packet](release-readiness/CLAUDE-DELTA-REVIEW-2026.10.10.md) names the unmerged responses and remaining ownership; no new master prompt is needed.
