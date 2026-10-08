# Handover and priorities

## Current Astra continuation — reviewed merges, 8 October 2026

Integration `865e1a1e14d4d857d83f99d184cae2b67cb31e66` contains the requested #42 → #44 → #43 → #45 merges and Claude's fix PRs #51/#49/#48/#50. Every exact-head and merge-commit Windows run passed; full SHAs/runs and distinct package identity are in [MERGE-EVIDENCE-2026.10.08](MERGE-EVIDENCE-2026.10.08.md). Latest merge run 37831138250 passed 1,220 engine/CLI and 143 App tests, 45 layouts/85 commands, zero binding issues/tenant calls, fresh extraction/startup and portable CLI checks. Source remains unpublished Preview.19; this is not a release publication.

**Next, under the [current goal](release-readiness/astra-goal-prompt.md):**

1. Record #46 merge CI `37832476624`, now passed at `b755dbb3b8e1b958c12825ec296fee4e319e9ae4`; its final exact-head runs 37831467460 / 37831474759 passed at `b9aceae`, after independent review and corrected attribution. AST-05–09 are closed at source/synthetic level. The library is manual, copy-only and live-unverified; integrated execution is separate source work. #47 is ready with Claude's refreshed review record.
2. Claude independently reviews Astra #53 (R07 failed/malformed readiness reads) and #54 (Astra-owned tracker reconciliation). Reconcile only the owning agent's rows.
3. Finish remaining current guidance, module/role preflight, experimental/manual runtime gating and master-scope Reports/navigation/mailbox/script source through claimed, independently reviewed slices. Prepare the next preview and exact-source candidate record; no version has changed yet.
4. Prepare the approval-ready promotion PR and live/human forms. Keep William's stop points open. Existing publisher #34 is merged and remains Claude's area.

The continuation below records earlier 8 October checkpoints; its pending claims/CLI exclusion are historical where this current section differs. All historical source, tests and publication identities remain valid only for their original bytes.

## Astra continuation — 8 October 2026

PR #42 is ready for independent review at `6a511cf07f1f615fdeee0138147d2fcc9c7a53ce`, not merged. Exact-head Windows push `37717009564` and PR `37717013690` passed: 1,117 Engine + 130 App tests, 45 layouts / 113 synthetic images, zero binding issues, verified standards manifest and fresh 300-file extracted startup/shutdown. It implements actual dependency-only desktop checks and the same evaluator for offline CLI historical filtering, with separate strict partial evidence. Initial harness-integration failures are recorded in INT-076 on that branch; corrected runs do not close human/live gates. Portable CLI hosting is still unimplemented.

PR #43 continues read-only naming policy/audit in the engine. Local full suite at `330d1eb` passed 1,098 tests; exact-head Windows checks are running. Reports/UI exposure, authoring-pipeline integration and Microsoft limits not stated by the inspected resource pages remain explicitly open. No historical names or catalogue bytes were changed. Core naming documentation: NAMING-CONVENTION-AND-AUDIT-2026.10.08.md. Claude review is needed before merging newly implemented work under the delegation; William's previous specific merge approval applied to #36–#41.

William authorised merging PRs #36–#41 and continuing the master programme. This authorises the reviewed source/decision work, not live actions, main promotion, version changes, tags or publication. Preserve exact-head checks and safeguards.

PR #36 merged as `d5bbe2453d642a2c096bbfd51355724dfb5dba68` after exact-head Windows push run `37706807976` passed at `0fa23e6`: 1,061 Engine + 126 App tests, 45 native layouts, zero binding issues and extracted-package startup/shutdown. Integration merge CI run `37710467954` passed. AST-20261008-01–04 are implemented, human/live acceptance remains open.

PR #37 merged as `4129135b8e0c6d68007fa257820601051e90d605` after exact-head Windows run `37710529468` at `0efdb18` passed: 1,069 Engine + 126 App tests, 45 layouts, zero binding issues and fresh 299-file startup/shutdown. Its merge-commit CI run `37710948092` passed.

PR #39 merged as `3f946ae97a8e6c75c295b5447a788fb5ace2ab47` after exact-head Windows push run `37711063290` at `47b4a65` passed: 1,069 Engine + 126 App tests, native checks and fresh-package startup/shutdown. INT-070–072/074 now authorise dependent source implementation under the existing boundaries. Merge CI run `37711588742` passed.

