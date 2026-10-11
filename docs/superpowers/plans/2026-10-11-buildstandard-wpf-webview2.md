# BuildStandard WPF and WebView2 Implementation Plan

> **For agentic workers:** After William explicitly approves execution of this plan, use **Superpowers executing-plans**, with **using-git-worktrees** for isolation. GPT/Codex is the initial implementer across all tasks. Do not select subagent-driven-development or launch other implementers automatically. Checkboxes below are future work, not completed implementation.

**Goal:** Deliver the organisation-neutral BuildStandard Windows engineer workspace, translating concept B plus A’s working context into native WPF, preserving the shared engine and existing features, and adding one contained catalogue reader.

**Architecture:** Preserve .NET 10 WPF, Core/Graph/Engine/CLI boundaries and guarded native actions. Correct the selected shared state/safety seams; consolidate native presentation decisions and routes. Host only the existing Engineer Build Standard HTML in a lazily created, restricted WebView2 control.

**Tech stack:** Existing .NET SDK `10.0.401`, .NET 10, WPF, C#, PowerShell packaging and existing test frameworks; proposed pinned `Microsoft.Web.WebView2` SDK `1.0.4258.31` in the desktop project only. Organisation-managed Evergreen Runtime is an optional reader prerequisite, separate from the SDK and packaged .NET runtime.

**Repository copy provenance:** William approved execution of the returned plan on 11 October 2026. Original plan SHA-256: `e725bce1bd5d257fafcfb3c0d505f79045137507ef94e2ab48c19e4f4ee16417`. This copy changes only the spec link and local-only supporting-record references, and adds this notice. Historical planning-only/stop text below records its original status; the current authorisation and remaining gates are in `docs/GOAL.md`. No scope or review gate is changed.

**Approved specification:** [BDIT-WPF-WebView2-Design-Spec-4a7425f.md](../specs/2026-10-11-buildstandard-wpf-webview2.md), v0.2, SHA-256 `3c9170867b8be187be712bccf83416cd4c764544659756dcd8288fd7466f5361`. William’s current message approves it for planning and supersedes its older unapproved-status text. Its filename is provenance, not product branding. Read it alongside this plan. D/AC identifiers and original review IDs are retained.

## Global constraints

