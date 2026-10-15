# Next release plan: standard 2026.09.12, Exchange and Purview, and the build standard documents

Written 25 September 2026 for Astra/Codex to carry out autonomously over several hours, after PR #18. It records what the maintainer has already decided, so the work does not reopen settled questions, and it sets the order, the stop points and what "done" means.

Read first, in this order: [AGENTS.md](../../AGENTS.md), [AGENT-COORDINATION.md](AGENT-COORDINATION.md), [HANDOVER.md](HANDOVER.md), [DECISION-LOG.md](DECISION-LOG.md), [FULL-REVIEW-2026-09-24.md](FULL-REVIEW-2026-09-24.md), [ARCHITECTURE-ROADMAP.md](ARCHITECTURE-ROADMAP.md), [automation coverage](../AUTOMATION-COVERAGE.md) and [application setup](../APPLICATION-SETUP.md).

## Ground rules

These outrank everything else in this plan. If one gets in the way of a task, stop that task, record why in the handoff and carry on with the next.

- **No live tenant work of any kind.** No tenant sign-in, Graph, Exchange, Intune, application, consent or device operation, not even read-only and not even against a disposable tenant. Synthetic fixtures only. Never read existing client evidence, tokens or saved connection data. The maintainer tests against a real tenant afterwards.
- **Microsoft behaviour is researched, not remembered.** Every endpoint, CSP path, setting ID, cmdlet, permission and licence requirement in the new release cites a primary Microsoft source (learn.microsoft.com, the Graph or CSP reference, the settings catalog definition) in `references` or `documentationNotes`. Anything that could not be confirmed is marked unverified in the control and in the handoff. Do not describe untested Microsoft 365 behaviour as verified.
- **The safety invariants hold for everything new.** Assessment stays read-only. A complete pre-change snapshot before any write. Writes previewable, selective and individually reported, with durable intent recorded before the request. Conditional Access created disabled. Intune policies and apps created unassigned; assignment stays a separate reviewed step. Groups created empty; named locations created untrusted. The operator stays excluded. No adoption by name. Unknown is never reported as missing. No automatic retry after an uncertain write. The typed tenant confirmation (`TenantConfirmation.Matches`) before every write.
- **Published standard files are never edited.** The new release is a new file, `standards/2026.09.12.json`, with its manifest entry. Report text that renumbers or retires a control says so, as preview.13 did.
- **Permission changes are documented.** Every new delegated scope goes in the permission documentation, in `DECISION-LOG.md` with its reason, and in the changelog with the note that both applications need administrator consent again.
- **Schema changes are decisions.** A new field in the standard schema or the client profile (the office location list, the manual implementation fields, the Exchange section) is recorded in `DECISION-LOG.md` before it is built on, with a test that fails if it regresses.
- **Coordination.** Branch `astra/…` from the current `integration` tip. Open a draft pull request as the first act and add a `WORK-CLAIMS.md` row. Do not touch `main`, `integration`, any `claude/…` branch or either preserved source repository. Do not merge or force-push.
- No secrets, client data or tenant identifiers in source control. UK English throughout.

## What the maintainer has decided

Settled. Implement these; do not re-argue them. Where an item turns out to be impossible without a custom template, a script or a new credential type, do not work around it: make it a documented manual step (it then appears in the manual implementation guide) and say so in the handoff.

### Identity

- **Passkeys** enabled in the authentication methods policy, including passkeys in Microsoft Authenticator.
- **Phishing-resistant authentication strength** (the built-in strength) required for administrator roles, as a Conditional Access candidate created disabled like every other.
- **System-preferred MFA** on. **Registration campaign** on, nudging users to Microsoft Authenticator.
- **User consent to applications off**, with the **admin consent workflow** on. The workflow needs reviewers: make them a required client input, not a default.
- **Self-service password reset stays off.** Checked and reported only; never changed.
- **Web sign-in on Windows** enabled, so a Temporary Access Pass can be used at the Windows sign-in screen.

### Office locations (replaces PRE-008 as a single location)

- The client input becomes a list of locations, each with a **name** and **one or more IP ranges**. Several locations per client.
- Each is created as an untrusted IP named location, as today.
- Ranges are validated as **public**: refuse private (RFC 1918), loopback, link-local, carrier-grade NAT (100.64.0.0/10), documentation, multicast and unspecified ranges, `0.0.0.0/0` and `::/0`, and malformed CIDR. At least one range per location; the name is mandatory.

### Devices and enrolment

- **Everyday join path:** Settings › Accounts › Access work or school › *Join this device to Microsoft Entra ID*, with automatic MDM enrolment (ENR-001).
- **Windows Autopilot device preparation** (the newer model) is the provisioning standard. It needs a device security group whose owner is the Intune Provisioning Client service principal: create the group empty like the other prerequisites, and treat setting its owner as a reviewed write. Classic Autopilot registration (hardware hashes, tying a device to the tenant) is **deferred**, as is anything using serial numbers or corporate identifiers. Recommend in the handoff what 2026.09.12 does with ENR-003 (classic Autopilot profile) and ENR-004 (Enrolment Status Page); do not delete them from older releases.
- **Leave "Reset this PC" alone.**

