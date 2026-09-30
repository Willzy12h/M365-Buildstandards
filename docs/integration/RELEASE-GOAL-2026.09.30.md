# M365 BuildStandard: release goal and acceptance criteria

## Objective

Deliver **M365 BuildStandard 1.1.0-preview.15**, a portable Windows x64 application on **.NET 10 LTS**, using immutable standard **2026.09.30**. An engineer must be able to understand the standard, capture and assess a tenant, select supported changes, review their exact scope, execute only approved actions, and reopen evidence that clearly separates write acceptance, configuration readback and functional effectiveness.

The immediate delivery is an offline-tested preview for human review. Readiness for controlled client use is a later acceptance decision supported by explicitly authorised Microsoft service, identity and device results. Neither a successful build nor synthetic tests establish that readiness.

This specification defines the goal; [COMPLETION-REGISTER.md](COMPLETION-REGISTER.md) records actual status. It continues PR #19 on the existing Astra/Codex branch, targeting `integration`. No merge or live operation is implied.

## Product and implementation boundaries

- **In scope:** the M365 WPF application, Core/Graph/Engine services, CLI validation support, standards, engineer documents, regression/UI harnesses, portable packaging and development setup.
- **Reference material:** the supplied BDIT Diagnostics, Setup & Repair, launcher and available Engineer Console material. Adapt useful workflow/report conventions. Record the exact supplied revisions and gaps; do not assume they are the latest laptop copies.
- **Separate products:** do not merge, rebuild or migrate the device toolkit or Engineer Console as part of this M365 release. Their PowerShell/WinForms runtime remains separate.
- Preserve other contributors' work, historical standard bytes and readable historical evidence. Continue the owned release PR; use reviewed PRs for integration.
- Keep authentication, collection, assessment, planning, execution and evidence logic outside UI controls. No new general-purpose command bridge, arbitrary Graph writer or automatic remediation engine.
- Development permits source changes and safe offline/CI validation. Tenant sign-in, consent, registrations, DNS probes, policy writes and real device operations require their own identified scope and authorisation.

## 1. Complete and document the standard

The successor contains **93 controls and 61 candidate payloads**. Retire classic Autopilot registration and ESP (`ENR-003`, `ENR-004`) and Defender EDR onboarding (`SEC-WIN-002`) from the new default. Retain device preparation, ESET and independently agreed Windows protections such as SmartScreen. Keep all earlier standard files unchanged.

Use Business Premium as the default SME planning baseline. Explain service prerequisites, actual user eligibility, Windows edition restrictions and separately purchased products. Tenant seat counts must never be presented as proof of a user's entitlement.

Every current recommendation must have an explicit disposition: implemented with evidence; implemented with named checks outstanding; a complete manual procedure; superseded with a reason; or blocked by an identified dependency. This includes native Windows configuration, Outlook mobile account configuration, passkey-profile migration, Exchange execution and own-domain SPF handling.

For manual controls, provide prerequisites, exact portal/configuration steps, intended settings and scope, expected result, verification and current Microsoft references. Identify values the engineer must supply. Do not substitute guessed setting identifiers, imported ADMX, arbitrary scripts or new credentials for an unconfirmed supported mechanism.

Keep Exchange execution manual unless a supported route demonstrably meets the existing durable-intent and no-ambiguous-replay requirements. Keep the own-domain SPF bypass disabled/audit-only until header trust and From-domain alignment are established in controlled tests.

**Pass criteria:** the new catalogue loads and verifies against its manifest; automated checks assert the retirement set, retained controls, unchanged candidate payloads, complete manual sections and historical digests. Every outstanding service/device check is visible in the completion register and engineer guidance.

## 2. Deliver a clear engineer workflow

The interface must support these distinct stages:

1. **Connect and establish identity:** display tenant, primary domain, signed-in engineer, access mode and selected standard. Explain the difference between assessment and deployment access.
2. **Capture and assess:** show collection completeness separately from findings. Missing reads remain unknown. Explain why a control needs attention and whether action is supported.
3. **Inspect and select:** selecting a row opens its purpose, intended result, prerequisites, inputs and limitations; ticking an eligible control selects an action. Changing an area/filter must follow the documented selection rules.
4. **Review a plan:** display the selected actions, exact proposed changes, affected scope, blockers and required approvals. Manual-only controls must not appear executable.
5. **Execute approved actions:** show progress and individual outcomes; maintain the existing typed-tenant confirmation and execution guards. Explain stop/cancellation and uncertain-result handling.
6. **Review and reopen evidence:** expose before, requested and after records, copy/export actions and saved runs. Display write acceptance, configuration verification and functional checks separately. Missing historical fields must not become success.

Keep the next available action visible. Advanced payloads and supporting detail remain accessible without dominating the primary task. Switching clients must clear the previous client's selected evidence and visible summaries.

Improved WPF is the default. Compare a representative local HTML/CSS workflow for usability, accessibility, offline behaviour, portability, authentication integration, testing and maintenance. Clearly distinguish a standalone browser prototype from a validated embedded .NET browser host. A shell migration requires demonstrated benefit and tested engine safeguards; it is not a prerequisite to releasing the improved WPF implementation.

