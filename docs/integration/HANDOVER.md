# Handover and priorities

## Architecture roadmap

The plan for making this maintainable and extensible is [docs/integration/ARCHITECTURE-ROADMAP.md](ARCHITECTURE-ROADMAP.md). It is written for a model to execute: workstreams in dependency order, machine-checkable acceptance criteria, the invariants that outrank the plan, and the non-goals. Start there before proposing structural change.

## Current work

**Preview.13 / standard 2026.09.11 is on integration.** PR #17 (Claude) is the full code and interface review; its record is [FULL-REVIEW-2026-09-24.md](FULL-REVIEW-2026-09-24.md). Read that first: it lists what was fixed, and seven findings in protected areas reported for a decision rather than changed.

The independent safety review F1–F5 merged as PR #9 (`1021a23`). Read [the bounded change/validation record](SAFETY-REVIEW-2026-09-21.md): strict CA material-baseline checks, corrected enrolment contracts, blocked generic Autopilot assignments, beta-aware recovery verification, conservative application deployment assessment and fixed array caveats. Read [REVIEW-FIXES](REVIEW-FIXES.md) before testing or upgrading existing evidence. Historical catalogue and evidence compatibility remain constrained; do not adopt drift or replay Unknown writes.

Claude's review of that merge, and the fixes it produced, are in [POST-MERGE-REVIEW-2026-09-21.md](POST-MERGE-REVIEW-2026-09-21.md), merged as PR #12 (`e188d9e`): the headless runner now reads stored records through `EvidenceStore`, and the portable package ships a named operator document set rather than all of `docs/`. PR #13 (`55cb822`) brought the architecture roadmap back in step with the code.

Astra then reviewed `55cb822` end to end and confirmed the build, the 542 synthetic tests, the package and the offline renders, with four documentation findings and no reproduced safety bypass. Its most useful result is one the source-scan conformance tests could not give: an executed parity check of the real `Workspace` assessment against the real CLI report, identical but for `id`, `assessedAt` and `assessedBy`. Those four findings are fixed in PR #14.

PR #15 finished the application: its first test project (`BDIT.TenantToolkit.App.Tests`, net8.0-windows), accessible names on every operable control, and roadmap items W0, W2 and 9d. PR #16 finished 9e and S1.

**S1 was a real defect, and it was not only on the Plan page.** Measured at the minimum window size, a WPF DataGrid with a star-sized column squeezes *every* column towards 20px to avoid scrolling — fixed-width ones included. The Plan page drew its Select checkboxes and its Explanation column at 20px, and five other tables lost columns the same way. `Infrastructure/ColumnSizing`, switched on from the one DataGrid style, holds every column to its designed width and its header; a table that does not fit now scrolls. The harness measures every table on every page and fails the build if a column is squeezed.

**Correction to PR #15.** Its accessible-name check enforced nothing. Both interface checks skipped elements whose `IsVisible` was false, and WPF reports `IsVisible` only inside a window that has been shown — the harness shows its window only at the end, for the shutdown test, so everything was skipped and both checks passed on nothing. The names PR #15 added were all present; nothing held them. PR #16 replaced `IsVisible` with an off-screen test, and the harness now refuses to pass if either check inspected nothing. This was found by pushing the new column check before its fix, expecting it to fail, and seeing it pass.

**PR #17 made the interface checks answer "does it work", not only "does it draw".** CI fails on any compiler warning. The harness now also measures text and input contrast, clipping, keyboard reach on a shown window, every local command (from a register that fails the build when a command is added without being classified), the final tenant confirmation, and a third window size — 1180x640, which is what a 1080p laptop at 150% scaling offers. Every check was pushed before its fix and observed failing. The largest findings were near-invisible input edges on every page, a window that did not fit that laptop, and six pages that hid content when short.

**Next: human-authorised disposable-tenant acceptance.** No live validation has been performed — no tenant authentication, no Graph call, no device. The synthetic tests prove safety logic against a scripted client, not that Microsoft accepts these payloads. `docs/LIVE-VALIDATION.md` is the staged checklist and is unexecuted. Do not start another automation batch before it. What remains needs a person rather than a model: [the fifteen-minute checklist](../TESTING-THIS-BUILD.md) covers 150% scaling, keyboard only, Narrator and readability. Contrast, keyboard reach, clipping and accessible names are now machine-checked; how they feel in use is not.

## Historical handover (superseded where above differs)

