# Release completion register

## Preview.16 usability continuation

The active supplement is [QOL-GOAL-2026.10.01.md](QOL-GOAL-2026.10.01.md). User-requested changes cover Quick Connect, explicit partner sign-in, fewer redundant prompts, visible application creation approval without repeated setup GUID entry, clear app IDs/browser consent/assignment, deployment setup routing, read-only capture/state explanations, corrected Graph relationship reads and portable text-context-menu validation. Standard 2026.09.30 is unchanged. Source development and local synthetic tests are in progress; the older Preview.15 CI counts/artifacts below do not validate these changes. Windows CI and exact new artifact evidence will be recorded before completion. The supplied live exports are private diagnostic inputs and are not committed. No live operation has been performed by the agent.

First Preview.16 Windows run [36794151068](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/36794151068), source `5e9255d`, passed strict builds, 842 engine tests and 95 application tests (zero failures/skips), and package construction. Native layout validation correctly failed because the new configuration guidance clipped the Raw JSON expander at the two smaller sizes. The object-detail panel now scrolls its guidance, settings and raw JSON together. Fresh-extraction interaction was not run after that failure; a subsequent green run is required before claiming it.

Additional source checks cover direct setup-to-connection handoff, preserving a verified target and clearing another client's edit buffer, and bringing the actual creation/Quick Connect confirmation buttons into view. Strict cloud solution/harness builds pass without warnings. Negative controls removed the discovery tenant-confirmation guard (1/6 tests failed) and converted missing/truncated embedded arrays into empty successful lists (3/6 tests failed); after restoring both guards, all 32 focused tests passed. These are synthetic source safeguards, not live Microsoft acceptance.

Reconnect review found that local save timestamps prevented reuse of otherwise unchanged one-time profiles. The reuse comparison now excludes only top-level creation/update timestamps; all other profile fields remain compared. The connection form also retains office locations and per-control inputs edited elsewhere, using the latest same-client values, and clears these when a new client is selected. Application tests cover both retention and separation.


Continuation of Astra/Codex PR #19, authorised 30 September 2026. Baseline: `687e756eb626b1ab62b61faa672b8825033abcab`, Preview.14 / standard 2026.09.12. Claude's C# implementation remains the foundation; source repositories and Claude branches are preserved. The user removed the missing project-context file prerequisite.

The existing release claim is continued on `astra/release-2026-09-12`, targeting integration. PR #19 is draft during implementation. Planned, implemented, synthetically verified, UI inspected, packaged and live accepted are separate states. This register is the current release record; earlier validation documents remain historical. The [detailed release goal](RELEASE-GOAL-2026.09.30.md) defines scope, measurable pass criteria and the distinction between implementation, offline-tested preview and controlled client-use acceptance.

