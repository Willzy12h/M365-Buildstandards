# Shared product feedback register

**Initial review: 6 October 2026. Baseline: `2bf96a83846474a522b074479f07a3e076a5a03d`.**

This is the shared Astra/Claude product feedback list requested by the user. Initial recommendations below are **proposed**, not approved or implemented. The review documents are delivered; that does not complete these runtime/release recommendations. Existing historical findings and validated work remain in [COMPLETION-REGISTER](COMPLETION-REGISTER.md), [DECISION-LOG](DECISION-LOG.md) and earlier review dispositions.

Read the [review](FINAL-PRODUCT-REVIEW-2026.10.06.md), [plan](PRODUCT-COMPLETION-PLAN.md), [agent handoff](PRODUCT-AGENT-HANDOFF.md) and [coordination protocol](AGENT-COORDINATION.md). This register tracks feedback, not live work ownership; draft PRs and WORK-CLAIMS retain that role.

## How to collaborate

1. Re-fetch, check open PRs/claims and choose a non-overlapping approved area. Use your own agent branch. Do not concurrently edit this file in a shared worktree; nominate one integrator, or submit register updates on separate branches/PRs.
2. Give each new item a stable unique ID: `AST-YYYYMMDD-NN` or `CLA-YYYYMMDD-NN`. R01–R16 are the initial integrated review IDs. Link duplicates rather than delete them.
3. Classify **confirmed defect**, **observed limit**, **proposal** or **test hypothesis**. Cite revision/file/check and distinguish source, synthetic, native, human and live evidence. Do not call an inferred possibility a verified failure.
4. Record the user's scope approval/reference before implementation. Claim accepted work through the existing PR protocol. Record material contracts/choices in DECISION-LOG; the ledger does not settle architecture by itself.
5. The implementation author records the fix, exact commit/PR and checks. A separate reviewer records independent disposition and remaining tests. A model review does not substitute for live or human acceptance.
6. Tick **Done** only when the approved item meets its stated acceptance, with evidence linked. Implemented-awaiting-live remains unticked. Rejected/deferred items stay unticked with rationale and review trigger; they are accounted for, not silently completed.
7. Preserve other agents' entries, original finding text and historical evidence. Append a dated response when disagreeing. An invariant concern must be answered by a fix or evidence before merge. Human merge authority remains.

Statuses: Proposed → Approved → Claimed → Implemented → Verified. Also Blocked, Deferred, Rejected, Superseded. Use an implementation status plus named pending gates where one word would hide the limit.

Priorities: **P0** defines trustworthy release/backfill scope or blocks the advertised outcome; **P1** completes core internal workflows; **P2** improves usability/support or is a small finish correction; **P3** is optional expansion. Priority is product sequencing, not vulnerability severity.

## Initial review items