### Windows configuration

- **ESET is the antivirus and EDR.** Remove SEC-WIN-001 (Defender antivirus) and CMP-WIN-002 (Defender compliance supplement) from 2026.09.12. ESET is a per-client package that engineers install or deploy through Intune (APP-WIN-008); the standard does not ship it. For SEC-WIN-002 (Defender EDR onboarding) and any other Defender-dependent setting, do not remove silently: list each with a recommendation under *Decisions needed* in the handoff.
- **Windows Firewall is not managed.** Remove SEC-WIN-003 from 2026.09.12.
- **BitLocker** (CFG-WIN-001) adds **recovery password rotation**.
- **Windows Hello PIN** (CFG-WIN-003): minimum eight characters, **at least one lowercase letter required**, so a digits-only PIN is refused; digits allowed. Confirm the PassportForWork complexity values and their defaults from Microsoft's CSP reference, not from the current payload's comments, and pin them with a test.
- **Long paths** (CFG-WIN-007) on by default rather than optional.
- **Chrome single sign-on** through the native settings catalog setting (`CloudAPAuthEnabled`). **No uploaded ADMX.** If the setting is not in the built-in catalog, it becomes a manual step.
- **Quality of life**, each through a native setting where one exists:
  - UK region and keyboard at setup, and the UK time zone. The Windows Welcome screen and new-user regional settings are left to engineers.
  - Show file extensions.
  - OneDrive Files On-Demand and Storage Sense.
  - Hide consumer suggestions and consumer features where the edition supports it (say which editions).
  - A taskbar **starting** layout, not enforced: Edge, File Explorer, Outlook, Teams, Microsoft 365 Copilot. No Word or Excel.
  - Microsoft Store **not** restricted; store apps may be deployed.
  - Enterprise State Roaming on.
  - Fast startup off; no sleep on mains power.
- **Remove the consumer Copilot app.** Keep Copilot changes to that alone; the Microsoft 365 Copilot app stays and is pinned.
- Excluded: Remote Help, Quick Assist, Wi-Fi profiles, Universal Print. Branding is a suggestion in the documents, not a control.

### Compliance and updates

- **Grace period five days** (long weekends) before a device is marked non-compliant, on every compliance policy in the standard.
- **Compliance notifications** (email to users): not configured.
- **Windows Autopatch for all devices**, plus read-only checks of its tenant prerequisites: Windows diagnostic data for processor configuration, Windows licence verification, and no conflicting update rings or feature-update policies.

### Mobile

- Deploy, created unassigned: Outlook, Teams, Microsoft Authenticator, OneDrive, Edge, Word, Excel and Company Portal, for iOS/iPadOS and Android. No Apple Business Manager, so no VPP licences or supervised-only settings. Android store apps depend on the Managed Google Play connection (ENR-006).

### Exchange and Purview (new sections)

Read-only assessment first; writes after, under the stop point in phase 5.

- **Unified audit log on.** Report audit retention: 180 days is the default; longer needs an add-on licence or export. Report it, do not change it.
- **Spam bypass transport rule**, deliberately narrow: messages **from the client's own domain** whose **SPF passes** get SCL −1. The domain is an **engineer-entered text box, mandatory** when the control is selected. Nothing else in the rule. The documents explain why the SPF condition is required (without it, anyone spoofing the domain bypasses filtering).
- **Standard preset security policy** on for all recipients. Impersonation protection: the preset's *users to protect* list is filled in by the engineer by hand; the tool does not manage it.
- **Block external automatic forwarding.**
- **SMTP AUTH off** for the organisation.
- **External sender tagging** in Outlook on.
- **Mailbox auditing** on (report if disabled).
- **DKIM:** report status, **show the CNAME records** to publish, and enable signing only once those records resolve.
- **DMARC:** check the record and report it; no write.
- Retention policies and DLP: **out of scope** (future).

### Everywhere

- **Mandatory fields as needed.** A required input refuses to save empty and says what it needs.

### Not decided: propose only

Put a recommendation in the handoff; do not build these.

- Outlook for iOS/Android automatic account setup through app configuration.
- Classic Autopilot registration and tying devices to the tenant (deferred by the maintainer).
- Anything the investigation shows needs a custom template, a platform script or a remediation script. Those are a new write mechanism, not a setting.

## Work plan

Work in this order. Each phase ends with a commit and a push, so a stop at any point leaves usable, reviewable work. CI on the pull request is the build evidence; run the same checks locally first.

### Phase 0 — pre-flight and a review from scratch (short)

1. The pre-flight check in `AGENT-COORDINATION.md`. Draft pull request and claim row.
2. Build with `-warnaserror`, run both test projects, run the interface harness and read its output (`commands.txt`, `tab-order.txt`, the refusal list). Record the counts.
3. Read the code the plan touches end to end — the standard loader and schema, the planner and creation guards, the collectors, the assessment engine, NameResolver, the reports, the client profile and its validation, the Workspace, the navigation and the harness. Write `docs/integration/ASTRA-REVIEW-2026-09-25.md`: defects found, each with file and line and severity. Fix defects in the areas you are claiming; report the rest.

