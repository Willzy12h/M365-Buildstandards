# BuildStandard engineering expansion — proposed design

Date: 18 October 2026  
Implementation base checked: `main` at `2e4a0635ddfdf28330f8271942d53fb2a8ca3b0a`  
Status: **William requested implementation of all recommendations; this technical design is proposed for approval. No new implementation, permission, safety-review or release acceptance is claimed.**

## 1. Outcome and scope

Extend the existing BuildStandard Windows application so a second engineer can build, backfill, investigate and periodically review a Business Premium client using consistent evidence and task-specific instructions. Preserve the current .NET 10/WPF interface, contained catalogue reader, shared engine, portable packaging, standards history and all execution safeguards.

The twelve recommendations in `BuildStandard-Vendor-Capability-Review-2026-10-18.md` are the scope reference. The separate “defer or keep external” list is not included. No cloud service, database, generic workflow designer, new UI framework, content backup engine, migration engine or macOS support is introduced.

### Approach selected for review

**Extend the current engine and native pages in bounded packages.** Reuse evidence, jobs, report registries, typed forms, exact-context admission and existing exporters. This offers more continuity and less duplicated execution logic than integrating multiple administration tools as runtimes.

Alternatives considered:

- Launch CIPP/Microsoft365DSC/Maester as general-purpose backends: faster apparent breadth but introduces overlapping permissions, deployment semantics, dependencies and ambiguous provenance. Do not choose this. Later external-test import is a separate, strict read-only format.
- Introduce an independent local administration platform: duplicates the existing application and increases handover/support work. Do not choose this.

## 2. Baseline reconciliation

- PR #137 and #138 are merged; #138's actual merge is the implementation base above. There are no open PRs in M365-Buildstandards or the two preserved source repositories at this check.
- Issue #83's latest closeout records main run `38829350691`, its source-matched package and remaining acceptance limits. That is existing evidence; no checks have been rerun for this design.
- The #138 WORK-CLAIMS row still says “Implementing”. Issue #83 explicitly records its completed disposition and describes that row as historical. Preserve the original attribution; reconcile the GPT-owned row when the next authorised coordination PR is opened.
- The current working checkout is clean on local branch `work` at `81b18f5`; it is not the intended implementation base. A separate read-only archive of main was inspected. Do not build new features on this older checkout accidentally.
- Existing INT-088/121/123 contracts admit four registered mailbox reports. Permission/calendar/log families explicitly require further identity/schema contracts. They are not covered by acceptance of the earlier adapters.
- `global.json` pins .NET SDK `10.0.401`. This cloud shell currently has neither dotnet nor pwsh on PATH. Windows Actions previously provided native/hosted/package execution. Environment setup is needed when implementation is authorised; historical CI does not verify new code.

## 3. Complete recommendation-to-package mapping

| Report recommendation | Package | Intended delivered behaviour | Key dependency/gate |
|---|---|---|---|
| 1. Deployment coverage | B | Evidence-backed journey from candidate through targeting, pilot and observed results, with explicit unknowns. | Supported targeting/result schemas; execution changes retain named safety review. |
| 2. Integrated Exchange reports | C | Registered mailbox/calendar permissions, inbox-rule investigation, message trace, then selected group/policy reports. | Composite/object identities, query/retention semantics and runner/evidence review. |
| 3. Domain/email readiness | D | Selected-domain MX/SPF/enrolment checks and DNS-owner worksheet, extending existing DKIM/DMARC. | Bounded resolver contract; no DNS changes or lookups during development. |
| 4. Collaboration access | E | SharePoint/Teams/OneDrive ownership, guests, grants, inheritance and explicit scan coverage. | Workload-specific API/RBAC/permission contract. |
| 5. Licence usage | F | Usage-based review suggestions joined to existing entitlement/capacity evidence. | Reports.Read.All proposal, role and safe download contract. |
| 6. Client portfolio/partner readiness | A + G | Local client summary/CSV import first; GDAP relationship and role readiness later. | A needs no service access; G needs partner-read contract. |
| 7. Joiner/mover/leaver | H | Discovery/checklist first, then individually reviewed supported Graph actions with durable per-step outcomes. | Explicit action/permission/recovery contracts; Exchange changes remain manual. |
| 8. CA scenarios | B | Dedicated read-only What If evaluation, separate from disabled-candidate projections. | Exact POST evaluation contract; no relaxation of generic write guard. |
| 9. Pack authoring/comparison | I | Supported-setting editor, comparison and explicit client overrides with verified release lineage. | New catalogue publication still separately approved. |
| 10. Recurring review/handover | A | Single evidence-backed local engineer/client review pack with citations and limitations. | Diagnostic history reads and fixed-source export. |
| 11. Service-health context | G | Correct portal links first; optional incident/advisory reads and escalation context. | ServiceHealth.Read.All proposal. |
| 12. Security/incident context | J | Read-only incident links/status, selected control mappings and separately labelled external-test evidence. | Service/licence/API contracts; no incident-response writes. |

