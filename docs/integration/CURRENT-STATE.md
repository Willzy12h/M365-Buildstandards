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
| #67 | Astra | Seven-case transport/report-service corrections at `3401980`, refreshed with integration `5ef5470`; response source unchanged; fresh Windows push/PR 38060710948 / 38060713489 passed; delta review pending |
| #69 | Astra | Application-report refinements plus concrete offline-doctor result/exit contract at `e0611bf`; purpose acknowledgement retained; fresh Windows push/PR 38064527697 / 38064532488 passed; independent delta review required |
| #73 | Astra | Guard/production-auth corrections plus preview-budget and modal cancellation correction at `dc2f5de`; 66 focused local cases and strict cross-build passed; fresh Windows push/PR 38064417447 / 38064420362 passed (1,774 engine/CLI +170 App); independent delta review required |
| #76 | Astra | Current index, own trackers, exact evidence/cleanup corrections and focused xhigh/candidate-draft packet; fresh review/checks required |
| #77 | Claude / William | Draft main-promotion placeholder; inaccurate description corrected, final candidate/approval still required |
| #78 | Astra | #65 truncation and #68 BOM/line-break gaps corrected at `b272edf`, refreshed with integration `5ef5470`; response source unchanged; fresh Windows push/PR 38060763307 / 38060766622 passed; independent review/merge pending |
| #79 | Astra | AST-20261010-07 startup-history integrity correction; current `4a57ac1` has an ID-only documentation follow-up to Windows-green source `816567e`; fresh push/PR 38064755065 / 38064759648 passed (1,722 engine/CLI +157 App); independent review required |

PR links use https://github.com/Willzy12h/M365-Buildstandards/pull/ followed by the number. This table is a dated snapshot: check the actual current heads and states before action.

#73's wrapped-header regression and production authentication/setup coverage corrections are implemented on its branch. [Exact branch and merge evidence](MERGE-EVIDENCE-2026.10.10.md) retains the original failures, corrected source, mutation results and Windows package identities; those branch results do not certify integration or live behaviour.

The latest focused xhigh findings and dependencies are in [the pinned review packet](release-readiness/XHIGH-REVIEW-2026.10.10.md); [REVIEW-STRATEGY](release-readiness/REVIEW-STRATEGY-2026.10.10.md) preserves the earlier findings and cost guidance. AST-20261010-05 is a newly confirmed owned-process cancellation defect awaiting Claude’s correction before runnable adapters ship. This is source review and a dated snapshot, not a declaration that the master programme or final candidate is complete.

## Continue without rereading every historical checkpoint

1. Follow [AGENT-COORDINATION](AGENT-COORDINATION.md): fetch integration, check open PRs in all three repositories, read changed files/overlaps and current claims before each branch. Claim through a draft PR; never overwrite the other agent's branch.
2. Start with this index and the current goal in [release-readiness](release-readiness/astra-goal-prompt.md). The dated checkpoints in [HANDOVER](HANDOVER.md) and the decision/feedback/completion registers remain available for affected contracts and evidence. Read the relevant changes since the last inspected revision; a stale main README is not current capability evidence.
3. Keep verification bound to the exact head and actual merge. Once applicable checks pass, repeat only after source/base changes, failures or unresolved concerns. Do not replace required checks with an index or cached summary.
4. Record engineer-visible changes, exact source/checks and next blocker concisely. Retain historical proof and attribution rather than duplicating the entire timeline in each continuation.

## Cleanup and release boundaries

William authorised removing unnecessary merged branch references. 29 remote and 26 local Astra branches were deleted (initial 24/21, four newly merged slices, then reviewed #57) only after proving their heads are reachable from retained integration and checking there is no open PR. Active branches, Claude remote branches, tags, releases and evidence were retained. [The audit](archive/BRANCH-CLEANUP-2026.10.10.json) records original heads for recovery; deleting a reference did not delete integration history.

William acknowledged reusing **Application.Read.All** for tenant-wide application/service-principal credential-expiry metadata and app-role assignments on 10 October. Exclude secret values and all writes; add no Directory.Read.All consent. Dependent source waits for #69's merged contract. Live reads, human acceptance, security-owner distribution decisions, main promotion, tags and publication remain separate approval gates.

Remaining product source includes report/navigation integration after #73, Claude’s bounded read-only runner/adapters/mailbox integration, and application reports/offline prerequisites after #69. The [candidate draft and blank acceptance form](release-readiness/RELEASE-CANDIDATE-DRAFT-2026.10.10.md) exist; the next-version exact-source candidate does not yet exist. Preserve existing features; do not rebuild the script library. Live-unverified features remain gated and Exchange changes remain copy-only.

## Other-agent work without an open PR

Claude's preserved `claude/records-after-46-2026-10-08` (`61eee2e`) and `claude/scripts-run-2026-10-08` (`5a003fa`) were earlier paused records/runner work. Check their actual heads and Claude's current claim before reuse; the old runner must conform to merged INT-088 and #74/#75. Neither is an Astra cleanup candidate. Claude's #74/#75 claim rows remain stale Open in integration; a reconciliation note was posted on each merged PR. Astra does not overwrite them.

Latest source tip is `5ef5470` (#57); actual integration push 38057154787 is independently read and green, including exact package/source identity. The compact [Claude delta-review packet](release-readiness/CLAUDE-DELTA-REVIEW-2026.10.10.md) names the unmerged responses and remaining ownership; no new master prompt is needed.
