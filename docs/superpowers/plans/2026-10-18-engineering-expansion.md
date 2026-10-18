# BuildStandard engineering expansion implementation plan

> **For agentic workers:** use Superpowers **executing-plans** with existing workspace isolation. GPT is the implementer. Do not restart the accepted application, delegate implementation or ask for routine between-task approval.

**Goal:** implement the twelve approved vendor-comparison recommendations through bounded packages A–J, retaining truthful evidence and controlled execution.

**Architecture:** extend existing shared engine services, native WPF pages, typed registries and local evidence. Keep authentication, execution and catalogue-reader boundaries intact. No second platform or generic automation runtime.

**Tech stack:** .NET SDK 10.0.401, C# 12, native WPF, existing contained WebView2 catalogue reader, existing fixed PowerShell adapters.

**Spec:** [approved expansion design](../specs/2026-10-18-engineering-expansion.md), SHA-256 `9ee10a539f641c7f6293d15a9115fa5dc9ebfb2aab27cb708fd4617e12d1cdf7`. William approved the design and continuous implementation on 18 October 2026; its preserved proposed-status wording describes the earlier design checkpoint, not an approval backdated to then.

## Global constraints

- Base: canonical main `2e4a0635ddfdf28330f8271942d53fb2a8ca3b0a`; retain all frozen/phase/history branches and existing coordination.
- No live tenant/device/DNS operations, new service permissions, publication or automatic main acceptance. New shared schemas/contracts require a merged decision before dependent implementation.
- The current cloud checkout is isolated; use an owned branch here. Add another worktree only for an independently useful branch. Never reset or reuse broken historical worktree pointers.
- No service access from navigation. New reports never become complete deployment before-evidence. Unknown, failed, cancelled and incomplete remain distinct from empty success.
- Existing disabled CA/unassigned Intune candidates, exact approval, complete before-evidence, tenant/account pinning, no replay and uncertain-write blocking remain authoritative.
- Exchange changes stay manual/Copy-Save. macOS stays unsupported, default-off and blocked. No new UI framework/database/hosting or historical-catalogue rewrite.
- Main merge, named independent safety reviews, new permissions and human/live/release acceptance remain separate gates. Continue unaffected work. A requested review is not an acknowledged review.
- Local progress lives under `.superpowers/sdd/2026-10-18-engineering-expansion/progress.md`; material source/check/approval facts also go in the existing PR, COMPLETION-REGISTER and issue83. Local scratch is not a remote handoff.

## Review focus

1. A corrupt or inaccessible history directory must prevent a clean absence claim; exercise a path occupied by a file and a wrong-tenant record (A2).
2. An apparently valid customer CSV must not overwrite another engineer's intervening profile edit; exercise changed preview/save state (A1/A3).
3. Same-client A–B–A selection and changed evidence bytes must reject a delayed result/export (A3/A4).
4. Large or privacy-obscured inventories must not become a falsely complete or name-joined result (E/F/G/J contract and implementation tests).
5. Failed or ambiguous multi-step operations must never offer blind replay or infer verified effect (B/H, named safety review).

## Verification conventions

`DOTNET=/workspace/toolchain/dotnet/dotnet` in this cloud session; activate the pinned SDK normally elsewhere. Build current source/configuration before `--no-build`. Targeted test output and baseline logs are retained outside tracked source. Native/hosted/PowerShell/package checks run on the existing Windows workflow; Linux engine tests/cross-build are not Windows evidence. Never run a report against a tenant in development.

Each task: write the named behavioural assertions; observe the relevant failure; implement the smallest correction; run targeted and affected broader tests; commit with exact results in the ledger. One fresh-context whole-branch engineering review supplements, not replaces, named safety review. Do not silently suppress a failed or unavailable check.

## Task 0: approved scope and diagnostic-history contract