Packages A–J are finite delivery areas, not parallel agent assignments. GPT is the initial implementer. Start with one writer and existing isolation; do not create one worktree per small edit.

## 4. Shared rules

### Evidence and UI meaning

Every result retains tenant ID, resource/audience, source or adapter version, collection interval, requested scope, returned scope, completeness, failed/truncated portions and original evidence identity. Historical/imported evidence remains historical/imported. New report evidence cannot satisfy complete deployment before-evidence.

Reuse current report schemas where their semantics fit. New identities or meanings get a reviewed version/registration; never add invented GUIDs, silently reinterpret older records or admit generic untyped dictionaries.

Views may show cached/local evidence without connecting. Live actions are deliberate and name their tenant/account/resource and required access. No page navigation starts collection or prompts authentication. A current-client result must pass the existing presentation-context/generation checks after asynchronous work and before export. A client summary does not itself connect, alter a selected profile or confer execution authority.

Use friendly names with exact IDs available in details/copy/export. Every unavailable action states the next step. Preserve keyboard access, visible focus, virtualised tables, progress and cancellation. Failed/cancelled/partial reads are not blank-success tables.

### Execution

Existing engine rules remain authoritative: complete durable before-evidence, exact approval and immutable payload, tenant/account pinning, final admission, single-use plans, durable intent, uncertain-write blocking and no write replay. Candidate CA policies remain disabled with the reviewed targeting/exclusions; Intune candidates remain unassigned. Assignment, activation, retirement and recovery stay separate.

A workflow step can be recorded as not attempted, accepted, rejected, uncertain or verified according to the existing semantics. “Accepted” is not “effective”. Multi-step administration cannot offer a blind Retry All operation. Local state cannot prove which remote request succeeded after an interruption; require reconciliation.

No shared token/cache between Graph and PowerShell, blanket permission escalation, global administrator fallback or generic script escape hatch. New code uses synthetic data; no live tenant/device/consent/DNS actions occur during source work.

### Coordination and integration

Use canonical main as the source base, preserving integration/frozen/phase history. Proposed feature branches are small `astra/bs-<package>-<slice>` branches with reviewed PRs. No direct main push, force-push, settings changes or inferred blanket main-merge acceptance.

Before branching, fetch, inspect all three repositories' open PRs and current claims. Use the existing WORK-CLAIMS, DECISION-LOG, COMPLETION-REGISTER and issue #83/inbox; do not create a second coordination system. Shared contracts need a decision PR merged before dependent implementation under AGENT-COORDINATION. Applicable independent/named reviews remain outstanding until actually provided. A previous candidate's owner acceptance does not accept future code.

The existing build workflow validates PRs and pushes to main/integration/phase. Do not add another long-lived branch or weaken workflow checks to reduce CI usage. Run targeted local tests before a purposeful composed Windows run; retain necessary head/merge evidence.

## 5. Package A — first complete subproject design

### Engineer journey