| Item | State | Evidence / remaining work |
| --- | --- | --- |
| Baseline reconciliation | Verified | Only PR #19 open across the three repositories; its head and source refs unchanged. Both prior Windows workflows passed. Current cloud strict cross-build and 804 engine tests passed |
| .NET 10 application and pipeline | Verified for the offline preview | SDK 10.0.401, runtime/ProtectedData 10.0.12; all projects, bootstrap, CI and package metadata migrated. Cloud strict solution and harness builds pass. Final Windows run at 7fb73d1 passes 810 engine and 79 application tests (zero failures/skips). Migration Windows workflow 36783189048 at bdac57b passes engine/application tests, UI harness and portable builder. Successor/interface Windows checks pass at 13b3fbe (run 36785503355). Fresh extracted startup/shutdown now pass at 7fb73d1; WAM acceptance remains separate |
| Immutable successor standard | Implemented; synthetic verified | 2026.09.30: 93 controls/61 unchanged candidate payloads; approved three retirements, all ten historical digests pinned, complete manual sections and primary sources tested |
| Native Windows settings | Complete manual dispositions; live validation outstanding | NATIVE-SETTINGS-AND-MOBILE.md and generated guide record prerequisites/procedure/result/check. Public source research does not establish authenticated catalog IDs; no unsupported transport added |
| Exchange execution | Implemented; manual boundary retained | Existing strict read-only capture/import and selected inert proposals; module retries prevent supported single-attempt toolkit writes |
| Own-domain SPF bypass | Blocked for activation | Preserve disabled/audit-only proposal; trusted authentication-result and From alignment need controlled mail-flow acceptance |
| Outlook mobile configuration | Researched; complete manual procedure | Managed-device ModernAuth account configuration supported, Android Enterprise/Managed Play and separate SMTP/UPN documented. New guarded app-configuration transport deferred; MAM-only setup not substituted |
| Passkey profile handling | Complete manual procedure; live validation outstanding | Successor documents Default-profile migration, targets/exclusions, attestation/AAGUID preservation, eligible Authenticator devices and effective pilot tests; legacy profile-edit refusal retained |
| Business Premium default | Implemented; live validation outstanding | Subscription counts never establish user entitlement; preserve service-plan/prerequisite checks and explicit add-on requirements |
| Engineer interface | Implemented; native automated checks passed; visual inspection blocked | Clear stages, detail/prerequisites, reviewed queue and outcomes; persistent tenant/account/access/standard, before/requested/after and copy/export/reopen |
| Interface choice | WPF retained; comparison recorded | INTERFACE-DECISION-2026.09.30.md: standalone HTML selection/review/outcomes prototype, 26 browser checks; embedded host/authentication/bridge parity not claimed |
| Architecture roadmap | Reconciled | ARCHITECTURE-DISPOSITION-2026.09.30.md records each workstream; current cancellation, shared presentation and release provenance addressed; broader compiler/async/scheduling work deferred explicitly |
| Generated reports/manual procedures | Engine exports and browser/print review passed | Both documents contain 93 articles, verified digest and resolving anchors; no horizontal overflow; printed PDFs retain all IDs. Representative introduction and long payload pages visually inspected. Physical printing/Narrator remain open |
| Deployment safeguards | Regression baseline verified on the candidate | Full 810 engine/79 application suites pass, including refusal, tenant/evidence/ownership/drift, disabled/unassigned, cancellation/recovery and ambiguous-write cases. Live service acceptance remains open |
| Portable Windows release | Final candidate verified and published for review | Run 36787138102 at 7fb73d1 passes strict builds, both suites, native UI harness and actual extracted-package first launch/shutdown. 287 files match stage/checksums; blank connection settings and no shipped evidence verified |
| Human and tenant acceptance | Unperformed | CONTROLLED-ACCEPTANCE.md defines setup/read-only checks, two inert candidates and separately approved pilot/recovery. Physical DPI/keyboard/Narrator, WAM/consent and service/device effectiveness remain unperformed |

Device references: verified 30 September handoff, preserved rc.15 runtime, Preview 23 components and subsequent snapshot. Only workflow/design patterns are used; the PowerShell/WinForms device product is separate from the .NET migration. No real device or tenant operations are authorised by this development task.

## Verified portable candidate — 7fb73d1

The [successful Windows run 36787138102](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/36787138102) builds source **7fb73d1e90ccd26eb62be8b0bfab7a3cc8751198**, application **1.1.0-preview.15**, standard **2026.09.30**. Subsequent changes recording these results are documentation only; use this exact package/run identity when reproducing acceptance.

| Check | Observed result |
| --- | --- |
| Strict solution and UI harness builds | Pass |
| Engine tests | 810 executed, 810 passed, 0 failed, 0 skipped |
| Windows application tests | 79 executed, 79 passed, 0 failed, 0 skipped |
| Native WPF | 42 page/size records, 69 command presses, zero binding issues; idle window closes; zero tenant calls |
| Large synthetic fixtures | 10,000 group objects over 100 GET pages retained; 10,000 finding filter/restoration test passes |
| Fresh portable extraction | 287 files; all checksums and stage bytes match; no connection IDs or private/generated evidence shipped |
| Actual packaged executable | Window opens, default standard loads, expected writable directories are created, clean shutdown |
| Browser/reference reports | 26 checks pass, zero JavaScript errors/external requests; representative print pages inspected |
| Live Microsoft/device operations | None performed |

