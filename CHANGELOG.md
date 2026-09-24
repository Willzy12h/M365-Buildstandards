# 1.1.0-preview.13

- Standard 2026.09.11 brings the toolkit to 50 controls and 42 creation recipes. The directory prerequisites are created through the normal Plan and Deploy path, so the exclusion groups and the office named location are no longer a manual step before everything else. Groups are created empty and the named location untrusted; populating and trusting them remain separate decisions.
- **The prerequisites were renumbered.** The exclusion groups are now PRE-009 and PRE-010; MAM Only Users is PRE-004, Pilot Devices PRE-005 and the office named location PRE-008. Two of the old numbers now name a different control: a report written against 2026.09.9 or .10 says PRE-004 for pilot devices and PRE-005 for the office location, and both resolve to something else here. Read the control name, not only the ID, when reconciling an older report. [Automation coverage](docs/AUTOMATION-COVERAGE.md) carries the full mapping.
- Conditional Access activation now requires a verified baseline. Extra targeting, application exclusions, device filters, session controls or unknown fields on a policy block activation rather than being carried along, and the approved payload, the verified observation and the policy's current state must all agree. Emergency containment still disables a policy without requiring unchanged targeting.
- Generic Autopilot group assignment and removal are rejected before any request is sent. They are not a containment path. Enrolment assignment and removal use the resource-specific envelope Microsoft documents.
- Application deployment is no longer inferred from an assignment record. The eight Windows application controls report for review rather than compliant, because each names an exclusion group whose effective membership a name or object ID cannot establish. Confirm application deployment in Intune.
- Recovery re-verification understands beta collections, so an accepted Defender EDR restoration can be confirmed with reads alone. The v1.0 payload guard is unchanged.
- The Windows Hello policy configures the whole feature rather than only enabling it: an eight-character minimum PIN requiring a digit, no expiry, PIN recovery permitted, TPM 1.2 excluded, and biometric and FIDO2 sign-in allowed.
- Reviewable client inputs fall back to a dated default with a warning on the plan row instead of blocking the control. Identity inputs — account, group and location IDs — carry no default and still block, because a wrong identity is worse than a missing one.
- The package now ships eleven operator documents rather than the whole documentation folder, and carries its own README written for the engineer running it rather than for a contributor. The build fails if a packaged document is missing or if any link in one does not resolve inside the package.
- Every control an engineer can operate announces itself to a screen reader, and the offline interface harness fails the build if one does not.
- Tables no longer squeeze columns out of sight. At the minimum window size a table used to shrink every column towards 20px rather than scroll, so the Plan page's Select and Explanation columns - among others - were drawn too narrow to read while looking complete. A table that does not fit now scrolls, and each column keeps the width it was designed for.

# 1.1.0-preview.12

- Standard 2026.09.10 configures Windows Hello for Business end to end instead of only enabling it. The dedicated policy now sets a minimum eight-character PIN requiring at least one digit, permits but does not require letters and special characters, never expires the PIN, allows PIN recovery, excludes TPM 1.2, and permits biometric and FIDO2 security key sign-in. The tenant enrolment setting still stays Not configured.
- Composition rules follow the CSP convention where 1 requires a character class and 2 forbids it, so lowercase, uppercase and special characters are left unset, which permits them without demanding them. A test pins that, because setting 2 by mistake would forbid the class outright.
- Deliberately not configured: phone sign-in, which is a retired feature; enhanced anti-spoofing and Enhanced Sign-in Security, which depend on specific hardware and silently disable biometrics on devices that lack it; and cloud Kerberos trust, because this standard targets cloud-only tenants. A hybrid client needs that one added deliberately, or users sign in with Hello and are then prompted for a password by the file server.
- Maximum PIN length and PIN history are omitted: a maximum only constrains a user who wants a longer PIN, and history only acts when PINs expire.

# 1.1.0-preview.11

- The offline interface harness now captures the Build Standard and Policy automation pages as images, seeded with a supplied value and a rejected one so both states of the generated input form are visible. Every page was already materialised and binding-checked; these two were the pages a reviewer most needs to see and were the only ones not pictured.
- Added [testing this build](docs/TESTING-THIS-BUILD.md): where to download the ready-to-run package, how to unblock it, what to review without connecting a tenant, the order to follow when connecting one, and the known limits of this preview.

# 1.1.0-preview.10