PR #38 merged as `1622356b7bd3aa554405e47e792112c7b23c5170` after exact-head Windows push run `37711624221` at `856d9bd` passed: 1,069 Engine + 128 App tests, all 45 native layouts including full blocker text/minimum requirement rows, zero binding issues and fresh 299-file startup/shutdown. Its merge CI run `37712074918` passed.

PR #40 merged as `f6053de172ca25a7d8607f4145d287be51432992` after exact-head Windows push run `37712581187` at `fd0451a` passed: 1,079 Engine + 128 App tests, native checks and fresh 299-file startup/shutdown. Its merge CI run `37713041584` passed. HTML inventory is implemented in the shared engine/developer CLI; desktop export and portable CLI hosting still need source work.

PR #41 merged as `692d2d5aa8ed0f30b882065cfe3a778a4e1478a3` after exact-head Windows push run `37713528749` at `ee3b59c` passed: 1,079 Engine + 128 App tests, 45 layouts and fresh **300-file** startup/shutdown (the run sheet is now shipped). Its merge CI run `37713916834` passed. All PRs #36–#41 requested by William are merged.

**Current source work:** #42 is ready at `6a511cf07f1f615fdeee0138147d2fcc9c7a53ce`: exact Windows push `37717009564` and PR `37717013690` passed (1,117 Engine + 130 App tests, 45 layouts, zero binding issues, 300-file fresh startup/shutdown). It implements scoped engine, strict store, desktop and offline CLI; independent review/merge and human/live gates remain open. #43 is ready at `f72bba0432b340173d7b0063ed6aaf619479a638`: exact Windows push `37718011272` / PR `37718015982` passed (1,098 Engine + 128 App tests and native/package checks). Its naming audit is read-only engine/API work; desktop/automatic authoring integration remains pending.

#44 is stacked on #42 and implements the separate typed Graph report core/storage/exports under INT-071. Local full engine tests: 1,142 passed; strict solution cross-build: zero warnings/errors. Windows/native/package evidence must bind its pushed source head before review readiness. No Reports UI, mailbox/quota adapter or script library is claimed. Claude's independent review of #42–#44 is requested through William's project thread; no review has been fabricated.

**Next:** obtain #44 exact Windows/package evidence, independent reviews and authorised merges. Then naming/Reports navigation, capability evidence, portable CLI hosting under INT-074, mailbox reporting and safe scripts. The current ZIP still excludes the CLI. Keep source/synthetic/native/human/live evidence distinct and obtain exact-source candidate records. Claude retains publisher ownership.


**Current state, 7 October 2026 (night):** PR #19–#28 are merged into `integration` (latest `7edadb8`, PR #28, exact-head CI run 37687028581: 1029 Engine + 119 App tests, harness 42 layouts / 0 binding issues / 0 tenant calls, fresh 299-file package). PR #29 (`9ca5ebb`) moved CI off Node 20 and is merged. The source is `1.1.0-preview.19`, unpublished. PR #22–#28 merged on William's approval without an Astra/Codex review, so the next agent's first task is an independent post-merge review of them. The next agent starts from [GPT-USABILITY-CONTINUATION-PROMPT](GPT-USABILITY-CONTINUATION-PROMPT.md). Read [PRODUCT-FEEDBACK-REGISTER.md](PRODUCT-FEEDBACK-REGISTER.md) and [PRODUCT-AGENT-HANDOFF.md](PRODUCT-AGENT-HANDOFF.md). Live tenant actions, promotion to `main` and publication still need William's explicit approval. Older Preview.15–18 evidence below is historical; current checks bind the exact source commit. Do not copy private client exports into source or tests.

## Owner delegation of commits and merges — 7 October 2026

William (repository owner) said on 7 October 2026: "I never want to do the commits or merges manually. I can advise or approve but likely fine to do yourself." From that date agents commit, push and merge; William advises and approves. The conditions:

- An agent may merge a pull request into `integration`, or into the branch a stacked pull request targets, with a merge commit (no squash, rebase or force-push), when all of these hold on the exact head being merged:
  - every required check has passed, read from the actual run;
  - the other agent has reviewed it, or William has approved it, and no open finding or review thread says a safeguard was weakened;
  - it is mergeable, and any conflict was resolved on the branch owner's side, keeping the stricter behaviour, with checks re-run.