ZIP SHA-256: `35aa42bd0bc6be2c48b1c71b026d28fdb20ff70670363d074f072a19c7f5409c`.

Artifacts from that exact run:

- [Portable Windows package, checksum and extraction result](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/36787138102/artifacts/11130272322).
- [Actual synthetic .NET 10 WPF renders and UI records](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/36787138102/artifacts/11130476810).
- [Generated engineer documents](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/36787138102/artifacts/11130372157).
- [Test results](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/36787138102/artifacts/11130297305).

These are an offline-tested preview for review, not client-use acceptance. Native screenshots were generated and checked by the Windows harness; downloading and visually inspecting them here remains blocked by the cloud network policy. PR #19 remains draft while that review is outstanding. Physical accessibility, Windows sign-in/consent, effective exclusions, service/device behaviour and supported live recovery remain separate gates in CONTROLLED-ACCEPTANCE.md. No authorisation for those live operations is implied.

## Validation history

Baseline checks are recorded in RELEASE-2026.09.12-VALIDATION.md and the exact baseline PR-head CI. New release results will be added here as they execute; installed tools and historical pass counts do not certify changed source.

30 September: .NET 10 migration exposed synchronous-read cancellation bypass. Two new regression tests and the unchanged LAPS stop test failed before the fix (3/3 negative controls); explicit read-boundary cancellation checks restore accepted-write / unknown-verification behaviour. All 806 engine tests pass after the fix. No write retries or approval contracts changed. TRX files are retained outside the distributable.

The Windows UI failure after the successor update came from the render fixture: ENR-007 has manual instructions but no prerequisite-panel rows. The harness now selects CFG-WIN-003, a current control with actual prerequisites. No assertion was removed; the full harness passed at 13b3fbe. Historical tests still use .12 unchanged. New UI tests distinguish inspecting a row from ticking it, preserve unknown/missing historical fields and clear selected evidence/visible run summary on client switch.

Chromium review: 26 passed, 0 failed, 0 JavaScript errors, 0 external requests. The reusable `build/Review-Browser.py` was executed after extraction from the session helper. Engine-generated HTML/Markdown used the actual .30 catalogue and verified manifest digest. Tagged A4 PDFs contain 132 Build Standard and 323 Manual Guide pages; all control IDs survive printing. These are catalogue/reference files, not tenant evidence.

Native UI artifacts exist for the successful WPF harness at [run 36785503355](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/36785503355), artifact `synthetic-ui-review` (11129643160). Cloud retrieval is blocked by the runtime network allowlist at `productionresultssa13.blob.core.windows.net`; logs also redirect to `results-receiver.actions.githubusercontent.com`. Both exact hosts were added to the saved environment configuration draft while preserving existing domains. Saving the draft does not apply it to the running environment. Screenshots have not yet been downloaded or visually inspected here.

At 239ed2e, Windows workflow 36786162193 passes 809/809 engine tests and 78/78 application tests (zero failed or skipped), plus 42 native page/size renders, 69 command presses, zero binding issues and graceful idle closure. No tenant calls occurred. The extracted-package check rejects a valid `app/Accessibility.dll` path. Its extraction root now uses the same GetFullPath normalisation as each child to handle Windows PowerShell/.NET Framework path aliases; containment checks remain enabled. That correction and the final documentation/package need their own final run.


At 7df3d22, Windows run 36786588960 again passes 809 engine tests, 78 application tests and the complete native UI harness. Extraction file/hash/containment checks now pass. The next verifier assumption failed: `Compress-Archive` omits empty data/log/report directories, which the application creates on launch. The verifier now accepts an absent pre-launch directory as empty, still refuses any shipped evidence file, and requires all directories to exist after actual first launch. This corrects the fixture contract without disabling content or startup checks.

Added explicit scale checks: 10,000 synthetic group objects across 100 HTTP pages, preserving every ID and GET-only requests; 10,000 synthetic findings filtered and restored without changing the underlying evidence or creating a plan. The pagination check passed locally (1 executed, zero failures/skips) and strict cross-build passed. Full Windows counts and the finding-filter result must come from the final CI head. These are functional scale tests, not measured real-window responsiveness or a production capacity claim.
