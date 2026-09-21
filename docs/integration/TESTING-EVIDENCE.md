# Testing evidence — 17 September 2026

## Confirmed safety review F1–F5 — 21 September 2026

See [exact commands, test failures and regression comparison](SAFETY-REVIEW-2026-09-21.md). Current local working-tree validation: 536 tests passed, none failed/skipped; Release build 0 warnings/errors; portable package built; offline WPF 44 renders / 28 combinations / 0 bindings / 0 tenant calls. Clean-head and new CI evidence are recorded separately after publication.

The earlier 495-test local result below is historical. Reviewer-observed 498-test [CI run 35545344377](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/35545344377) tested synthetic merge `ec3049b18bf23bcf1b13810e096234414fa58495`, not pure head `1d47c814...`; do not describe it as clean-head validation.

## Preview.13 independent review fixes — 21 September 2026

Branch `astra/review-fixes`, [PR #9](https://github.com/Willzy12h/M365-Buildstandards/pull/9), base integration `d4e1e9a`.

- Local Windows .NET SDK 8.0.425: full synthetic suite **495 passed / 0 failed / 0 skipped**, captured in ignored `TestResults/review-fixes.trx`.
- Windows Release solution build: **0 warnings / 0 errors**. Portable self-contained win-x64 package built successfully.
- Offline WPF harness: **44 images / 28 page-size combinations / 0 binding issues**; includes expanded prerequisite guidance, explicit All users/All devices choices, same-tenant input refresh and idle shutdown. This does not establish human accessibility or live authentication behaviour.
- Published .3–.10 standard files are unchanged. .11 parses with 50 controls, 42 recipes and 23 collections. No live tenant data, credentials or outputs are committed.
- Independent read-only code check found no blocking issue in directory creation-only routes, exclusion-purpose checks, historical digest compatibility or .11 identity mapping.
- CI evidence must come from this PR's current head; consult its checks for the exact run and counts. Local results above are not CI evidence.

Fixture corrections: `ExclusionGroupCoverageTests` now supplies the recorded original group payload needed to establish purpose; existing assertions remain. `BuildStandardDocumentTests` and `WindowsHelloRecipeTests` now fail when required catalogue fixtures are missing rather than silently returning. New test fixtures were corrected to contain complete group-member reads and to exercise a supported device-configuration update; a separate regression preserves the existing manual-only compliance/related-settings update rule. No existing assertion was weakened.

New tests exposed two additional implementation gaps fixed here: numeric array-count limits must accept in-memory integer nodes, and an explicitly empty optional ESP application list must resolve without allowing empty emergency/identity lists.

Not verified: application registration, admin consent, tokens/permissions against Graph, policy acceptance or device effects, real Autopilot/LAPS/Hello setup, production rollback, keyboard/screen-reader/high-DPI human acceptance. No live operation was attempted. Disposable-tenant testing remains the maintainer's decision.


## Windows Hello configured end to end — preview.12

- Same sandbox, no .NET SDK, so **no local build or test run was possible**. The GitHub `build` job on the submitted head is the only compilation and test evidence.
- New synthetic coverage: `WindowsHelloRecipeTests` (enabled on a hardware security device with TPM 1.2 excluded; provisioning not forced and PIN recovery allowed; minimum length eight with a digit required and the three character-class rules left unset; expiration zero and no history; biometrics and security keys present and not placed under the tenant node; phone sign-in, enhanced anti-spoofing and cloud Kerberos trust absent; ten unique device-scoped Passport paths).
- The composition value convention (1 requires a class, 2 forbids it, unset permits it) and the two device-scoped paths for biometrics and security keys were checked against published Microsoft CSP documentation on 17 September 2026 rather than recalled. Microsoft's own site is unreachable from this environment, so they were confirmed through mirrored documentation and should be re-checked against the Intune portal on first deployment.
- Not verified: that a device accepts these settings, that a user can enrol a PIN under them, and that biometric and security key sign-in work. All three need the pilot device the control's engineer action calls for.
- No live tenant, consent, registration, write or GUI acceptance was performed.

# Testing evidence — 15 September 2026 (preview.10)

## Client build standard document — preview.10

- Same sandbox, no .NET SDK, so **no local build or test run was possible**. The GitHub `build` job on the submitted head is the only compilation and test evidence.
- New synthetic coverage: `BuildStandardDocumentTests` (every control reaches the HTML and Markdown documents with its own wording; payloads, Graph paths, service hostnames and unresolved templates stay out; delivery wording follows whether a control is created, read or manual; required and defaulted inputs are separated, and the tenant identifier is never presented as something to ask a client for).
- Not verified: how the document reads to an actual client, and printing to PDF from a browser.
- No live tenant, consent, registration, write or GUI acceptance was performed.

# Testing evidence — 15 September 2026 (preview.9)

## Input form, reviewed defaults and built-in targeting — preview.9

- Same sandbox, still no .NET SDK, so **no local build or test run was possible**. The GitHub `build` job on the submitted head is the only compilation and test evidence.
- New synthetic coverage: `PolicyInputParserTests` (identifier lists typed with commas, new lines or mixed spacing; a bad identifier named in the problem text; empty text meaning "not supplied" rather than an error; invalid JSON and a JSON object where an array is required; per-type acceptance; render and read round-trips) and `PolicyInputDefaultsTests` (a missing reviewable input takes the default and warns; a supplied value is never replaced; an input the payload does not use is untouched; an identity input is never defaulted; a default older than 90 days says so).
- Standard 2026.09.9 was generated from 2026.09.8: prerequisite groups reduced from seven to four, expected production targeting moved to the built-in populations, minimum iOS raised to 26.7, and dated defaults added for the Android minimum version and Enrolment Status Page blocking applications.
- Minimum operating system versions were checked against vendor support on 15 September 2026, not carried over from training. Windows 11 build families 26100 (24H2) and 26200 (25H2), with 24H2 Home and Pro ending 13 October 2026. Android 14 and later receive Google security bulletins; 13 ended in March 2026. iOS 27 shipped in September 2026 alongside 26.7.
- Not verified: that the generated form renders correctly in WPF beyond the synthetic harness, and everything already listed as unverified for preview.8.
- No live tenant, consent, registration, write or GUI acceptance was performed.

# Testing evidence — 15 September 2026 (preview.8)

## Directory prerequisites and administrator-access assessment — preview.8

- Authored in the same sandbox, which still cannot download any .NET SDK, so **no local build or test run was possible**. The GitHub `build` job on the submitted head is the only compilation and test evidence for this increment.
- New synthetic coverage: `DirectoryPrerequisiteSafetyTests` (an empty security group is accepted; members, owners, dynamic rules, role-assignable and `@odata.bind` payloads are refused; mail-enabled, non-security, unified and badly named groups are refused; a named location is refused if trusted, a country location, empty, or carrying a range that is not a whole CIDR of the declared family, or extra range properties), and a standards test asserting 53 controls, 45 recipes, writable group and named-location collections, every prerequisite payload passing the write guard, and ID-003 declaring `AtLeast`/`AtMost` signals against `directoryRoles`.
- Standard 2026.09.8 was generated from 2026.09.7: eight prerequisite controls added, the groups collection given a write scope and a wider `$select` so a created group can be compared with its recipe, named locations given a write scope, a `directoryRoles` collection added and ID-003 given equivalence signals. No existing recipe payload changed.
- Not verified: that Graph accepts these group and named-location payloads, that `/directoryRoles` with a `members` relationship reads as expected under the existing `RoleManagement.Read.Directory` scope, and that a created group is returned by the widened `$select`. All three need the first real capture.
- No live tenant, consent, registration, write or GUI acceptance was performed.

# Testing evidence — 15 September 2026 (preview.7)

## Evidence-based assessment, cancellation and persistence fixes — preview.7

- Authored in a sandbox that could not download any .NET SDK (egress blocked to every Microsoft download host), so **no local build or test run was possible**. The GitHub `build` job on the submitted head is the only compilation and test evidence for this increment; read the check for the exact commit rather than this document.
- New synthetic coverage: `ReviewedChangeSafetyTests` (authentication-method state changes, TAP fixed settings and single group, MDM scope limited to the Intune policy and `all`, tenant compliance preserving other settings, CA activation state-only on v1.0 by ID, group assignment/removal, Autopatch 1–50 distinct devices, and that plan free-text route/method/permission/API cannot widen a change); Entra LAPS payload preservation and refusal of unknown properties; capture cancellation marks remaining collections *Not attempted* and reads nothing further; `CanonicalJson.TryAt` array selector; 2026.09.7 declares equivalence on ID-002, ENR-001 and CMP-001 and on no manual-by-nature control.
- Standard 2026.09.7 was generated from 2026.09.6 with equivalence rules added to three controls and their manual instructions updated; no recipe payload changed. The CI `standard` job checks every release for disabled Conditional Access state and absent assignments.
- No live tenant, consent, registration, write or GUI acceptance was performed. The authentication methods, MDM policy and tenant settings shapes the equivalence paths rely on are taken from the Graph v1.0/beta resource documentation as previously recorded in AUTOMATION-COVERAGE.md and must be confirmed against a real capture.

# Testing evidence — 14 September 2026 (historical)

## Expanded automation and follow-up review — preview.6

- Final Windows Release solution compilation with SDK 8.0.425 and existing packages: **0 warnings, 0 errors**. The local standards integrity manifest was regenerated. No new package restore or vulnerability audit is claimed.
- No test suite, new tests, GUI rendering, portable install, live sign-in, Graph writes, device assignment or package deployment was run for this increment. The user explicitly requested implementation without further testing. Existing CI was not disabled.
- Source checks confirmed existing Graph page/loop caps, generic route signatures, ProfileValidator namespace and implicit LINQ imports. Added per-item pagination checks, shutdown diagnostics/exception containment and null guards.
- New standard JSON was generated from unchanged 2026.09.5: 45 controls, 37 recipes. This inventory is not runtime or live payload verification.
- Prior preview.5 test/build results below are historical and do not validate these changes.


## Policy automation code — preview.5

- Full Windows Release solution build: **0 warnings, 0 errors**.
- Full Release test suite: **295 passed, 0 failed, 0 skipped**; 44 new cases cover imports, LAPS prerequisite evidence/approval/drift, full-PUT preservation, not-sent/rejected/ambiguous outcomes, read-only re-verification, cancellation and all four candidate deployment/recovery paths. TRX retained locally as `work/test-results-policy/policy-automation.trx`.
- Standard 2026.09.5 parses: **45 controls, 18 recipes, 15 collections**. Historical releases are unchanged. Only four control definitions differ from 2026.09.4.
- Used existing SDK 8.0.425 and workspace NuGet packages without restore. This is not a fresh vulnerability audit. PR checks provide separate clean restore/package evidence when completed for the submitted commit.
- No UI changes or new manual GUI acceptance were performed. Import and Entra LAPS service integration is pending. No live tenant write, consent, device assignment, Defender onboarding or LAPS password retrieval was performed.

## Engineer workflow preview 1.1.0-preview.4

- Windows Release solution build: **0 warnings, 0 errors**. Complete local suite: **251 passed, 0 failed, 0 skipped**. The build script generated the self-contained win-x64 portable package.
- New setup coverage includes explicit-ID repair, exact consent/native redirects, branding writes with empty HTTP 204 responses, current-engineer assignment, preservation of unconsented state, input drift/credentials rejection, accepted updates whose homepage readback differs, disallowed redirect/credential/app-only payloads, pre-send setup auth failures and no replay of uncertain follow-up writes.
- The actual shipped BitLocker and long-path payloads pass synthetic planning, durable execution/readback and selective deletion/absence tests; assigned objects are protected. Beta recovery is limited to the device-configuration route and uncertain DELETE is attempted once.
- Offline WPF harness: **26 page/size combinations, 0 binding errors**, including top/bottom setup screens and per-write result details at 1180x760 and 1480x940. Idle-window shutdown passed. The setup permission text encoding was corrected and renders repeated.
- Standard 2026.09.3 bytes remain unchanged (SHA-256 `a498550d2cbeb0dccefe78bde8bb955a7113096de338d680741222e1d3c20e9d`). New 2026.09.4 has 45 controls, 14 recipes and 14 collections. MSAL Broker 4.89.0 / NativeInterop 0.20.6 were restored from the workspace feed; no fresh local vulnerability audit is claimed.

Live WAM/browser authentication, existing registration repair, admin consent and branding propagation, GDAP, Intune omitted defaults, device encryption/escrow and long-path effects remain unverified. No live tenant actions or real evidence migration occurred. See [live acceptance](../LIVE-VALIDATION.md), [application setup](../APPLICATION-SETUP.md) and [device automation](../DEVICE-AUTOMATION.md). GitHub checks must be read for the new submitted head, not inferred from previous green runs.


## Independent review fixes — 1.1.0-preview.3

- Complete local Windows Release suite: **237 passed, 0 failed, 0 skipped**. Build-Portable ran the suite after a solution build with **0 warnings / 0 errors**. The previous targeted pass was 90/90, followed by 236/236 before adding malformed-write-route coverage.
- New tests prove pre-token/pre-route/pre-send cancellation failures send no HTTP write, and engine/recovery record NotAttempted without permanently blocking a fresh plan. Follow-up failures preserve Accepted.
- Re-verification tests cover delayed CA/Intune deletion, read-only deployment checks, restore mapping finalisation, state-only containment, repeated read verification, unknown-write refusal, tenant/ownership/drift gates, cancellation, corrupted verification evidence and acknowledged 1.0.0 reconciliation with unchanged original files.
- Hung-preflight and after-capture-budget tests exercise Stop, truthful terminal states and retained incomplete evidence. They do not simulate operating-system or disk hangs.
- Offline WPF harness: **26 page/size combinations, 26 synthetic PNGs, 0 binding errors**, including the new recovery verification panel; idle-window shutdown passed. New panel inspected at 1180x760. No tenant calls.
- Local SDK 8.0.425, workspace offline NuGet feed; no fresh vulnerability audit is claimed. Self-contained win-x64 packaging uses the repository build script. Current GitHub validation is available through [PR #2 checks](https://github.com/Willzy12h/M365-Buildstandards/pull/2/checks); verify the submitted head's conclusion rather than relying on previous green runs.

No real tenant evidence was inspected or migrated. Real token revocation, admin-consent localhost redirect, Graph replication, connected shutdown timing and human accessibility remain acceptance tasks. See [Claude review response](CLAUDE-REVIEW-RESPONSE.md). Historical preview.2 evidence below is preserved separately.

## Engineer workflow preview 1.1.0-preview.2

This section describes PR #2; the 110-test baseline import below is historical evidence.

- Release Windows solution build: zero warnings and zero errors. MSAL token-cache Windows guards remove the original platform warnings.
- Automated tests: **211 passed, 0 failed, 0 skipped** in the complete local Release suite. New coverage includes recovery deletion/absence, restoration, unexpected CA activation, approval/tenant/drift/assignment gates, shared evidence-store exclusion, licence counts/user search/scope, HTTP 408/5xx ambiguity and earlier preview safety boundaries. TRX retained locally.
- The immediately preceding full run passed 210/211 and encountered a reset on the valid localhost callback after invalid requests. The unchanged complete rerun passed. This remains evidence of local callback-test intermittency, not proof of production consent acceptance. Earlier GitHub CI exposed IPv6 fallback delays; the test transport now connects to the IPv4 listener explicitly while retaining the validated localhost URI/Host.
- Real WPF views constructed offline: all 13 pages at 1480x940 and 1180x760, 26 combinations, no binding errors. Twenty-four PNGs captured using synthetic data, including licence and recovery views. The harness also opens and closes an idle window and requires completed shutdown. The harness throws if asked to contact a tenant. Source is tests/BDIT.TenantToolkit.UiReview and CI uploads the renders.
- Local response-filtering software (AdGuard) altered localhost HTTP test responses. Exact callback response headers, escaping and absence of reflected parameters are therefore checked directly against emitted bytes; socket tests separately enforce correlation, rejection, cancellation and successful valid completion. A rejected request may return HTTP 400 or a TCP reset; it must never complete approval. Security filtering was not disabled.
- A transient Windows evidence-file replacement lock was reproduced. Only local atomic replacement retries sharing/access errors, for at most 300 ms in total; permanent failure remains explicit. Graph writes are never retried. A reader-lock regression test covers this distinction.
- Local restore uses the workspace offline feed described below. It does not establish a current package vulnerability audit. GitHub CI performs the normal online restore separately.

[PR #2 checks](https://github.com/Willzy12h/M365-Buildstandards/pull/2/checks) validate the submitted commit independently, including Windows tests, portable packaging, offline UI rendering, standards and tracked-source/configuration checks. Check the run conclusion for the current head; local results are not a substitute for CI.

Confirmed GitHub evidence: [run 34817971384](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/34817971384), code commit `1ff9fe0741b9e95531c0f56adc20f5eba5fafc90`, succeeded in all three jobs. The Windows log confirms 211 passing tests, zero build warnings/errors, self-contained packaging and 24 UI images / 26 combinations with zero binding errors. Test, package and synthetic UI artifacts were uploaded. Subsequent handover-only changes do not change the tested code.

The local preview.2 package was independently launched with an empty synthetic data root: a WPF window opened, standard 2026.09.3 loaded all 45 controls, and normal close exited with code 0. All 280 staged-file checksums matched; the ZIP contained no populated runtime data/log/report folders, token caches or saved profiles. Local and CI ZIP hashes differ because package manifests include build time/machine/runtime metadata. Verify the sidecar accompanying the chosen package.

Human keyboard/screen-reader/high-DPI testing, connected-session shutdown, real sign-in, app creation, consent, assignments, Graph collection, policy acceptance and recovery remain unverified. This is an internal preview, not live-tenant acceptance. No live tenant operation was performed.

## Source baselines

- Claude main 337c8e6: [Windows CI failed](https://github.com/Willzy12h/m365-Tenant-Toolkit-Claude/actions/runs/34625802405). Newer documentation-only 21d8fea also [failed](https://github.com/Willzy12h/m365-Tenant-Toolkit-Claude/actions/runs/34627546140). Local original build reproduced 13 missing reporting-type errors after successful dependency restore; tests could not run against that source.
- Asta main 020c69b: [CI succeeded](https://github.com/Willzy12h/m365-tenant-console-Asta/actions/runs/34624562838). Newer documentation-only 84f32c2 also [succeeded](https://github.com/Willzy12h/m365-tenant-console-Asta/actions/runs/34628240515). Asta tests were inspected selectively, not rerun locally.

## Baseline import local Windows checks

Used existing .NET SDK 8.0.425 in the original local Claude checkout, read-only. No SDK or runtime installed machine-wide. Dependency restore used copies of existing NuGet package archives in workspace-local cache/feed folders because this sandbox could not restore from api.nuget.org. This is not evidence of a fresh online restore or package vulnerability audit; CI checks the normal restore independently.

1. Original source: build failed with missing Reports, ReportExporter and ExportFormat.
2. Recovered reports: exposed one further CS0103 error, CopySummaryCommand referencing nonexistent Summary. Fixed to use SnapshotText.
3. Recovered tests: 108 passed, one failed on stale six-sheet expectation. Kept eight exported evidence tables; corrected the assertion to exact names and added an unmet-equivalence/caveat test.
4. Full WPF solution: build succeeded. Clean compilation reports three existing CA1416 platform warnings; an incremental successful build reports zero. Warnings were not suppressed.
5. Tests: **110 passed, 0 failed, 0 skipped**, Release/net8.0. TRX retained locally. HTML escaping, CSV formula guards, XLSX inline strings, report generation, tenant binding, collection/planner/executor and equivalence tests are included.
6. Ignore check: original reports/ pattern matches C# Reports/ under case-insensitive Git matching. Master /reports/ ignores generated reports without hiding source. CI also requires all six source files to be tracked.
7. Independent review caught the master's inherited evidence/ pattern hiding EvidenceStore.cs. Anchored runtime evidence/connections/exports/snapshots paths, staged EvidenceStore.cs, and checked every selected original source path against the Git index. CI explicitly checks EvidenceStore.cs too.
8. Local Build-Portable.ps1 -SkipTests completed after the passing suite, publishing a self-contained win-x64 application and ZIP/checksum. A limited process smoke test created a WPF window, loaded standard 2026.09.3 and closed gracefully with exit code 0. It used an empty synthetic workspace and did not sign in. This does not verify GUI interactions or connected-session shutdown.

## Baseline PR verification

[PR #1 checks](https://github.com/Willzy12h/M365-Buildstandards/pull/1/checks) are the source of truth for normal GitHub-hosted restore, Windows build/tests, portable packaging, standard checks and tracked-evidence/configuration checks. Portable ZIP and SHA-256 sidecar are review artifacts, not a production release. Test results are uploaded even on failure.

## Not verified

Interactive GUI workflows, connected-session cancellation/shutdown, live Microsoft sign-in or consent, real Graph reads/writes, recipe acceptance, app registration, client-specific licensing, device/sign-in outcomes and recovery against a tenant. No tenant was contacted. No automatic rollback claim is made.