### Phase 1 — investigation (no product code)

Write `docs/integration/API-INVESTIGATION-2026.09.12.md`. One row per decided item: how it is read, how it is written, the exact Graph route or CSP path or settings catalog ID or cmdlet, v1.0 or beta, the delegated permission and whether it is new, the licence and Windows edition it needs, how the result is verified afterwards, and the Microsoft source. Mark anything unconfirmed.

The Exchange item decides the architecture of phases 4 and 5, so answer it properly: Exchange Online settings are not in Microsoft Graph. Compare the supported routes — the Exchange Online PowerShell module run by the tool under delegated sign-in with a cmdlet allow-list, generated scripts the engineer runs, and anything else Microsoft documents — against the ground rules: delegated only, no secrets or certificates, no mandatory administrator rights, portable. Reject an undocumented API even if it works. Record the choice in `DECISION-LOG.md`.

### Phase 2 — standard 2026.09.12: Entra, Intune, Windows and mobile

- New release file and manifest entry; every change listed above; retired controls removed from the new release only.
- The office location list input, its validation and the planner change, with tests for every refused range class and for a location without a name.
- Assessment for every new control, including unknown handling. New collections go through the existing collector and snapshot path.
- Client inputs: new required inputs block with a clear message; reviewable defaults warn, as today.
- Tests: each new payload pinned; each safety invariant tested with a case that fails if it is removed; the Hello complexity values pinned against the CSP convention.

### Phase 3 — interface sections

The navigation groups the product by area: **Entra**, **Intune**, **Exchange**, **Purview**, alongside the existing flow pages. Choose the simplest structure that reads well (grouped navigation, or an area filter on Assessment and Plan) and record it. Every new page follows the design system, is reachable by keyboard, names its controls for screen readers and passes the harness at all three window sizes. Register every new command in the harness command register; new tenant-facing commands are *not pressed* with a stated reason.

### Phase 4 — Exchange and Purview, read-only

Using the phase 1 decision: read each item, assess it, report it, show the DKIM records to publish and the DMARC result. DNS lookups go through an interface the tests replace with a fake. Evidence is captured like any other collection.

### Phase 5 — Exchange and Purview writes (stop point)

Only if phase 1 found a supported, delegated, secret-free route. Each write is previewable and selective, has before and after evidence, needs the typed tenant confirmation and is never retried after an uncertain response. DKIM is enabled only when its records resolve. The spam bypass rule refuses to plan without the domain. If the route needs anything the ground rules forbid, stop here: generate the reviewed PowerShell for the engineer instead, which also serves the manual guide, and record the proposal for the maintainer.

### Phase 6 — the two build standard documents

Both generated from the loaded standard, never written by hand, exported as HTML and Markdown from the Build Standard page like the existing client document, and covered by tests.

1. **The Build Standard** — what the standard is. For each control, grouped by area: what it does, the setting and value, why, who it applies to, licence, and what the client must supply. Engineer-facing, so settings and values are included; tenant identifiers never are.
2. **Manual implementation and verification guide** — how to do each control by hand and how to check it. For each control, in this order:
   - **Before running the automation:** prerequisites and anything that must be done first.
   - **By hand:** the portal path, step by step, and the PowerShell (Microsoft Graph PowerShell or Exchange Online PowerShell) for the same result.
   - **After the automation:** what to check, where, and what a pass looks like. Where the automation cannot confirm something (application deployment, licence effect, device behaviour), this is the check.

   It needs new catalogue fields for each control's manual steps; add them to the schema through `DECISION-LOG.md`, fill them for every control in 2026.09.12, and test that no control ships without them.

### Phase 7 — finish

- Build with `-warnaserror`, both test projects and the harness green on CI. Package link check passes.
- Documentation: changelog (new preview version), automation coverage, application setup and permissions, testing this build, handover, comparison matrix and decision log.
- Pull request body completed from the template; marked ready for review. Do not merge.

## Done means

- 2026.09.12 implements every decided item or names it as a manual step with a reason.
- Every new write is covered by a test that fails if its safeguard is removed.
- Both documents generate from 2026.09.12 with no control missing a section.
- CI green on the pull request head, with the counts in the pull request.
- The handoff below is complete and honest about what was not verified.

## Handoff

Finish with exactly this block, and nothing after it:

```
HANDOFF
- Branch / PR / head: <branch> / #<n> / <sha>
- Phases completed: <list>; stopped at: <phase and reason, or "none">
- Build: <warnings/errors>; engine tests <n passed>; app tests <n passed>; harness <summary lines>
- Standard 2026.09.12: <controls added / changed / removed, with IDs>
- New permissions: <scope, why, where documented>
- Decided items implemented: <list>
- Decided items made manual, with reason: <list>
- Unverified Microsoft behaviour: <list>
- Decisions needed from the maintainer: <each with a recommendation>
- Review findings (phase 0): <severity, file:line, one line each; fixed or reported>
- Known limits: <list>
END HANDOFF
```
