# Current source and next work — 10 October 2026

This is a navigation index, not a release approval or substitute for fresh repository checks. Update the snapshot after material merges; use commit-bound evidence, not older green runs, for changed bytes.

## Verified baseline

- Integration `f03b995049e4e10e35c9a9d25044cef6fc63591d`, source **1.1.0-preview.19, unpublished**.
- Latest actual integration Windows run [38016401510](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/38016401510) succeeded. #61 runner contract and #71 enrolment naming decision are merged. #70/#72 contain the post-merge Exchange script corrections; the current library remains Copy/Save only.
- Existing engine workflows, historical standards, evidence, reviewed approvals and protected publisher remain authoritative. No live tenant action or publication is authorised by this index.

## Active work

| PR | Owner | Remaining action |
|---|---|---|
| #57 | Astra | Implement the merged ENR naming decision, refresh and validate imported candidates |
| #58 | Astra | Refresh reviewed mailbox-capacity evaluator; it does not collect a mailbox inventory |
| #62 | Astra | Refresh reviewed offline report exports and validate the package |
| #65 | Astra | No Claude finding; refresh and validate scale tests, without claiming an optimisation |
| #67 | Astra | Remove the non-blocking initial-dispatch timing assumption, then revalidate |
| #68 | Astra | No Claude finding; refresh and validate export robustness |
| #69 | Astra | Refresh report contract and record William's application-inventory acknowledgement; no new consent |
| #73 | Astra | Full validation passed on refreshed head `198cd9d`; independent source review before integration |
| #74 / #75 | Claude | Separate Exchange report evidence and reviewed read-only runner; Astra independent review, then source/UI integration |
| #76 | Astra | This index, own tracker reconciliation and safe branch-cleanup record |

PR links use https://github.com/Willzy12h/M365-Buildstandards/pull/ followed by the number. This table is a dated snapshot: check the actual current heads and states before action.

The #73 header regression was a wrapped write-access badge increasing the persistent header height. `3ea8485` keeps write access and experimental state visible in a compact label. Its full Windows runs 38011061374 / 38011064887 passed 1,414 engine/CLI and 162 App tests, 48 layouts/89 command presses, zero binding issues/tenant calls, PowerShell 5.1 checks and fresh extracted startup/context-menu/CLI verification. The refreshed combined-source head `198cd9d` also passed Linux 1,414 tests with no skips and strict cross-builds. Exact-head Windows push/PR runs 38039989331 / 38039991349 both passed the full suite: 1,414 engine/CLI, 162 App, 48 layouts/89 commands, zero binding issues/tenant calls, PowerShell 5.1 and fresh 302-file package checks. The original ZIP SHA-256 for push run 38039989331 is `c9ffce4642362ee39f5fe75bba1497f90838489670936972304e286bcc7002b1`. Independent review is still required before merge.

## Continue without rereading every historical checkpoint

1. Follow [AGENT-COORDINATION](AGENT-COORDINATION.md): fetch integration, check open PRs in all three repositories, read changed files/overlaps and current claims before each branch. Claim through a draft PR; never overwrite the other agent's branch.
2. Start with this index and the current goal in [release-readiness](release-readiness/astra-goal-prompt.md). The dated checkpoints in [HANDOVER](HANDOVER.md) and the decision/feedback/completion registers remain available for affected contracts and evidence. Read the relevant changes since the last inspected revision; a stale main README is not current capability evidence.
3. Keep verification bound to the exact head and actual merge. Once applicable checks pass, repeat only after source/base changes, failures or unresolved concerns. Do not replace required checks with an index or cached summary.
4. Record engineer-visible changes, exact source/checks and next blocker concisely. Retain historical proof and attribution rather than duplicating the entire timeline in each continuation.

## Cleanup and release boundaries

William authorised removing unnecessary merged branch references. 24 remote and 21 local Astra branches were deleted only after proving their heads are reachable from retained integration and checking there is no open PR. Active branches, Claude remote branches, tags, releases and evidence were retained. [The audit](archive/BRANCH-CLEANUP-2026.10.10.json) records original heads for recovery; deleting a reference did not delete integration history.

William acknowledged reusing **Application.Read.All** for tenant-wide application/service-principal credential-expiry metadata and app-role assignments on 10 October. Exclude secret values and all writes; add no Directory.Read.All consent. Dependent source waits for #69's merged contract. Live reads, human acceptance, security-owner distribution decisions, main promotion, tags and publication remain separate approval gates.

Remaining product source includes report/navigation integration, bounded read-only runner integration, the agreed application reports and offline prerequisite diagnostics, followed by exact-source candidate/package and promotion preparation. Preserve existing features; do not rebuild the script library. Live-unverified features remain gated and Exchange changes remain copy-only.