- Added the client-facing build standard document, generated from the loaded standard rather than written by hand. It gives each control in plain English: what it protects, what good looks like, what happens without it, how it is applied, who it applies to, and what the client still has to supply or has had assumed on their behalf. It ends with a table of everything still waiting on the client. Exported as HTML or Markdown from the Build Standard page, prepared for a named client.
- The document deliberately excludes payloads, Graph paths and object identifiers. Those belong in the engineer reports, and a test holds the client document to it.
- Generating rather than writing is the point: a document maintained separately from the recipes drifts from them within a release or two, and then it describes a tenant nobody has.

# 1.1.0-preview.9

- Client inputs are entered as a generated form, one labelled field per input, instead of one hand-written JSON object. A list is typed the way people write lists, separated by commas or new lines, and a wrong value is explained beside the field it was entered in rather than when a plan later refuses to build. The saved JSON remains visible, read-only, behind an expander.
- A reviewable input the client has not supplied now falls back to a dated default and creates the candidate with a warning, instead of blocking the control. The candidate is inert either way, and the plan row says which value was assumed and that it must be confirmed before assigning. Identity inputs - emergency accounts, the office location, targeting groups - declare no default and still block, because a plausible but wrong exclusion is how a tenant locks itself out. A default older than 90 days reports itself as needing a re-check.
- Standard 2026.09.9 targets the built-in All users and All devices populations. Groups are created only where a population has to be named: user and device policy exclusions, unenrolled mobile users, and pilot devices. The managed-users, managed-devices, Office-install and Autopilot groups are gone; the built-in targets replace them.
- Reviewed minimum operating system versions: Windows 10.0.26200.0 (the 25H2 family; 24H2 Home and Pro reach end of updates on 13 October 2026), iOS 26.7 (the security-patched previous major; iOS 27 shipped in September 2026), Android 14 (Google issues security bulletins for 14 and later).
- An optional identifier list may now be empty, so an Enrolment Status Page with no blocking application is expressible.
- 50 controls, 42 recipes. Windows Autopatch is unchanged and remains deployment-mode only: the beta Windows updates namespace exposes no read-only permission.
- CI is the only build and test evidence for this increment; the authoring environment could not run the .NET SDK. No live tenant, consent or write was performed.

# 1.1.0-preview.8

- Standard 2026.09.8 provisions the directory objects every other control depends on through the existing Plan → Deploy path: seven security groups (managed users, managed Windows devices, Office install ready, MAM-only users, pilot devices, Autopilot devices, Conditional Access exclusions) and the office IP named location. This closes the gap that left an engineer building groups by hand before any assignment or Conditional Access recipe could be used.
- Group and named-location creation is constrained by a new guard rather than trusted from the recipe: only empty, assigned-membership security groups (no member, owner, dynamic rule, mail enablement or role-assignable flag), and only untrusted IP named locations carrying client-confirmed IPv4/IPv6 CIDR ranges. Existing objects are still never adopted by name or modified.
- ID-003 (administrator access) is assessed from directory role membership: covered when between two and four accounts hold Global Administrator directly. It cannot see eligible Privileged Identity Management assignments, so that remains an engineer check.
- Deployment mode now requests Group.ReadWrite.All and Conditional Access write for named locations. Both registrations need administrator consent again; assessment mode is unchanged and stays read-only.
- 53 controls, 45 recipes, 23 collections. 49 of 53 controls now report automatically.
- CI is the only build and test evidence for this increment; the authoring environment could not run the .NET SDK. No live tenant, consent or write was performed.

# 1.1.0-preview.7

- Standard 2026.09.7 assesses ID-002 (authentication methods and TAP), ENR-001 (MDM scope) and CMP-001 (tenant compliance default) from captured evidence using equivalence signals, so 40 of 45 controls now report automatically; the reviewed tenant actions that change them are unchanged. Equivalence paths can address array elements by key (`authenticationMethodConfigurations[id=Sms].state`).
- Cancelling a partial capture (operator stop, verification time budget) now stops reading at the first cancellation and records every remaining collection as *Not attempted* instead of a read error, so after-change evidence says what was and was not tried.
- Terminal evidence saves in the LAPS, reviewed-change, package-publishing and recovery services no longer mask the exception that ended the run; a save failure is recorded on the run and retried once, matching the deployment executor.
- Added synthetic tests for the reviewed-change guard (every kind: authentication methods, TAP, MDM scope, tenant compliance, CA activation, assignment, Autopatch, and that plan free-text cannot widen a route or payload), Entra LAPS payload preservation, capture cancellation and the equivalence array selector. These are the first tests covering the preview.6 write surface.
- CI is the only build and test evidence for this increment; the authoring environment could not run the .NET SDK. No live tenant, consent or write was performed.