1. Open **Clients and reviews**, an offline native workspace view.
2. See saved clients and available local evidence: last readable assessment, its standard/source/time, incomplete or unreadable evidence, unresolved outcomes and overdue deviations.
3. Select **Review saved evidence** to use the existing offline-client path. It cannot silently switch an active tenant connection. Existing unsaved-output/busy guards remain in force.
4. Optionally choose **Import customer list**, preview a Partner Center CSV, select new clients, then save the local records.
5. Open one client's review and export a fixed-source engineer/client pack. No tenant connection is required or implied.

### Existing components to reuse

| Existing component | Use |
|---|---|
| `Core.Models.TenantProfile`, `ProfileValidator` | Existing client identity, validation and one-profile-per-tenant meaning. |
| `Engine.Evidence.EvidenceStore.LoadProfiles/SaveProfiles` | Existing local profile storage; preserve cache/evidence/lock roots. |
| `Engine.Evidence.LoadAssessmentHistory` | Readable assessment citations and explicit unreadable-history records. Do not use the compatibility method that discards diagnostics. |
| `EvidenceStore.ListSnapshots/LoadRuns/LoadDeviations` | Sources for summaries; snapshot/run listings require diagnostic counterparts before they support absence claims. |
| `Core.Models.Deviation` | Existing owner, approver, reason, reference and review-by date. No second register. |
| `Engine.Workflow.JobProjection/JobCompletion`, `Engine.Drift.DriftAnalyser` | Reuse existing completion and comparison meaning where source scopes/releases are compatible. |
| `App.Services.PresentationContextKey`, `Workspace` | Existing context admission, profile selection, exclusive work and export completion guards. |
| `App.ViewModels.ConnectViewModel`, `RouteCatalog`, `ShellViewModel` | Saved-client/offline selection and native routing. No alternative authentication flow. |
| `Engine.Reports` exporters | Existing safe HTML/CSV/XLSX generation and local output conventions. |

### Intended new responsibilities

Names below are proposed, not claims of existing implementation:

- `Engine/Workflow/ClientReviewProjection.cs`: deterministic, read-only aggregation over explicit source records and diagnostics, using a fixed clock. No Graph/PowerShell dependency or new database.
- Diagnostic history readers in `Engine/Evidence`: return records plus unreadable/truncated/unavailable status for the new projection. Preserve compatibility of existing callers and historical bytes; make the narrow API change a reviewed contract.
- `Engine/Identity/PartnerCustomerImport.cs`: bounded CSV parsing and import preview. Reuse a suitable existing dependency if present; otherwise a small standard-library parser with quoted-field/newline tests, not naive comma splitting.
- `Engine/Reports/ClientReviewDocuments.cs`: one-client output from a captured immutable review model. Reuse existing encoding and spreadsheet formula protections; add a regression if an exporter lacks protection for imported strings.
- Native client/review view model and view: presentation only. CLI gets an offline summary/export command using the same projection after the interface is agreed.

### Portfolio result rules

- Display **last readable assessment** rather than imply it is the latest when unreadable records exist. Preserve source identity and observed standard release; do not recalculate old results against today's standard silently.
- Missing, malformed or unavailable evidence is a visible collection state. A readable clean assessment cannot hide an unreadable later run or unknown outcomes. A dashboard is never proof of “safe to deploy”.
- Unknown write-acceptance/status values remain unresolved. Use the current conservative outcome rules; do not count unrecognised values as not attempted.
- Compare UTC timestamps as parsed instants; invalid timestamps are unknown. Review dates follow the existing date contract. Display the local review time separately from evidence collection time.
- Directory-wide failure remains one visible client error. One corrupt client must not discard unrelated readable client rows.
- Use bounded local reads with cancellation and progress. Any cap is explicit and makes the section incomplete, never a smaller successful inventory. No unbounded deserialisation on the UI thread.
- Do not repair, mark interrupted, delete or rewrite evidence during a summary scan.

### CSV import rules