**Files:** this plan; approved spec copy; GOAL; WORK-CLAIMS; DECISION-LOG INT-124; COMPLETION-REGISTER.
**Outcome:** decision-only PR documents additive read-only history interfaces; no source, evidence, scopes or schema changes yet.
**Dependencies:** actual three-repository pre-flight; owner authority for the new main merge. Existing #138 state reconciled without altering Claude records.
**Gate:** reviewed decision PR must merge before A2/A4 depend on its shared interface. A1 pure CSV parsing and unrelated contract preparation may proceed without it.
**Verification:** approved spec hash unchanged; `git diff --check`; existing Windows required check on exact PR head. Record resulting main check if/when authorised merge occurs.
**Rollback:** close unmerged decision; no runtime effect.

## Task A1: bounded customer import preview

**Files:** new `src/BuildStandard.Engine/Identity/PartnerCustomerImport.cs`; new `tests/BuildStandard.Tests/PartnerCustomerImportTests.cs`.
**Interfaces:** `PartnerCustomerImport.Preview(byte[] csv, IReadOnlyList<TenantProfile> profiles)` returns immutable preview rows, duplicate counts and profile fingerprint; `PrepareAdditions(preview, selectedTenantIds, currentProfiles, now)` returns a complete new list without writing it. New local parser only, no shared stored schema.
**Behaviour:** UTF-8 optional BOM; comma CSV with quoted newlines; 5 MiB/5,000 data-row limits; documented Microsoft ID and Company name required; Primary domain name optional. Explicit invalid/conflicting/existing/ready rows. Exact duplicate rows collapse visibly; canonical tenant GUID identity. Never overwrite any existing profile or treat relationship/tags as access. Reject stale preview. Validate new profiles through ProfileValidator; no automatic truncation of imported names/domains.
**RED tests:** quoted comma/newline/BOM; invalid UTF-8/quoting/headers/IDs; duplicate/conflict; existing profile unchanged; stale profile fingerprint; invalid selected row; row/byte limits; control characters; cancellation/no write is owned by A3.
**Run:** `dotnet test tests/BuildStandard.Tests/BuildStandard.Tests.csproj -c Release --filter FullyQualifiedName~PartnerCustomerImportTests` → all intended tests pass after observed RED. Run engine suite before branch completion.
**Review/rollback/evidence:** bounded parser/identity review; remove new utility without migrating profiles. Record source, failed-first cases and test counts. No permission or main-merge authority inferred.

## Task A2: diagnostic evidence and client review projection

**Files:** new `Engine/Evidence/ClientReviewEvidence.cs`, `Engine/Workflow/ClientReviewProjection.cs`; existing EvidenceStore/AssessmentEvidence only for narrow reuse; new `tests/BuildStandard.Tests/ClientReviewEvidenceTests.cs` and `ClientReviewProjectionTests.cs`.
**Interfaces:** INT-124 bounded read snapshot contains selected original source digests, diagnostic rows and records. `ClientReviewProjection.Project(profile, evidence, now)` returns one immutable client result with last readable assessment, standard/time, collection limits, run outcomes, deviation review and linked job summaries. No Graph/PowerShell dependencies.
**Dependencies:** merged INT-124; existing WriteOutcomeClassifier, timestamps, job meaning and assessment history.
**RED tests:** corrupt/null/wrong-tenant records; integrity mismatch; missing versus inaccessible directory; explicit record/byte cap; cancellation; timestamp offsets/invalid dates; unrecognised acceptance; overdue deviation; unchanged disk bytes after scan. Known clean assessment cannot hide unreadable run history.
**Run:** engine tests filtered to `ClientReview` → pass; engine suite → pass. Directory errors remain visible, not exceptions that discard other clients.
**Gate/rollback/evidence:** shared read semantics review; preserve older caller behaviour and stored bytes. Revert additive readers/projection without altering originals. Record exact revision/tests and remaining source-read limits.

## Task A3: native portfolio and atomic import journey