Every item is currently unassigned for implementation, with no implementation commit or new validation. Packages are in [the completion plan](PRODUCT-COMPLETION-PLAN.md#work-packages).

| Done | ID | Priority / type | Recommendation and source evidence | Package | State |
|---|---|---|---|---|---|
| [ ] | R01 | P1 / proposal | Durable tenant completion workspace; current advice is in-memory projection, ShellViewModel.cs:178–198, DeployViewModel.cs:135 | B | Proposed |
| [ ] | R02 | P0 / proposal | Legacy per-control dispositions without ownership adoption; AssessmentEngine.cs:243/313, DeploymentPlanner.cs:241–261 | C | Proposed |
| [ ] | R03 | P0 / observed limit + proposal | Release lineage and source/target impact; Mapping.cs:9–29, DriftAnalyser.cs:30/131, AUTOMATION-COVERAGE.md:118. No unsafe write was demonstrated | C | Proposed |
| [ ] | R04 | P1 / proposal | Evidence-backed cutover case preserving existing protection; DeploymentPlanner.cs:278–318, ReviewedChangeService.cs:96/143 | C | Proposed |
| [ ] | R05 | P1 / observed limit + proposal | Historical/version-aware manual checks and exception renewal; ManualCheck.cs:4–17, Deviation.cs:12–31, Workspace.cs:764–780. No automatic-assessment bypass is alleged | B | Proposed |
| [ ] | R06 | P1 / observed limit + proposal | Tested upgrade/backup/restore/handoff; ToolkitPaths.cs:14–24/43–62, RECOVERY.md:27/41 | E | Proposed |
| [ ] | R07 | P1 / proposal; live gates P0 | Derived access/role/app/module preflight and lifecycle runbook; APPLICATION-SETUP.md:63–84, ExchangeCaptureRunner.cs:13 | A/D | Proposed |
| [ ] | R08 | P1 / observed reuse gap | Combined Graph/separate Exchange desktop–CLI parity; Workspace.cs:522–530, Cli/Program.cs:117–144, AssessmentEngine.cs:37–48 | D | Proposed |
| [ ] | R09 | P2 / proposal | Previewable allowlisted support bundle/copy diagnostics; SettingsViewModel.cs:9/30–54, ShellViewModel.cs:154–158, ToolkitLogger.cs:93–100 | E | Proposed |
| [ ] | R10 | P2 / confirmed guidance inconsistency; accessibility hypothesis | Current labels/start guide/confirmation wording; ShellViewModel.cs:198, StandardViewModel.cs:41, AUTOMATION-COVERAGE.md:122, TESTING-THIS-BUILD.md:3/66, HANDOVER.md:3. Dynamic announcements need actual Narrator testing | A/G | Proposed |
| [ ] | R11 | P0 / observed acceptance limit + proposal | Capability support matrix and two recorded business journeys; COMPLETION-REGISTER.md:7, CONTROLLED-ACCEPTANCE.md:11–48, AUTOMATION-COVERAGE.md:3 | A/F/G | Proposed |
| [ ] | R12 | P1 / proposal | Durable approved release/promotion/servicing, resolved inventory and signing decision; Build-Portable.ps1:69–82/97–118/144–156, build.yml:88–101/141–149 | F | Proposed |
| [ ] | R13 | P3 / optional proposal | Future reproducible standard-authoring sources/compiler; ARCHITECTURE-ROADMAP W1 and ARCHITECTURE-DISPOSITION:9 | Later | Proposed; exclude from recommended A–G |
| [ ] | R14 | P3 / optional proposal | Offline portfolio reporting and one chosen import adapter; Cli/Program.cs:62/117, PolicyImporter.cs:12/80, disposition W4/W8 | Later | Proposed; exclude from recommended A–G |
| [ ] | R15 | P1 / operating-model proposal | Named product/release/standard/support/evidence owners, custody and incident workflow; UNRESOLVED-WRITES.md:5–15, RECOVERY.md:21–43 | A/E | Proposed |
| [ ] | R16 | P1 / process proposal | Adopt this feedback/handoff cycle and perform independent review; AGENT-COORDINATION.md:38–72. Documents prepared; Claude review and protocol adoption not performed | G | Documented; adoption/review proposed |

Paths above are repository-relative source filenames. For implementation, use the full paths in the review and inspect the current revision; line numbers describe the reviewed baseline and can move. New defects require their own precise entry instead of being hidden inside a broad recommendation.

## Acceptance and closure evidence

The package acceptance paragraphs define completion; reviewers may split a broad R item into stable child IDs before claim. Do not mark a broad parent Verified while a required child/live gate remains open.

| Item / sub-item | User approval reference | Owner / claim PR | Implementation commit | Actual checks / evidence | Independent reviewer / disposition | Pending gates | Decision / date |
|---|---|---|---|---|---|---|---|
| Initial R01–R16 | Pending | Unassigned | None | Source review only; historical CI belongs to baseline | Four Astra perspective reviews; no Claude review | Scope approval; future implementation and applicable acceptance | Proposed 2026-10-06 |

## New finding / response template

```text
ID:
Author/date:
Type and priority:
Reviewed revision / scope:
Finding or recommendation:
Exact evidence / reproduction:
Current behaviour and user impact:
Proposed change / alternatives:
Acceptance, including a meaningful negative case:
Approval reference:
Owner / claimed PR:
Implementation commit:
Checks actually executed (counts/status/identity):
Independent reviewer and response:
Pending live/human gates:
Decision / rationale / next review trigger:
Status / Done checkbox:
```

Customer evidence, names, tokens, private keys and configured client details must remain outside this register and the repository. Use synthetic examples and non-sensitive evidence references.

## Proposal-document review — 6 October 2026

The engineer-experience and legacy-migration reviewers independently read all four proposal documents. Both reported a pass subject to three wording/scope corrections: assign support export consistently to E; include R10 accessibility/newcomer checks in G's mapping; bound manual migration procedures to enumerated supported scenarios, with escalation/approved deferral for unsupported cases. The integrator applied all three corrections. Live/human acceptance limits remain. This is document/source review, not implementation or operational acceptance of R01–R16.
