# Testing evidence — 14 September 2026

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