- After merging: confirm CI on the merge commit, update WORK-CLAIMS and record the merge SHA and verifying run here or in COMPLETION-REGISTER.
- Still needs William's explicit approval each time: promoting `integration` to `main`, tags, releases, running a publish workflow, repository or branch-protection settings, and any live tenant action. Agents never approve a pull request on William's behalf.
- Everything else in AGENTS.md and AGENT-COORDINATION still applies, including never pushing to the other agent's branch.

First use, 7 October 2026, with William's explicit approval: Claude merged PR #20 (`bf8e049`, integration CI run 37593659305 green), then PR #21 (`d39eb73`) after exact-head CI at `a515e6a` passed (run 37593722744: 928 Engine + 119 App tests, harness 42 layouts / 79 commands / 0 binding issues / 0 tenant calls, PowerShell 5.1 packaging, fresh 297-file package). PR #21 had merged `integration` in to resolve the one DECISION-LOG conflict on Claude's own branch, so PR #19's unchanged commits arrived with it and GitHub marked #19 merged. Nothing was pushed to Astra's branch.

## Current continuation — after the 7 October merges

**Merged into `integration`:** Claude's Preview.18 review ([CLAUDE-PREVIEW18-REVIEW](CLAUDE-PREVIEW18-REVIEW.md), CLA-20261006-01…17) and its fixes:
- Evidence integrity in every result.
- Stored evidence judged as of its capture.
- An honest capability matrix.
- Manifest verification at build.
- Verified hand-off: a SHA-256 fingerprint, adoption into an empty workspace and `bdit verify-restore`.
- A separate, protected publisher.
- Opt-in support sections (CLA-10, field list approved by William).

The usability pass is merged too:
- Guided hand-off and receive steps on Settings.
- Task-specific progress titles, and results that give the next step.
- The Quick Connect session notice.
- More room in plan review.
- A short first-assessment path on Overview and in the start guide.

The CHANGELOG's `1.1.0-preview.19` entry lists everything. The feedback register records each CLA ID's status.