**Files:** new `App/ViewModels/ClientReviewsViewModel.cs`, `App/Views/ClientReviewsView.xaml`; Workspace, ShellViewModel, RouteCatalog and templates; new App tests and UiReview cases.
**Interfaces:** consumes A1/A2; uses existing offline selection, busy gate, presentation ticket and local atomic profile write. No second authentication/context system.
**Behaviour:** offline Clients and reviews route, explicit loading/empty/partial/error; cancellable background scan; visible invalid/existing/selected import preview; one atomic save after current-profile recheck; save failure retains preview; active connection never silently switches. Selected client uses existing offline review path with unsaved-result guards.
**RED tests:** disconnected zero tenant calls; selection guard; cancellation; failed save; changed profile file; A–B–A/delayed completion; every disabled action explains next step. Native three-size, keyboard/accessibility-name and clipping checks.
**Run:** current-source App build/test plus UiReview on Windows; local shared-logic tests where already supported. Expected no binding failures or tenant calls.
**Gate/rollback/evidence:** context/tenant safety review if touched; independent UI review of actual renders. Revert route/view, retain compatible profiles. Record native screenshots and source-matched commands, not cross-build as execution.

## Task A4: one-client review pack and offline CLI parity

**Files:** new `Engine/Reports/ClientReviewDocuments.cs`; existing CLI dispatch/help; new CLI/report tests; A3 export command; OPERATOR-START and CLI guide.
**Interfaces:** captured A2 model and file digest manifest → engineer/client HTML, CSV and manifest with identical known/unknown counts. CLI `bdit clients` and `bdit review --tenant <guid> --output <directory>` consume the same projection; offline only.
**Behaviour:** check captured context and source bytes before publication; no automatic refresh; safe HTML/CSV text; explicit incompatible scope/release rather than a numerical trend; one client per pack. Use existing local export safety/cancellation semantics.
**RED tests:** hostile imported text/formulas; stale context/changed source/deleted file; interrupted export; empty/partial data; identical CLI/UI projection counts; no authentication. Tests pin citation digests and distinguish byte integrity from source authenticity.
**Run:** targeted CLI/report/App tests, full required composed Windows check and fresh portable `bdit` smoke. Expected exact package identity and no tenant calls.
**Gate/rollback/evidence:** independent review of export admission, exact-source package original ZIP/hash, guide. Remove new commands; do not delete original evidence. Human usability remains unrun.

## Remaining bounded packages (each gets its exact adapter contract before dependent code)

These are required deliverables, not completed features or optional backlog. The detailed task brief is refined from the inspected existing symbols as its reviewed contract settles; inventing unapproved routes or permissions here would be unsafe. No gate silently removes a recommendation.

