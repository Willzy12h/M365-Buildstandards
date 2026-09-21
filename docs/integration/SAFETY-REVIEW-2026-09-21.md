# PR #9 safety review implementation — 21 September 2026

Reviewed production source: `1d47c81499988a7bc3b4c3e0eb324cfab4fcf216`; reviewed base: `d4e1e9a9bfed620c06103a54a28a0cf0a574a5fb`. Work remains on `astra/review-fixes`, PR #9, targeting integration. Main, integration, Claude branches and both source repositories were not changed by this work. No tenant operation is authorised or performed.

Base-ref distinction: PR metadata still reports `d4e1e9a...`, but the actual integration ref is `3fd4167fb9555e9173f9e19c376a66811c1788ab`. Both the reviewer's old synthetic merge and the new PR merge have that integration parent. Its already-merged roadmap/headless-runner work adds solution, CLI, three tests and handover guidance. Those changes were compared with the reviewed base and preserved: no base merge, overwrite or source-repository update was performed. The synthetic PR merge retains the roadmap handover addition.

## Finding dispositions

| Finding | Resolution | Evidence |
|---|---|---|
| F1 — material CA additions accepted | Enable/report-only preview and execution require a complete, intact accepted write tied to the current exact-ID mapping and its verified observation. Compare material policy state against both the approved payload and verified observation. Extra targeting, exclusions, filters, controls and unknown fields block activation. | Service tests include application exclusions, device filters, session controls, unknown fields, missing baseline, an old internally consistent preview, separate read-only verification and emergency containment. |
| F2 — enrolment/Autopilot contracts | Enrolment assignment/removal uses `enrollmentConfigurationAssignments`; readback selects the resource-specific envelope. Autopilot generic group assignment and removal, including empty removal, are rejected before transport. | Independent literal HTTP expectations and service readback tests. Accepted historical enrolment envelopes can be interpreted through GET-only verification without rewriting evidence; Unknown writes remain blocked. |
| F3 — beta recovery | Recovery re-verification passes the collection API version into the existing payload guard. No beta-only guard is relaxed. | Accepted beta EDR restoration with unavailable initial readback later verifies through beta reads, finalises only the local mapping and preserves source/recovery evidence. v1.0 EDR is still rejected. |
| F4 — application compliance | Assignment records no longer establish required deployment. A typed, assessment-only expectation identifies intent, built-in population and exclusion prerequisite for the eight .11 application controls. | Uninstall, available-only, exclusion-only, missing/wrong/group targets, mixed intents, filters and missing scope cannot establish compliance. A required, unfiltered assignment matching an explicitly declared population is a positive case. |
| F5 — array caveats | Only .11's `partialClients` and `exemptsMany` change to count operators. | Tests load the shipped catalogue and cover one/two clients and five/six platforms. TAP numeric maximum-lifetime tests remain unchanged. |

## Comparison and compatibility boundaries

- Global `CanonicalJson.IsSubset` remains unchanged. Its deployment, planning, recovery and equivalence callers were inspected; this change does not claim every subset comparison is an equality check or repair unconfirmed defects.
- CA normalisation is path-specific: service identity/label/timestamps/ETag, known optional null objects, known empty scalar lists and order-independent documented scalar sets. Unknown properties are not silently discarded. Disabled state and emergency/operator exclusions retain their separate gates. Emergency disablement does not require unchanged targeting.
- An observation that already contains material additions beyond the approved payload is not an adequate baseline, even if an older subset-based readback marked it Pass. Missing/partial historical payloads fail closed for activation. Re-verification never adopts current drift.
- `expectedProduction.applicationDeployment` is optional and omitted when absent, preserving historical model digests. Existing .3–.10 catalogue files are unchanged. It is assessment metadata, never a Graph payload or permission grant.
- The .11 application controls deliberately remain manual-review when their expected exclusion group's effective scope is not established. Resolving a GUID/securityEnabled does not prove compatible user/device exclusion or membership. No full membership inference was added.
- Plans affected by changed catalogue/approval inputs need fresh previews. Retain the original reviewed catalogue when re-verifying historical accepted evidence; do not edit old plans, runs or digests.
- No automatic retry/replay, broader ownership, directory update/deletion, assignment during creation, or permission/application changes were introduced.

