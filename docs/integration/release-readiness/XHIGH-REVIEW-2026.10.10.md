# Focused Astra review and release position — 10 October 2026

**Pinned integration:** `5ef5470e1d6d28653261d88f40e854ebe9d09d02`, unpublished `1.1.0-preview.19`. Actual integration Windows run [38057154787](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/38057154787) passed. No PR merged during this pass: the latest corrected Astra deltas still require independent review. This is a review of the current source and pending changes, not final 1.1.0 acceptance.

## Findings and concrete corrections

| ID | Confirmed failure / attribution | Disposition |
|---|---|---|
| AST-20261010-07 | At integration `5ef5470`, `EvidenceStore.cs:345–367` automatically re-sealed a Running record whose digest was invalid. Editing In progress to Pending removed the unresolved-write refusal after startup. | #79 validates all history before changing any record. Five original-source refusal cases fail; the valid-history control passes. Restored 138 related checks pass locally; source `816567e` passed full Windows. Latest ID-only `4a57ac1` passed push/PR 38064755065 / 38064759648 (1,722 engine/CLI +157 App, native/PS5.1/fresh package); independent review remains. |
| AST-20261010-05 | At the same integration, `OwnedProcess.cs:43` lets a failed stop-file callback escape before terminating its child. `ExchangeReportRunner.cs:122` catches IOException but not UnauthorizedAccessException. Actual local write denial caused that exception and left the owned child running. | [Posted on #75](https://github.com/Willzy12h/M365-Buildstandards/pull/75#issuecomment-6099108394). Claude owns the fix. Reproduction used the real process-owner source, a synthetic sleeping child and a temporary directory; the harness explicitly killed its own surviving child afterwards. No Microsoft module or tenant. |
| AST-20261010-06 / Claude CLA-20261010-48 | At #73 `1996c0d`, `MsalAuthenticator.cs:144–178` started the sign-in timeout before human permission review. The dialog also did not observe operation cancellation while open. | Corrected at #73 `dc2f5de`: one cumulative acquisition budget excludes review time, late replies refuse, cancellation stays cancellation, and the dialog closes on cancellation. 66 related offline cases pass; reinstating a whole-flow timer fails five. Strict cross-build and full Windows push/PR 38064417447 / 38064420362 pass (1,774 engine/CLI +170 App, native/PS5.1/fresh package). Independent review remains required. |

The startup finding initially reused AST-20261010-04 in #79. That older ID belongs to #77's promotion-description finding; it remains preserved. **-07 is the canonical startup ID.** Earlier comments/commits are historical proof, not silently rewritten findings.

Exact branch source/run/original-ZIP hashes are in [MERGE-EVIDENCE](../MERGE-EVIDENCE-2026.10.10.md#focused-xhigh-follow-up-heads--not-merged). They do not certify integration or live behaviour.

## Review coverage and limits

Inspected the actual broker/silent-renewal and explicit-connect paths, default-off operation binding on #73, complete-before/single-use/unresolved-write guards, scoped/report evidence and export status handling, job/completion/cutover projections and owned-process cancellation/result admission. The startup guard correction has executor/workflow/completion/disposition/cutover regression coverage. Existing report/null/blank/identity corrections are retained; none is rebuilt from older prompts.

Separate hypotheses remain: an OS refusal of process termination is swallowed before an unbounded exit wait; real MSAL reserved-scope variations; concurrent authorisation invalidation; and direct-export disk-full/concurrent-change behaviour where no reachable concurrent caller was established. These are not reported as reproduced bypasses. Claude's runner timing and final-close-only mutation coverage follow-ups remain open. This focused source/synthetic review does not prove WAM, CA/MFA, Graph query acceptance, Exchange module/RBAC/token shapes, physical scaling or Narrator behaviour.

## Required next source work

Use the current feature matrix at the top of [COMPLETION-REGISTER](../COMPLETION-REGISTER.md), plus the existing per-control [capability matrix](../../AUTOMATION-COVERAGE.md). Already present: collection/assessment/planning/deployment, scoped checks, jobs/legacy/cutover/lineage, full configuration HTML export, strict Graph report core and offline exports, 29 Copy/Save scripts, mailbox evaluator, Exchange evidence and runner engine.

Still not complete: connected Reports/navigation and naming-audit presentation; application expiry/consent reports; offline doctor; shipped Exchange adapters, Run UI and integrated mailbox size/quota/entitlement report. No broad feedback item is closed or deferred on William's behalf.

1. Claude reviews the current #67/#69/#73/#76/#78/#79 deltas. #69 now includes concrete offline-doctor result/exit semantics. His own stale tracker rows stay for his reconciliation.
2. Once independently reviewed, merge eligible Astra PRs with merge commits, refresh subsequent branches, rerun exact-head checks and inspect each actual integration push. #79 is a focused safety correction; do not merge it on Astra's own review alone.
3. After #73 releases the shared shell/workspace claim, Astra implements connected Reports/navigation/naming. After #69's contract merges, Astra implements application reports and doctor. Application.Read.All metadata-purpose reuse is already acknowledged; no Directory.Read.All consent is added.
4. Claude fixes AST-05, then completes its owned adapters/Run UI and mailbox integration under INT-088. The engine alone is not a runnable product feature. Exchange changes remain Copy/Save only.
5. Validate the integrated milestone once, prepare the authorised next-preview version and original exact-source ZIP/hash, then complete the [candidate record and blank acceptance form](RELEASE-CANDIDATE-DRAFT-2026.10.10.md). #77 remains a draft promotion placeholder requiring William’s explicit approval.

## Review efficiency

This pass already provides the requested focused xhigh review. Review the confirmed fixes and subsequent integration deltas next; do not restart the entire conversation or repeat unchanged whole-repository passes. Preserve a final exact-source Windows/native/package run and an independent review. Additional broad review is justified by new interacting source or a demonstrated regression, not simply by another model setting. No review can promise flawless Microsoft behaviour.