**Pass criteria:** native Windows checks exercise the changed selection and evidence behaviour, all pages and relevant tabs, accessible names, column widths, contrast, clipping, keyboard reach, confirmation/refusal and local commands. Supply real .NET 10 WPF captures at the tested sizes, labelled as synthetic. Human keyboard/Narrator and physical DPI acceptance remain separate.

## 3. Establish .NET 10 and portable operation

Retarget the application, libraries, CLI and test/harness projects to .NET 10. Pin the SDK, record dependency compatibility and keep MSAL/broker versions coherent. Ship a self-contained Windows x64 package so engineers do not need an SDK or installed .NET runtime. Preserve extract-and-run operation without mandatory administrator rights.

Keep setup repeatable in the cloud and Windows. Verify official SDK integrity. Record Windows-only capabilities explicitly; a Linux cross-build does not count as running WPF. Save reusable environment installation/startup instructions and required network destinations without credentials.

**Pass criteria:** strict solution and harness builds succeed; engine and Windows application suites execute with exact counts; native rendering succeeds. The final ZIP and every extracted file verify by SHA-256 against the staged release. A fresh extraction starts the actual packaged executable, loads the default standard and shuts down cleanly. Connection settings are blank and private/generated evidence is absent.

## 4. Preserve safety and truthful evidence

Retain read-only assessment; complete durable before evidence; exact tenant/operator approval; disabled CA candidates with verified exclusions; unassigned Intune candidates; exact-ID ownership; drift refusal; durable intent; and no automatic replay after an ambiguous write. Recovery remains scoped to supported owned objects and separately reviewed actions.

Cancellation must not erase an accepted write or invent successful verification. Unknown and interrupted outcomes require read-only reconciliation. Historical evidence must remain readable without silently acquiring new approval or success semantics.

**Pass criteria:** relevant refusal and regression tests pass. Safety changes include a demonstrated failing case before the correction or with the safeguard removed. No assertion is weakened to obtain a pass. No new permissions, write routes or schema contracts are introduced without an explicit recorded decision and appropriate tests.

## 5. Verify documents and realistic workflows

Generate the catalogue-only Build Standard and Manual Implementation and Verification Guide in HTML and Markdown. Include all current controls, settings, dependencies, procedures and checks. Keep these distinct from reports containing actual client evidence.

Exercise capture/import, assessment, planning, selection, confirmation/refusal, outcomes, exports, evidence reopening, cancellation/recovery and existing representative large-data fixtures where supported by the harnesses. Inspect HTML in a browser and representative printed pages; check navigation, long content and payload readability.

**Pass criteria:** all 93 control sections appear, internal anchors resolve, retired controls are absent from the successor documents, catalogue identity is visible, representative viewport layouts do not overflow and printed documents retain the control content. Packaged documentation links resolve. Record browser, native UI and live-service results separately.

## 6. Prepare bounded live acceptance

Before requesting live authorisation, prepare a concrete checklist identifying the disposable tenant, engineer and emergency accounts, licensed pilot users, enrolled test devices, application IDs, exact candidate payloads, proposed pilot scope and recovery limits. Unknown identifiers remain unresolved prerequisites, never invented values.

Start with setup/consent/assignment and read-only capture/licence checks. Then select a small approved candidate set, verify its disabled/unassigned state, and only under separate approval perform pilot activation/assignment, functional checks and supported recovery. Record service configuration and effective sign-in/device behaviour independently.

**Pass criteria for controlled client use:** reviewed code/package; successful Windows authentication and consent; complete tenant evidence; accepted candidate payloads and readback; effective exclusions and pilot behaviour; tested supported recovery; physical accessibility acceptance; and documented outstanding limitations accepted by the maintainer. A failed or unperformed applicable gate prevents a client-readiness claim.

## Required delivery

- PR #19 with coherent title, description, decisions and reviewable changes; human review/merge status stated honestly.
- Self-contained portable Windows ZIP, SHA-256, internal checksums, exact source commit, application version, runtime and standard identity.
- Updated immutable standard, complete manual dispositions and generated HTML/Markdown engineer documents.
- Interface decision, representative real WPF screenshots and clearly labelled comparison material.
- Repeatable setup/build/test/package instructions and machine-readable results with exact pass/fail/skip counts.
- One completion register linking each requirement to evidence, dependencies and remaining acceptance criteria.
- A bounded live-acceptance plan, without implying authorisation or performed live operations.

## Delivery milestones

| Milestone | Required evidence | Permitted description |
| --- | --- | --- |
| Implementation complete | Code, catalogue, documentation and decisions cover the agreed scope; unresolved dependencies are explicit | Implemented; validation status specified |
| Offline-tested Windows preview | Final-source automated checks, native UI checks, report review, fresh package verification and recorded provenance pass | Tested preview for human/lab review |
| Controlled client-use acceptance | Applicable authorised tenant/device, authentication, accessibility and recovery checks pass and human review is complete | Ready for the accepted scope, with recorded limits |

At specification creation, the .NET 10 builds, engine/application tests and native WPF harness have passed on the then-current CI head. The new fresh-extraction package check has failed and is under diagnosis. Native screenshot retrieval is blocked by the cloud network allowlist; browser checks and representative print inspection have completed separately. These are current facts, not waived acceptance criteria. Consult the completion register for subsequent results.
