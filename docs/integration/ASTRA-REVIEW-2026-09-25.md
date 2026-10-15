# Release pre-flight review — 25 September 2026

Fresh review of integration `5cadd3a070b0e308bdffc327dfc7954bd69b9e83` in a new clone, for the scope in NEXT-RELEASE-PLAN. PR #19 claims phases 0–7. No open claims or pull requests conflicted at pre-flight. Published standards and the two preserved source repositories were not changed. Earlier review findings are not carried forward.

## Findings

| Severity | Location (fixed source) | Defect and resolution |
|---|---|---|
| P2 | `src/BDIT.TenantToolkit.Engine/Reports/HtmlReports.cs:100` | The engineer HTML report used a positional sheet index for Collection status, which now selected Equivalent configuration. Failed collection outcomes were omitted from that section. Select the named sheet; regression test checks both successful and failed outcomes. Fixed. |
| P2 | `src/BDIT.TenantToolkit.Engine/Standards/StandardsLoader.cs:46` | Lexical release sorting placed `2026.09.9` before `.10` and `.11`. When the configured release was unavailable, Workspace could choose that older release as its fallback. Sort parsed numeric versions first, retaining a deterministic text fallback. Fixed. |
| P2 | `src/BDIT.TenantToolkit.App/App.xaml:138` | A harness rerun found six unreachable controls in nested Plan tabs. The custom template omitted WPF's selected-content part and tab-header grouping. Restore `PART_SelectedContentHost` and a single tab-navigation stop for the header panel. The unchanged keyboard harness passed after the correction. Fixed; [Microsoft template contract](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/tabcontrol). |

Both new regression tests were run against the original implementation and failed for the stated reasons before the fixes. No test was weakened or removed.

## Review coverage and evidence

Read the catalogue schema and loader, client profile validation, input resolution, planner and execution path, creation guards, collector and snapshot requirements, assessment and equivalence handling, NameResolver, report renderers/exporter, Workspace, shell/navigation, and offline interface harness. This is a bounded source review, not proof of tenant or device behaviour. Later phases will add tests for their own new routes and safeguards.

Baseline: strict solution and harness builds, **0 warnings / 0 errors**; engine **579 passed, 0 failed, 0 skipped**; application **57 passed, 0 failed, 0 skipped**. After the two fixes: strict solution and harness builds, **0 warnings / 0 errors**; engine **581 passed, 0 failed, 0 skipped**; application **57 passed, 0 failed, 0 skipped**.

Offline baseline harness: 247 operable controls named; 22 tables with readable columns; 3,667 text elements and 125 input edges passed contrast; 4,276 elements checked without clipping. Keyboard walk covered 14 pages / 22 tab views / 1,842 stops, with every operable control reached. Final confirmation dialog: 10 checks passed. All 48 registered commands were exercised in 57 presses; 61 commands were not pressed by design. Four deliberate synthetic refusals were reported: application setup without sign-in, deviation save without selection, duplicate synthetic client save, and import without a name. 68 images / 42 page-size combinations; zero binding errors. Read the command register, tab order and refusal output.

All local evidence is in fresh task-specific synthetic directories outside the repository. Tenant/authentication/network implementations in the harness throw instead of accessing a tenant. No live tenant calls, saved client evidence, tokens or connection data were accessed. No Microsoft service or licence behaviour was tested.

The final phase 0 harness after the tab correction passed with the same counts above. The failing intervening run is retained in local synthetic evidence; it is not counted as a pass. The final command register and tab order were checked again.

## Delivery

GitHub publication uses the authenticated GitHub connector because the shell has no non-interactive push credentials. Each phase is committed and its `astra/release-2026-09-12` ref advanced without force; the clone is fetched and checked against that exact commit. PR #19 targets integration and remains a draft until phase 7.
