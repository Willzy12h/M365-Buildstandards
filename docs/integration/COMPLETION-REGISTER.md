# Release completion register

Continuation of Astra/Codex PR #19, authorised 30 September 2026. Baseline: `687e756eb626b1ab62b61faa672b8825033abcab`, Preview.14 / standard 2026.09.12. Claude's C# implementation remains the foundation; source repositories and Claude branches are preserved. The user removed the missing project-context file prerequisite.

The existing release claim is continued on `astra/release-2026-09-12`, targeting integration. PR #19 is draft during implementation. Planned, implemented, synthetically verified, UI inspected, packaged and live accepted are separate states. This register is the current release record; earlier validation documents remain historical.

| Item | State | Evidence / remaining work |
| --- | --- | --- |
| Baseline reconciliation | Verified | Only PR #19 open across the three repositories; its head and source refs unchanged. Both prior Windows workflows passed. Current cloud strict cross-build and 804 engine tests passed |
| .NET 10 application and pipeline | Implemented; Windows validation pending | SDK 10.0.401, runtime/ProtectedData 10.0.12; all projects, bootstrap, CI and package metadata migrated. Cloud strict solution and harness builds pass; 809 engine tests pass after successor tests. Migration Windows workflow 36783189048 at bdac57b passes engine/application tests, UI harness and portable builder. Later interface/successor/extracted-startup and WAM acceptance remain separate |
| Immutable successor standard | Implemented; synthetic verified | 2026.09.30: 93 controls/61 unchanged candidate payloads; approved three retirements, all ten historical digests pinned, complete manual sections and primary sources tested |
| Native Windows settings | Complete manual dispositions; live validation outstanding | NATIVE-SETTINGS-AND-MOBILE.md and generated guide record prerequisites/procedure/result/check. Public source research does not establish authenticated catalog IDs; no unsupported transport added |
| Exchange execution | Implemented; manual boundary retained | Existing strict read-only capture/import and selected inert proposals; module retries prevent supported single-attempt toolkit writes |
| Own-domain SPF bypass | Blocked for activation | Preserve disabled/audit-only proposal; trusted authentication-result and From alignment need controlled mail-flow acceptance |
| Outlook mobile configuration | Researched; complete manual procedure | Managed-device ModernAuth account configuration supported, Android Enterprise/Managed Play and separate SMTP/UPN documented. New guarded app-configuration transport deferred; MAM-only setup not substituted |
| Passkey profile handling | Complete manual procedure; live validation outstanding | Successor documents Default-profile migration, targets/exclusions, attestation/AAGUID preservation, eligible Authenticator devices and effective pilot tests; legacy profile-edit refusal retained |
| Business Premium default | Implemented; live validation outstanding | Subscription counts never establish user entitlement; preserve service-plan/prerequisite checks and explicit add-on requirements |
| Engineer interface | Implemented; Windows inspection pending | Clear stages, detail/prerequisites, reviewed queue and outcomes; persistent tenant/account/access/standard, before/requested/after and copy/export/reopen |
| Interface choice | Evaluation pending | WPF default; working representative local HTML comparison must record offline, portability, accessibility, authentication, safety and maintenance trade-offs |
| Architecture roadmap | Reconciled | ARCHITECTURE-DISPOSITION-2026.09.30.md records each workstream; current cancellation, shared presentation and release provenance addressed; broader compiler/async/scheduling work deferred explicitly |
| Generated reports/manual procedures | Implemented; review pending | Check exact prerequisites, procedure, expected results and verification, exports, browser rendering, reopening and packaged links |
| Deployment safeguards | Existing regression baseline verified | Re-run negative/refusal, tenant/evidence/ownership/drift, disabled/unassigned, cancellation/recovery and ambiguous-write cases after changes |
| Portable Windows release | Pending | Final Windows tests/UI harness, guarded package, checksum/fresh extraction, startup and offline workflow checks |
| Human and tenant acceptance | Unperformed | Physical DPI/keyboard/Narrator, WAM/consent, representative tenant/device behaviour and effectiveness require separately authorised acceptance |

Device references: verified 30 September handoff, preserved rc.15 runtime, Preview 23 components and subsequent snapshot. Only workflow/design patterns are used; the PowerShell/WinForms device product is separate from the .NET migration. No real device or tenant operations are authorised by this development task.

## Current validation

Baseline checks are recorded in RELEASE-2026.09.12-VALIDATION.md and the exact baseline PR-head CI. New release results will be added here as they execute; installed tools and historical pass counts do not certify changed source.

30 September: .NET 10 migration exposed synchronous-read cancellation bypass. Two new regression tests and the unchanged LAPS stop test failed before the fix (3/3 negative controls); explicit read-boundary cancellation checks restore accepted-write / unknown-verification behaviour. All 806 engine tests pass after the fix. No write retries or approval contracts changed. TRX files are retained outside the distributable.

Successor publication and UI/package changes are undergoing a new Windows workflow. Legacy-reference fixture selection changed from retired ENR-003 to ENR-007; historical tests still use .12 unchanged. New UI tests distinguish inspecting a row from ticking it, and preserve unknown/missing historical result fields.