- Accept Partner Center's documented Microsoft ID, Company name and Primary domain name columns; require the ID and name. Relationship/tags are optional display-only metadata in the preview, not proof of access or new profile authority.
- Initial supported format is UTF-8 (optional BOM), comma-separated CSV with quoted values and CRLF/LF. Refuse malformed quoting, duplicate headers, control-character identities and unsupported encodings with a useful message. Limit to 5 MiB and 5,000 data rows; exceeding a limit refuses the import rather than silently dropping rows.
- Canonicalise and validate tenant GUIDs. Preserve the domain as unverified metadata, using existing profile constraints. Never infer a tenant ID from a domain or connect to verify it automatically.
- Group repeated tenant IDs in preview. Exact duplicate rows may be collapsed with an explicit count; conflicting names/domains are blocked until resolved. Existing tenants are skipped and shown; this first import does not overwrite their application IDs, parameters, exclusions, names or evidence.
- Snapshot the profile list at preview. Recheck it at save; if it changed, invalidate the preview and ask the engineer to review again. Save selected valid additions in one atomic profile write, not a loop with partial success. Reuse existing busy/state guards.
- Cancelling the dialog writes nothing. A failed local save retains the preview and does not claim import success. Imported CSV fields are untrusted text; never execute formulas or create clickable arbitrary script/command URLs.

### Review-pack rules

- One client per pack. Capture selected evidence identities/digests and the current presentation ticket at the start; reject changed context or changed source bytes before publishing the result.
- Include last assessment, collection limits, standard version, known deviations/review dates, linked job status and unresolved outcomes. Include only available credential/report metadata; never fetch new access silently.
- Compare like-for-like evidence; incompatible scope or standard differences are explained and excluded from a numerical trend, not reported as improvement/regression.
- Client summary and engineer detail share the same source model. They differ in detail, not in what is counted as known. Bundle citations and a small manifest; hashing establishes byte identity, not Microsoft authenticity.
- Use the existing local export workflow so cancellation/save failure is truthful. No upload, email or PSA integration.

### Completion and acceptance

Behavioural tests must first fail, then pass for: corrupt/missing/wrong-tenant evidence; unknown write status; stale context and A→B→A; timestamp ordering; cancellation; source changes during export; quoted CSV/newlines/BOM; malformed headers/IDs; duplicate/conflicting tenants; existing-client preservation; changed preview/save state; atomic write failure; HTML/CSV hostile strings; disconnected operation with zero tenant calls; desktop/CLI projection parity.

Add native harness cases at its three supported sizes for the portfolio, import preview, empty/error/partial states and keyboard navigation. No live report commands run in the harness. Build the matching source/configuration before `--no-build` tests. Final composed Windows build, engine/App tests, native renders, hosted-reader regression and fresh portable-package checks remain required by the existing workflow.

Evidence must identify requirement, source SHA, exact test outcomes and original package/hash. Human accessibility and live acceptance remain separate. Rollback removes the new view/commands and local projections; existing profiles/evidence remain readable. No profile schema migration or historical evidence rewrite is required.

## 6. Remaining package constraints and deliverables

### B — coverage and CA evaluation

Reuse ApplicationDeploymentAssessment, ReviewedChangeService, Workflow/Jobs and current source-bound selection. Add per-control coverage projections only for supported membership/filter/result inputs. Unsupported combinations stay unknown; no inferred device success from assignment presence.

CA evaluation uses the documented exact `POST /identity/conditionalAccess/evaluate` as a dedicated read-only operation, separate from generic Graph write transport. Request schema, token/tenant/resource admission, response schema and cancellation need a reviewed contract. Policy.Read.All is already named by existing collection scopes, but returned token access must be verified. Microsoft evaluates enabled/report-only policies, not disabled candidates; show separate labels for any offline candidate analysis. Do not enable candidates to obtain a test result.

Acceptance includes partial membership, stale snapshots, missing licences, delayed device results, unknown filters, evaluation errors, unsupported scenarios, wrong identity and preservation of assessment-mode write refusal.

### C — registered Exchange report families

Reuse the fixed runner, ReadOnlyRunWorkflow, schema registry and original-output recovery. Add separate registrations and versioned row schemas for mailbox/calendar permission edges, rule identities, message traces and group/policy observations. Store actual service identity or an explicitly typed composite identity; do not fabricate GUIDs or force mailbox rows onto non-mailbox objects.

