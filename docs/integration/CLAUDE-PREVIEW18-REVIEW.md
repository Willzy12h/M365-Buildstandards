# Claude independent review — Preview.18 and PR #19

**Reviewer:** Claude (independent, review-documentation only). **Date:** 6 October 2026.
**Request:** [CLAUDE-PREVIEW18-REVIEW-PROMPT](https://github.com/Willzy12h/M365-Buildstandards/blob/astra/release-2026-09-12/docs/integration/CLAUDE-PREVIEW18-REVIEW-PROMPT.md) on `astra/release-2026-09-12`. This report follows that prompt.

This is a review: it proposes changes and does not make them. No runtime code, standard, workflow or Astra-claimed file was changed. Nothing was merged or published, and no tenant, consent, DNS, module or device operation was performed. Findings are proposals for the integrator (Astra/Codex), who records each disposition in `PRODUCT-FEEDBACK-REGISTER.md`. The A–G source approval is not reopened.

## Implementation status (added after the review, at William's request)

William then asked Claude to fix the recommendations. They are implemented on this branch, on top of PR #19 head `2668d00`; see the `Unreleased` CHANGELOG entry. Status:

| ID | Status |
|---|---|
| 01, 02, 03, 04, 05, 06, 08, 11, 13, 14, 15, 16 | Implemented with tests |
| 07 | Fixture only; lineage messaging waits for the PR #20 merge (INT-051) |
| 09 | Workflow split and SHA pins done. Adding required reviewers to the `release` environment and enabling immutable releases are repository-owner settings |
| 12 | Settings, Deviations and Manual-checks renders added. The "primary export off-screen at 1180×640" observation is **withdrawn**: that render scrolls the page deliberately to show the expander, so it is not evidence of a defect |
| 10, 17 | Not implemented. 10 needs the privacy owner to approve the field list; 17 is a post-merge decision row |

Two corrections from implementation:
- CLA-02 affected **20** controls, not 14. The six release-identity controls (ID-004…008 and UPD-001) were also labelled "Manual".
- `CHANGELOG.md` has no Preview.18 entry. That is for the release owner to add.

Checks executed with the source-built .NET SDK 10.0.112 on Linux (a scratch copy of `global.json`, never committed):
- Full solution build with `-warnaserror`, including WPF App, App.Tests and the UI harness via Windows targeting: 0 warnings, 0 errors.
- Engine tests: **917 passed**, 0 failed. Baseline before changes was 902.
- PowerShell 7.6: `Test-StandardsManifest.ps1` passes, and fails on a changed, unlisted or missing catalogue; the ZIP writer emits `/` names.
- New tests were checked to fail on the pre-fix code.

Windows CI on the final head `529aeaf` (run 37526837469): all jobs passed. I read the whole build-job log, which shows:
- Engine tests: 917 passed.
- App tests: 113 passed. This includes the new adoption-overwrite test.
- Native WPF harness: 42 layouts, 79 commands (including the new Verify restored folder), 0 binding issues, 0 tenant calls.
- Windows PowerShell 5.1 packaging, with the committed manifest verified.
- Fresh 297-file package: startup, shutdown and right-click Copy.

Code-review corrections made after the first implementation:
- Restore hashes and extracts one read-locked stream.
- Adoption never deletes recursively.
- Adopted clients load at once, so a new client cannot overwrite them; Adopt is offered only with no client selected.
- A malformed trusted digest is refused.
- Stored-evidence time now includes later DNS refreshes.

None of this is live or human acceptance.

## 0. Exact revisions reviewed

| Item | Identity | How it was checked |
|---|---|---|
| Released application source | `10808de054a195c73ae742a0ae2f6240a318e421` (tag `v1.1.0-preview.18`) | Read-only worktree; tag resolved to this commit |
| Latest PR #19 head | `2668d00b955317f90840332198791ee66d3d2b86` | Read-only worktree. The only changes after `10808de` are `build.yml` (publish job), `build/Publish-ValidatedPreview.py`, and docs, so no runtime change is attributable to the released ZIP |
| PR #20 contract head | `674b1ba6b638a4cb43da74d3217cfff0aac14bd2` | **Open, not merged** (`mergeable_state: clean`) when this review was written. INT-049–051 remain proposals |
| Integration base | `5cadd3a070b0e308bdffc327dfc7954bd69b9e83` | Base for PR #19, PR #20 and this branch |
| Released ZIP | `M365-BuildStandard-Tool-1.1.0-preview.18-win-x64.zip`, 62,318,077 bytes, SHA-256 `9155fad4…3495cc` | Downloaded from the release and hashed locally. Matches the release notes, `SHA256SUMS.txt`, `RELEASE-RECORD.json`, the GitHub asset digest and the build log of run 37515288607 |
| Validated source run | [37515288607](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/37515288607) (push, `10808de`, success) | Read the whole build-job log (560 lines): 0 warnings; **902/902 Engine**, **112/112 App** passed; WPF `Status=Passed; pagesAtSizes=42; commandsPressed=78; bindingIssues=0; tenantCalls=0`; PowerShell 5.1 parser negative passed; "Verified 297 extracted files" |
| Publisher run | [37517705135](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/37517705135) (dispatch, `2668d00`, attempt 3, success) | Job list and check runs read |
| Open PRs across the integrated repository | #19 (draft) and #20 (ready, unmerged) | GitHub API. The preserved Astra and Claude source repositories are outside this session's GitHub scope, so I did not list their PRs; that limit is recorded here |

### Evidence classes used in this report

- **source**: code or document inspection at an exact revision.
- **artifact**: hashing and inspection of the published release bytes (offline, synthetic).
- **synthetic**: the CI test and harness results I read from the exact-run log.
- **native**: the Windows WPF renders shipped in `native-ui-review.zip`, which I inspected visually.
- **human / live**: none were performed, and nothing below claims either.

### Checks I personally executed

1. Downloaded all seven release assets and ran `sha256sum -c SHA256SUMS.txt`: all OK. The ZIP digest equals the pinned value.
2. Verified all 11 historical and current standards against `standards/manifest.json` at `10808de`: all OK. The packaged catalogue bytes are identical to the source, and the default catalogue digest is `124a0334…1302f`, which matches `RELEASE-RECORD.json`.
3. Confirmed the default catalogue structure with a Python script: 93 controls and 61 payloads. 10 Conditional Access payloads are `state: disabled`. Areas: Intune 58, Entra 25, Exchange 8, Purview 2.
4. The definition export's `.json` is byte-identical to `standards/2026.09.30.json`. The export ZIP digest matches its sidecar.
5. Python `zipfile` inspection of the application ZIP shows **288 of 297 entry names use `\` separators**.
6. Visually inspected these native renders: `standard-1480x940`, `standard-definition-formats-1180x640`, `plan-review-1180x640` and `quick-connect-confirmation-1480x940`.
7. **I did not run the .NET tests.** The environment's network policy denied the .NET SDK download (`builds.dotnet.microsoft.com`). App, WPF and PowerShell checks also require Windows. Test counts above are read from the exact CI run, not re-executed.

## 1. Verdicts

### Offline prerelease scope: **ready for the stated offline prerelease scope**

The three most consequential reasons:

1. **Provenance is intact and checkable.** The published ZIP, sidecar, release record, CI log and pinned publisher constants all agree on source `10808de`, run 37515288607 and SHA-256 `9155fad4…`. The publisher (`Publish-ValidatedPreview.py`) promotes original bytes, refuses to repoint tags, and refuses to replace a published release. Later commits are separately identified.
2. **Preview.18 adds no new authority.** The new surfaces are catalogue export, shared read-only assessment loading, previewed support metadata, and byte-preserving backup with separate restore. None adds a write, authentication or approval path. Restore never makes old captures, plans or approvals live. A restored Unknown write still blocks (AST-20261006-04). The CLI remains free of write-capable types.
3. **Claims are honestly scoped.** The release notes, `RELEASE-RECORD.json` (`pending[]`), the capability matrix ("Not run live") and the guides consistently present this as an unsigned synthetic preview with live, human and contract gates open.

**Corrections are still needed before Preview.19.** None withdraws the preview, but three affect trust in what the product reports:

- Assessment silently accepts a modified primary snapshot (CLA-20261006-01).
- The published capability matrix mislabels 14 evidence-assessed controls as "Manual" (CLA-20261006-02).
- The build re-blesses whatever catalogue bytes are present instead of verifying them (CLA-20261006-03).

### Finished product / GA: **insufficient evidence**

- There is **no live acceptance of anything**: WAM/consent/grants, Graph payload acceptance, readback, RBAC, device effectiveness or recovery. `CONTROLLED-ACCEPTANCE.md` is unexecuted.
- **B and C (persistent job, observations, legacy disposition, lineage, cutover) are not implemented.** Their contracts (PR #20) are unmerged. Without them the legacy-backfill and repeat-review journeys rely on engineer memory and a handoff checklist.
- **The business decisions are unmade:** named owners, support matrix, storage/retention, exception-closure policy, and signing or unsigned distribution policy. Neither the R11 capability matrix nor the two recorded business journeys exist yet in their accepted form.

## 2. What works (verified in the class stated)

- **Release identity** (artifact + synthetic): pinned source/run/hash agree across the release record, notes, log and asset digests. `VERSION.json` records the SDK `10.0.401`, runtime `10.0.12`, `selfContained` and the source commit. `DEPENDENCIES.json` and `licenses/` ship in the package.
- **Catalogue immutability for historical releases** (source): `.3`–`.12` are pinned by publication digest in `tests/BDIT.TenantToolkit.Tests/Release20260930Tests.cs:52-72`, independent of the regenerable manifest.
- **Definition export (R17)** (artifact): exact JSON bytes, a loader-compatible single-file manifest, defaults/settings HTML and Markdown, manual guides and a capability matrix. The README states plainly that this is not evidence, not a plan and not an installer. Placeholders such as `{{officeLocationId}}` are labelled "Client input required; unresolved", and fixed values are labelled as fixed.
- **Backup and restore engine** (source + synthetic):
  - path, type, reparse-point, Windows-reserved-name, duplicate and size checks;
  - exact-coverage checksum list;
  - staged restore into a new folder only, with the active data folder refused;
  - re-hash after backup to detect concurrent change;
  - MSAL caches and lock files excluded.
  
  This is careful code.
- **Supplemental Exchange parsing** (source + synthetic): strict nested parse, rejection of duplicate and unknown members, tenant binding, integrity check of wrapped snapshots. The CLI refuses malformed raw or wrapped input with exit 2 (AST-20261006-09).
- **Safety invariants in the planner** (source):
  - no adoption by name (`DeploymentPlanner.cs:241-246`);
  - overlap blocks creation (`:253-262`);
  - drift blocks update (`:291-296`);
  - assigned or unknown-assignment objects are Manual (`:284-290`);
  - directory prerequisites are creation-only (`:310-315`);
  - CA forced to disabled, with emergency, operator and group exclusions injected (`:185-224`).
  
  The CI `standard` job independently rejects enforcing CA state or payload assignments.
- **Native UI** (native, offline): 42 page/size records, 78 commands, zero binding issues, contrast, clipping and keyboard checks, and a write-capable banner ("DEPLOYMENT ACCESS — WRITES POSSIBLE") visible in every render I inspected.

## 3. Prioritised findings

Each ID is stable. Priority follows the register scale: P0 release or backfill trust, P1 core workflow, P2 usability or small finish, P3 optional. "Rev" is the revision at which I observed the problem. Line numbers are at that revision.

| ID | Type · priority | Rev / location | Finding and user impact | Evidence (class) | Recommended change · alternative/tradeoff | Acceptance incl. negative case | Package · dependency |
|---|---|---|---|---|---|---|---|
| **CLA-20261006-01** | Confirmed defect · **P1** | `10808de` `src/BDIT.TenantToolkit.Cli/Program.cs:132-156`; `src/BDIT.TenantToolkit.Engine/Evidence/EvidenceStore.cs:154-171`; `src/BDIT.TenantToolkit.App/Services/Workspace.cs:509-528`; `src/BDIT.TenantToolkit.Core/Models/Assessment.cs:130-149`; contrast `Assessment/AssessmentContext.cs:43-48` | **Assessment does not enforce or surface integrity of the primary Graph snapshot.** The CLI deserialises `--snapshot` with no digest check. The desktop `LoadSnapshot` only logs a warning on mismatch, and `LoadStoredSnapshot`/`RunAssessment` proceed. `AssessmentResult` has no integrity field. The supplemental Exchange snapshot, by contrast, is **refused** on mismatch. **Impact:** a hand-edited or corrupted historical capture yields a normal-looking HTML/JSON/XLSX report, including "snapshot complete" and "Compliant". This undermines repeat-review and legacy evidence, and the trust asymmetry contradicts the "strict supplemental evidence" claim. Deployment is **not** affected: `RequireDeploymentSnapshot` re-verifies. | source | Add one shared loader, `AssessmentContext.LoadPrimary(file, expectedTenant)`, that verifies `IntegrityDigest`. Record the integrity state (Intact / Modified / NoDigest-legacy) in the result and show it as a banner in UI and reports. The CLI refuses Modified (exit 2) and labels NoDigest as "unverified". **Alternative with no schema change:** add the state to `Limitations` instead of a new field. Tradeoff: an extra field on a persisted assessment record may need an integrator compatibility ruling. | Intact fixture: unchanged desktop/CLI parity. One-byte-modified fixture: CLI exit 2, and the desktop shows a "Modified evidence" banner with no "Compliant" summary unqualified. Legacy no-digest fixture: report says "integrity not recorded". The test must fail if the verify call is removed. | D (R08 follow-on). Autonomous under A–G; no PR #20 dependency |
| **CLA-20261006-02** | Confirmed defect · **P1** | `10808de` `src/BDIT.TenantToolkit.Engine/Reports/CapabilityDocuments.cs:18-28` (col. 2 = `c.Assessment.Mode`); routing at `Assessment/AssessmentEngine.cs:162,171-175`, `Assessment/ExchangeAssessment.cs:10-14` | **The generated capability matrix (R11's basis) labels 14 evidence-assessed controls "Manual":** ID-002, ID-003, ENR-001 and CMP-001 (manual mode with equivalence signals), plus EX-001…008 and PUR-001/002 (assessed from an Exchange/Purview capture). It contradicts `AUTOMATION-COVERAGE.md` and `HANDOVER.md` ("assessed from evidence"). The Activation, Effective and Acceptance columns are constant text, so the matrix carries no per-control licence, scope, collection, owner or evidence reference. **Impact:** standards and release owners read the wrong automation coverage from a published release asset. R11 (P0) cannot be built on it as-is. | source + artifact (`capability-and-access-matrix.md` in `standard-definition-exports.zip`) | Derive "Read assessment" from actual routing: *Settings comparison* / *Equivalence evidence* / *Exchange–Purview capture* / *Manual only*. Add per-row licence, collection, required read/write scope, manual-guide availability and prerequisite IDs. Keep live acceptance as "Not run" until a checked-in acceptance ledger supplies evidence references. Tradeoff: a wider table; consider a CSV twin. | ID-002, ID-003, ENR-001, CMP-001, EX-*, PUR-* are not labelled "Manual only". **Negative:** ID-001 (manual, no equivalence) stays "Manual only". Every row's licence equals the catalogue's. | A/R11. Autonomous |
| **CLA-20261006-03** | Confirmed limit · **P1** | `10808de` `build/Build-Portable.ps1:43-45,63,113`; `build/Update-StandardsManifest.ps1`; `tests/…/Release20260930Tests.cs:52-72` | **The build regenerates the tracked standards manifest instead of verifying it, and the now-published default 2026.09.30 is not digest-pinned.** The packaged `standards/manifest.json` (SHA-256 `7487fbf4…`) differs from the committed file (`1531eaa8…`); only `generatedAt` and `generatedBy` differ, and the file digests are equal. Yet `VERSION.json` says `sourceTreeDirty: false`, because cleanliness is captured before the build mutates a tracked file. A PR that edits `2026.09.30.json` and reruns the manifest script passes every check. **Impact:** "immutable published catalogue" for the shipping default depends on review vigilance alone. | artifact + source | Build should **verify** committed manifest digests against the staged files and fail on any difference, then ship the committed bytes. Add `["2026.09.30"]="124a0334…1302f"` to the pinned publication list. Tradeoff: a new release needs a deliberate pin edit, which is the point. | Changing one byte of `2026.09.30.json` while also regenerating the manifest fails CI. Packaged `manifest.json` bytes equal the committed bytes. `sourceTreeDirty` is re-evaluated after packaging. | F (R12). Autonomous |
| **CLA-20261006-04** | Observed limit + proposal · **P1** | `10808de` `docs/WORKSPACE-CONTINUITY.md:15,23`; `src/BDIT.TenantToolkit.App/ViewModels/SettingsViewModel.cs:29-31`; `src/BDIT.TenantToolkit.Engine/Evidence/WorkspaceBackup.cs:61-109` | **Handoff and upgrade end in a manual file copy with no verification tool.** Restore writes `data/` plus `README.txt` and `SHA256SUMS.txt` into a separate folder, by default under `reports/restored-evidence-*`, mixing sensitive evidence with shareable reports. The guide then says "copy the verified restored `data/`… Check all original hashes before opening it", but no command does that check. A partial copy that drops `runs/` or `recovery/` loses **unresolved-write blockers**, and that is a safety-relevant consequence. | source | Add **"Adopt verified backup into this empty workspace"**. It requires the workspace to be disconnected and idle, with `data/` absent or empty, and a trusted archive digest (see 05). It copies through staging, re-hashes every file in place, and writes an adoption record. Add a read-only **"Verify workspace against manifest"** in the app and as `bdit verify-workspace`. Move the default restore location out of `reports/` (e.g. `transfers/`). Tradeoff: one more guarded command, versus documenting a manual procedure. | Adoption into a non-empty `data/` is refused. A one-byte-changed file is refused with no partial adoption. After adoption a pre-existing Unknown run still blocks a fresh plan and the old plan still refuses. No session, acknowledgement or approval is restored. | E (R06). Autonomous; no new persisted contract |
| **CLA-20261006-05** | Confirmed limit · P2 | `10808de` `WorkspaceBackup.cs:16,61-89`; `SettingsViewModel.cs:29-30` | **Restore cannot take a trusted reference digest.** It checks only the archive's own `SHA256SUMS.txt`, which a tamperer can regenerate. The guide (`WORKSPACE-CONTINUITY.md:15`) asks the engineer to check the digest by hand first. | source | Add `RestoreSeparate(archive, destination, expectedSha256)` and an "Expected archive SHA-256" field. Refuse before staging on mismatch. Allow an explicit, logged "no trusted digest available" acknowledgement. | Wrong digest: refused and no staging folder is created. Right digest: restore proceeds. | E. Autonomous |
| **CLA-20261006-06** | Observed limit / test hypothesis · P2 | `10808de` `Assessment/ExchangeAssessment.cs:19-20` vs `AssessmentEngine.cs:47-48` | **Assessment is not reproducible for historical evidence.** Exchange/Purview findings become "over 24 hours old… UnableToAssess" relative to the wall clock. The Graph snapshot is assessed regardless of age. Re-running a stored Graph+Exchange pair a day later (desktop history, or the CLI on another machine) changes 10 findings. The R08 parity claim ("identical except volatile metadata") therefore holds only within 24 hours of capture. | source (inference about the parity impact) | For stored evidence, measure Exchange age relative to the paired Graph `CapturedAt` (e.g. both within N hours), and label the report "historical evidence as of …". Keep the wall-clock freshness rule only for live planning. Tradeoff: two notions of freshness to explain. | The same Graph+Exchange fixture assessed with the clock at +1h and +72h yields identical findings, labelled historical. **Negative:** Exchange captured 72h before the Graph snapshot is still flagged stale. | D. Autonomous |
| **CLA-20261006-07** | Observed limit + test hypothesis · P2 (**links R03**) | `10808de` `Core/Models/Mapping.cs:9-29`; `AssessmentEngine.cs:127-131,241,281`; `DeploymentPlanner.cs:186,247-252,272-276,310-315`; catalogues `.10` vs `.30` | **Reused PRE IDs produce misleading, though safe, output.** In `.10`, PRE-004 is *GRP – Pilot Devices* and PRE-005 is *LOC – M365 Office* (`namedLocations`). In `.30`, PRE-004 is *GRP – MAM Only Users* and PRE-005 is *GRP – Pilot Devices*. Mappings are keyed by control ID only. For a tenant built on `.10`, under `.30`: assessment marks the Pilot group as "Owned" for the MAM control; PRE-005 says the owned object "is no longer present" (it is present, as a named location); the planner returns Manual or Conflict with unrelated reasons. **No unsafe write** follows, which confirms R03's "no unsafe write demonstrated". There is no synthetic fixture for this path (no test references a `.10` mapping under `.30`). | source | Add the fixture now (test only). After PR #20 merges, make it the first INT-051 slice: when `mapping.Release`'s catalogue defines a different collection or name for the same ID, project `NeedsReview (lineage)` with the old/new meaning instead of "missing" or "drift". Do not rebind mappings. | Fixture: `.10` mappings for PRE-004 and PRE-005 under `.30` give no Create or Update and no "Owned" attribution to the wrong requirement, and show a lineage message. **Negative:** unchanged-ID controls (e.g. CA-001) keep the current NoChange/Drift behaviour. | C (R03). **Waits for PR #20 merge** (message behaviour); the fixture is autonomous now |
| **CLA-20261006-08** | Confirmed defect (text) · P2 | `10808de` `AssessmentEngine.cs:281` | **Wrong explanation for toolkit-owned partial matches.** The reason "A safe candidate is created with the deploying operator excluded, so this is expected until that exclusion is removed" is used for **every** collection, including groups and Intune policies that have no operator exclusion. An engineer may dismiss real drift as expected. | source | Use the operator-exclusion wording only when `ConditionalAccessSafety.IsConditionalAccess(def)` is true and the only difference is the recorded operator exclusion. Otherwise say "differs from the recipe; review". | An Intune owned object with a changed setting gives a reason without "operator". **Negative:** a CA owned candidate differing only by the operator exclusion keeps the current wording. | D. Autonomous |
| **CLA-20261006-09** | Proposal (process/security) · P2 | `2668d00` `.github/workflows/build.yml:6-14,21,189,226,258-297`; release API `immutable:false` | **Publication controls are weaker than the documented intent.** (a) The "manual release-owner approval only" publish job has no `environment:` with required reviewers; anyone with write access can dispatch it with `contents: write`. (b) Dispatching it creates **skipped** `build`/`standard`/`secrets` check runs on the same SHA (observed on `2668d00`, run 37517705135), and GitHub treats skipped runs as satisfying required checks. (c) GitHub immutable releases are off, so published assets remain replaceable. (d) Actions are tag-pinned, and the runner warns that Node 20 actions are deprecated. | source + GitHub API | Move publication to its own `release.yml`, protected by a `release` environment with a required reviewer. Parameterise source, run and hash, or delete the Preview.18-specific job after use. Enable immutable releases. Pin actions by SHA and plan the v5/Node 24 upgrade. Tradeoff: one approval click per release. | A dispatch waits for environment approval. The PR head shows no skipped `build` check from a dispatch. An attempt to replace an asset on a published release is refused by GitHub. | F (R12). Repo-settings parts need the owner (human) |
| **CLA-20261006-10** | Proposal · P2 (R09 follow-on) | `10808de` `Reports/SupportBundle.cs:38-44` | **The support bundle is safe but nearly diagnostic-free:** version, framework, architecture, standard digest and control count only. The incident tree (`INTERNAL-OPERATING-MODEL.md`, step 6) asks for "page/error classification", which the bundle cannot carry. | source | Keep the default minimal. Add **opt-in, previewed** sections: OS build, display scale, last N error classifications with HTTP status and Graph `request-id`/`client-request-id` (no tenant ID, UPN or object names), per-collection status counts, timeout settings. | Seeded UPN, tenant ID, token and object-ID values never appear in any section. Opt-in text equals the preview exactly. Default output is unchanged. | E. Autonomous; business privacy owner reviews the field list |
| **CLA-20261006-11** | Confirmed doc/label inconsistency · P2 (links R10) | `10808de` `docs/TESTING-THIS-BUILD.md:7-12`; `docs/CONTROLLED-ACCEPTANCE.md:1`; `App/ViewModels/StandardViewModel.cs:104`; `ConnectViewModel.cs:365` | **Engineer guidance points at the wrong place.** TESTING-THIS-BUILD tells engineers to download **Actions artifacts** rather than the published release and `RELEASE-RECORD.json`, which encourages unpromoted builds. CONTROLLED-ACCEPTANCE is still titled "Preview.15". The UI says "61 **automated** recipes", while documents say "inert candidate recipes". | source + native (`standard-1480x940.png`) | Point to the release and its record first; keep Actions artifacts for reviewers. Retitle the acceptance proposal for the current package. Use "61 candidate recipes (inert)". | A grep check finds no "automated recipes" string. Guides name the release URL and record. | A/G. Autonomous |
| **CLA-20261006-12** | Test hypothesis / observed (native) · P2 | `10808de` `native-ui-review.zip` contents; `standard-definition-formats-1180x640.png` | **Native review coverage misses the new Preview.18 surfaces.** No render exists for **Settings and diagnostics**, where backup, restore and support live, although `verification.json` records the settings page. Deviations and Checks have no render either. At 1180×640 the export area is a scroll region nested inside the page scroll, and the primary **Export full standard set** button is off-screen. | native | Capture `settings-*`, `deviations-*` and `checks-*` at all three sizes. Add a harness check that a primary action stays reachable without nested scrolling at 1180×640. | Renders exist for those pages. **Negative:** the harness fails if the export button's bounds fall outside the viewport at the minimum size. | G. Human visual/Narrator acceptance still pending |
| **CLA-20261006-13** | Proposal · P2 (R17 follow-on) | `10808de` `Reports/StandardSpecificationDocuments.cs:45,55,87`; `Planning/DeploymentPlanner.cs:194-218` | **The defaults/settings specification shows only the raw catalogue payload per CA control.** A standards owner reading CA-001 sees `excludeUsers = {{emergencyAccountIds}}` but not the plan-time safety additions: additional exclusion accounts, `caExclusionGroupId`, the standard exclusion group and the signed-in operator. Also, "Default review checked as of <export date>" reads as if defaults were reviewed that day. | artifact + source | Add a per-CA-control "Plan-time safety additions" line. Reword to "Default staleness evaluated on <date>; last reviewed: <reviewedOn>". | CA-001 section lists the four injected exclusions. **Negative:** Intune controls show no such line. | A. Autonomous |
| **CLA-20261006-14** | Confirmed limit · P3 | `10808de` `build/Build-Portable.ps1:160`; compensation at `2668d00` `build/Publish-ValidatedPreview.py:95` | **The application ZIP uses `\` path separators in 288/297 entries** (`Compress-Archive`). This is contrary to the ZIP specification (APPNOTE 4.4.17). Windows extraction is fine (native check passed), but non-Windows verification tooling warns or misreads, and the publisher already has to normalise names. | artifact | Build the ZIP with `System.IO.Compression.ZipArchive`, using `/` names and deterministic order. Takes effect on the next release only; this release's bytes and identity stay unchanged. | Zero backslash entries. Test-Portable passes. A new provenance record is produced. | F. Autonomous |
| **CLA-20261006-15** | Test hypothesis (hardening) · P3 | `10808de` `WorkspaceBackup.cs:18-59`; `Evidence/RecoveryEvidence.cs:10-16` | **Backup asks for other tool copies to be closed but takes no per-tenant write lease.** A backup taken while another instance is mid-write captures intent without an outcome. That fails safe (Unknown stays blocking) but creates avoidable reconciliation work after handoff. | source | Attempt each tenant's `policy-write.lock` non-blockingly during backup, and refuse with a clear message if one is held. | With a lease held by another handle, backup is refused and no ZIP is left behind. | E. Autonomous |
| **CLA-20261006-16** | Proposal · P3 | `10808de` `Reports/StandardDefinitionExporter.cs` (file naming); export outputs | **Individual exports are hard to reuse.** Names are random GUIDs, and only the full-set ZIP gets a `.sha256`. A standalone JSON export loses its provenance link. | artifact | Use `standard-definition-<release>-<UTC timestamp>.<ext>`, write `.sha256` for every file, and keep the source digest in each document footer. | Every export file has a sidecar. Names sort by time. | A. Autonomous |
| **CLA-20261006-17** | Proposal on contract · P3 | `674b1ba` `docs/integration/PRODUCT-CONTRACT-DECISIONS-2026.10.06.md` ("Reviewed client scope") | **The INT-049/050 validity binding uses the full profile digest** (excluding only volatile timestamps). Editing a non-material display field, such as the client label, will push every observation and disposition to `NeedsReview` on the next repeat review. That is safe but noisy, and it risks reviewers rubber-stamping. | source | **Not a merge blocker.** After merge, record a follow-up decision listing explicitly non-material display fields. Keep the conservative default until then. | A label-only edit leaves observations valid. **Negative:** an office, targeting or input change still projects `NeedsReview`. | B/C. **Needs a decision row after PR #20 merges** |

No finding above shows a write, authentication or approval bypass. I traced the execution boundary (`RequireDeploymentSnapshot`, tenant write lease, ownership and drift checks), and the Preview.18 additions do not reach it.

## 4. Journeys

Responsibilities are labelled **[tool]**, **[engineer]**, **[client/vendor]** and **[reviewer]**.

### New client build

**Flow:** Quick Connect **[engineer]** → verify organisation, domain and account **[tool shows, engineer confirms]** → application readiness/Quick setup **[tool checks; admin approves consent]** → licences, emergency accounts and client inputs **[client supplies; engineer enters]** → capture **[tool]** → assessment **[tool]** → plan **[tool proposes; engineer selects]** → exact review and approval **[engineer + reviewer]** → inert candidates **[tool]** → activation/assignment **[separate approved workflow; client approves pilot]** → effectiveness **[engineer/device; not tool]** → recapture and handover.

**Confusing:**
- During the Quick Connect confirmation the header can still show an existing **deployment** session, so "Mode: read-only assessment" sits next to "WRITES POSSIBLE". This is visible in the synthetic render `quick-connect-confirmation-1480x940.png`. Test hypothesis: with a live write session open, a newcomer may not know which identity applies.
- At 1180×640 the plan-review detail pane shows about two lines of content (`plan-review-1180x640.png`).

**Missing:** the per-control capability matrix (CLA-02, R11) that would answer "will the tool do this, or must I?" up front.

**Smallest useful improvement:** correct CLA-02, then link each Plan row's "engineer/client/vendor must act" reason to that matrix row.

### Legacy backfill

**Flow:** capture → assess against `.30` → (missing) disposition per control → retain external coverage → create only genuinely missing candidates → cutover case → retirement.

**Today:** the safety half works. There is no adoption by name, overlap blocks creation, and drift blocks update. The workflow half does not exist yet:
- There is no disposition record (INT-050), so "retained external coverage" lives in notes or deviations.
- Reused PRE IDs produce misleading messages (CLA-07).
- Requirement change cannot be told apart from tenant drift (INT-051).

**Smallest useful improvement:** before INT-050 lands, add the CLA-07 fixture now. After PR #20 merges, ship the INT-051 read-only `.9/.10 → .30` impact report **first**, before any disposition UI. It touches no ownership and removes the most confusing output.

### Repeat review

**Flow:** select client → fresh capture → assess → compare with the previous capture (drift report) → review only outstanding work.

**Confusing:**
- Historical re-assessment silently degrades Exchange findings after 24 hours (CLA-06).
- A modified stored snapshot is not flagged (CLA-01).

**Missing:** a job that remembers what was outstanding (INT-049), and version-aware manual observations (R05).

**Smallest useful improvement:** the CLA-01 integrity banner plus the CLA-06 historical labelling. Both are read-only and need no contract.

### Second-engineer handoff and upgrade

**Flow:** custodian creates the backup → transfers it with a trusted digest → second engineer restores separately → **manual copy** into a new package's `data/` → offline check → independent sign-in → fresh capture.

**Confusing:**
- The restore default sits under `reports/`.
- The digest check is manual (CLA-05).
- The copy and verify steps are manual (CLA-04).

**Missing:** adoption with verification, and a single "what is outstanding for this tenant" view, which is INT-049.

**Smallest useful improvement:** CLA-04 and CLA-05 together. That command is the difference between "documented" and "safe by construction" for unresolved-write blockers.

## 5. Sequenced next work

| Order | Item | Who / authority |
|---|---|---|
| **Release blockers for Preview.19** (none block Preview.18 offline scope) | CLA-01, CLA-02, CLA-03 | Agent, autonomous under A–G (D/A/F) |
| **Approved A–G, no contract dependency** | CLA-04/05 (E adoption + trusted digest), CLA-06 (D), CLA-08 (D), CLA-10 (E), CLA-13/16 (A), CLA-14/15 (F/E), CLA-07 fixture only | Agent, autonomous |
| **Approved A–G, after PR #20 human merge** | INT-051 read-only lineage/impact first (absorbs CLA-07 messaging), then INT-049 jobs/observations, then INT-050 dispositions/cutover; CLA-17 follow-up decision row | Human merges PR #20; then agent |
| **Low-effort QoL** | CLA-11 wording, CLA-12 renders, stable export names (CLA-16) | Agent |
| **Business decisions** | Named owners (R15); support matrix scope; evidence storage and retention; exception-closure policy; signing versus controlled unsigned distribution; enabling a `release` environment and immutable releases (CLA-09) | William / business owner |
| **Human / live / publication authority** | CONTROLLED-ACCEPTANCE Stages A–C; Narrator, keyboard and physical scaling; newcomer walkthrough; promotion beyond prerelease | Separately authorised engineer and tenant |
| **Optional, out of scope** | R13 compiler, R14 portfolio roll-up, central storage, app-only operation | Separate scope decision; do not fold into A–G |

## 6. Paste-ready feedback-register entries

For the integrator to append under a new "Claude Preview.18 review — 6 October 2026" heading in `PRODUCT-FEEDBACK-REGISTER.md`. These are proposals, and none is ticked. The register's own `How to collaborate` rules apply.

```text
| ID | Reviewer / scope | Finding | Proposed response | Verification / remaining gate |
|---|---|---|---|---|
| CLA-20261006-01 | Claude / R08, evidence | P1 confirmed defect: primary Graph snapshot integrity not verified by CLI (Program.cs:132) and only logged by desktop (EvidenceStore.cs:168); AssessmentResult has no integrity state; supplemental Exchange is strictly refused (AssessmentContext.cs:47) | Shared verifying loader; Intact/Modified/NoDigest state in result or Limitations; CLI exit 2 on Modified; UI/report banner | Modified-byte fixture refused/bannered; legacy no-digest labelled; parity unchanged for intact. Source evidence only |
| CLA-20261006-02 | Claude / R11, R17 | P1 confirmed defect: capability-and-access-matrix labels 14 evidence-assessed controls (ID-002, ID-003, ENR-001, CMP-001, EX-001..008, PUR-001/002) "Manual"; constant columns lack licence/scope/owner/evidence | Derive read column from routing; add per-row licence, collection, scopes, guide, prerequisites | Test: those rows not "Manual only"; ID-001 stays manual. Artifact + source evidence |
| CLA-20261006-03 | Claude / R12, standards | P1 confirmed limit: Build-Portable regenerates tracked manifest (packaged 7487fbf4.. vs committed 1531eaa8..) while VERSION.json reports clean; 2026.09.30 not pinned in Release20260930Tests | Verify-not-regenerate; ship committed manifest; pin 2026.09.30 = 124a0334..1302f | CI fails on edited catalogue + regenerated manifest; packaged manifest == committed |
| CLA-20261006-04 | Claude / R06, E | P1 observed limit: handoff/upgrade ends in manual copy; no verify command; restore default under reports/ | Adopt-verified-backup-into-empty-workspace + Verify-workspace (app + CLI); default transfers/ | Non-empty data refused; tamper refused; restored Unknown still blocks; no authority restored |
| CLA-20261006-05 | Claude / R06, E | P2 confirmed limit: restore checks only internal SHA256SUMS; no trusted archive digest input | expectedSha256 parameter/field; explicit logged acknowledgement if absent | Wrong digest refused before staging |
| CLA-20261006-06 | Claude / R08, D | P2 observed limit: Exchange findings age against wall clock (ExchangeAssessment.cs:19), Graph does not; stored-evidence re-assessment changes 10 findings after 24h | Age Exchange relative to paired Graph CapturedAt for stored evidence; label historical | Same fixture at +1h/+72h identical; Exchange 72h older than Graph still stale |
| CLA-20261006-07 | Claude / R03 (duplicate evidence) | P2 observed limit + test hypothesis: .10 PRE-004/005 mappings under .30 give wrong "Owned"/"no longer present" messages; no unsafe write; no fixture exists | Add fixture now; INT-051 first slice projects NeedsReview(lineage) without rebinding | Fixture: no Create/Update, no wrong ownership attribution; unchanged-ID controls unaffected. Waits for PR #20 merge for behaviour |
| CLA-20261006-08 | Claude / assessment text | P2 confirmed defect: owned partial-match reason cites operator exclusion for all collections (AssessmentEngine.cs:281) | CA-only wording; generic otherwise | Intune drift reason lacks "operator"; CA-only-operator diff keeps wording |
| CLA-20261006-09 | Claude / R12, release | P2 proposal: publish job lacks environment protection; dispatch creates skipped build/standard/secrets checks on head SHA; release immutable=false; tag-pinned Node-20 actions | Separate release.yml + protected environment; immutable releases; SHA-pinned actions | Dispatch awaits approval; no skipped build on PR head; asset replacement refused. Repo settings need owner |
| CLA-20261006-10 | Claude / R09 | P2 proposal: support bundle too sparse for incident step 6 | Opt-in previewed redacted sections (OS build, scale, error class, request-id, collection counts) | Seeded identifiers never present; default unchanged |
| CLA-20261006-11 | Claude / R10 | P2 doc inconsistency: TESTING-THIS-BUILD directs to Actions artifacts; CONTROLLED-ACCEPTANCE titled Preview.15; UI "automated recipes" | Release-first guidance; retitle; "candidate recipes (inert)" | grep check; native render |
| CLA-20261006-12 | Claude / R10, G | P2 test hypothesis: no settings/deviations/checks renders; nested scroll hides primary export at 1180x640 | Add renders; reachability check | Renders present; harness fails when primary action off-screen. Human acceptance pending |
| CLA-20261006-13 | Claude / R17 | P2 proposal: CA spec omits plan-time exclusions; "review checked as of" wording | Per-CA "plan-time safety additions"; reword staleness line | CA-001 lists four injected exclusions; Intune none |
| CLA-20261006-14 | Claude / R12 | P3 confirmed limit: 288/297 ZIP entries use backslash separators | ZipArchive with '/' names, deterministic order (next release only) | Zero backslashes; Test-Portable passes |
| CLA-20261006-15 | Claude / R06 | P3 test hypothesis: backup takes no tenant write lease | Non-blocking lease probe; refuse if held | Held lease → refused, no ZIP left |
| CLA-20261006-16 | Claude / R17 | P3 proposal: GUID export names; no sidecar for individual files | Release+timestamp names; .sha256 per file | Every export has sidecar |
| CLA-20261006-17 | Claude / PR #20 INT-049/050 | P3 proposal (not a merge blocker): full-profile-digest binding makes label-only edits invalidate all observations | Post-merge decision listing non-material display fields | Label edit keeps validity; targeting/input change → NeedsReview |
```

Acceptance and closure rows, one per ID, all in this form:

```text
| CLA-20261006-NN | Covered by existing A–G approval (2026-10-06) except where marked "owner"/"PR #20" | Integrator to assign | None | Claude source/artifact review at 10808de/2668d00/674b1ba; no tests executed by Claude (SDK download blocked) | Pending integrator disposition | As listed in "Verification / remaining gate" | Proposed 2026-10-06 |
```

## 7. Update for William

**Verdict.** Preview.18 is fine as what it claims to be: an unsigned, offline-tested internal prerelease. I downloaded it and confirmed it is exactly the bytes CI tested (SHA-256 `9155fad4…`, source `10808de`). It is **not** a finished product: nothing has been tried against a real tenant, and the legacy-backfill/job features are still waiting on PR #20, which is unmerged.

**What already works.**
- Standard export (all 93 controls, exact JSON, printable HTML, Markdown).
- Shared read-only reporting between the app and the CLI.
- Safe backup and separate restore that never brings back old approvals.
- A careful release publisher.
- All historical standards intact.
- The deployment safety rules (no adoption by name, disabled CA, unassigned Intune, no retry of uncertain writes) are unchanged.

**What is unfinished.**
- Persistent jobs, manual-check history, legacy dispositions, lineage and cutover. These need PR #20 merged first.
- Live and human acceptance.
- Business decisions: named owners, support scope, storage/retention, exception policy, and signing.

**Top five recommendations.**
1. **Flag or refuse modified snapshots in assessment reports** (CLA-20261006-01). Today a hand-edited capture produces a normal-looking report. Deployment is already protected.
2. **Make handoff and upgrade one verified step** (CLA-20261006-04/05): adopt a backup into an empty workspace using a trusted digest, so unresolved-write blockers cannot be lost in a manual copy.
3. **Fix the capability matrix** (CLA-20261006-02). It wrongly shows 14 automatically assessed controls as "Manual", and it is the basis for your support matrix.
4. **Lock down the current standard** (CLA-20261006-03): pin 2026.09.30's digest and make the build verify, not regenerate, the manifest.
5. **Merge PR #20 when you are satisfied, then build lineage (INT-051) first** (with CLA-20261006-07's fixture). It is read-only and removes the most confusing legacy messages before any disposition screens.

**Decisions only you can make.**
- Whether to protect publishing with a GitHub `release` environment and immutable releases (CLA-20261006-09).
- Named product, release, standards and support owners.
- Evidence storage and retention.
- Whether overdue exceptions block sign-off.
- Signing versus hash-pinned unsigned distribution.

None of the CLA items needs new A–G approval except where marked "owner" or "PR #20".

**Tests I ran.**
- Hash verification of all release assets.
- Manifest verification of all 11 standards.
- Export byte-equality and ZIP-entry checks.
- Catalogue structure counts.
- Reading the full CI log of run 37515288607.
- Visual inspection of four native screenshots.

I could **not** run the .NET test suites. The environment's network policy blocks the SDK download, and App/WPF checks require Windows anyway. The 902 + 112 passes are CI's results, which I read from the exact run log.
