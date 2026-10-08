# Shared product feedback register

**Initial review: 6 October 2026. Baseline: `2bf96a83846474a522b074479f07a3e076a5a03d`.**

This is the shared Astra/Claude product feedback list requested by the user. On 6 October the user explicitly approved work packages A–G for source development, preserving safeguards/historical standards and reserving live actions/publication for separate approval. R01–R12/R15–R16 are approved within those packages; R13/R14 remain outside that scope. They additionally approved R17, integrated catalogue defaults/settings exports. Approval is not implementation or verification. Existing historical findings and validated work remain in [COMPLETION-REGISTER](COMPLETION-REGISTER.md), [DECISION-LOG](DECISION-LOG.md) and earlier review dispositions.

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

Approved source work continued in PR #19 and the persisted contracts merged in PR #20; implementation since then is in #21–#33 (state column updated 7 October 2026). Independent portions are implemented with the checks below; package-level live/native/contract gates remain open. Packages are in [the completion plan](PRODUCT-COMPLETION-PLAN.md#work-packages).

| Done | ID | Priority / type | Recommendation and source evidence | Package | State |
|---|---|---|---|---|---|
| [ ] | R01 | P1 / proposal | Durable tenant completion workspace; current advice is in-memory projection, ShellViewModel.cs:178–198, DeployViewModel.cs:135 | B | Implemented, live-unverified: job records (#25, INT-060), completion projection and Jobs and completion page with `bdit jobs`/`bdit job` (#30, INT-063), outcomes citing stored assessments (#32, INT-065); synthetic and native harness only. Astra post-merge review and human journey acceptance pending |
| [ ] | R02 | P0 / proposal | Legacy per-control dispositions without ownership adoption; AssessmentEngine.cs:243/313, DeploymentPlanner.cs:241–261 | C | Implemented, live-unverified: immutable dispositions (#27, INT-061), recorded from the Jobs page (#30); no mapping or tenant write. Astra post-merge review and live legacy journey pending |
| [ ] | R03 | P0 / observed limit + proposal | Release lineage and source/target impact; Mapping.cs:9–29, DriftAnalyser.cs:30/131, AUTOMATION-COVERAGE.md:118. No unsafe write was demonstrated | C | Implemented, live-unverified: release lineage into 2026.09.30 (#22, INT-057; review fixes #24, INT-059) and upgrade impact report in the app and `bdit upgrade-impact` (#23, INT-058). Astra post-merge review pending |
| [ ] | R04 | P1 / proposal | Evidence-backed cutover case preserving existing protection; DeploymentPlanner.cs:278–318, ReviewedChangeService.cs:96/143 | C | Implemented, live-unverified: cutover cases with stage rules and fresh-capture closure (#28, INT-062), recorded from the Jobs page (#31, INT-064). Real pilot/effectiveness evidence needs authorised live acceptance |
| [ ] | R05 | P1 / observed limit + proposal | Historical/version-aware manual checks and exception renewal; ManualCheck.cs:4–17, Deviation.cs:12–31, Workspace.cs:764–780. No automatic-assessment bypass is alleged | B | Partly implemented: versioned observations with supersession and NeedsReview on changed evidence, legacy manual checks shown as attestations (#25, INT-060). Exception renewal rules and overdue-review policy await the business owner (R15) |
| [ ] | R06 | P1 / observed limit + proposal | Tested upgrade/backup/restore/handoff; ToolkitPaths.cs:14–24/43–62, RECOVERY.md:27/41 | E | Implemented; 16 transfer tests and native backup/restore passed; human custodian/handoff acceptance pending |
| [ ] | R07 | P1 / proposal; live gates P0 | Derived access/role/app/module preflight and lifecycle runbook; APPLICATION-SETUP.md:63–84, ExchangeCaptureRunner.cs:13 | A/D | Partly implemented: generated access inventory with the standard exports (Preview.18, f200371). Actionable module/role/app preflight not yet verified; live WAM/consent/RBAC gates pending |
| [x] | R08 | P1 / observed reuse gap | Combined Graph/separate Exchange desktop–CLI parity; Workspace.cs:522–530, Cli/Program.cs:117–144, AssessmentEngine.cs:37–48 | D | Verified synthetic read-only parity and refusals at f200371; no live behaviour claim |
| [x] | R09 | P2 / proposal | Previewable allowlisted support bundle/copy diagnostics; SettingsViewModel.cs:9/30–54, ShellViewModel.cs:154–158, ToolkitLogger.cs:93–100 | E | Verified allowlist/privacy/preview and native command at f200371; uploads none |
| [ ] | R10 | P2 / confirmed guidance inconsistency; accessibility hypothesis | Current labels/start guide/confirmation wording; ShellViewModel.cs:198, StandardViewModel.cs:41, AUTOMATION-COVERAGE.md:122, TESTING-THIS-BUILD.md:3/66, HANDOVER.md:3. Dynamic announcements need actual Narrator testing | A/G | Implemented current guidance; native passed; human Narrator/newcomer acceptance pending |
| [ ] | R11 | P0 / observed acceptance limit + proposal | Capability support matrix and two recorded business journeys; COMPLETION-REGISTER.md:7, CONTROLLED-ACCEPTANCE.md:11–48, AUTOMATION-COVERAGE.md:3 | A/F/G | Partly implemented: capability matrix from the engine's real read routes (CLA-20261006-02). Supported/experimental runtime gating, recorded journeys and live evidence pending (Astra workstream 5) |
| [ ] | R12 | P1 / proposal | Durable approved release/promotion/servicing, resolved inventory and signing decision; Build-Portable.ps1:69–82/97–118/144–156, build.yml:88–101/141–149 | F | Implemented: exact Windows inventory/package verified; one reviewed-pin publisher for every version (`publish-release.yml`, INT-066). Repository release settings, approval of each publication and support ownership pending |
| [ ] | R13 | P3 / optional proposal | Future reproducible standard-authoring sources/compiler; ARCHITECTURE-ROADMAP W1 and ARCHITECTURE-DISPOSITION:9 | Later | Proposed; exclude from recommended A–G |
| [ ] | R14 | P3 / optional proposal | Offline portfolio reporting and one chosen import adapter; Cli/Program.cs:62/117, PolicyImporter.cs:12/80, disposition W4/W8 | Later | Proposed; exclude from recommended A–G |
| [ ] | R15 | P1 / operating-model proposal | Named product/release/standard/support/evidence owners, custody and incident workflow; UNRESOLVED-WRITES.md:5–15, RECOVERY.md:21–43 | A/E | Proposed roles only (INTERNAL-OPERATING-MODEL). Named owners, retention and exception/deferral policy await William |
| [ ] | R16 | P1 / process proposal | Adopt this feedback/handoff cycle and perform independent review; AGENT-COORDINATION.md:38–72. Documents prepared; Claude review and protocol adoption not performed | G | Claude Preview.18 review performed (CLA-20261006-01–17, fixes merged in #21). Astra/Codex review of #22–#33 pending |
| [x] | R17 | P1 / user-approved addition | Integrated offline standard export set: exact verified catalogue JSON/manifest, full defaults/settings HTML, document-ready Markdown and existing manual references. No tenant data or invented manual defaults | A | Verified: eleven releases, 11 browser/print checks and native export paths at f200371 |

Paths above are repository-relative source filenames. For implementation, use the full paths in the review and inspect the current revision; line numbers describe the reviewed baseline and can move. New defects require their own precise entry instead of being hidden inside a broad recommendation.

## Acceptance and closure evidence

The package acceptance paragraphs define completion; reviewers may split a broad R item into stable child IDs before claim. Do not mark a broad parent Verified while a required child/live gate remains open.

| Item / sub-item | User approval reference | Owner / claim PR | Implementation commit | Actual checks / evidence | Independent reviewer / disposition | Pending gates | Decision / date |
|---|---|---|---|---|---|---|---|
| R01–R12, R15–R17 | User explicitly approved A–G and integrated exports, 2026-10-06 | Astra / #19; persisted contracts require dedicated decision PR | None | Source review only; historical CI belongs to baseline | Four Astra perspective reviews; no Claude review | Contract merge where applicable; implementation; live/human/release gates | Approved 2026-10-06 |
| R13/R14 | Not included in approved A–G | Unassigned | None | None | Optional follow-on recommendations | Separate scope decision | Deferred 2026-10-06 |

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


## Independent implementation review — 6 October 2026

| ID | Reviewer / scope | Finding | Response | Verification / remaining gate |
|---|---|---|---|---|
| AST-20261006-01 | Engineer experience / R17 | Historical optional ESP empty default was rejected by generic resolver | Use existing PolicyInputDefaults.Resolve allowance; check actual retired-control use in the fixture | All eleven historical releases export; Linux source tests passed. Native final at f200371 passed. |
| AST-20261006-02 | Engineer experience / R17 | Four new export commands missing from native registry | Register and exercise all four | Strict native harness cross-build passed; Windows f200371: 78 commands passed. |
| AST-20261006-03 | Engineer experience / R17 | Main export result hidden in collapsed section | Copyable result sits outside the expander | Source corrected; native f200371 layout and package Copy checks passed; image visual inspection unavailable. |
| AST-20261006-04 | Architecture / R06 | Byte fixture did not prove a real unresolved write remained blocking | Added genuine saved Unknown run, new restored store, digest equality and fresh/old plan refusal | Independent focused run: 16 passed, 0 failed/skipped. Closed for synthetic engine scope. |
| AST-20261006-05 | Architecture / R06 | Create/restore total-size definitions differed by generated metadata | Both include README/checksum list; test exact-limit round-trip and one-byte refusal | Independent focused run: 16 passed. Closed. |
| AST-20261006-06 | Legacy / PR20 | Identity/cardinality, reviewed inputs, supersession and cutover closure contracts underspecified | Added common identity/history/validity rules and negative acceptance | Independent rereview of 674b1ba: all four resolved; implementation-ready after human merge. |
| AST-20261006-07 | Release / R12 | Inventory verifier checked only identity/notices | Validate name/version/kind, both inventory arrays and notices; add changed-field/missing-notice negatives | Source corrected; exact Windows f200371: 78 commands passed. |
| AST-20261006-08 | Release / R12 | Local dirty builds recorded only HEAD | Record source cleanliness at build start; dirty builds cannot serve as clean promotion proof | Source corrected; f200371 complete package metadata passed. |
| AST-20261006-09 | Engineer experience / R08 | Wrapped supplemental Exchange JSON bypassed strict member parsing and could turn malformed items into empty success | Strictly parse original nested JSON, reject duplicates/unknowns, require explicit collected arrays; allow stored DNS only after wrapper integrity | Independent actual CLI rerun: original raw/wrapped malformed inputs both exit 2, no report. Full Linux Engine 902 passed; f200371 Windows: 902 Engine + 112 App passed. |
| AST-20261006-10 | Native Windows / R17 | Extra export toolbar content hid prerequisite title at 1180×760 and 1180×640 | Primary export beside title, secondary formats grouped inside bounded scrollable expander; assertions preserved | First CI at 47dcc50: 895 Engine + 112 App passed, layout failed; f200371 native: 42 layouts/78 commands, zero bindings/tenant calls, passed. |

| AST-20261006-11 | Windows packaging / R12 | PowerShell 5.1 wrapped a converted JSON array in an extra array | Preserve converter output by assignment on both 5.1/7; keep exact count/identity/version/kind/notices assertions | f200371 fresh extraction and all inventory negative checks passed. Closed. |

No remaining blocking finding in the independently reviewed export/transfer/package source scope. This does not close live, native or contract-merge gates. Claude has not performed a review in this session. A later reviewer appends new findings without rewriting these observations.

## Astra post-merge review — 8 October 2026 (in progress)

Baseline: integration `8f0285a48bdd719914037b0ab2d9eeb6e7f6cc2b`; draft PR #36. These are confirmed projection/recording defects, not a demonstrated bypass of deployment execution guards. No human/live acceptance or broad feedback item is closed.

| ID | Reviewer / scope | Confirmed defect | Response | Evidence / remaining gate |
|---|---|---|---|---|
| AST-20261008-01 | Astra / completion | `JobCompletion.Build` could claim completion with an intact unresolved deployment write for a requirement | Reuse strict existing run, recovery, reviewed-change and LAPS history checks as read-only completion blockers | `Accepted_passes_do_not_complete_a_job_with_an_unresolved_tenant_write` failed on baseline; 1,058 engine/CLI tests passed; full solution cross-build passed with zero warnings/errors. Windows/native and independent review pending |
| AST-20261008-02 | Astra / requirement identity | `JobWorkflow.CheckSupersession` allowed a second semantic identity for the same instance; completion selected the first outcome | Reject divergent identities during recording; block completion for already persisted conflicting outcome/decision groups without changing historical bytes | `A_second_semantic_identity_cannot_hide_a_failed_outcome_for_the_same_requirement` failed on baseline; persisted-history regression passed with 1,058 engine/CLI tests; strict cross-build passed. Windows/native and independent review pending |
| AST-20261008-03 | Astra / cutover evidence | `JobProjection` checked only the current revision's evidence, so deleting the candidate run left a later closed case accepted | Re-verify predecessor stage references by their original pinned digests; do not change any historical record or grant execution rights | Missing-run regression observed failing before the fix; deletion and modification regressions pass in the 1,060-test engine/CLI suite. Exact-head Windows and independent review pending |

## Astra independent review of #46 — 8 October 2026

Reviewer: Astra (review 5461388737, posted from the Willzy12h account) on PR #46 head `3d993c9`. Synthetic evidence only: the stub-module harness and engine tests on Linux (PowerShell 7). Windows PowerShell 5.1 CI, independent rereview and any live run remain pending. Details: [SCRIPT-LIBRARY-2026.10.08](SCRIPT-LIBRARY-2026.10.08.md#review-corrections-astra-8-october-2026).

| ID | Reviewer / scope | Finding | Response | Evidence / remaining gate |
|---|---|---|---|---|
| AST-20261008-05 | Astra / script library (P2) | Quota audit skipped every null percentage without IncludeAll, so Unlimited or unreadable quotas and unreadable sizes vanished and could produce zero rows | Fixed by Claude in #46 (27c40b0): always listed with `Status` Unknown and a `Reason`, plus a `BDIT:UNKNOWN` warning | Stub cases for Unlimited, malformed quota and missing size, with default inputs and IncludeAll, failed on `3d993c9` and pass. Live run pending |
| AST-20261008-06 | Astra / script library (P2) | Nullable hold, sign-in and visibility values cast to bool, so null became False | Fixed by Claude in #46 (27c40b0): True/False/Unknown in all ten scripts' true/false columns, with `Notes` in inventory and shared mailbox rows | Null, false and true cases for inventory, shared mailboxes, forwarding, inbox rules, mailbox permissions and user access failed on `3d993c9` and pass. Live run pending |
| AST-20261008-07 | Astra / Copy wrapper (P2) | The wrapper verified the tenant but not the account shown in the confirmation | Fixed by Claude in #46 (27c40b0): case-insensitive UPN check after the tenant check and before any read; refusal, no CSV, disconnect, non-zero exit. No tenant call added to the desktop Copy | Same-tenant mismatch refused for all ten items with no stand-in read; engine tests on the generated order. Both failed on `3d993c9`. Live run pending |
| AST-20261008-08 | Astra / script library (P2) | User access matched display names and ignored Full Access Deny and Send As AccessControlType | Fixed by Claude in #46 (27c40b0): exact identities only; Unresolved, Denied and Unknown statuses; direct entries, not effective access, stated in the item and its output | Two recipients sharing a display name, deny Full Access, deny Send As and an exact allow failed on `3d993c9` and pass. Live run pending |
| AST-20261008-09 | Astra / script library (P2) | Audit search did not detect more records when a page ended exactly at MaxRows; Send As stopped silently at 5,000 | Fixed by Claude in #46 (27c40b0): one sentinel page after an exact stop; Send As read to 5,001 and marked partial when full | Exact-cap-with-more and saturated Send As cases failed on `3d993c9` and pass; exact-cap-with-nothing-more stays complete. Live run pending |

Non-blocking notes recorded, not closed: the slice is manual and live-unverified; entitlement validation, integrated report execution, archive size/quota reporting and the full reporting scope are not implemented; the manifest timeout is a future runner limit, not enforced by the copied script.

Historical toolkit inspection: [source inventory and reuse limitations](HISTORICAL-TOOLKIT-REVIEW-2026.10.08.md).

## Claude Preview.18 review — 6–7 October 2026

Source: [CLAUDE-PREVIEW18-REVIEW](CLAUDE-PREVIEW18-REVIEW.md), reviewing `10808de`/`2668d00`/`674b1ba`. William then asked Claude to implement the fixes; they are in PR #21. "Windows CI" means the exact-head run named in the review's implementation status or in COMPLETION-REGISTER, read in full. No live tenant, Narrator or newcomer acceptance is claimed.

| ID | Reviewer / scope | Finding | Response | Verification / remaining gate |
|---|---|---|---|---|
| CLA-20261006-01 | Claude / R08, evidence | P1: primary snapshot integrity not verified by the CLI and only logged by the desktop; results carried no integrity state | Modified snapshots refused at assessment (CLI exit 2); `AssessmentResult.SnapshotIntegrity` = intact / modified / notRecorded in every report; engineer report bannered, client summary "Not for issue" | Implemented with tests; Windows CI green. Closed for synthetic scope |
| CLA-20261006-02 | Claude / R11, R17 | P1: capability matrix labelled 20 evidence-assessed controls "Manual" | Read route derived from engine routing (enum); evidence-read and licence columns | Implemented with tests. Correction: 20 controls, not 14 |
| CLA-20261006-03 | Claude / R12, standards | P1: build regenerated the tracked manifest; 2026.09.30 unpinned | `build/Test-StandardsManifest.ps1` verifies, never regenerates; 2026.09.30 pinned by publication digest | PowerShell 5.1/7 positive and negative checks; Windows packaging. Closed |
| CLA-20261006-04 | Claude / R06, E | P1: handoff ended in a manual folder copy | Backups in `transfers/`; adopt from the archive into an empty workspace with in-place re-verification and quarantine; `bdit verify-restore` | Forged-archive, lease and adoption tests; App reload test. Closed for synthetic scope |
| CLA-20261006-05 | Claude / R06, E | P2: restore trusted only the archive's own checksum list | Trusted archive SHA-256 (fingerprint) or explicit logged acknowledgement; one read-locked stream hashed and extracted | Wrong/malformed digest refused before staging. Closed |
| CLA-20261006-06 | Claude / R08, D | P2: stored Exchange evidence aged against the wall clock | Stored evidence judged as of the latest Graph, Exchange or DNS time, capped at now | Re-assessment at +72 h identical; DNS refresh test. Closed |
| CLA-20261006-07 | Claude / R03 | P2: reused PRE IDs across .10/.30 give misleading ownership messages | Fixture holds no-write/no-duplicate; shipped release lineage explains each earlier-release record without moving it (INT-057) | Lineage verified against all catalogues; tests for tampering, conflicts and the .10→.30 messages. Upgrade impact report is the next slice |
| CLA-20261006-08 | Claude / assessment text | P2: operator-exclusion wording on non-CA drift | CA-only wording | Implemented with tests. Closed |
| CLA-20261006-09 | Claude / R12, release | P2: publish job without environment protection; tag-pinned actions | Separate `publish-preview18.yml` behind `release`; actions SHA-pinned. Generalised on 7 October to `publish-release.yml` with reviewed per-version pins (INT-066) | Repository settings (environment reviewers, immutable releases) remain with the owner |
| CLA-20261006-10 | Claude / R09 | P2: support bundle too sparse for incident step 6 | Four opt-in, previewed sections (OS/scale, recent Graph errors with request IDs, collection status, timeouts); field list approved by William on 7 October | Seeded identifiers never present; default unchanged; export equals preview. Windows CI green at a515e6a. Closed for synthetic scope |
| CLA-20261006-11 | Claude / R10 | P2: guidance pointed engineers at CI artifacts; "automated recipes" wording | Release-first guidance; "candidate recipes (inert)" | Closed |
| CLA-20261006-12 | Claude / R10, G | P2: no Settings/Deviations/Checks renders | Renders added, plus a Settings hand-off render; nested-scroll observation withdrawn | Native harness. Human visual/Narrator acceptance pending |
| CLA-20261006-13 | Claude / R17 | P2: CA spec omitted plan-time exclusions | Plan-time additions listed per CA control | Closed |
| CLA-20261006-14 | Claude / R12 | P3: backslash ZIP entry names | `/` names, ordinal order | Test-Portable passes. Closed |
| CLA-20261006-15 | Claude / R06 | P3: backup ignored tenant write leases | Read-only lease probe; refused while held | Closed |
| CLA-20261006-16 | Claude / R17 | P3: GUID export names, no per-file sidecar | Release + timestamp names; `.sha256` beside every file | Closed |
| CLA-20261006-17 | Claude / PR #20 INT-049/050 | P3: full-profile-digest binding makes label-only edits invalidate observations | INT-056: only company, notes and edit times are non-material; `ReviewedClientScope.Digest` | Label edit keeps the digest; domain, exclusion, group, emergency-account and application changes alter it. Closed for the contract; INT-049/050 records will consume it |

### Usability pass — 7 October 2026 (PR #21)

At William's request Claude also made the tool easier to use, without changing any safeguard: a guided hand-off/receive flow on Settings with a copyable fingerprint, pasted checksum lines and `.sha256` loading (with a warning when the file sits beside the backup); task-specific progress titles; results that say what happened and what to do next, with **Open folder**; a Quick Connect notice saying the header still describes the session already open; more room for the change detail in plan review at 1180×640; a short first-assessment path in OPERATOR-START and on Overview; consistent "SHA-256 fingerprint" wording. Native renders and App tests cover these; human newcomer and Narrator acceptance remain open.

PR #36 bounded review detail and baseline file/line references: [ASTRA-POST-MERGE-REVIEW-2026.10.08](ASTRA-POST-MERGE-REVIEW-2026.10.08.md). Final local engine/CLI suite passes 1,060 tests; conflicting histories and unresolved writes also show Outstanding completion rows. Earlier Windows evidence at 4724b66 is recorded separately; final-head Windows and independent review remain required.

| AST-20261008-04 | Claude independent review of e433513 / completion readability | IOException or denied access while reading write evidence escaped the completion projection | Astra reproduced an exclusive-file lock, added IOException/UnauthorizedAccessException completion blockers, and kept all rows Outstanding until evidence becomes readable; no retry or write reconciliation | `An_exclusively_locked_run_keeps_completion_readable_and_outstanding` observed failing before fix; recovery after unlock included. Final validation/Claude delta review remain required |