# 1.1.0-preview.6

- Expand to 37 candidate recipes, with typed tenant inputs, broader Graph JSON imports and saved local standards.
- Add reviewed tenant compliance/authentication/MDM changes, CA activation/report-only, group assignment/removal and Autopatch device enrolment/removal, with before-evidence and read-only re-verification.
- Add encrypted Win32 package publishing for client-supplied RMM/endpoint installers; retain app/version/file IDs and forbid automatic write replay.
- Integrate policy/import/LAPS/package/readiness workflows into WPF. Four identity/service controls retain explicit external/engineer steps.
- Log unexpected deployment completion errors, show a shutdown summary, contain close-handler exceptions, tighten paging item validation and add null guards.
- Compilation only for this increment. Tests and live/GUI acceptance were not performed at the user's request.

# 1.1.0-preview.5

- Add Windows LAPS, Defender Antivirus, firewall and target-tenant Defender EDR candidates in standard 2026.09.5 (18 recipes; 27 manual controls).
- Add a bounded Graph JSON import API with type/URI validation, explicit source-metadata removal and a fresh catalogue digest.
- Add Entra LAPS preview, approved full-PUT enablement, preserved registration settings, durable evidence and read-only re-verification. Unknown writes are never retried.
- Add LAPS prerequisite and Defender EDR entitlement gates. Import and prerequisite UI integration is deferred to the next UI pass. Live tenant/device behaviour remains unverified.

# 1.1.0-preview.4

- Windows sign-in pop-up and browser fallback; exact registered administrator-consent callback.
- Reviewed existing-app repair, custom icon/GitHub branding, engineer assignment and clearer permission counts/handoff.
- Generic product naming; versioned BitLocker and optional long-path candidates, including bounded beta policy recovery.
- Original standards/evidence retained. Live authentication, consent and device effects remain unverified.

# Changelog

## 1.1.0-preview.3 (2026-09-14) — independent review fixes

- Distinguish confirmed pre-request failures from uncertain write outcomes. Auth/guard failures before transport no longer permanently lock a control; original plans remain single-use.
- Add read-only deployment/recovery re-verification with separate integrity-checked evidence and safe local mapping finalisation. Unknown modern writes remain blocked; completed 1.0.0 records require explicit acknowledgement and fresh exact-ID, ownership and settings checks.
- Cancel deployment reads on Stop while preserving the in-flight write; bound policy readback and after-capture to 60 seconds and retain incomplete capture evidence.
- Explain consent redirect fallback to Validate setup, display interrupted runs on Overview and send the current application version during setup sign-in.
- Extend regression tests and offline UI rendering. Real consent redirects, Graph propagation, recovery and connected shutdown still require authorised tenant validation.

## 1.1.0-preview.2 (2026-09-14) — recovery and licence visibility

- Added a durable policy change register, selective deletion of recorded creations, restoration of supported inactive updates and reviewed Conditional Access disablement.
- Recorded exact returned IDs, write payloads and before/after objects. Recovery previews bind tenant, engineer, application, standard, source run, ownership and fresh evidence; writes are single-use and uncertain outcomes block further writes.
- Added tenant overview subscription counts, searchable assigned users and conservative licence checks for directly resolved Conditional Access user scopes. Failed or unsupported checks remain unknown.
- Tightened service-plan checks to active, provisioned subscriptions with enabled capacity. Recovery and deployment share a tenant evidence-store lock.
- Expanded synthetic safety and WPF coverage. Fixed idle window shutdown and IPv6 fallback delays in localhost callback tests. No live tenant acceptance is claimed.

## 1.1.0-preview.1 (2026-09-14) — controlled setup and engineer workflow

- Added a separate delegated app-setup wizard: permission preview, two single-tenant registrations and enterprise apps, explicit creation approval, consent links and configuration/grant/engineer-assignment validation.
- Added one-time connections and tenant-bound account lookup with exclusions, purpose, reason and persistent-effect review. Dedicated emergency-access and creator safeguards remain.
- Enforced complete durable snapshots and all plan bindings inside the executor, licence/reference readiness, single-use plans and truthful write acceptance/readback reporting.
- Fixed forced silent renewal after read 401, missing-versus-null comparison, unexpected terminal results and inaccurate client-report wording.
- Redesigned the Windows shell and pages, added cooperative stop/shutdown and moved exports off the UI thread.
- Review build only. Live app setup, consent, policy acceptance and functional tenant outcomes remain unverified. Scope remains 45 controls and 12 creation recipes.