- Product **BuildStandard**; subtitle **Engineer workspace**. Neutral `BuildStandard.*` technical names, `BuildStandard.App.exe` and `buildstandard.cmd`; no invented business/publisher/support identity.
- Native working controls, connection, approvals, execution, recovery and authoritative results. One catalogue-only reader: `EngineerDocumentKind.BuildStandard`, full existing HTML, selected control anchor. No manual-guide/defaults-document embedding, tenant-report preview, PDF feature or detached companion.
- Concept B’s teal composition plus A’s context; docked native pin. Initial tokens: `#152C38`, `#627580`, `#007B79`, `#F2F5F5`, `#DCE5E8`; contrast/system accessibility takes precedence.
- Reader origin `https://reader.buildstandard.example/`; `CoreWebView2HostResourceAccessKind.Deny`; no scripts, injected script, CDP workaround, web messages, host objects or command bridge. External native action admits only exact HTTPS `learn.microsoft.com`, standard port, no user information.
- Reader storage `%LOCALAPPDATA%\BuildStandard\Reader\<application-instance-id>\<viewer-session-id>\`, separate `content` and `profile`; neither enters evidence, transfer, adoption, support or catalogue bundles.
- Preserve complete before-evidence, exact approval, tenant/account binding, single-use plans, unresolved-write blocking, disabled CA candidate targeting/exclusions, unassigned Intune candidates, uncertainty/no replay and existing separate experimental gates.
- No new Microsoft permissions, live sign-in/reads/writes/DNS, activation, generic runner, new reports, database, #110 automation, main promotion, tags or publishing. No test deletion. Existing manual/unsupported features stay honestly labelled and reachable.
- Historical standards, lineage, stored evidence, registrations, mappings and required notices are immutable/provenance exceptions. No blanket replacement. Developer-only GPT/Claude identities, `astra/*`/`claude/*`, inbox routing and handoff history survive.
- This document authorises no execution. Every command below is a future verification instruction unless explicitly recorded as an inspection already performed.

## Review focus

These additional boundary cases are assigned to the indicated task rather than left as generic “edge cases”.

1. Conflicting neutral/legacy root overrides, two application copies and old unresolved intent: refuse ambiguous root choice; preserve existing lock/evidence identity (T1).
2. A token/preflight await crosses the deadline after earlier successful rows: block only the unsent row and preserve earlier/uncertain outcomes (T4).
3. Same tenant, different account/connection, followed by a late export or focus callback: never admit it as current or restore the old selection (T5, T9).
4. Encoded/deceptive links, query/path changes and script injection against actual hosted content: deny requests; a permitted fragment must not weaken `Deny` (T8, T9).
5. Concurrent reader instances, crash retirement, reparse-point substitution and permission-denied cleanup: never delete active/unrelated data or claim secure erasure (T9).

## 1. Planning baseline and inspected evidence

Read-only inspection on **11 October 2026** established:

| Item | Actual observation and consequence |
|---|---|
| Proposed implementation base | **`4a7425f55385e84462975bcc5bdde5644815ca8f`**. Freshly fetched `origin/integration` equals the reviewed SHA; no newer commits or tree delta. |
| Active checkout | `/workspace/M365-Buildstandards`, clean, detached at the same SHA. One worktree; `.git` equals common Git directory; no superproject. No checkout/reset/clean or source change performed. |
| Main | `81b18f5240bb8c36e3b090cb599c6decf1f06d61`; remains the default branch. Do not move it. |
| Open work | Read-only PR lists are empty across `M365-Buildstandards`, `m365-tenant-console-Asta` and `m365-Tenant-Toolkit-Claude`. No current competing PR claim was found. This must be refreshed at execution. |
| Local-only material | `/workspace/delegated-release-readiness/…` contains earlier candidate/patch-check copies; `/workspace/reviews/BDIT-4a7425f/…` contains reviews and visuals. These are not active worktrees or remotely shared claims; do not integrate their patches or treat their notes as current source. |
| Coordination | Root `AGENTS.md`, `docs/GOAL.md`, `docs/integration/AGENT-COORDINATION.md`, `HANDOVER.md`, `WORK-CLAIMS.md`, and `agent-comms/README.md`/`templates/message.md` exist. **Issue #83 is the shared inbox**, independent of branch. Its body/index still describes older blocked work; latest comment **6112345416**, `ASTRA-20261011-010`, records the final freeze. Use the later disposition, preserving the older record. |
| Historical validation | GitHub check-runs at the frozen SHA show **build / standard / secrets = success**, run **38163688160**. Freeze record states 2,206 Engine/CLI + 468 App tests, 63 renders and 129 command attempts; these counts were not rerun here and do not validate proposed changes. |
| Workflow | `.github/workflows/build.yml`: PR trigger has no base-branch filter; pushes cover `integration`, `main`, `incoming/**`, **not `phase/wpf-webview2`**. SDK 10.0.401; build/test/UI/package/PS checks are present. Publisher remains separately guarded and admits main/integration only. |
| Protection | Ruleset list and branch-rules endpoints for integration/proposed phase returned `[]`. Classic integration protection returned **HTTP 403, “Resource not accessible by integration”**. Therefore actual classic required/protection settings are **unknown**, not absent. Observed job names are not proof of enforced requirements. Proposed phase branch does not exist. |
| Execution availability | This session supplies Linux source inspection and GitHub reads. Future Windows runner capacity, hosted Runtime availability, an independent reviewer and human acceptance are not promised. No dependencies, builds, tests, prototypes or CI were run. |

Supporting planning records were produced outside the repository at `/workspace/planning/BuildStandard-WPF-WebView2/`: `planning-source-evidence.json`, `planning-github-evidence.json` and `vendor/retrieval.json`. They are local-only attachments, not shared remote evidence. The observations above preserve their relevant facts and describe planning inspection, not a fresh product acceptance.

### Branch choice and merge validation

Use the requested **temporary, history-preserving `phase/wpf-webview2`** based on the full SHA above, with bounded `astra/bs-<task>` branches targeting it. It isolates a coordinated naming/UI transition while keeping main, integration and the frozen source intact. It is not a new default branch. Do not decide final integration or phase-branch retirement until the assembled result is approved.

Alternative: targeting integration would reuse its known push triggers and usual merge convention, and Git history would still preserve the frozen SHA. It would, however, advance the held integration baseline during the cross-cutting transition. The phase branch is justified here by that isolation requirement, not appearance.

**T0 must establish the new target’s authority and validation before product work merges:** add exactly `phase/wpf-webview2` to the build workflow’s push list, preserve every phase merge run as integration runs are preserved, retain the unfiltered PR trigger and existing pinned actions/jobs. Check both the actual PR head SHA and GitHub’s tested PR merge tree; after every merge read the phase push run’s actual source SHA and result. Do not equate a prior PR result with a later composed tree. No duplicate manual run if the applicable push run already tests that exact source; rerun only for changed source or a diagnosed infrastructure failure. Do not broaden the publisher’s permitted refs.

William/security administration must inspect classic protection and confirm the phase’s intended required checks/review/merge rules. Repository/protection-setting changes require William’s separate approval and are not silently included in workflow editing. A reviewed branch-policy amendment should explicitly extend the existing merge-commit delegation to this phase target. If enforcement cannot yet be confirmed, source development may remain on review branches, but **phase merges are blocked**; do not substitute an unprotected merge or retarget to integration silently.

### Isolation and durable coordination

At execution, detect native/platform isolation first, including Git/common directory and submodule checks. The existing managed cloud workspace may already provide isolation even when not a linked Git worktree. Reuse an appropriate isolated workspace; prefer an available native worktree tool. Otherwise use an ignored `.worktrees` directory or an agreed outside-repository location. Never create a nested worktree blindly, force a branch checked out elsewhere, or run two writers on one branch. One initial implementer, one active implementation branch at a time; generated output stays local to its worktree. No agents are launched by creating worktrees.

Every branch repeats the three-repository pre-flight, inspects changed overlapping PRs and claims its area through an early draft PR plus `WORK-CLAIMS.md`. Cross-cutting contracts/rename policy use T0’s decision PR before dependent work. Commit small verified steps within a bounded branch; a fresh independent reviewer reviews the completed branch, not every tiny edit. Reviews stay in PRs; material decisions go in the existing `DECISION-LOG.md`.

After execution approval, make the **smallest dated phase amendment** to `docs/GOAL.md`, coordination and HANDOVER: current task scope/target, GPT lead across components, no automatic take-next backlog, finite branch review/check rules and current blockers. Preserve old desktop ownership as history. Do not erase Claude’s claims, review attribution, freeze evidence or closed/deferred backlog. Issue #83 receives one compact material status update with current links; existing `agent-comms` rules/template remain the handoff mechanism.

Use executing-plans’ local ledger for task-level memory; before each handoff/cleanup, checkpoint accepted rulings, task commits, actual commands/results, unrun checks and next action in the **existing COMPLETION-REGISTER/WORK-CLAIMS and PR**, then link a sanitised summary from #83. A local ignored ledger is never the only durable record. A later delegation contains task/claim ID, actual owner, base/head SHA, branch/PR, permitted scope, dependencies/interfaces, review disposition, evidence links, unrun gates, next action, recipient acknowledgement and return criteria. Until the recipient actually acknowledges, status is **requested**, not accepted or started. No comment wakes another session. No private client data enters these records.

## 2. File map and compatibility decisions

Paths in T0/T1 are current. Thereafter use this exact mechanical mapping; all unlisted relative filenames retain their responsibilities:

| Current path/name | Proposed successor |
|---|---|
| `BDIT.TenantToolkit.sln` | `BuildStandard.sln` |
| `src/BDIT.TenantToolkit.{App,Core,Engine,Graph,Cli}/` and corresponding project/root namespace | `src/BuildStandard.{App,Core,Engine,Graph,Cli}/` and `BuildStandard.*` |
| `tests/BDIT.TenantToolkit.{Tests,App.Tests,UiReview}/` and project names | `tests/BuildStandard.{Tests,App.Tests,UiReview}/` |
| App and CLI assemblies `BDIT.TenantToolkit.App` / `bdit` | `BuildStandard.App` / `BuildStandard.Cli`; retain one desktop executable and the managed CLI dependency |
| `packaging/bdit.cmd` primary invocation | new `packaging/buildstandard.cmd`; retain `bdit.cmd` only as a documented compatibility wrapper to the same executable, never an alternate host |
| Package stem `M365-BuildStandard-Tool-…` | `BuildStandard-<version>-win-x64.zip` for new phase artifacts; historical package/pin names stay readable |

**Compatibility policy to settle in T0 and prove in T1/T2:**

- Keep `config/toolkit.settings.json`, settings JSON keys, portable `data/tenants`, evidence schema fields/discriminators, IDs, digests, mappings, lineage and `policy-write.lock`. No automatic root migration or evidence adoption. Old data read by neutral binaries must not become fresh/live/approved.
- Introduce `BUILDSTANDARD_ROOT`; retain `BDIT_TOOLKIT_ROOT` as a documented legacy alias. Explicit `--root` wins; absent an explicit root, differing simultaneously configured aliases cause refusal. Equal canonical paths are allowed; do not silently adopt an unrelated newly discovered folder. Existing ancestor discovery stays bounded.
- Preserve exact DPAPI entropy **`BDIT.TenantToolkit.TokenCache.v1`**, `msal-<mode>.cache` identity and existing account/registration retention/deletion policy as internal compatibility exceptions. No cache move/copy, browser reuse or crypto migration. Test with synthetic local protected bytes; no real token cache is inspected.
- Preserve any externally consumed `BDIT:` PowerShell protocol markers and stable registered script IDs as explicitly enumerated wire-compatibility exceptions where the fixed reader/parser depends on them. Neutralise human-facing labels/comments. Changed script bytes require fresh registry pins, not rewritten old evidence or changed script meaning.
- Rename `x:Class`, CLR namespaces, `InternalsVisibleTo`, pack/resource URIs, `ProductLogo` manifest-resource names, harness resource-loader strings, launchers, doctor package-layout expectations and package tests together. Update assembly-qualified strings only where actual source needs them; keep supported serialised data contracts stable.
- Do not invent copyright/authorship. Preserve required legal notices and historical attribution; blank optional organisation defaults. Do not rewrite a user's explicit organisation report setting. The effective fixed product label becomes BuildStandard without silently editing old settings on disk.
- Keep all `standards/*.json`, standards manifests and lineage bytes unchanged. Use a new **presentation identity** for neutral headers/CSS; old catalogue owner/recipe/manual text is labelled exact catalogue provenance. Enumerate those exceptions; do not alter intended payloads to remove a brand. A later semantic successor catalogue would need a separate release/lineage decision and is not scheduled here.
- Historical source citations, screenshots, findings, agent names/branches/inbox, release pins and required notices are retained. The allowlist must be path/string/reason specific; a broad “ignore every BDIT match” check is unacceptable.
- New executable paths/binary hashes invalidate assumptions about old WDAC/AppLocker/SmartScreen approvals. Record fresh fingerprints and the security-owner route; no writable-path exception, bypass or claim that the new package is already approved.

## 3. Verification conventions

All tasks are owned by **GPT/Codex** unless explicitly handed off later. Every behavioural task uses: write named synthetic regression → observe the intended failure → minimal implementation → targeted pass → affected regression pass → commit → fresh independent branch review. Mechanical renames use existing functional regressions plus explicit compatibility cases; no artificial failing test that merely asserts a new name. Preserve all existing tests/assertions.

Commands below assume an **authorised, provisioned Windows environment** after T1; paths before T1 use the original names. Define test filters by the actual class names listed in each task; no zero-test result counts as success.

```powershell
# Build prerequisites once when source/project changes require them.
dotnet restore BuildStandard.sln
dotnet build BuildStandard.sln -c Release --no-restore -warnaserror
dotnet build tests/BuildStandard.UiReview -c Release -warnaserror

# Targeted task tests (replace <TaskClasses> with the named class filter).
dotnet test tests/BuildStandard.Tests -c Release --no-build --filter '<TaskClasses>' --logger trx
dotnet test tests/BuildStandard.App.Tests -c Release --no-build --filter '<TaskClasses>' --logger trx

# Native synthetic UI/harness; no production startup/authentication.
dotnet run --project tests/BuildStandard.UiReview -c Release --no-build -- "$pwd" "$pwd/dist/ui-review"
```

Branch CI retains full required checks. Local work uses targeted tests; do not rerun the whole Windows pipeline between small commits unnecessarily. Reuse old evidence only for unchanged source and the same claim. Renames/shared styles/package changes affect broad native loading, so they require composed Windows/package checks. Cross-compilation does not prove native UI execution.

Every completion record contains requirement/D/AC/review IDs; exact base/head and merge SHA; files/interfaces; regression failure and pass evidence; actual commands/results/counts/skips; reviewer disposition; CI run/artifact IDs; package/original ZIP hash where applicable; limitations and unrun acceptance. Do not describe a future command as completed validation.

## 4. Independently reviewable tasks

### T0 — establish the phase contract and validation target

**Branch:** `astra/bs-phase-contract`. **Depends:** plan execution approval. **Scope:** all D/AC coordination dependencies; no product behaviour.

**Files:** existing `docs/GOAL.md`, `docs/integration/{AGENT-COORDINATION,HANDOVER,DECISION-LOG,WORK-CLAIMS,COMPLETION-REGISTER}.md`, `agent-comms/README.md`, `.github/workflows/build.yml`. Preserve existing templates/history. At execution, add the approved spec at `docs/superpowers/specs/2026-10-11-buildstandard-wpf-webview2.md` and this plan at `docs/superpowers/plans/2026-10-11-buildstandard-wpf-webview2.md`, linked from the existing GOAL/claim. These are proposed new design/plan files, not pre-existing inbox paths or a second review/handoff system. Preserve approval provenance and spec hash; do not treat the file copy as a new approval.

**Produces:** agreed rename/compatibility contract above; D-02 admission interface policy; D-03 context/projection and D-06 document identity boundaries used below; phase merge authority/check policy. Interface implementations wait for this decision PR.

- [ ] Refresh actual heads, inbox/claims, worktree/isolation, required checks and overlapping work. Reconcile any new divergence before choosing the implementation base.
- [ ] Create the approved phase target at the pinned base and a separate feature worktree/branch using the isolation rules. Claim T0 with an early draft PR; do not mutate held integration.
- [ ] Record the finite phase/ownership amendment and compatibility/contract decisions; add the exact phase push trigger and merge-run preservation. Do not change publisher admission or protection settings.
- [ ] Have William/admin confirm classic/phase protection and any separately approved settings changes; record evidence. Obtain independent review and any required phase delegation before merge.
- [ ] Verify actual PR head/check identities and the resulting phase push run; checkpoint the accepted contracts/merge SHA in existing records.

**Verification:** read `gh api …/branches/<target>/protection`, rulesets/rules, PR checks and `gh run view <run> --json headSha,event,conclusion,jobs`; inspect workflow diff and pin preservation. No build status inferred from YAML syntax. **Rollback:** revert the phase workflow/document amendment; retain original freeze and decisions as history. **Gate:** unresolved protection/merge authority blocks merging, not planning unrelated tasks.

### T1 — mechanical technical neutralisation with backwards compatibility

**Branch:** `astra/bs-neutral-identity`. **Depends:** T0. **IDs:** D-07; AC-24/25/26/31.

**Files:** exact path mapping in §2; `Directory.Build.props`; all affected `.csproj`/XAML namespaces; `Graph/Properties/AssemblyInfo.cs`, `Engine/InternalsVisibleTo.cs`, `Graph/Setup/ProductLogo.cs`, `Core/Configuration/ToolkitPaths.cs`, `Core/Json/ToolkitJson.cs` (inspect contracts; alter only if needed), `App/Services/PortableCliHost.cs`, `Engine/Diagnostics/{OfflinePackageFiles,OfflinePrerequisiteService}.cs`; packaging launchers; `build/{Build-Portable,Test-Portable,Get-DependencyInventory,Setup-And-Build}.ps1`; workflow path references and affected tests. Add `tests/BuildStandard.Tests/IdentityCompatibilityTests.cs`.

**Interfaces:** same public engine/CLI/data contracts under neutral namespaces. `ToolkitPaths.Resolve(string? overrideRoot = null)` keeps its signature; applies the explicit alias policy. Existing CLI arguments/output streams/exit codes remain compatible. Do not rename `ToolkitPaths` merely for style.

- [ ] Inventory remaining exact identifiers and consumers; record the finite compatibility allowlist and baseline hashes for immutable catalogues plus synthetic historical evidence/configuration fixtures.
- [ ] Add behavioural cases for root alias conflicts/equality/explicit override, old JSON and unresolved-write history, shared old/new write lock and synthetic DPAPI data. Establish existing package/CLI/harness results for the unchanged base from valid evidence.
- [ ] Apply the mechanical rename atomically, including resource strings, launchers, managed CLI dependency and diagnostic checks. Keep legacy cache/protocol/alias exceptions. Do not modify tenant registrations or create a second executable to bypass controls.
- [ ] Run `IdentityCompatibilityTests`, `PortableCliHostTests`, `OfflinePrerequisiteTests`, evidence/restore/lineage/recovery regressions and the full Windows build, native harness and fresh package checks. Compare historical fixture/catalogue bytes. Use the existing seven declared CRLF launcher conversions correctly; do not mistake line-ending corruption for a harmless rename.
- [ ] Obtain independent review; safety-relevant root/cache/lock changes also require the existing safety gate. Commit/merge and record exact evidence before other feature branches diverge.

**Rollback:** revert mechanical source changes as one reviewed unit; keep data untouched. Existing old package remains separate; no in-place binary overlay or automatic cache migration. **Evidence:** rename map, enumerated exceptions, zero unexplained old active names, package/CLI/resource-loading and byte-preservation results.

### T2 — neutral active product language, defaults and future generated output

**Branch:** `astra/bs-neutral-content`. **Depends:** T1. **IDs:** D-07; AC-23/25/26/32.

**Files:** `Core/Configuration/ToolkitSettings.cs`, `config/toolkit.settings.json`, App title/resources/help, CLI help, `Graph/Setup/ApplicationSetupService.cs` default suggestions, `Engine/Reports/*.cs`, active script/templates and parser-facing tests as necessary; `packaging/README.md`, `README.md`, current `docs/{OPERATOR-START,APPLICATION-SETUP,ARCHITECTURE,DESIGN-SYSTEM,RELEASE-AND-SERVICING}.md` and other active guides identified by the scoped inventory. Add `tests/BuildStandard.Tests/ProductIdentityTests.cs`; extend `EngineerStandardDocumentsTests`/`BuildStandardDocumentTests`/`StandardDefinitionExportTests`.

**Interfaces:** fixed product display BuildStandard, optional organisation blank by default; existing saved report organisation values survive. New suggested registration names do not identify existing apps by display name or rename/recreate them. New document presentation version is **`buildstandard-native-reader-v1`**, distinct from the unchanged catalogue identity.

- [ ] Add cases for clean defaults, legacy configured organisation retained, no old fixed title restored from default handling, neutral new HTML/CLI/help and unchanged raw historical exports. Capture exact immutable provenance exceptions.
- [ ] Neutralise active human text/comments/templates, preserving each safety instruction, developer coordination identity and required notice. Update new probe prefixes to `.buildstandard-doctor-` without broadening old cleanup rules. Keep fixed script markers where required by the accepted compatibility contract.
- [ ] Refresh script pins for changed bytes using `python build/Update-ScriptRegistry.py`, then `--check`; run affected existing PowerShell parser/stub cases. Do not alter catalogue payload/manual bytes or policy names.
- [ ] Run named content/export/compatibility tests and a path-specific active-brand scan with reviewed exceptions. Record new output hashes and verify historical bytes remain unchanged.
- [ ] Obtain independent review; registration-discovery/permission behaviour must remain unchanged, otherwise route the change through the named safety gate. Commit and record evidence.

**Rollback:** revert active template/presentation defaults; never rewrite old exports. **Completion:** neutral ordinary output, accurate historical provenance, preserved coordination and no licence/authorship invention.

### T3 — one returned-token capability classification

**Branch:** `astra/bs-access-truth`. **Depends:** T0/T1. **IDs:** D-01; AC-01/02; GPT-R-001/012, bounded CLA-R-018.

**Files:** `Core/Models/Session.cs`; new `Core/Safety/TokenCapabilityClassifier.cs`; `Graph/{TenantConnectionService,TenantDiscoveryService}.cs`; native access presentation in `App/ViewModels/ShellViewModel.cs` and `App/Services/ExperimentalOperationGuard.cs`; `packaging/README.md`, `docs/APPLICATION-SETUP.md`. Add `tests/BuildStandard.Tests/TokenCapabilityTests.cs`; extend `PermissionAcquisitionTests` and App `ConnectionGuidanceTests`.

**Produces:** `TokenCapabilityClassifier.Classify(IEnumerable<string> returnedScopes)` → `TokenCapabilityResult` with `ReadOnlyVerified`, `BroaderPresent` or `Unclassified`, plus the actual classified/unclassified scope lists. Classification uses an explicit reviewed supported-read set and protocol-scope handling; resource-qualified forms are accepted only for known supported Graph representations. This is information/admission, not a grant of permission. No substring-based “write” proxy remains authoritative.

- [ ] Write failing cases: supported read/protocol scopes; supported Graph-qualified forms; `Directory.AccessAsUser.All`, delegated `User.Invite.All`, `Mail.Send`; unknown resource/scope; empty/incomplete returned capabilities; dedicated versus shared registration. Do not use application-only `Group.Create` as a delegated-token example.
- [ ] Implement the shared classifier and admission/display consumers. Dedicated assessment rejects broader/unclassified authority; shared fallback retains its reviewed policy and clear warning. Preserve deployment-required-scope checks and independent Graph assessment write refusal.
- [ ] Use specification copy: “Assessment mode — toolkit changes blocked”; token classification separately; offline “No live connection”; distinct tenant-change and Exchange Run opt-ins. Correct packaged token-isolation claims.
- [ ] Run `TokenCapabilityTests|PermissionAcquisitionTests`, existing Graph refusal/renewal tests and `ConnectionGuidanceTests`; prove no new scopes/sign-in-on-navigation/access changes.
- [ ] Obtain **other-agent safety review under GOAL/HANDOVER** before merge; generic self-review cannot satisfy this. Record gate/disposition and actual checks.

**Rollback:** do not revert to false read-only assurance; hold the candidate or disable connected pilot use until a reviewed correction is available. **Evidence:** classification matrix, synthetic admission results, unchanged consent/route policy and native wording.

### T4 — preserve reviewed freshness through initial dispatch admission

**Branch:** `astra/bs-write-freshness`. **Depends:** T0/T1. **IDs:** D-02; AC-03/04; GPT-R-002.

**Files:** `Engine/Execution/DeploymentExecutor.cs`, `Core/Graph/IGraphClient.cs`, new `Core/Safety/ReviewedWriteAdmission.cs`, `Graph/GraphClient.cs`; `tests/BuildStandard.Tests/{ExecutorTests,GraphClientTests,SafetyReviewRegressionTests}.cs` and affected fake Graph implementations. No recovery semantics redesign.

**Interfaces:** add required `IGraphClient.WriteReviewedAsync(GraphApi api, GraphWriteMethod method, string path, JsonObject payload, ReviewedWriteAdmission admission, CancellationToken ct)` for this executor’s candidate writes. No default implementation forwarding to unguarded `WriteAsync`. `ReviewedWriteAdmission.AssertAllowed()` owns immutable original plan/snapshot expiry inputs and a trusted native/engine callback to recheck current identity/input binding. Existing separate write operations keep their current contracts; do not claim they acquired this plan deadline automatically.

- [ ] Add failing fake-clock cases: preflight finishes beyond/at/just inside existing limits; expiry during token acquisition; context/approval change during the await; no new send; earlier accepted row preserved. Preserve current `age > limit` equality semantics. Add cancellation/transport failures after admission as possibly sent and no replay.
- [ ] Capture original timestamps/limits/identities once; recheck after preflight before current-row intent. Pass the same immutable admission through Graph request preparation; invoke it after token acquisition immediately before initial `HttpClient.SendAsync`, with no further awaited work between barrier and handoff. Include an already-requested pre-send stop/cancellation in admission; once transport may have sent, keep the existing uncertain-outcome rules. Do not add another replayable send path.
- [ ] Keep `WriteNotSentException` limited to provably pre-send failures, durable per-row outcomes and existing unresolved-write blocking. Once SendAsync is invoked, retain conservative ambiguity. Never refresh/reapprove the plan or issue an interactive retry.
- [ ] Run `ExecutorTests|GraphClientTests|SafetyReviewRegressionTests` plus recovery/no-replay/approval regressions. Check a two-row run does not produce run-wide “nothing sent” after row one succeeded.
- [ ] Obtain the **execution/Graph-write safety review required by GOAL/HANDOVER**, then exact-source Windows checks and actual phase merge evidence.

**Rollback:** preserve journal/schema and earlier rows; block deployment if the correction must be withdrawn. No replay or intent deletion. **Completion:** failing-first timing evidence and a reviewer-confirmed last cancellable software admission boundary, without claims of remote atomicity.

### T5 — current-context result admission and shared next-action decision

**Branch:** `astra/bs-context-guidance`. **Depends:** T1/T3; accepted T0 contract. **IDs:** D-03/04; AC-05/06/07/30; CLA-R-004/001/002, constrained CLA-R-031.

**Files:** `App/Services/{Workspace,GraphReportsController,ReadOnlyRunWorkflow}.cs` (reuse strong admission; no runner redesign); `App/ViewModels/{DeployViewModel,HomeTaskProjection,HomeViewModel,ShellViewModel,EvidenceReviewContext}.cs`; new `App/Services/PresentationContext.cs`; extend `HomeTaskProjectionTests`, `InterruptedConnectionTests`, `ClientHandoffQolTests`; add `CurrentContextAdmissionTests`.

**Interfaces:** native immutable `PresentationContextKey` includes profile/tenant, verified account/resource/session generation, selected standard digest and selected evidence identity. `Workspace.CapturePresentationContext()` and `IsCurrent(PresentationContextKey)` admit display/copy/export notifications, never execution. Keep editor-only `EvidenceReviewContext` semantics where valid. Extend `HomeTaskContext` for run/outcome-review/plan/access facts; `HomeTaskProjection.Create` returns one action key/route/reason/priority/disabled explanation. Shell, Home and attention consume it; remove the competing `ShellViewModel.NextStep` decision tree.

- [ ] Write failing A→disconnect→B, failed B, same-tenant changed-account/session, source/filter/rerun and late export-notification cases. Saved A history/export provenance remains A; no current B action can select it implicitly.
- [ ] Invalidate transient results/commands immediately on context replacement. Keep durable history and explicitly opened historical views. Do not merely compare tenant strings or delete historical records.
- [ ] Implement the specified precedence: active operation; relevant current uncertainty; missing client/standard; setup; unreviewed relevant run; evidence; assessment; plan/access; approval readiness. An uncertain run with no snapshot must lead to recovery. A viewed/reconciled historical run cannot trap navigation forever; viewing alone does not reconcile uncertainty.
- [ ] Run `CurrentContextAdmissionTests|HomeTaskProjectionTests|InterruptedConnectionTests|ClientHandoffQolTests` and existing Run/report context regressions. Test Home/header/attention routes agree without I/O, auth or execution.
- [ ] Independent branch review; any tenant-confirmation/recovery authority change also satisfies the existing safety gate. Record immediate invalidation and durable-history evidence.

**Rollback:** revert presentation changes without rewriting evidence; retain conservative current-context refusal. No broad Workspace rewrite, notification framework or performance-cache project.

### T6 — native command centre, grouped routes and docked context

**Branch:** `astra/bs-native-command-centre`. **Depends:** T2/T5. **IDs:** D-05; AC-02/06/11/13/14/29/30/32.

**Files:** `App/{App.xaml,MainWindow.xaml,MainWindow.xaml.cs}`, `App/Views/HomeView.xaml`, `App/ViewModels/{ShellViewModel,HomeViewModel}.cs`; new `App/ViewModels/RouteCatalog.cs`, `PinnedReferenceViewModel.cs`, native `Views/PinnedReferenceView.xaml`; existing/new App `HomeNavigationTests`, `ReportNavigationTests`, `PinnedReferenceTests`; UiReview `HomeTaskCentreChecks`, `OfflineClientJourneyChecks`, `InterfaceChecks`, `Program`.

**Interfaces:** one route metadata catalog supplies group/title/step/contextual route information to existing page instances. Preserve all 21 existing keys: `home, overview, recovery, connect, setup, automation, configuration, assessment, deviations, plan, deploy, history, jobs, checks, scripts, run-reports, reports, mailbox-capacity, naming, standard, settings`. Docked pin stores an immutable saved-record reference and original provenance, no auth/approval or new-client toolbar authority.

- [ ] Add route-reachability and no-I/O-on-display tests; card routes: discover→connect/configuration, assess→assessment, backfill→jobs, review plan→plan, each with honest availability/reason. Use existing local records for recent evidence; attention uses T5 only.
- [ ] Translate original teal B+A layout into native styles/controls and five groups: Workspace, Build standard, Reports, Scripts, Settings. Preserve commands/services and accessible selected-route/skip-to-content. No concept demo toolbar, fake identities/counts/progress or permanent navy rail.
- [ ] Implement docked details/pin, explicit repin/close, mismatch banner and collapsed sensitive detail after client change. Panel copy is explicitly labelled original provenance; no export/run/approve via the new client. Stack/drawer layouts must preserve warnings/focused controls.
- [ ] Run named navigation/pin tests and native harness at all historical sizes plus compact work-area cases. Compare new actual WPF renders to the supplied originals, recording intentional differences. Test late updates do not steal focus.
- [ ] Fresh independent UI branch review; record all routes reachable. Human visual/assistive acceptance remains H1.

**Rollback:** route keys/services unchanged, so revert layout/projection consumers; pins are transient and grant no authority. **Evidence:** before/after native renders, route matrix, mismatch and empty/offline proofs; concept images remain labelled concepts.

### T7 — usable findings, exact plan review and truthful outcomes

**Branch:** `astra/bs-working-journeys`. **Depends:** T4/T5/T6. **IDs:** D-05; AC-07–14/22/27/30; GPT-R-009/010, #109 wording, bounded CLA-R-003/005/006/013/014/017/018/022.

**Files:** `App/Views/{AssessmentView,PlanView,DeployView,ConfigurationView,GraphReportsView,RunReportsView,MailboxCapacityView}.xaml`, their view models, native `Views/OutcomeSummary.xaml` and pure `ViewModels/AssessmentSummaryProjection.cs`; relevant existing confirmation components; `docs/OPERATOR-START.md`, active help and `docs/CONTROLLED-ACCEPTANCE.md`. Tests: `AssessmentSummaryProjectionTests`, `OutcomePresentationTests`, existing `ResultEvidenceTests`, plan/permission/dialog/App tests and native journey checks.

**Interfaces:** one presentation-only partition of all nine `FindingStatus` values; no status/schema/assessment mutation. Outcome view accepts independent lifecycle/read/acceptance/readback/save/protection facts; it never infers readback from intended payload. Route/step references come from T6 metadata.

- [ ] Add exhaustive partition assertions: Compliant; four Needs review subcounts; SettingsMatchNotEnforced; UnableToAssess; separate CompliantWithDeviation and NotApplicable. Every row appears once; original qualifications remain; no compliance score. Include zero rows and partial-source fixtures.
- [ ] Reflow findings/detail and plan columns for usable initial content. Keep selection only under T5-valid source identity; a hidden filtered selection is explicit, a new result clears/rebinds detail. Show “Last built plan” validity separately from current selection.
- [ ] Disclose recapture/replanning before access transition, visible disabled reasons and the specification’s exact outcome messages. Unknown readback never becomes disabled/protecting; earlier sent rows preclude run-wide “nothing sent”. Preserve read/save/attestation distinctions and original limitations.
- [ ] Add keyboard CA-001 select→exact changes→deliberate approval journey with a synthetic guarded boundary: no Enter-triggered write. Preserve cell editing/checkboxes, error announcements, focus and contrast. Align #109 readiness numbers through route metadata rather than rewriting every page manually.
- [ ] Run the named projection/outcome/App tests and affected native journeys; independent review of changed confirmation paths satisfies the safety gate. Record H1 tasks without marking human acceptance passed.

**Rollback:** presentation/guide reversion only; persisted statuses and safeguards unchanged. **Excluded:** AC-21’s conditional exception/office/unsaved-Run fixes are not implemented or closed by this task.

### T8 — immutable catalogue document identity and exact-byte export

**Branch:** `astra/bs-catalogue-document`. **Depends:** T1/T2; T0 content contract. **IDs:** D-06/07; AC-15/16/18/26/28/31.

**Files:** `Engine/Reports/{EngineerStandardDocuments,ReportExporter,StandardDefinitionExporter}.cs`; new `Engine/Reports/CatalogueDocumentService.cs`, `Core/Models/CatalogueDocument.cs`; shared verified-source helper extracted narrowly from `StandardDefinitionExporter.VerifiedSource`; App `StandardViewModel`; tests `CatalogueDocumentTests`, existing `EngineerStandardDocumentsTests`/`StandardDefinitionExportTests`.

**Produces:** `CatalogueDocumentService.Create(StandardCatalogue standard)` → immutable `CatalogueDocument` containing kind BuildStandard, catalogue release/digest, presentation version, generation time, actual UTF-8 bytes without BOM, HTML SHA-256, valid anchors and renderer-emitted references. Defensive byte ownership prevents later mutation. `SaveHtml(CatalogueDocument document)` uses the existing approved export destination/write infrastructure and those exact bytes. No client/connection argument or mutable catalogue regeneration at save.

- [ ] Add failing identity/byte tests for mutable source replacement after creation, invalid manifest/catalogue, changed CSS/branding/CSP, missing selected anchor, failed save and attempted descriptor-byte mutation. Historical raw JSON/HTML retain their identities.
- [ ] Reuse `EngineerStandardDocuments.Html(..., BuildStandard)` for the whole catalogue; embed restrictive CSP in generated bytes and preserve legitimate fragments. No per-control renderer, tenant data or script. The admitted bytes must be the bytes saved, including presentation/security metadata.
- [ ] Keep catalogue digest, generated-document digest and generation time distinct. Save mismatch fails visibly. Manual-guide/defaults/document exports retain their existing routes and semantics.
- [ ] Run `CatalogueDocumentTests|EngineerStandardDocumentsTests|StandardDefinitionExportTests`; inspect generated structure and malicious encoded text as data. Do not count this as hosted security acceptance.
- [ ] Independent branch review of source verification, byte ownership and export consistency; record exact synthetic input/output hashes.

**Rollback:** remove only the new document service/UI entry; existing export remains. No old document rewriting. **Evidence:** identical displayed-source/save byte proof and explicit failure outcomes.

### T9 — contained native catalogue reader and owned cache lifecycle

**Branch:** `astra/bs-catalogue-reader`. **Depends:** T5/T6/T8. **IDs:** D-03/05/06/07; AC-05/15–20/28–32.

**Files:** desktop `.csproj` SDK reference; new `App/Services/Reader/{CatalogueReaderHost,ReaderSessionLease,ReaderNavigationPolicy}.cs`, `App/ViewModels/CatalogueReaderViewModel.cs`, `App/Views/CatalogueReaderView.xaml`; `StandardViewModel` entry and shell task-area selection; new App `ReaderPolicyTests`, `ReaderLifecycleTests`; UiReview `CatalogueReaderChecks.cs` (separate explicit hosted mode); packaging dependency inventory/notices and `Test-Portable.ps1` reader cases. Keep WebView2 types outside Core/Engine/CLI.

**Interfaces:** `CatalogueReaderViewModel.OpenAsync(CatalogueDocument document, string controlId, PresentationContextKey context, CancellationToken ct)`; native Back/Save/Open Microsoft reference actions; state Closed/Preparing/Loading/Ready/Unavailable/Invalid/Failed/Superseded. `ReaderNavigationPolicy` validates full canonical URI plus current document/generation and user gesture; a pending external link is native data, never a web command. `ReaderSessionLease` owns one marked instance/session’s content/profile and retirement, not the evidence root.

- [ ] Write policy/lifecycle regressions before host code: exact origin/path/no query, valid/invalid fragments, deceptive hosts/userinfo/nonstandard ports/encoded paths, stale/non-user links, superseded callbacks and unchanged-context Back restoration. Add two-instance/crash/permission/reparse cleanup cases with synthetic temporary folders; no real browser profile is used.
- [ ] Add pinned SDK and lazy standard WPF WebView2 creation only on **View reference**. Detect managed Evergreen and required API availability; record actual Runtime/SDK version. Missing/incompatible/blocked Runtime or cache fails closed with truthful native state; no bootstrapper, alternate browser channel/host bypass or core-startup dependency.
- [ ] Write admitted bytes to the sole mapped document under the owned `content` directory; separate fresh `profile`. Set mapping with **Deny**, required settings and handlers **before navigation**. Disable script/dialogs/web messages/host objects/DevTools/default menus/autofill/password persistence. Deny unexpected resource/frame/navigation/download/window/permission requests; CSP denies script/connect/frame/object/form/base and permits required static styles only. Ban production `ExecuteScript`, document-created scripts and CDP calls. Native find/copy/zoom use supported non-script facilities (`CoreWebView2.Find`/permitted keyboard facilities and WPF zoom), never injected code.
- [ ] Permit legitimate same-document anchors; observe `SourceChanged`/history because fragments do not cause NavigationStarting. Say “Opened at …”, not inferred scroll position. A user-initiated blocked renderer reference may populate the native pending field only if it belongs to the current document’s references and exact Microsoft policy; revalidate on native launch and clear on supersession. Other references are non-launchable.
- [ ] Close/hide immediately on client/account/connection/standard/document replacement, clear pending links, discard late callbacks and prevent focus theft. Back restores selection/filter/scroll/focus only in the unchanged context. Native toolbar/status stay outside the non-overlapped web rectangle; errors replace/hide it.
- [ ] Dispose owned control/controller appropriately; wait for relevant profile/browser lifetime before deletion. Lease/marker/path/reparse validation prevents cross-instance or unrelated cleanup; no killing shared processes. Failed retirement reports “Reader cache cleanup incomplete”; deferred sweep only touches proved retired owned sessions. Verify all export/backup/support/adoption paths exclude browser/content cache even when it contains JSON-like files.
- [ ] Run fake-policy/lifecycle tests, then **real hosted Windows negative tests** against synthetic fixtures and production host settings: injected markup/script, frame/resource/redirect/download/scheme attempts, absent Runtime, process failure, stale callbacks, fragment navigation, focus return, exact Save bytes and concurrent cleanup. Record denied requests without granting an external network route. Source inspection confirms no host-injected scripting path; page-script disablement alone cannot prove that. Test normal package/CLI without initialising Runtime.
- [ ] Fresh independent boundary review and all source-matched Windows checks; hosted and H1 accessibility results are separate. If required host enforcement cannot be demonstrated, the reader remains unavailable/not accepted; do not weaken the design for a passing demo.

**Rollback:** hide/disable the new View reference route and preserve native catalogue/export; retire only owned cache. No browser-cache migration or inferred secure erasure. **Evidence:** host settings/version record, observed refusal cases, exact-byte save, cache exclusion/concurrency and native return-state results.

### T10 — assembled source/package validation and approval packet

**Branch:** `astra/bs-phase-acceptance`. **Depends:** all preceding merged/verified tasks. **IDs:** all in-scope AC; especially AC-10–14/17–20/22–26/31/32.

**Files:** affected existing packaging/build/tests/guides only where integration requires it; `build/{Build-Portable,Test-Portable,Get-DependencyInventory}.ps1`, `build/Publish-ValidatedRelease.py` and existing publisher tests for backward-compatible package naming if required, existing acceptance/release docs and COMPLETION-REGISTER. Preserve historical release pins and the publisher’s allowed branches/approval gates. No publication or version/tag authority inferred.

- [ ] Refresh phase source and review dispositions; confirm every task’s exact merge/check identities and no unresolved safeguard finding. Do not repeat the whole historical audit. Run one fresh independent review of the assembled changed boundaries/features and their interaction; preserve all findings/limitations.
- [ ] Run the complete Windows solution/App suite, historical native harness cases and new hosted cases on the exact composed phase SHA. Keep 1480×940, 1180×760, 1180×640 continuity; add adaptive compact layouts without treating logical pixels as physical-DPI proof. Exercise every preserved route and synthetic new-client, legacy, repeat-review and engineer-handover flow.
- [ ] Build the fresh self-contained win-x64 package through existing scripts; verify stage/ZIP bytes, dependencies/notices, blank settings/evidence, new and legacy CLI launch/redirect/exit behaviour, actual startup/shutdown/right-click text Copy and Runtime-unavailable native operation. Check compatibility against unchanged historical fixture/evidence/catalogue hashes and shared lock rules. Do not merge old/new app folders in place.
- [ ] Produce the original new ZIP and SHA-256, source/standard/presentation identities, dependency inventory, neutral guide, capability/AC matrix, known limits and unrun human/live forms. Keep unpublished candidate status and source SHA explicit; obtain version authority before any changed release number/pin. Do not reuse historical package hash/proof.
- [ ] Record final source/run/artifact identities in the existing durable records and #83 after authorised execution. Request H1/William’s review and later integration/distribution decision; no automatic main merge, tag, release, settings change or backlog continuation.

**Commands (Windows, future execution):** T9 adds the explicit `--reader-hosted` mode to the existing harness; it must reject unknown switches and produce separate hosted evidence. The commands below use that future interface, not a presently available flag.

```powershell
dotnet test tests/BuildStandard.Tests -c Release --no-build --logger 'trx;LogFileName=tests.trx' --results-directory TestResults
dotnet test tests/BuildStandard.App.Tests -c Release --no-build --logger 'trx;LogFileName=app-tests.trx' --results-directory TestResults
./build/Setup-And-Build.ps1 -SkipTests
# The existing harness command in §3; proposed explicit hosted switch is added by T9:
dotnet run --project tests/BuildStandard.UiReview -c Release --no-build -- "$pwd" "$pwd/dist/reader-review" --reader-hosted
./build/Test-Portable.ps1 -ZipPath <original-new-zip> -StagePath <matching-stage> -ResultPath <new-verification-json>
python build/Update-ScriptRegistry.py --check
python build/Publish-ValidatedRelease.py --validate-pins
python build/Test-PublishValidatedRelease.py
Get-FileHash <original-new-zip> -Algorithm SHA256
```

Run existing PS5.1 template/parser/identity/library stub commands from `build.yml` unchanged in meaning. If CI already produced this exact complete evidence, inspect/reuse it rather than launch a duplicate validation. A required hosted run with unavailable Runtime is **unrun**, not a passing skip. **Rollback:** retain frozen original package/source and revert bounded phase commits; preserve all historical evidence and approval invalidation. Distribution/security-owner approvals remain outside this task.

### H1 — separate human/native acceptance, owned by William and a second engineer

**Depends:** T10 exact candidate. This is a future acceptance gate, not an agent test claim or live-action authorisation.

- [ ] On the intended managed Windows workstation, record OS, Runtime, policies, actual usable work area and 1920×1080 scaling at 100/125/150/175/200%; test keyboard/Narrator/status announcements, system contrast, focus, zoom/find/copy and native/browser entry/return/layering. Record unsupported conditions rather than relabelling renders as acceptance (AC-10/11/12/20/30).
- [ ] Compare actual native Home/detail/docked pin with the supplied B+A originals and neutral branding; approve documented differences (AC-14/23).
- [ ] Second engineer completes synthetic new-client → capture/assessment → CA-001 exact plan/disabled-candidate review → simulated result → handover, then saved legacy/repeat/offline paths. Record assistance, wrong turns and interpretation of failure/partial/zero/disabled/unknown states (AC-22 and working-flow ACs).
- [ ] Security owner reviews fresh binary/package fingerprints, Runtime servicing and applicable application-control route; no old hash approval transfers automatically (AC-17/24/26).

Live sign-in/consent/service reads/disabled writes require a **different, explicit authorisation and evidence record**. Human gates may remain open while source is ready; the product must not claim accepted completion solely because CI passes.

## 5. AC coverage and completion gates

All implementation tasks are initially GPT-owned; independent review is a real branch gate, not claimed by authorship. H1 belongs to William/second engineer/security owner as stated. AC-21 is expressly conditional/outside this phase, not forgotten.

| AC | Owner/task | Completion evidence / remaining gate |
|---|---|---|
| 01 | GPT T3 | Classifier/admission/refusal matrix; safety review; live access unclaimed |
| 02 | GPT T3/T6 | Distinct mode/token/connection/opt-ins, offline/setup identity tests |
| 03 | GPT T4 | Delayed preflight + equality/in-budget cases, original rows preserved |
| 04 | GPT T4 | Token delay/initial-send admission and not-sent/ambiguous/no-replay cases |
| 05 | GPT T5/T9 | Cross-client/same-tenant transitions and delayed export/callback refusal |
| 06 | GPT T5/T6 | One state/action matrix across Home/attention/navigation |
| 07 | GPT T5/T7 | Pre-transition warning and replacement-session approval invalidation |
| 08 | GPT T7 | Failed/partial/cancelled/not-attempted/zero and save-integrity dimensions |
| 09 | GPT T7 | Intended versus observed candidate state; unknown remains unknown |
| 10 | GPT T7, H1 | Native synthetic CA-001 keyboard path, then human keyboard acceptance |
| 11 | GPT T6/T7, H1 | Reachable routes/skip/details at compact sizes; real usable work area |
| 12 | GPT T6/T7, H1 | Automation/events plus actual Narrator/contrast/scaling acceptance |
| 13 | GPT T2/T6/T7 | All 21 routes and guide/step metadata agree; no capability expansion |
| 14 | GPT T6, William H1 | Actual WPF comparison with identified originals; human visual sign-off |
| 15 | GPT T8/T9 | Catalogue-only/no-operation opening and valid Back restoration |
| 16 | GPT T8/T9 | Exact admitted UTF-8 preview/save equality; mismatch refusal |
| 17 | GPT T9/T10, H1 | Missing/blocked/incompatible Runtime/cache; independent native/CLI startup |
| 18 | GPT T9 | Hosted negative enforcement plus no-host-script/bridge source review |
| 19 | GPT T5/T9 | Supersession, concurrent lifecycle and transfer exclusion evidence |
| 20 | GPT T9, H1 | Actual host/runtime evidence plus human assistive/resize/scaling acceptance |
| 21 | Explicitly excluded | GPT-R-003/004/007, CLA-201/-202 remain conditional; preserve attribution/limits |
| 22 | GPT T10, second engineer H1 | Synthetic journey plus recorded human unaided outcome; live separate |
| 23 | GPT T2/T6/T10 | Active brand inventory/exception list and ordinary UI/output evidence |
| 24 | GPT T1/T10 | Neutral package/CLI/resource identities and documented aliases |
| 25 | GPT T1/T2/T10 | Historical byte/data/lock compatibility; no restored authority/adoption |
| 26 | GPT T2/T8/T10 | New output/package fingerprints; historical source unchanged |
| 27 | GPT T7 | Exhaustive nine-status partition with original qualifications |
| 28 | GPT T8/T9 | Fragments retain Deny; deceptive/stale/non-user external-link negatives |
| 29 | GPT T6 | Explicit saved-source docked pin; mismatch/collapse/no authority tests |
| 30 | GPT T5/T6/T9, H1 | Late completion/disposal focus tests plus actual keyboard experience |
| 31 | GPT T1/T9/T10 | Neutral owned reader paths; no cache bytes in any transfer/support route |
| 32 | GPT T2/T6/T9 | Ordinary empty/offline truth, separately labelled synthetic harness data |

## 6. Dependencies, risks and approval boundaries

**No source divergence or open competing PR currently blocks planning.** Recheck at execution; later changes invalidate that observation. Serialise T0→T1 and contract acceptance before dependent feature work. Recommended one-implementer order: **T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, H1**. T8 can be prepared while a safety review is pending once its dependencies merge; do not implement against an unapproved shared contract or bypass a merge gate.

Material execution dependencies:

1. **William’s explicit approval of this plan for execution.** Planning approval is already granted; no need to choose the execution method again.
2. **Classic/phase protection visibility and phase merge authority.** HTTP 403 prevents confirming actual classic requirements here. William/admin must confirm settings; changing them requires separate approval. Existing Windows jobs remain the minimum planned evidence, not proof of actual protection.
3. **Independent reviewers.** Fresh independent review is required for every bounded completed branch. Existing GOAL explicitly requires the other agent for execution/recovery/Graph write/tenant-confirmation/registration/permission paths. Claude is the existing counterpart for that named process; a different reviewer or human substitution needs William’s explicit gate amendment where applicable. His former App ownership does not block GPT work, but his availability/review is not invented. Unavailable required review blocks merge, not unaffected authorised work.
4. **Windows/Runtime and human access.** Historical CI proves past availability only. New native/package/host evidence needs a provisioned authorised Windows runner; hosted Runtime installation, if missing, is separately approved—never hidden in tests. H1/live gates remain open until actually performed.
5. **Version/distribution choice.** Keep current source version until existing policy permits a successor; distinguish phase artifacts by full source/hash, never publish/reuse a release pin for changed bytes. Final phase→integration/main, release and security-owner adoption are later decisions.

Principal risks are mechanical resource/package breakage (T1), accidental evidence/root/cache migration (T1/T2), weakening write timing/outcomes (T4), stale native/browser context (T5/T9), and a browser dependency silently breaking ordinary portable use (T9/T10). Each has a named negative/compatibility test and rollback boundary above. Performance gains, token savings and delivery dates are not assumed.

## 7. Primary documentation and plan self-check

Read directly from Microsoft Learn on 11 October 2026; saved retrieval status is local. These informed planned APIs rather than implying a hosted prototype ran:

- [WPF .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100): keep existing WPF resources; no wholesale Fluent/third-party framework dependency.
- [Local content](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/working-with-local-content), [host resource access](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2hostresourceaccesskind), [navigation events](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/navigation-events): mapped-file origin, Deny semantics and fragment-event limitations.
- [Security](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security), [IsScriptEnabled](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2settings.isscriptenabled), [settings](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2settings): restrictions before navigation; disabling page scripts does not disable host-injected script routes.
- [Runtime distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution), [UDF lifetime](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder), [BrowserProcessExited](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2environment.browserprocessexited), [WPF hosting](https://learn.microsoft.com/en-us/microsoft-edge/webview2/platforms/wpf): optional managed Runtime, owned lifecycle and non-overlapped native host.
- [Find](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.find), [resource filters](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.addwebresourcerequestedfilter), [Runtime detection](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2environment.getavailablebrowserversionstring): non-script facilities and explicit capability detection. The guessed `ShowFind` page returned 404 and is **not** used. API/Runtime support must be tested for the selected managed deployment; no invented numeric Runtime floor.
- NuGet’s official WebView2 version index returned stable **1.0.4258.31**. This is the proposed exact SDK pin, not an installed dependency or a tested .NET 10 host. Confirm its required APIs on the intended Runtime at T9; unsupported restrictions make the reader unavailable.

**Skills available/used:** Superpowers writing-plans to structure this plan; using-git-worktrees and executing-plans read to preserve the selected future execution/isolation rules; verification-before-completion for source/artifact/coverage claims. Earlier using-superpowers guidance remains applicable. Cloud runtime/onboarding guidance was read for access/isolation constraints; no setup change was made. No brainstorming restart, implementation workflow, subagent or independent-review claim was made during planning.

**Self-check:** all D-01–D-07 map to bounded tasks; all 32 AC IDs appear in the coverage table; AC-21 is explicitly excluded; human/native/live gates are separate. Proposed shared interface names are used consistently by their consumers. Branch/protection/CI, compatibility exceptions, durable coordination, fresh review and rollback are explicit. This plan does not reproduce method bodies or reopen unrelated findings.

**Planning completion:** one coordinated plan outside the repository, ready for William’s review. No repository edit, branch/worktree, restore/install, build/test/prototype, CI trigger, GitHub mutation or tenant action occurred. **Stop until William explicitly approves plan execution.**
