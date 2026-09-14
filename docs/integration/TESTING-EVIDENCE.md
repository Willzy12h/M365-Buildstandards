# Testing evidence — 14 September 2026

## Source baselines

- Claude main 337c8e6: [Windows CI failed](https://github.com/Willzy12h/m365-Tenant-Toolkit-Claude/actions/runs/34625802405). Newer documentation-only 21d8fea also [failed](https://github.com/Willzy12h/m365-Tenant-Toolkit-Claude/actions/runs/34627546140). Local original build reproduced 13 missing reporting-type errors after successful dependency restore; tests could not run against that source.
- Asta main 020c69b: [CI succeeded](https://github.com/Willzy12h/m365-tenant-console-Asta/actions/runs/34624562838). Newer documentation-only 84f32c2 also [succeeded](https://github.com/Willzy12h/m365-tenant-console-Asta/actions/runs/34628240515). Asta tests were inspected selectively, not rerun locally.

## Local Windows checks

Used existing .NET SDK 8.0.425 in the original local Claude checkout, read-only. No SDK or runtime installed machine-wide. Dependency restore used copies of existing NuGet package archives in workspace-local cache/feed folders because this sandbox could not restore from api.nuget.org. This is not evidence of a fresh online restore or package vulnerability audit; CI checks the normal restore independently.

1. Original source: build failed with missing Reports, ReportExporter and ExportFormat.
2. Recovered reports: exposed one further CS0103 error, CopySummaryCommand referencing nonexistent Summary. Fixed to use SnapshotText.
3. Recovered tests: 108 passed, one failed on stale six-sheet expectation. Kept eight exported evidence tables; corrected the assertion to exact names and added an unmet-equivalence/caveat test.
4. Full WPF solution: build succeeded. Clean compilation reports three existing CA1416 platform warnings; an incremental successful build reports zero. Warnings were not suppressed.
5. Tests: **110 passed, 0 failed, 0 skipped**, Release/net8.0. TRX retained locally. HTML escaping, CSV formula guards, XLSX inline strings, report generation, tenant binding, collection/planner/executor and equivalence tests are included.
6. Ignore check: original reports/ pattern matches C# Reports/ under case-insensitive Git matching. Master /reports/ ignores generated reports without hiding source. CI also requires all six source files to be tracked.
7. Independent review caught the master's inherited evidence/ pattern hiding EvidenceStore.cs. Anchored runtime evidence/connections/exports/snapshots paths, staged EvidenceStore.cs, and checked every selected original source path against the Git index. CI explicitly checks EvidenceStore.cs too.
8. Local Build-Portable.ps1 -SkipTests completed after the passing suite, publishing a self-contained win-x64 application and ZIP/checksum. A limited process smoke test created a WPF window, loaded standard 2026.09.3 and closed gracefully with exit code 0. It used an empty synthetic workspace and did not sign in. This does not verify GUI interactions or connected-session shutdown.

## PR verification

[PR #1 checks](https://github.com/Willzy12h/M365-Buildstandards/pull/1/checks) are the source of truth for normal GitHub-hosted restore, Windows build/tests, portable packaging, standard checks and tracked-evidence/configuration checks. Portable ZIP and SHA-256 sidecar are review artifacts, not a production release. Test results are uploaded even on failure.

## Not verified

Interactive GUI workflows, connected-session cancellation/shutdown, live Microsoft sign-in or consent, real Graph reads/writes, recipe acceptance, app registration, client-specific licensing, device/sign-in outcomes and recovery against a tenant. No tenant was contacted. No automatic rollback claim is made.
