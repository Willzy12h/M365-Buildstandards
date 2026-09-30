# Release completion register

Continuation of Astra/Codex PR #19, authorised 30 September 2026. Baseline: `687e756eb626b1ab62b61faa672b8825033abcab`, Preview.14 / standard 2026.09.12. Claude's C# implementation remains the foundation; source repositories and Claude branches are preserved. The user removed the missing project-context file prerequisite.

The existing release claim is continued on `astra/release-2026-09-12`, targeting integration. PR #19 is draft during implementation. Planned, implemented, synthetically verified, UI inspected, packaged and live accepted are separate states. This register is the current release record; earlier validation documents remain historical.

| Item | State | Evidence / remaining work |
| --- | --- | --- |
| Baseline reconciliation | Verified | Only PR #19 open across the three repositories; its head and source refs unchanged. Both prior Windows workflows passed. Current cloud strict cross-build and 804 engine tests passed |
| .NET 10 application and pipeline | Implemented; Windows validation pending | SDK 10.0.401, runtime/ProtectedData 10.0.12; all projects, bootstrap, CI and package metadata migrated. Cloud strict solution and harness builds pass; 806 engine tests pass. Windows app, packaging and WAM acceptance remain separate |
| Immutable successor standard | Planned | Preserve every historical standard byte; retire ENR-003/004 and SEC-WIN-002 from the new default, retaining SmartScreen and ESET package/licensing guidance |
| Native Windows settings | Investigation pending | Confirm Chrome SSO, file extensions, OneDrive FOD/device-preparation identifiers and other unresolved mechanisms; no invented template IDs or new script-deployment mechanism |
| Exchange execution | Implemented; manual boundary retained | Existing strict read-only capture/import and selected inert proposals; module retries prevent supported single-attempt toolkit writes |
| Own-domain SPF bypass | Blocked for activation | Preserve disabled/audit-only proposal; trusted authentication-result and From alignment need controlled mail-flow acceptance |
| Outlook mobile configuration | Assessment pending | Supported native route, prerequisites and licence/platform qualifications or complete manual/deferred disposition |
| Passkey profile handling | Implemented with validation outstanding | Existing migrated-profile capture and legacy-edit refusal; assess supported profile editing and effective Authenticator coverage |
| Business Premium default | Implemented; live validation outstanding | Subscription counts never establish user entitlement; preserve service-plan/prerequisite checks and explicit add-on requirements |
| Engineer interface | Planned | Clear stages, detail/prerequisites, reviewed queue and outcomes; persistent tenant/account/access/standard, before/requested/after and copy/export/reopen |
| Interface choice | Evaluation pending | WPF default; working representative local HTML comparison must record offline, portability, accessibility, authentication, safety and maintenance trade-offs |
| Architecture roadmap | Reconciliation pending | Preserve completed workstreams; implement release-critical correctness/maintenance items and record longer-term dispositions |
| Generated reports/manual procedures | Implemented; review pending | Check exact prerequisites, procedure, expected results and verification, exports, browser rendering, reopening and packaged links |
| Deployment safeguards | Existing regression baseline verified | Re-run negative/refusal, tenant/evidence/ownership/drift, disabled/unassigned, cancellation/recovery and ambiguous-write cases after changes |
| Portable Windows release | Pending | Final Windows tests/UI harness, guarded package, checksum/fresh extraction, startup and offline workflow checks |
| Human and tenant acceptance | Unperformed | Physical DPI/keyboard/Narrator, WAM/consent, representative tenant/device behaviour and effectiveness require separately authorised acceptance |

Device references: verified 30 September handoff, preserved rc.15 runtime, Preview 23 components and subsequent snapshot. Only workflow/design patterns are used; the PowerShell/WinForms device product is separate from the .NET migration. No real device or tenant operations are authorised by this development task.

## Current validation

Baseline checks are recorded in RELEASE-2026.09.12-VALIDATION.md and the exact baseline PR-head CI. New release results will be added here as they execute; installed tools and historical pass counts do not certify changed source.

30 September: .NET 10 migration exposed synchronous-read cancellation bypass. Two new regression tests and the unchanged LAPS stop test failed before the fix (3/3 negative controls); explicit read-boundary cancellation checks restore accepted-write / unknown-verification behaviour. All 806 engine tests pass after the fix. No write retries or approval contracts changed. TRX files are retained outside the distributable.