**Merged since, on William's approval (7 October 2026):**
- [PR #22](https://github.com/Willzy12h/M365-Buildstandards/pull/22) (`fbce197`): release lineage into 2026.09.30 (INT-051 first slice, INT-057) and the reviewed-client-scope digest (INT-056). `standards/lineage/` is verified at build and load. Assessment explains earlier-release ownership records whose control ID now means something else. Nothing is moved or rebound.
- [PR #23](https://github.com/Willzy12h/M365-Buildstandards/pull/23) (`34dbb72`): the upgrade impact report (INT-051 second slice, INT-058). It assesses one capture under the source and target releases, separately from tenant drift. It is available on the Assessment page and as `bdit upgrade-impact`.
- [PR #24](https://github.com/Willzy12h/M365-Buildstandards/pull/24) (`9617d92`): fixes from the post-merge review of #22 (INT-059). Exclusion-account `resolvedAt`, `selectedBy` and list order are non-material to the scope digest. Unreadable lineage is reported, not fatal. Skipped lineage is explained, and the validator rules are tightened.
- [PR #25](https://github.com/Willzy12h/M365-Buildstandards/pull/25) (`19e28fb`): INT-049 job and observation records, engine only (INT-060). It adds tenant-partitioned jobs and immutable observations, strict readers, supersession checks and a read-only projection that reports `NeedsReview`. Old manual checks are shown as legacy attestations.
- [PR #27](https://github.com/Willzy12h/M365-Buildstandards/pull/27) (`0ebe7a2`): INT-050 legacy dispositions, engine only (INT-061). It adds immutable `dispositions/<id>.json` records that use the INT-060 conventions. Retention names exact objects and creates no mapping. An approved departure cites an existing in-date deviation and creates none. An unchanged repeat writes nothing.
- [PR #28](https://github.com/Willzy12h/M365-Buildstandards/pull/28) (`7edadb8`): INT-050 cutover cases, engine only (INT-062). Immutable `cutovers/<id>.json` revisions move one stage at a time, each with its own evidence. Closure needs a fresh complete capture, and approval alone never closes a case.
- [PR #29](https://github.com/Willzy12h/M365-Buildstandards/pull/29) (`9ca5ebb`): GitHub Actions pinned to their first Node 24 majors by commit SHA (checkout v5.1.0, setup-dotnet v5.4.0, cache v5.1.0, upload-artifact v6.0.0, download-artifact v7.0.0), in `build.yml` and `publish-preview18.yml`. The publisher only runs on a manual, approved dispatch, so CI does not exercise its download steps; they download by name, which these majors did not change.
- [PR #34](https://github.com/Willzy12h/M365-Buildstandards/pull/34) (`c04a7ba`, integration push run 37702136051): one reviewed-pin publisher for every release (INT-066) and feedback register states brought up to date. Merged on William's approval.

**Next, in order:**
1. An independent Astra/Codex review of PR #22–#32. They merged on William's approval without the other agent's review.
2. Further usability from real engineer feedback.

The INT-049/050 workflow surfaces are in place. The completion projection, the Jobs page and `bdit jobs`/`bdit job` landed in PR #30 (`0105676`, INT-063). Recording cutover revisions from the Jobs page landed in PR #31 (`5ce7f56`, INT-064). Outcomes that cite stored assessments landed in PR #32 (`7608150`, INT-065).

**Owned by others:**
- `release` environment reviewers and immutable releases (William, in repository settings).
- Publishing Preview.19 (William's approval). The publisher is now version-independent: add `build/releases/1.1.0-preview.19.json` and `.md` from the candidate's green push run through a reviewed PR, then dispatch **Publish validated release** from `integration` or `main` ([RELEASE-AND-SERVICING](../RELEASE-AND-SERVICING.md#publishing-a-release), INT-066).
- Named support and custodian owners.
- Human Narrator, keyboard-only, physical scaling and newcomer acceptance.
- All live tenant acceptance.

## Architecture roadmap

The plan for making this maintainable and extensible is [docs/integration/ARCHITECTURE-ROADMAP.md](ARCHITECTURE-ROADMAP.md). It is written for a model to execute: workstreams in dependency order, machine-checkable acceptance criteria, the invariants that outrank the plan, and the non-goals. Start there before proposing structural change.

## Previous continuation — Preview.15 / 2026.09.30

The user authorised completion on 30 September. PR #19 remains the owned Astra/Codex release claim, now a draft while the successor changes. Use [COMPLETION-REGISTER](COMPLETION-REGISTER.md) for current results and acceptance status. The missing Claude context document is no longer a prerequisite. .NET 10 migration, legacy retirements, engineer detail/evidence workflow and package validation continue together. The verified candidate is 7fb73d1e90ccd26eb62be8b0bfab7a3cc8751198: Windows run 36787138102 passes 810 engine/79 application tests, 42 native page/size checks and actual fresh-package startup/shutdown with 287 matching files. The completion register links the ZIP, checksum, generated documents and real WPF renders. Cloud artifact retrieval/visual inspection and human/live acceptance remain outstanding. No tenant or device operations have occurred. The user saved the short goal pointing to RELEASE-GOAL-2026.09.30.md; use that document for the definition of done.

## Prior release work - PR #19

**Preview.14 / standard 2026.09.12** is implemented on `astra/release-2026-09-12`, based on integration `5cadd3a070b0e308bdffc327dfc7954bd69b9e83`. [PR #19](https://github.com/Willzy12h/M365-Buildstandards/pull/19) targets integration; it is for human review, not merged or approved by the agent. Each NEXT-RELEASE-PLAN phase was committed and published in order. Use [the release validation ledger](RELEASE-2026.09.12-VALIDATION.md) and the exact PR-head CI run for observed counts; older evidence below is historical.

The release has **96 controls, 61 candidate recipes**, repeatable public-IP offices, the agreed identity/Windows/mobile changes and Entra/Intune/Exchange/Purview area filters. Both engineer documents generate all controls in HTML/Markdown without client data; CI publishes the four generated files. Exchange/Purview use a strict delegated read-only capture import and fakeable explicit DNS checks. The authorised phase 5 fallback produces selected inert PowerShell proposals: module retries cannot meet the uncertain-write rule, and no toolkit Exchange write execution is added.

No tenant sign-in, Graph/Exchange/Intune call, consent, application operation, DNS query or device operation was performed. No existing client evidence, token or saved connection was read. Microsoft contracts are researched, and their runtime acceptance remains unverified. The WPF interface was rendered offline; the browser URL policy blocked local HTML preview, so generated HTML browser/print appearance remains unverified.

### Maintainer decisions and manual acceptance

- Recommend retiring classic ENR-003/004 from default scope in favour of device preparation. They remain deferred references; no hardware hashes, serial numbers, corporate identifiers or registration are added.
- Recommend retiring SEC-WIN-002 Defender onboarding under ESET. Retain CFG-WIN-004 SmartScreen as independent shell protection unless deliberately changed. Removed only from .12: SEC-WIN-001, CMP-WIN-002 and SEC-WIN-003.
- Recommend keeping Exchange writes manual until a supported single-attempt delegated route is established. Keep the own-domain SPF bypass disabled/in audit mode until trusted SPF/header and From-domain alignment is demonstrated. This does not claim that a matched text header proves sender authenticity.
- Native catalogue IDs/complete mechanisms are unconfirmed for Chrome SSO, file extensions, OneDrive Files On-Demand and device preparation. UK setup, five user-changeable starting pins, Store access and fast-startup force-off are manual. Do not add uploaded ADMX, custom templates, platform/remediation scripts or new credentials as a workaround. Recommend a separately scoped native-definition investigation before any new write mechanism.
- Recommend deferring Outlook mobile account configuration. Modern passkey-profile migration needs manual handling; legacy passkey editing refuses populated profiles. Consumer Copilot Pro applicability is contradictory in Microsoft documentation and must be checked manually. Keep Microsoft 365 Copilot.
- The maintainer's later authorised acceptance must cover both apps' renewed consent, exact API/module responses, eligible licences/editions, pilot sign-in/recovery, app/ESET installation, Autopatch prerequisites/all-device coverage and the generated after-checks. Do not infer those from synthetic tests or CI.

New scopes and reasons are in [application setup](../APPLICATION-SETUP.md); decisions INT-030 through INT-037 record the contracts and manual boundaries. The phase 0 findings were independently reproduced and fixed; prior PR #17/#18 findings are closed history and were not reopened.

## Completed baseline history

**Preview.13 / standard 2026.09.11 is on integration.** PR #17 (Claude) is the full code and interface review; its record is [FULL-REVIEW-2026-09-24.md](FULL-REVIEW-2026-09-24.md). Read that first: it lists what was fixed, and seven findings in protected areas that were reported for a decision. PR #18 settled them — five fixed, one (finding 4) kept as a hardening inventory — and made application setup one guided sequence (see [application setup](../APPLICATION-SETUP.md)) and object IDs read as friendly names.

The independent safety review F1–F5 merged as PR #9 (`1021a23`). Read [the bounded change/validation record](SAFETY-REVIEW-2026-09-21.md): strict CA material-baseline checks, corrected enrolment contracts, blocked generic Autopilot assignments, beta-aware recovery verification, conservative application deployment assessment and fixed array caveats. Read [REVIEW-FIXES](REVIEW-FIXES.md) before testing or upgrading existing evidence. Historical catalogue and evidence compatibility remain constrained; do not adopt drift or replay Unknown writes.

Claude's review of that merge, and the fixes it produced, are in [POST-MERGE-REVIEW-2026-09-21.md](POST-MERGE-REVIEW-2026-09-21.md), merged as PR #12 (`e188d9e`): the headless runner now reads stored records through `EvidenceStore`, and the portable package ships a named operator document set rather than all of `docs/`. PR #13 (`55cb822`) brought the architecture roadmap back in step with the code.

Astra then reviewed `55cb822` end to end and confirmed the build, the 542 synthetic tests, the package and the offline renders, with four documentation findings and no reproduced safety bypass. Its most useful result is one the source-scan conformance tests could not give: an executed parity check of the real `Workspace` assessment against the real CLI report, identical but for `id`, `assessedAt` and `assessedBy`. Those four findings are fixed in PR #14.

PR #15 finished the application: its first test project (`BDIT.TenantToolkit.App.Tests`, net8.0-windows), accessible names on every operable control, and roadmap items W0, W2 and 9d. PR #16 finished 9e and S1.

**S1 was a real defect, and it was not only on the Plan page.** Measured at the minimum window size, a WPF DataGrid with a star-sized column squeezes *every* column towards 20px to avoid scrolling — fixed-width ones included. The Plan page drew its Select checkboxes and its Explanation column at 20px, and five other tables lost columns the same way. `Infrastructure/ColumnSizing`, switched on from the one DataGrid style, holds every column to its designed width and its header; a table that does not fit now scrolls. The harness measures every table on every page and fails the build if a column is squeezed.

**Correction to PR #15.** Its accessible-name check enforced nothing. Both interface checks skipped elements whose `IsVisible` was false, and WPF reports `IsVisible` only inside a window that has been shown — the harness shows its window only at the end, for the shutdown test, so everything was skipped and both checks passed on nothing. The names PR #15 added were all present; nothing held them. PR #16 replaced `IsVisible` with an off-screen test, and the harness now refuses to pass if either check inspected nothing. This was found by pushing the new column check before its fix, expecting it to fail, and seeing it pass.

**PR #17 made the interface checks answer "does it work", not only "does it draw".** CI fails on any compiler warning. The harness now also measures text and input contrast, clipping, keyboard reach on a shown window, every local command (from a register that fails the build when a command is added without being classified), the final tenant confirmation, and a third window size — 1180x640, which is what a 1080p laptop at 150% scaling offers. Every check was pushed before its fix and observed failing. The largest findings were near-invisible input edges on every page, a window that did not fit that laptop, and six pages that hid content when short.

**Next build work: [NEXT-RELEASE-PLAN.md](NEXT-RELEASE-PLAN.md)** — standard 2026.09.12, the Exchange and Purview sections and the two generated build standard documents, with the maintainer's decisions recorded so they are not reopened.

**Next validation: human-authorised disposable-tenant acceptance.** No live validation has been performed — no tenant authentication, no Graph call, no device. The synthetic tests prove safety logic against a scripted client, not that Microsoft accepts these payloads. `docs/LIVE-VALIDATION.md` is the staged checklist and is unexecuted. Do not start another automation batch before it. What remains needs a person rather than a model: [the fifteen-minute checklist](../TESTING-THIS-BUILD.md) covers 150% scaling, keyboard only, Narrator and readability. Contrast, keyboard reach, clipping and accessible names are now machine-checked; how they feel in use is not.

## Historical handover (superseded where above differs)

Preview.12 is the current state of `integration` (Claude, pull requests [#3](https://github.com/Willzy12h/M365-Buildstandards/pull/3) to [#7](https://github.com/Willzy12h/M365-Buildstandards/pull/7), all merged; branch `claude/github-repo-access-ygieh2` is level with `integration` and holds nothing unmerged). Standard 2026.09.10, 50 controls, 42 recipes, 46 reporting automatically.

Since preview.8: client inputs are entered as a generated form rather than one hand-written JSON object, and a reviewable input the client has not supplied falls back to a dated default and warns instead of blocking the control, while identity inputs still block. Policies target the built-in All users and All devices populations, created groups are down to four (user and device exclusions, unenrolled mobile users, pilot devices), and every Conditional Access candidate also excludes the user exclusion group while keeping the verified operator's direct exclusion, which is what actually prevents a lockout. A client-facing build standard document is generated from the catalogue. Windows Hello is configured end to end rather than only enabled.

**Nothing here has been accepted by a live tenant.** CI on Windows is the only build and test evidence throughout; the authoring environment cannot run a .NET SDK. The next step is an independent review by Astra/Codex, then the human's own testing against a disposable tenant. The Windows Hello values in 2026.09.10 are the least certain part: the composition convention and the two device-scoped paths were confirmed from mirrored Microsoft documentation because learn.microsoft.com is unreachable from the authoring environment, and should be re-checked in the Intune portal.

Preview.8 (Claude, [PR #3](https://github.com/Willzy12h/M365-Buildstandards/pull/3), branch `claude/github-repo-access-ygieh2`) closes the largest remaining automation gap: the directory objects every other control depends on. Standard 2026.09.8 provisions seven security groups and the office IP named location through the existing Plan → Deploy path, so an engineer no longer has to build them by hand before any assignment or Conditional Access recipe can be used. A new creation guard constrains those writes to empty, assigned-membership security groups and untrusted IP named locations with client-confirmed CIDR ranges. ID-003 is now assessed from Global Administrator membership through a new `directoryRoles` capture. 53 controls, 45 recipes, 49 reporting automatically. **Deployment mode now requests Group.ReadWrite.All, so both registrations need administrator consent again**; assessment mode is unchanged and stays read-only. CI on the PR head remains the only build and test evidence.

Preview.7 (Claude, [PR #3](https://github.com/Willzy12h/M365-Buildstandards/pull/3), branch `claude/github-repo-access-ygieh2`, which merges PR #2 @ `f5859da`) finishes the reporting side of the standard and closes the review findings against PR #2. Standard 2026.09.7 assesses ID-002, ENR-001 and CMP-001 from captured evidence through equivalence signals (40 of 45 controls now report automatically; the reviewed tenant actions that change them are unchanged). A cancelled partial capture records untried collections as *Not attempted*; terminal evidence saves in the LAPS, reviewed-change, package and recovery services no longer mask the ending exception. `ReviewedChangeSafetyTests` is the first synthetic coverage of the preview.6 write surface. **CI on the PR head is the only build and test evidence for this increment** — the authoring sandbox could not run a .NET SDK; read the checks for the exact commit. No tenant, consent or write was performed. The four controls without automation (ID-001, ENR-005, ENR-006, UPD-001) are manual by nature and recorded as such in [automation coverage](../AUTOMATION-COVERAGE.md).

Preview.6 implements the expanded policy scope and the additional source review. Standard 2026.09.6 contains 45 controls and 37 candidate recipes. Four further controls use reviewed tenant actions (authentication methods, MDM enrolment, secure compliance, Autopatch); four use read-only readiness plus engineer/external steps (emergency/admin identities and Apple/Google ownership). This is not 45 unattended automations. See [coverage and required inputs](../AUTOMATION-COVERAGE.md).

Policy automation UI includes local imports and reload, typed tenant inputs, Entra LAPS preview/enable/re-verify, exact tenant-change/activation/assignment previews, containment, package publication and readiness. `.intunewin` publication journals every Graph stage, retains returned IDs and keeps encryption keys/storage URLs out of evidence. Candidate creation and later activation are separate.

Validation for this increment is source/diff review and Windows Release compilation only. No tests, new GUI acceptance, package installation or live Graph actions were run, per the user's instruction to focus on implementation. Existing CI remains enabled. Do not describe the prior 295-test result as covering preview.6.

Next concrete acceptance work: approve required client values/exports/packages from the coverage document, then validate one candidate per newly supported Graph resource and each consequential workflow in an explicitly authorised disposable tenant. The application remains a preview until that work is complete. No tenant operation is authorised by source-code approval.

Preview.4 adds WAM sign-in, exact registered admin-consent callback, explicit existing-app repair, custom icon/GitHub metadata, approved engineer assignment and direct setup-to-connect handoff. Standard 2026.09.4 adds the supplied BitLocker candidate and optional long paths (14 recipes); other policy defaults require user input. See [application setup](../APPLICATION-SETUP.md) and [device automation](../DEVICE-AUTOMATION.md). Previous source namespaces and standard 2026.09.3 remain for compatibility and evidence. All new Microsoft interactions still require authorised live validation.


Preview.3 addresses Claude's review of `6712d8e`: explicit not-sent write classification, read-only re-verification, acknowledged historical evidence reconciliation, cancellable deployment reads and bounded follow-up capture. See [review dispositions](CLAUDE-REVIEW-RESPONSE.md). Original records remain unchanged; unknown modern requests remain blocked.

[PR #2](https://github.com/Willzy12h/M365-Buildstandards/pull/2), branch `astra/engineer-workflow`, targets integration and depends on [PR #1](https://github.com/Willzy12h/M365-Buildstandards/pull/1). Until #1 is merged, #2 includes its imported source. Both source repositories and Claude's coordination branch remain untouched. Merge through review.

The user authorised source development of app setup, account/exclusion automation, usability, selective recovery and licence visibility, and explicitly approved PR publication. No live tenant, consent or registration actions were performed in this development session.

## Implemented in the preview

- Complete durable before-evidence validation at the execution boundary, all plan/input bindings, private execution copies, licence and candidate-reference readiness.
- Truthful terminal states, separate write acceptance/readback, no write retry, single-use plans and unresolved-write protection.
- Forced silent token renewal after a GET 401; second rejection fails. Writes are never replayed.
- Missing properties remain different from JSON null in assessment and verification.
- Isolated delegated app-setup wizard: permission preview, explicit approval, two registrations/SPs, browser consent and actual grant/configuration/direct-assignment validation.
- One-time connections, optional saved profiles, account lookup and exclusions with purpose/reason/object-ID evidence. No name-based admin exemptions.
- Coherent WPF navigation and identity/access header, plan/exclusion review and result semantics; cooperative stop/shutdown; background report exports.
- Corrected report claims; historical run integrity preserved for records without acceptance metadata.
- Selective recovery: durable change register, exact returned IDs and before/after values, confirmed-creation deletion, latest inactive-update restoration and explicit CA disablement with drift review. Recovery/deployment share a tenant write lease. See [recovery](../RECOVERY.md).
- Main-page subscription counts, searchable assigned users and conservative direct-user policy scope checks. Unknown memberships and entitlement remain explicit review items. See [licensing](../LICENSING.md).

See [testing evidence](TESTING-EVIDENCE.md) for actual checks. Source and synthetic tests do not establish live acceptance.

## Remaining acceptance work

| Priority | State | Required verification / next work |
|---|---|---|
| P1 | Implemented but live-unverified | Authorised tenant: bootstrap sign-in, app creation, consent propagation, direct engineer assignment, separate assessment/deployment sign-in |
| P1 | Implemented but live-unverified | Capture pagination/details, actual permissions/licensing, all 37 candidate recipes, readback and supported inactive PATCH cases |
| P1 | Connected-session-unverified | Sign-in/capture/setup/policy cancellation and shutdown, network failure and recovery |
| P1 | Implemented; synthetic tests only | In an authorised disposable tenant, create a disabled/unassigned candidate, remove it and confirm absence; test restoration and reviewed CA disablement with full evidence |
| P1 | Manual recovery for unknown acceptance | Accepted writes now have read-only re-verification. Truly ambiguous requests still require separately reviewed reconciliation; no automatic rollback/adoption/delete-and-retry |
| P2 | Limited validation | Group-based assignment, PIM/custom-role effectiveness and Intune RBAC are not inferred from direct role/assignment reads |
| P2 | Acceptance pending | Human keyboard, screen-reader, high-DPI and representative large-tenant usability testing; offline rendering is narrower evidence |
| P2 | Manual-only scope | 8 of 53 controls have no creation recipe. ID-002, ENR-001, CMP-001 and ID-003 are assessed from evidence; ID-001, ENR-005, ENR-006 and UPD-001 need an engineer or vendor portal. Add a recipe only with reviewed settings, supported API behaviour, safeguards and tests |
| Later | Deferred | App-only identity, automatic policy activation/assignment, group membership management, cloud interface and extra workloads |

The next concrete acceptance task is **application setup → consent → engineer assignment → read-only capture → licence/user checks**, followed by candidate creation and selective recovery in an explicitly authorised disposable scope. The user plans to test the expanded preview. Repository approval does not authorise tenant operations.

Additional creation recipes remain follow-on work. Current manual controls often need client choices, provider dependencies or API validation; do not invent minimum OS versions, Defender risk thresholds or device targeting to claim broader automation. The four-policy SME batch is implemented as candidates; keep the current 45 recipes explicit.

Model preference: advise the user when a different model suits the next task. Astra with High reasoning is recommended for recovery, authentication, deployment safety and architecture; reserve faster models such as Spark for bounded low-risk presentation changes. This is guidance, not a claim that a model setting was changed.

## Product decisions

CA candidates remain disabled with stored targeting and exclusions. Stored targeting is separate from enforcement; do not clear it to interpret the historical word "unassigned". Creator and chosen exclusions persist if a policy is later enabled. Intune candidates remain unassigned. Activation requires a separate reviewed workflow and tenant scope.

One-time connection means the profile is not saved; local audit evidence is retained. Disconnecting setup removes temporary tokens but does not revoke Entra consent. Business Premium remains the normal SME baseline; the planner uses captured service-plan evidence and blocks unavailable or unknown requirements.

## Status vocabulary

Implemented and verified names the actual check. Implemented but unverified identifies missing verification. Planned is agreed work; Candidate is a proposal; Deprecated is superseded behaviour; Known issue is an observed defect. Passing tests do not verify sign-in, consent, live Graph or recovery.

## Waiting on William

- Run the prepared Stage A/Stage B live journeys and new report-query acceptance; provide sanitised results. No agent tenant action is authorised.
- Perform one interactive Windows `bdit.cmd` run, human accessibility/physical scaling and second-engineer journeys.
- Choose client banner colours and name product/release/security/support/evidence owners; set evidence/deferral and servicing policies.
- Confirm candidate signing/distribution and security-owner application-control approval; configure repository/protected release settings.
- Approve promotion to main, tags/releases/publishing and final 1.1.0 separately after reviewing the finished candidate record.