## 1.1.0 (unreleased) - equivalent configuration and usability

### Assessment
- **Equivalence signals.** Controls may declare, as catalogue data, what an existing object must look like to cover them. A client policy that meets every required condition is reported as a partial match naming the object and the properties that satisfied each condition, instead of *Missing* or *Requires manual review*. Equivalence never reports Compliant on its own, never overrides a stronger settings result, and always surfaces exclusions and disabled states as caveats.
- Signals support grouping, so alternative routes to the same outcome (an MFA built-in control or an authentication strength) both count.
- Signals drafted for CA-001, CA-003, CA-004, CA-005, CA-006, CA-007, CA-008, CA-009, CA-010, MAM-IOS-001 and MAM-AND-001.
- Engineer HTML and the CSV/XLSX exports gained an equivalence table with expected and observed values, plus a caveats sheet.

### Fixes
- The access check no longer probes `/subscribedSkus` with `$top=1`; collections may declare `supportsQuery: false`. Previously a Graph `400 UnsupportedQuery` was reported as if it were a permission failure on `Organization.Read.All`.
- Selecting a saved client reloads its details reliably. Previously the selection could be set internally without loading the form, so re-selecting a client did nothing and saving then created a duplicate client.
- Saving a second client for a tenant that already has one is refused; evidence, mappings and deviations are keyed by tenant ID and would otherwise be split in two.

### Usability
- Connection details, application details, the access check, export paths and finding details are selectable text with Copy buttons.
- Exports report how many objects were written and offer **Open containing folder**.
- The Connect page names the selected client and separates "connect to selected client" from "save and connect".

## 1.0.0 (2026-09-11) - consolidated design

Replaces two earlier prototypes (Tenant Console 0.3.2-rc.1, Node/PowerShell; and the .NET/WPF Tenant Toolkit 1.0.0 draft). Nothing was merged mechanically; the design decisions are recorded in `docs/REVIEW-REPORT.md`.

### Architecture
- Single .NET 8 solution: Core, Graph, Engine, WPF App, Tests. Self-contained win-x64 portable release; no runtime installation.
- Native Microsoft Graph HTTP client replaces the PowerShell worker and the Graph PowerShell module dependency.
- Removed the CLI project and the What-If (beta) service from scope; both are documented as future options.

### Safety
- Two application registrations (assessment and deployment) for token-level read/write separation; incremental consent within one registration was rejected because tokens carry every consented scope.
- Conditional Access candidates are created `disabled` with expected targeting, emergency accounts, optional exclusion group and the verified operator excluded; report-only was rejected as the initial state.
- Write guard enforced in the catalogue validator, the planner and the Graph client; assignments are never written.
- Plans are digest-bound to tenant, profile, standard, snapshot, managed-object mapping, account and application; they expire with their snapshot.
- Pre-write reconciliation, readback verification, after-change snapshots, no automatic retry of writes, honest interrupted-run marking, close-waits-for-write shutdown.

### Assessment
- Collection-based comparison with property-level differences and name resolution; statuses distinguish enforced, not enforced, partial, missing, unknown, manual, licence and not applicable.
- Licence evaluation from subscribed SKUs.
- Deviation register (approved deviation or not applicable) that never hides unknown data.
- Drift analyser classifies toolkit-managed objects (removed, modified externally, metadata only) and explains control status changes.

### Evidence and reports
- Tenant-partitioned evidence with integrity digests; every load checks tenant binding.
- Engineer HTML, Markdown, JSON, CSV and XLSX; client-facing HTML summary; run and drift reports. Formula-safe CSV/XLSX with no third-party dependency.

### Build
- `BUILD-ME-FIRST.cmd` installs a user-local .NET 8 SDK into `.dotnet\` (no administrator rights) and runs the full restore, build, test, manifest, publish and package pipeline; all output is written to `build\last-build.log`.
- Repo-level `nuget.config` pins nuget.org so restore does not depend on machine-level NuGet settings.
- First green build: 0 errors, 89 tests passing, packaged to `dist\BDIT-Tenant-Toolkit-1.0.0-win-x64.zip`.

### Standards
- Catalogue schema v3 with expected production state and safe deployment state per control; 45 controls (12 automated recipes) carried over from Tenant Console 2026.09.2 and enriched with severity, business impact, engineer action, licence and manual instructions.
- SHA-256 manifest integrity check (documented as a digest, not a signature).