Each family has a bounded date/row/byte contract, actual source commands, per-value read states and query interval. Preserve all existing standalone scripts/pins until a deliberate compatible new registration is reviewed. Exchange mutations remain Copy/Save/manual. Test wrong-account/tenant refusal before reads, duplicate/composite identities, unreadable principals, missing versus empty properties, truncation, cancellation and saving after disconnect.

### D — domain readiness

Extend the fixed Windows DNS adapter, not arbitrary process execution. Persist selected-domain query name/type, resolver/time, answers and explicit NXDOMAIN/NODATA/failure states. Bound SPF include/redirect traversal and lookups; refuse loops and avoid claiming an authorised sender simply because a record parses. Distinguish expected security-gateway MX from unexplained routing. Reuse observed DKIM targets; report enrolment CNAMEs and manual DNS changes without writing DNS.

Synthetic resolver tests cover loop/cap exhaustion, split TXT strings, multiple SPF records, timeouts, stale domain changes and malicious record text. No external DNS query during development.

### E — collaboration permissions

Start with explicitly scoped site/Team/OneDrive metadata, ownership and guest/grant inventory. Library inheritance and item-level sharing scans have distinct coverage and permission requirements; one does not imply the other. Do not label search-discovered sites as a complete tenant enumeration. No unsupported “effective access” claim based only on direct grants.

Before implementation, the contract must select documented APIs, least-privilege scopes/RBAC and identity/pagination semantics. If an API requires application-only access or a broader privileged SharePoint path, present that concrete decision instead of silently broadening the desktop registration. Keep unavailable portions visibly unimplemented/blocked; all twelve recommendations remain tracked. Content bodies and automatic revocation are excluded.

### F — licence usage

Extend GraphReportService with a separate usage-report contract and joins to existing exact entitlement/size evidence. Proposed access is Reports.Read.All plus an appropriate delegated role, subject to William's separate approval. Preserve privacy-obscured identifiers; do not toggle the tenant privacy setting or join by display name. Handle Microsoft's preauthenticated download redirect with bounded HTTPS admission, no forwarded Graph token and secret-safe logging.

Recommendations are review candidates, not licence-removal instructions. Missing activity, security entitlements, holds/archives, shared/resource accounts, group licensing and contract terms constrain interpretation. Test anonymisation, incompatible dates, missing plans, transient reports, redirect abuse, CSV injection and no double-counting.

### G — partner readiness/service health

Package A's customer import works without new permissions. Later GDAP inventory needs a dedicated partner-tenant context and an exact relationship/read-scope contract. A customer session must never be reused as an assumed partner session. Relationship existence is not proof the current engineer can access a workload.

Provide safe portal links first. API service-health reads propose ServiceHealth.Read.All separately; message-centre access is not bundled implicitly. An incident is contextual evidence, not an automatic explanation of every error. Test ambiguous customers, expired relationships, access-denied/partial responses, safe external links and sanitised diagnostics.

### H — joiner/mover/leaver

Begin with discovery and an explicit review checklist/proposal over current evidence. This does not satisfy the eventual controlled-write deliverable by itself. For each supported action, define the authoritative identity, exact permission, complete before-evidence, immutable preview, effect/readback, ambiguous outcome and recovery limit before enabling execution.

Graph account/group/licence/session actions are separate registrations, not an arbitrary workflow executor. Synced identities and dynamic groups are detected and directed to their authoritative management path. Exchange mailbox conversion/access changes remain manual under the existing decision. Licence removal waits for preservation/entitlement review. Avoid auto-purchases, destructive account deletion, wipes or new secret-custody features as defaults.

Named execution/permissions review remains required. Tests cover partial workflows, interruption between steps, changed access/identity, reconciliation, stale approval and no replay. Native and CLI authority remain explicit; the existing offline CLI is not silently made an unattended tenant writer.

### I — standards authoring