| Package / recommendations | Existing files/interfaces and intended additions | Required behaviour/tests | Contract / external gate and completion |
|---|---|---|---|
| B / 1,8 | ApplicationDeploymentAssessment, ReviewedChangeService, Workflow jobs; dedicated CA evaluation transport and native report presentation | Assignment is not observed effectiveness; partial membership/filter/result unknown; exact read-only evaluation route/response; wrong identity/cancellation/scopes; generic assessment write refusal preserved | Separate reviewed POST evaluation and coverage contracts; named execution review for affected guards. Done only when native journey and synthetic transport tests work; live remains unverified. |
| C / 2 | ExchangeReportRegistry, fixed runner, ReadOnlyRunWorkflow; strict permission/calendar/rule/trace/group/policy registrations | Real composite/service identities, missing vs empty, bounded interval/rows/bytes, wrong identity zero reads, truncation/cancellation/recovery; preserve 29 manual scripts | INT-121 does not cover these identities. Merge explicit versioned schema/runner contract first; independent runner/evidence review. Done includes native forms, saved evidence and HTML/CSV. |
| D / 3 | Existing fixed Windows DNS adapter and mail-authentication reports; MX/SPF/enrolment projection and worksheet | Resolver provenance, NXDOMAIN/NODATA/failure; bounded includes/redirect loops/lookup cap, split TXT/multiple records/timeouts; expected gateway differs from unexplained MX | Bounded resolver/result contract, synthetic responses only. Native/domain selection and export required; no DNS changes or live queries. |
| E / 4 | GraphReportService/registry and report grid; explicit site/Team/OneDrive owner/guest/grant and inheritance schemas | Scope/pagination/partial coverage explicit; no full-estate claim from search; no effective access from direct grant; no content bodies | Exact workload APIs and least-privilege scopes must be reviewed with William; no application-only/FullControl fallback. Item scans separate. Done only for explicitly admitted portions; blocked portions remain stated. |
| F / 5 | Existing users/licences reports and mailbox entitlement; usage report adapter/join/export | Privacy-obscured IDs never name-joined; incompatible periods unknown; bounded HTTPS redirect with no token forwarding/URL logging; licence suggestions not removal | Reports.Read.All and delegated role approval not yet given; source development cannot add it. Offline import/projection may proceed once schema accepted. |
| G / 6,11 | Existing partner connection/profile flow; explicit partner relationship/role schema; fixed service-health links/read adapter | Partner/customer context separation; expiry/ambiguous customers/access denied/partial; incident is context, not proof of failure cause | Concrete GDAP scope contract and ServiceHealth.Read.All proposal; portal links need no new scopes. No message-centre scope bundled. |
| H / 7 | Existing planner/executor/jobs/durable intent; registered lifecycle proposals/actions, native checklist | Synced/dynamic authority refusal; complete before and exact immutable approval; per-step accepted/uncertain/readback; interrupted steps never blind replay; Exchange manual | Each supported Graph action needs explicit contract/permission and named safety acceptance. Checklist alone is not completion of the controlled-action requirement. |
| I / 9 | StandardsLoader, PolicyImporter/DevicePolicyImporter, lineage/naming/parameters; candidate editor/comparison | Historical bytes unchanged; supported shapes only; foreign IDs unresolved; assignment diff separate; duplicate/conflicting overrides refused; deterministic output | Candidate shared schema/lineage contract before dependent editor; catalogue publication separate. Native draft editing and validation/export required. |
| J / 12 | Existing application/sign-in/registration reports; exact incident/context links/control mappings; strict external-result import | Business Premium/P1 cannot imply P2; imported source/version/time remains untrusted; no deployment authority; malformed/duplicate/schema mismatch cases | Exact service/licence/access and versioned import contracts; no incident writes/generic runtime. Native source/limits presentation required. |

Each package uses its own `astra/bs-*` branch and reviewed PR, CHANGELOG/guide updates and existing durable records. Shared cross-package interfaces are settled serially, not changed concurrently. Rollback removes additive routes/registrations while preserving historical evidence/schema dispatch, with no automatic remote reversal. Keep new evidence distinctly versioned and readable without reinterpreting earlier bytes.

## Final delivery task: assembled engineering candidate

- [ ] Reconcile all twelve recommendations to implemented/native/CLI/automated/native/live status; do not mark a package done because only its contract or parser exists.
- [ ] Review the changed composed source once with a fresh-context reviewer; retain all named safety-review gates separately.
- [ ] Build exact source Release on Windows, run engine/App tests, native three-size/keyboard renders, hosted-browser containment regressions, PS5.1/7 checks and fresh portable startup/CLI/package checks. Investigate failures, no repeated identical CI for unchanged trees.
- [ ] Retrieve the original unpublished Windows ZIP; verify its independent SHA-256 and source metadata. An inaccessible artifact is not verified possession.
- [ ] Supply updated engineer guidance, capability/limitations matrix, exact commits/checks/ZIP/hash and outstanding human/access/review decisions through existing records. Do not publish or mark H1/live accepted.

A package blocked by new access or contract/merge authority is reported with exact PR, action and owner after all independent work is complete. It remains incomplete until that gate and its implementation are actually satisfied.