Preview.12 is the current state of `integration` (Claude, pull requests [#3](https://github.com/Willzy12h/M365-Buildstandards/pull/3) to [#7](https://github.com/Willzy12h/M365-Buildstandards/pull/7), all merged; branch `claude/github-repo-access-ygieh2` is level with `integration` and holds nothing unmerged). Standard 2026.09.10, 50 controls, 42 recipes, 46 reporting automatically.

Since preview.8: client inputs are entered as a generated form rather than one hand-written JSON object, and a reviewable input the client has not supplied falls back to a dated default and warns instead of blocking the control, while identity inputs still block. Policies target the built-in All users and All devices populations, created groups are down to four (user and device exclusions, unenrolled mobile users, pilot devices), and every Conditional Access candidate also excludes the user exclusion group while keeping the verified operator's direct exclusion, which is what actually prevents a lockout. A client-facing build standard document is generated from the catalogue. Windows Hello is configured end to end rather than only enabled.

**Nothing here has been accepted by a live tenant.** CI on Windows is the only build and test evidence throughout; the authoring environment cannot run a .NET SDK. The next step is an independent review by Astra/Codex, then the human's own testing against a disposable tenant. The Windows Hello values in 2026.09.10 are the least certain part: the composition convention and the two device-scoped paths were confirmed from mirrored Microsoft documentation because learn.microsoft.com is unreachable from the authoring environment, and should be re-checked in the Intune portal.

Preview.8 (Claude, [PR #3](https://github.com/Willzy12h/M365-Buildstandards/pull/3), branch `claude/github-repo-access-ygieh2`) closes the largest remaining automation gap: the directory objects every other control depends on. Standard 2026.09.8 provisions seven security groups and the office IP named location through the existing Plan → Deploy path, so an engineer no longer has to build them by hand before any assignment or Conditional Access recipe can be used. A new creation guard constrains those writes to empty, assigned-membership security groups and untrusted IP named locations with client-confirmed CIDR ranges. ID-003 is now assessed from Global Administrator membership through a new `directoryRoles` capture. 53 controls, 45 recipes, 49 reporting automatically. **Deployment mode now requests Group.ReadWrite.All, so both registrations need administrator consent again**; assessment mode is unchanged and stays read-only. CI on the PR head remains the only build and test evidence.

Preview.7 (Claude, [PR #3](https://github.com/Willzy12h/M365-Buildstandards/pull/3), branch `claude/github-repo-access-ygieh2`, which merges PR #2 @ `f5859da`) finishes the reporting side of the standard and closes the review findings against PR #2. Standard 2026.09.7 assesses ID-002, ENR-001 and CMP-001 from captured evidence through equivalence signals (40 of 45 controls now report automatically; the reviewed tenant actions that change them are unchanged). A cancelled partial capture records untried collections as *Not attempted*; terminal evidence saves in the LAPS, reviewed-change, package and recovery services no longer mask the ending exception. `ReviewedChangeSafetyTests` is the first synthetic coverage of the preview.6 write surface. **CI on the PR head is the only build and test evidence for this increment** — the authoring sandbox could not run a .NET SDK; read the checks for the exact commit. No tenant, consent or write was performed. The four controls without automation (ID-001, ENR-005, ENR-006, UPD-001) are manual by nature and recorded as such in [automation coverage](../AUTOMATION-COVERAGE.md).

Preview.6 implements the expanded policy scope and the additional source review. Standard 2026.09.6 contains 45 controls and 37 candidate recipes. Four further controls use reviewed tenant actions (authentication methods, MDM enrolment, secure compliance, Autopatch); four use read-only readiness plus engineer/external steps (emergency/admin identities and Apple/Google ownership). This is not 45 unattended automations. See [coverage and required inputs](../AUTOMATION-COVERAGE.md).

Policy automation UI includes local imports and reload, typed tenant inputs, Entra LAPS preview/enable/re-verify, exact tenant-change/activation/assignment previews, containment, package publication and readiness. `.intunewin` publication journals every Graph stage, retains returned IDs and keeps encryption keys/storage URLs out of evidence. Candidate creation and later activation are separate.

Validation for this increment is source/diff review and Windows Release compilation only. No tests, new GUI acceptance, package installation or live Graph actions were run, per the user's instruction to focus on implementation. Existing CI remains enabled. Do not describe the prior 295-test result as covering preview.6.

Next concrete acceptance work: approve required client values/exports/packages from the coverage document, then validate one candidate per newly supported Graph resource and each consequential workflow in an explicitly authorised disposable tenant. The application remains a preview until that work is complete. No tenant operation is authorised by source-code approval.

Preview.4 adds WAM sign-in, exact registered admin-consent callback, explicit existing-app repair, custom icon/GitHub metadata, approved engineer assignment and direct setup-to-connect handoff. Standard 2026.09.4 adds the supplied BitLocker candidate and optional long paths (14 recipes); other policy defaults require user input. See [application setup](../APPLICATION-SETUP.md) and [device automation](../DEVICE-AUTOMATION.md). Previous source namespaces and standard 2026.09.3 remain for compatibility and evidence. All new Microsoft interactions still require authorised live validation.


Preview.3 addresses Claude's review of `6712d8e`: explicit not-sent write classification, read-only re-verification, acknowledged historical evidence reconciliation, cancellable deployment reads and bounded follow-up capture. See [review dispositions](CLAUDE-REVIEW-RESPONSE.md). Original records remain unchanged; unknown modern requests remain blocked.

[PR #2](https://github.com/Willzy12h/M365-Buildstandards/pull/2), branch `astra/engineer-workflow`, targets integration and depends on [PR #1](https://github.com/Willzy12h/M365-Buildstandards/pull/1). Until #1 is merged, #2 includes its imported source. Both source repositories and Claude's coordination branch remain untouched. Merge through review.

The user authorised source development of app setup, account/exclusion automation, usability, selective recovery and licence visibility, and explicitly approved PR publication. No live tenant, consent or registration actions were performed in this development session.

## Implemented in the preview

- Complete durable before-evidence validation at the execution boundary, all plan/input bindings, private execution copies, licence and candidate-reference readiness.
- Truthful terminal states, separate write acceptance/readback, no write retry, single-use plans and unresolved-write protection.
- Forced silent token renewal after a GET 401; second rejection fails. Writes are never replayed.
- Missing properties remain different from JSON null in assessment and verification.
- Isolated delegated app-setup wizard: permission preview, explicit approval, two registrations/SPs, browser consent and actual grant/configuration/direct-assignment validation.
- One-time connections, optional saved profiles, account lookup and exclusions with purpose/reason/object-ID evidence. No name-based admin exemptions.
- Coherent WPF navigation and identity/access header, plan/exclusion review and result semantics; cooperative stop/shutdown; background report exports.
- Corrected report claims; historical run integrity preserved for records without acceptance metadata.
- Selective recovery: durable change register, exact returned IDs and before/after values, confirmed-creation deletion, latest inactive-update restoration and explicit CA disablement with drift review. Recovery/deployment share a tenant write lease. See [recovery](../RECOVERY.md).
- Main-page subscription counts, searchable assigned users and conservative direct-user policy scope checks. Unknown memberships and entitlement remain explicit review items. See [licensing](../LICENSING.md).

See [testing evidence](TESTING-EVIDENCE.md) for actual checks. Source and synthetic tests do not establish live acceptance.

## Remaining acceptance work

| Priority | State | Required verification / next work |
|---|---|---|
| P1 | Implemented but live-unverified | Authorised tenant: bootstrap sign-in, app creation, consent propagation, direct engineer assignment, separate assessment/deployment sign-in |
| P1 | Implemented but live-unverified | Capture pagination/details, actual permissions/licensing, all 37 candidate recipes, readback and supported inactive PATCH cases |
| P1 | Connected-session-unverified | Sign-in/capture/setup/policy cancellation and shutdown, network failure and recovery |
| P1 | Implemented; synthetic tests only | In an authorised disposable tenant, create a disabled/unassigned candidate, remove it and confirm absence; test restoration and reviewed CA disablement with full evidence |
| P1 | Manual recovery for unknown acceptance | Accepted writes now have read-only re-verification. Truly ambiguous requests still require separately reviewed reconciliation; no automatic rollback/adoption/delete-and-retry |
| P2 | Limited validation | Group-based assignment, PIM/custom-role effectiveness and Intune RBAC are not inferred from direct role/assignment reads |
| P2 | Acceptance pending | Human keyboard, screen-reader, high-DPI and representative large-tenant usability testing; offline rendering is narrower evidence |
| P2 | Manual-only scope | 8 of 53 controls have no creation recipe. ID-002, ENR-001, CMP-001 and ID-003 are assessed from evidence; ID-001, ENR-005, ENR-006 and UPD-001 need an engineer or vendor portal. Add a recipe only with reviewed settings, supported API behaviour, safeguards and tests |
| Later | Deferred | App-only identity, automatic policy activation/assignment, group membership management, cloud interface and extra workloads |

The next concrete acceptance task is **application setup → consent → engineer assignment → read-only capture → licence/user checks**, followed by candidate creation and selective recovery in an explicitly authorised disposable scope. The user plans to test the expanded preview. Repository approval does not authorise tenant operations.

Additional creation recipes remain follow-on work. Current manual controls often need client choices, provider dependencies or API validation; do not invent minimum OS versions, Defender risk thresholds or device targeting to claim broader automation. The four-policy SME batch is implemented as candidates; keep the current 45 recipes explicit.

Model preference: advise the user when a different model suits the next task. Astra with High reasoning is recommended for recovery, authentication, deployment safety and architecture; reserve faster models such as Spark for bounded low-risk presentation changes. This is guidance, not a claim that a model setting was changed.

## Product decisions

CA candidates remain disabled with stored targeting and exclusions. Stored targeting is separate from enforcement; do not clear it to interpret the historical word "unassigned". Creator and chosen exclusions persist if a policy is later enabled. Intune candidates remain unassigned. Activation requires a separate reviewed workflow and tenant scope.

One-time connection means the profile is not saved; local audit evidence is retained. Disconnecting setup removes temporary tokens but does not revoke Entra consent. Business Premium remains the normal SME baseline; the planner uses captured service-plan evidence and blocks unavailable or unknown requirements.

## Status vocabulary

Implemented and verified names the actual check. Implemented but unverified identifies missing verification. Planned is agreed work; Candidate is a proposal; Deprecated is superseded behaviour; Known issue is an observed defect. Passing tests do not verify sign-in, consent, live Graph or recovery.