Reuse StandardsLoader, PolicyImporter/DevicePolicyImporter, lineages, naming and parameter validation. Edit only candidate supported settings with an explicit diff; keep current immutable releases intact. Compare configuration and assignment scope separately. Foreign IDs become unresolved inputs rather than copied values. Detect conflicting overlays instead of silently choosing precedence. Publishing a new catalogue/release is separately approved.

Tests cover old-catalogue byte identity, duplicate parameters, wrong object types, unsupported setting shapes, tenant-specific identifiers, conflicting overlays and deterministic exports.

### J — security context

Reuse application expiry/consent and existing sign-in/registration reports. Add selected incident/context and benchmark mapping only with exact source/licence/access status. Full Identity Protection risk APIs are not assumed available under Business Premium/P1. Preserve the current endpoint-security standard rather than replacing it with vendor defaults.

External Maester-style results, if included, use a strict independently versioned import envelope with source/version/time and untrusted/imported provenance. They cannot become complete before-evidence or authority to remediate. No new generic PowerShell runtime integration or incident-response writes.

## 7. Decisions and gates

1. **Approve this design before the detailed implementation plan for the new subsystems.** The existing Superpowers executing-plans method and initial GPT implementer remain the intended execution choice; no need to choose them again.
2. **Permissions are separate.** Reports.Read.All and ServiceHealth.Read.All are identified proposals, not granted or added now. Exact collaboration/GDAP/incident and lifecycle scopes must be presented with their route/action contracts. Do not request a blanket Directory.Read.All or FullControl grant to simplify development.
3. **Exchange writes remain manual.** This is the recommended default within the existing contract. Changing it is a separate safety decision, not implicit in accepting report integration.
4. **Use existing branches/process.** Main remains canonical source; each owned feature PR retains its applicable review/check gates. Future main merges or publication are not inferred from earlier candidate-specific acceptance.
5. **No live operations or new publication.** The programme's source/automated checks and unpublished candidate are distinct from human/live/release acceptance.

## 8. Verification and delivery standard

Every package must provide working native flows and shared engine behaviour, not only classes or scripts. Record source SHA, requirements covered, failing/passing behavioural regressions, required Windows/App/native/hosted/package results, original ZIP/hash, updated guide and remaining limits. Do not reuse an older package fingerprint for changed source. Reuse valid unchanged evidence instead of repeating historical audits.

The first implementation subproject is Package A. Its scope is concrete and has no new service permission. Packages B–J proceed in bounded slices as their contracts become reviewable; unrelated approved work can continue when an individual external gate is pending. No missing family is quietly dropped or declared complete because another package passed CI.

## Primary references and provenance

The preceding [vendor comparison](BuildStandard-Vendor-Capability-Review-2026-10-18.md) contains the successfully retrieved official vendor/API references and baseline inventory. Particularly relevant contracts:

- Microsoft [customer CSV export](https://learn.microsoft.com/en-us/partner-center/customers/see-your-customer-list).
- Microsoft [What If evaluation](https://learn.microsoft.com/en-us/graph/api/conditionalaccessroot-evaluate?view=graph-rest-1.0) and [limitations](https://learn.microsoft.com/en-us/entra/identity/conditional-access/what-if-tool).
- Microsoft [usage reports](https://learn.microsoft.com/en-us/graph/api/reportroot-getoffice365activeuserdetail?view=graph-rest-1.0) and [privacy settings](https://learn.microsoft.com/en-us/microsoft-365/admin/activity-reports/activity-reports?view=o365-worldwide).
- Microsoft [service-health issues](https://learn.microsoft.com/en-us/graph/api/serviceannouncement-list-issues?view=graph-rest-1.0).
- Existing repository `docs/integration/REGISTERED-EXCHANGE-EXTENSION-2026.10.15.md` and INT-088/121/123.
- Actual completed baseline: [issue #83 closeout](https://github.com/Willzy12h/M365-Buildstandards/issues/83#issuecomment-6205346426).

Superpowers using-superpowers and brainstorming were applied to this design. Cloud setup guidance was used to inspect existing isolation/tool availability; no environment installation or build readiness is claimed. No implementation workflow was run before design approval.