## Microsoft contracts checked

Checked 21 September 2026: [enrolment assign envelope](https://learn.microsoft.com/en-us/graph/api/intune-shared-deviceenrollmentconfiguration-assign?view=graph-rest-beta), [Autopilot assign device IDs](https://learn.microsoft.com/en-us/graph/api/intune-enrollment-windowsautopilotdeploymentprofile-assign?view=graph-rest-beta), [application assignment intents](https://learn.microsoft.com/en-us/graph/api/resources/intune-apps-mobileappassignment?view=graph-rest-1.0). Documentation and synthetic transport tests are not live acceptance.

## Local validation

Portable Git 2.55.0.5 and .NET SDK 8.0.425 were downloaded from official release metadata, checked against published SHA-256/SHA-512 respectively, and extracted into the workspace. No machine-wide Git/SDK installation. NuGet dependencies were restored into a workspace-local cache.

Commands below used that SDK on PATH, workspace-local `DOTNET_CLI_HOME`/`NUGET_PACKAGES`, and disabled MSBuild node reuse. No credential or tenant data was provided.

| Check | Actual result |
|---|---|
| `git fetch origin`, PR metadata, open PRs across all three repositories, claims, clean checkout | Head/base matched the reviewed refs; only PR #9 open; checkout initially clean. |
| `dotnet test tests/BDIT.TenantToolkit.Tests -c Release --no-restore --disable-build-servers -m:1 --logger "trx;LogFileName=safety-full-final.trx"` | 536 passed, 0 failed, 0 skipped. |
| `dotnet build BDIT.TenantToolkit.sln -c Release --no-restore --disable-build-servers -m:1` | Release succeeded, 0 warnings, 0 errors. |
| `./build/Build-Portable.ps1 -SkipTests -OutputDirectory <workspace>/dist/safety-review` | Self-contained win-x64 package produced; 302 files in checksum manifest. |
| `dotnet run --project tests/BDIT.TenantToolkit.UiReview -c Release -- "<checkout>" "<checkout>/dist/ui-review"` | 44 renders, 28 page/size combinations, 0 binding issues, 0 tenant calls, idle window closed. |
| `git diff --check` and .3–.10 comparison with reviewed head | No whitespace errors; no historical catalogue changes. |

Mutation-detection comparison: detached worktree at reviewed commit `1d47c814...`, with only the new `SafetyReviewRegressionTests.cs` and supporting fake-client instrumentation copied in. Production source and catalogue remained unchanged. Running the same class with `--filter FullyQualifiedName~SafetyReviewRegressionTests` produced **19 failed / 7 passed / 0 skipped**, including all five reported defects. This is an intentional failing regression experiment, not a clean-head build or a CI result.

Intermediate failures are retained in local TRX files: initial test source had a raw-string delimiter compile error; fixtures then needed the bound tenant, service route, EDR licence and full before/after assignment reads. An old-preview fixture initially attempted to replace an immutable plan and was corrected to use a distinct ID. No production assertion was weakened. Earlier suite runs reported 525/526 and 529/530 before these fixture corrections. The initial sandboxed MSBuild attempt stalled without test output; only processes using this task's workspace SDK were stopped, and subsequent builds/tests ran successfully outside that restriction.

## Published-head validation

Implementation commit `fb48159e5748fb8321021fe671008d2559e131ef` has exactly the locally staged tree `22a31841dc949ca1f1b362048bb49876d7cc92e2`; tree equality was verified before moving the remote branch. A transfer mismatch was corrected before any branch ref was moved.

A separate detached checkout of that pure head was clean before and after validation. Commands: `dotnet restore BDIT.TenantToolkit.sln`; `dotnet build BDIT.TenantToolkit.sln -c Release --no-restore --disable-build-servers -m:1`; `dotnet test tests/BDIT.TenantToolkit.Tests -c Release --no-build --logger "trx;LogFileName=clean-head.trx"`; `./build/Build-Portable.ps1 -SkipTests -OutputDirectory <checkout>/dist/clean-head`; the offline UI command above.

Actual clean-head results: **536 passed / 0 failed / 0 skipped**, **0 build warnings / 0 errors**, portable package produced, **44 renders / 28 combinations / 0 binding issues / 0 tenant calls**. All **303** staged file hashes matched; all nine packaged catalogues matched Git blob identities after LF normalisation. The extra packaged file versus the earlier 302-file working build is this safety handover document. Assignment, assessment and recovery-verification images were visually inspected at 1180×760; that is not human accessibility acceptance.

Actual GitHub results, read from completed job logs:

| Event | Checkout verified from log | Run / build job | Tests | Other results |
|---|---|---|---|---|
| Direct push | `fb48159e5748fb8321021fe671008d2559e131ef` | [35571146129](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/35571146129) / 106242857507 | 536 passed, 0 failed/skipped | All three jobs successful; Release 0 warnings/errors; package uploaded; UI 44/28/0. |
| PR synthetic merge | `751cf9823883aa760dfa9bc11c815f1e835c44c3` | [35571149970](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/35571149970) / 106242869152 | 539 passed, 0 failed/skipped | All three jobs successful; Release 0 warnings/errors; package uploaded; UI 44/28/0. |

The final small follow-up explicitly normalises absent versus null optional authentication-strength and session controls; non-null/unknown controls remain material. The existing positive service regression now exercises those defaults. Its full local suite also passed 536/0/0. Microsoft [grant controls](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccessgrantcontrols?view=graph-rest-1.0) and [session controls](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesssessioncontrols?view=graph-rest-1.0) were checked. Final-head clean-checkout and CI results are recorded in the PR after this evidence commit; the completed runs above are explicitly for `fb48159...`, not assumed results for a later head.

Historical evidence remains separate: the prior documented local result was 495 tests. [CI run 35545344377](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/35545344377), build job 106170125759, reports success, but checked out synthetic merge `ec3049b18bf23bcf1b13810e096234414fa58495`, not the pure reviewed head. Its reviewer-observed 498 tests, 0-warning/0-error build and 44/28/0 UI results are not this increment's results.

## Bounded acceptance proposal — not authorisation

The human maintainer must separately approve the disposable tenant, operator and application identities, emergency accounts, exact test populations/exclusions, permitted operations and recovery boundaries. Keep an independent administrator session available and retain evidence locally.

1. Review captured configuration and expected populations; record whether inclusion/exclusion groups are user- or device-based and why Intune exclusion rules apply. A group GUID alone is insufficient.
2. Test one unassigned ESP candidate: explicit narrow assignment, readback, separately approved removal and readback. Autopilot generic assignment/removal must remain blocked.
3. Test one disabled CA candidate scoped only to approved disposable identities. Add a material exclusion through a separately authorised procedure; activation preview must reject it. Test state-only emergency containment only within the approved scope.
4. Test one beta EDR recovery using an approved inert candidate. Accepted restoration with unavailable initial readback must later verify through reads only. Do not deliberately induce an uncertain write or replay one.
5. Compare application assessment against known required/available/uninstall assignments and actual population/exclusion rules. Configuration verification does not prove installation or device outcomes.

Windows Hello's conflicting documented omitted-character defaults remain explicitly unverified; no policy semantics were changed. Live Graph acceptance, setup/consent, roles/RBAC, device effects, production recovery, keyboard/screen-reader/high-DPI human acceptance and broad deployment readiness remain unverified.
