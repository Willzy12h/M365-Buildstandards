# Scoped checks, report evidence and scripts — settled contracts

**Current status, 8 October:** decision PR #39 merged at `3f946ae` with green exact-head/merge Windows CI. INT-070/071 are implemented in #42/#44; INT-073 HTML inventory is implemented in #40; INT-074 portable hosting is implemented in #45. INT-072 is settled, with Claude's copy-only library #46 merged at `b755dbb` after independent review; its merge CI is running and integrated execution is still unimplemented. These status updates do not change the approved schema or close acceptance gates. The original prerequisite and hosting inspection below describe the pre-implementation baseline.

Decision-only PR #39, based on integration `8f0285a`. William authorised merging the current PRs on 8 October 2026 ("Merge all of these and continue until it's complete"). These decisions take effect when this PR is merged with passing exact-head checks; no dependent runtime implementation precedes that merge. These contracts need review and merge before dependent runtime implementation. The authorised master programme remains an implementation task; this document is its shared-contract prerequisite. No tenant action, permission grant, release/version change or publication is authorised here.

## Existing contracts to preserve

Reuse tenant collection, assessment, guarded Graph routes, licence services, existing report exporters and the owned Exchange/Purview read template. Preserve the Windows broker, protected MSAL caches and explicit session/resource boundaries. Reports and scripts cannot confer deployment authority or bypass unsupported write routes. All historical standard/evidence bytes keep their original meanings.

The historical Engineer Console reference has been located in an uploaded archive, not the current repository. Its source is a reuse reference; copying its authentication, unrestricted Script-Runner or Exchange mutators is outside this decision. Device diagnostics/repair, endpoint packaging and on-premises Exchange remain separate.

## INT-070 — Registered modules and scoped checks

Use one shared engine registry, consumed by desktop, CLI, reports and script descriptors. Registration is package-owned reviewed source, not an arbitrary endpoint/command supplied by the engineer. Each entry identifies:

- stable module ID and friendly name;
- existing catalogue area/control selectors and required collection keys;
- explicit extra dependencies for equivalence/licence/prerequisite evidence;
- report sections and supported source adapters;
- required permissions, read status limitations and implementation/live-validation state.

Resolve repeated control instances through the existing `ControlInstances` service. Dependencies include those used by assessment logic; do not infer that a control's main collection is sufficient. Missing/unknown dependencies produce unable-to-check results. Unknown module/area/control IDs are refused before collection. Selection is validated against the exact manifest-verified catalogue and reviewed client inputs.

`bdit check --area <area>` and `bdit check --control <id>` and the desktop **Check this** action use this registry and the same selected-control assessment service. Desktop live scoped runs collect only the resolved dependencies, then assess only selected controls. The existing offline CLI reads `--snapshot` and is labelled an offline filtered review, not a scoped live capture. No module adds collection/assessment logic in WPF.

All CLI commands retain their offline/read-only boundary and its existing no-authentication conformance tests. The new check command requires `--snapshot`; it does not authenticate, acquire a token or link Graph/setup/deployment/recovery executors. Desktop/CLI parity tests compare selected assessment results against equivalent synthetic input, while desktop collection tests separately assert actual dependency-only routes. A future live CLI boundary requires a separate reviewed decision and must not be smuggled in by weakening those tests. This intentionally records the existing CLI limitation rather than claiming it can collect live data.

Scoped result evidence uses a new strict wrapper, separate from ordinary full captures:

| Schema 1 field | Meaning |
|---|---|
| `schemaVersion`, `kind` | `1`, `scopedCheck`; unknown values/members are refused |
| `id`, `tenantId`, `accountObjectId` | GUIDs for live reads; historical account object ID may be null when the original source did not record it, explicitly unknown rather than invented |
| `sourceMode` | `liveScoped` or `historicalFiltered`; never implies a fresh session for historical evidence |
| `recordedAt`, `catalogueRelease`, `catalogueDigest` | UTC time and exact original catalogue identity |
| `profileId`, `clientScopeDigest` | Existing reviewed client-scope binding |
| `areas`, `controlIds`, `collectionKeys` | Canonical validated selection/dependency lists |
| `sourceCapture` | Optional historical source ID/digest reference; the original full capture's bytes are never edited |
| `capture`, `assessment` | Scoped collected data and selected-control assessment; no full assessment claim |
| `integrityDigest` | Canonical content digest excluding this field; integrity detection, not a signature |

Nested scoped captures have their own new IDs and always remain `Complete=false`, even when all selected reads succeeded. Historical filtering creates a separate derived capture and source reference; it never changes or overwrites the original full capture. The wrapper, UI, CLI, HTML/CSV/JSON outputs and any cited assessment explicitly say **Partial — selected controls only**. Scoped wrappers must not load as ordinary configuration snapshots. Workspace keeps them separate from the active full before snapshot; planning/execution refuses scoped evidence regardless of successful selected results. A fresh full capture remains required for writes. Cancellation records attempted failures and unattempted dependencies separately, never empty-success.

Required tests: registry/dependency completeness, actual requested-route counts (no unrelated reads), desktop/CLI selected-control parity, repeated controls, missing dependencies, cancellation, identity/catalogue mismatch, unknown IDs, strict readers and deployment refusal. ARCHITECTURE documents adding an entry and its dependency/test obligations.

## INT-071 — Separate report evidence and honest row states

The full HTML configuration inventory renders the existing `TenantSnapshot` without altering its schema: observed tenant/domain identity, licences, collection status, all captured objects/properties/configuration and returned assignments. It is a current/historical configuration inventory, separate from assessment, standard-definition documents, drift and upgrade impact. Missing collections/properties and incomplete details remain explicit. Never imply that an empty result proves absence after a failed/partial read.

Large user/device/log/mailbox reports use a new strict `reportEvidence` wrapper rather than extending bounded configuration or Exchange schema1. Existing Exchange captures continue to mean exactly what their reader/assessment defines. New report imports cannot satisfy full deployment evidence.

| Schema 1 field | Meaning |
|---|---|
| `schemaVersion`, `kind`, `id` | `1`, `reportEvidence`, GUID |
| `reportId`, `reportSchemaVersion` | Registered report and its strict row schema version |
| `tenantId`, `accountObjectId`, `resource` | Verified tenant/account and Graph/Exchange/Purview resource context for live reads; historical account object ID may be explicitly unknown when absent from source |
| `sourceMode`, `startedAt`, `endedAt` | `live` or `historical`; UTC timing, never fabricated freshness |
| `toolkitVersion`, `moduleVersion`, `sources` | Exact adapter/tool/module provenance; no tokens or signed service URLs |
| `parameters` | Validated non-secret filter/date-range values; requested range is distinct from available returned coverage |
| `status`, `limitations` | Explicit overall status/reasons, including retention, licence/access and bounds |
| `sections` | Registered section IDs with their own status, safe error/limitations and strict typed rows |
| `integrityDigest` | Canonical content digest excluding this field |

Allowed read states: `Collected`, `Partial`, `Failed`, `NotAttempted`, `Cancelled`. Only successful completion of every required page/section may produce `Collected`. A successfully checked empty section is distinct from every other state. Per-row unreadable fields have nullable typed values and explicit read status; zero/false/empty values are never invented. Preserve genuine raw `Unlimited` quota values separately from parsed numeric capacity. Pagination, date-range retention limits, section errors and row caps all produce truthful partial status.

Reports share consistent filters, identity-first rows, timestamps/provenance, visible limitations and HTML/CSV export, preserving existing JSON/Excel routes. Rows join by exact user/object identity, never display name. Shared/resource mailboxes remain separate objects; do not duplicate user identities through name-based joins. No secrets, recovery codes or unnecessary contact detail are exported.

### First report registrations

1. Full configuration inventory from existing stored/live captures, including returned assignments and unreadable collection/detail explanations.
2. Users/licences with observed assigned SKU/service-plan details and per-user read status. The current configuration `users` selection does not return assigned licences/service plans; use the reviewed existing licence service or explicit registered reads, not guesses from that capture.
3. Intune devices with ownership/type, OS/version, compliance and last sync. The current catalogue device selection does not return every required field; extra report reads stay outside historical catalogue bytes.
4. MFA registration, sign-ins and directory audit logs. Registration does not prove enforcement or successful use; generic method labels do not identify a physical device. Date range, returned coverage, available retention and incomplete pagination are visible.
5. Exchange mailbox inventory/statistics/quotas, mailbox/calendar permissions, message trace and supported unified-audit queries through the reviewed read-only adapter. Each query has explicit supported module/RBAC/runtime, bounds, identity verification and no integrated mutation.

### Mailbox capacity and entitlement

Report observed mailbox identity/type, primary SMTP, size and raw warning/send/send-receive quotas; archive size/quota is separate. A **currently configured 100 GB primary quota** list is derived from observed primary quota values, not licence display names. Business Premium alone proves neither configured capacity nor 100 GB entitlement.

Entitlement is a separate documented evaluation using actual assigned products/service plans and current official Microsoft entitlement rules. Save the rule/source/version/date in the report provenance. Before implementation claims eligibility, verify those rules against current official documentation. Statuses are `Eligible`, `NotEligible`, `UnableToCheck`; combination results distinguish eligible-but-lower-quota and configured-but-entitlement-unconfirmed. A missing or failed service-plan read cannot become NotEligible. Distinguish primary mailbox, archive, Recoverable Items and local OST/PST limits. Numeric comparisons must retain the service's original quota strings and unit interpretation; do not interpret `Unlimited` as exactly 100 GB.

### Access changes

Prefer existing authorised `User.Read.All`, `Organization.Read.All` and `DeviceManagementManagedDevices.Read.All` for relevant Graph reads. MFA registration/sign-in/directory audit adapters may require **new `AuditLog.Read.All`** plus report-role/licence prerequisites. Verify exact endpoint-specific least-privilege requirements against current Microsoft documentation before implementing the registration. If an endpoint instead needs another scope, present that addition separately; this decision does not grant a broad fallback permission.

Document the exact routes, required read scopes, reason, affected assessment registration(s), role/licence requirements and consent impact in APPLICATION-SETUP. New access is proposed and ungranted until William approves live consent. Do not silently expand catalogue-wide authentication scopes or request unrelated report permissions just by opening a page. Graph registrations and Exchange/Purview delegated roles remain separate.

Required tests: identity joins/duplicates, missing values, failed/partial/cancelled pagination, retention/date ranges, raw Unlimited and non-100 GB values, licence-versus-configured-capacity combinations, strict historical schema compatibility, hostile HTML/CSV values and refusal to import report evidence as complete before evidence.

## INT-072 — Reviewed script manifests and bounded read-only execution

Scripts live under `scripts/<category>/` in the reviewed package. An explicit package registry binds each manifest and script content digest; no arbitrary path or engineer-supplied command is executable from a form. Manifest content is versioned, strict, and reviewed alongside the script.

| Manifest schema 1 field | Meaning |
|---|---|
| `schemaVersion`, `id`, `name`, `category`, `description` | Version and stable friendly identity/purpose |
| `mode` | `readOnly` or `copyOnlyChange`; no integrated-change mode in this release |
| `scriptPath`, `scriptSha256`, `supportedRuntimes` | Contained category path, pinned bytes and individually supported PowerShell 5.1/7 runtimes |
| `modules`, `roles`, `scopes`, `resources` | Exact prerequisites/access and Graph/Exchange/Purview separation |
| `parameters` | Stable parameter names, supported types, required/default/help, validation bounds/allowed values |
| `outputSchema`, `outputSchemaVersion`, `limits` | Strict result shape, file/row/time/output/process bounds |
| `prerequisites`, `liveStatus`, `limitations` | Actual readiness and source/synthetic/native/live evidence status |

Supported parameter types initially: string, boolean, integer, date/date-time, GUID, enum, bounded arrays of these and a controlled local output directory. Identity/date/unit values use shared typed validation. No credential/token/secret parameter type, arbitrary code, arbitrary executable, execution policy, module installation or unbounded command text is accepted. Forms show purpose, target, inputs, access and prerequisites before Run. Defaults remain visible and reviewable.

Execution uses an owned bounded process for a supported runtime, cooperative cancellation plus bounded termination, bounded UTF-8 result/error streams and secret-safe local files. Script files/manifest digests and contained paths are checked before launch. Parameters are bound as data, never `Invoke-Expression` or an interpolated `-Command`. No tokens/caches are copied into script arguments, files, outputs or handoff evidence. No cache/backend replacement is introduced; connected contexts are shared inside engine services where valid. A separately authenticated PowerShell module session must verify tenant/account and remains an explicit resource boundary, not assumed Graph-token reuse.

Copy script emits a reviewed standalone script with safely encoded typed values (or safe literal generation tested with hostile inputs), correct runtime/module requirements, target and read-only/change purpose. It contains no credentials, installation, control bypass or upload. Script/parser inspection is not execution acceptance; output existence/exit zero alone is not successful checking. A strict output envelope verifies the expected tenant/account/report schema and explicit completion/read status before results are accepted.

No alternate PowerShell/runtime host may be chosen to evade application control. If the approved runtime/module is unavailable or blocked, explain the prerequisite/security-owner approval route. Respect supported PowerShell version per script; never claim all scripts support both versions merely because both parsers accepted them.

Read-only seeds: licence/device/MFA reports, mailbox/calendar permissions, primary 100 GB quota audit, message trace and supported unified-audit queries. Reuse registered report/collection/evaluation services where valid rather than creating competing eligibility or identity logic. Script live status stays unverified until William records acceptance.

### Change proposals stay copy-only

AutoMapping removal/re-grant and mailbox quota changes have **Copy script, no Run**. Existing integrated Exchange writes remain blocked. Both the runner and UI enforce this regardless of a form/input choice; a generic host is not an execution contract. No automatic retries/replay, licence assignment/purchase, archive expansion or quota increase is introduced.

A proposal must name exact mailbox/object, observed current values/permissions, proposed changes and consequences; require verified entitlement before recommending 100 GB; retain before-evidence, tenant/account context, approval-needed wording and honest recovery limits. Unknown/partial reads produce a blocked/inert proposal, not a runnable guess. A failure between AutoMapping permission removal and re-addition is ambiguous consequential work, not automatically recoverable. Integrated mutation requires a later separately reviewed contract and William's required approval.

Required tests: strict manifests/contained paths/content pins, typed defaults/validation, hostile/injection-negative inputs and Copy output, PowerShell parser/runtime claims, cancellation/timeouts/bounds, wrong identity/schema, partial output and unconditional refusal to Run copy-only changes. No test requires live tenant access.

## INT-074 — Portable offline CLI through the existing application host (settled by PR #39)

Settled when PR #39 merged; the implementation is recorded as INT-079 in the [decision log](DECISION-LOG.md). The rest of this section is the contract as approved, so its present tense describes the package at that time.

Inspection of `build/Build-Portable.ps1` establishes that the ZIP deliberately ships one executable and excludes the developer/CI CLI. The master programme's portable `bdit` requirement is therefore not yet implemented. Do not advertise the current ZIP as providing it or ask field engineers to install the SDK.

Hosting contract:

- Keep one application executable. Add a `bdit.cmd` launcher that invokes that same executable with an explicit `--cli` discriminator, passing the remaining arguments unchanged. Include the existing CLI assembly as a managed dependency, with no second apphost executable. Preserve its existing offline/read-only conformance tests and forbidden types.
- Dispatch before creating the desktop `Workspace`, loading any connection/session or initialising broker/cache services. CLI mode must not open a window or initialise a desktop authentication context. It returns the existing CLI's actual exit code; exceptions must not fall back to desktop startup or a different process host.
- Support console invocation and redirected stdout/stderr with a bounded Windows console bridge. Preserve already redirected handles; attach to the parent console only when needed. Do not allocate an unexpected interactive window or silently discard output. Test pipes, file redirection, argument paths containing spaces, unsuccessful commands and help against the extracted self-contained package.
- This is a convenience wrapper for offline commands, not a new authentication or write route. A blocked executable remains blocked. The launcher must never select another host, change execution policy or bypass application control. Documentation retains the security-owner approval and independently trusted fingerprint route.
- Extend package validation to assert exactly one executable, presence of the launcher/CLI dependency, real synthetic inventory/job/report command results, truthful failures and absence of desktop session/cache initialisation. Existing desktop startup/shutdown checks remain required.

This changes the package/hosting contract and requires review and merge before implementation. It does not authorise a version bump, publishing, a second executable or live CLI authentication. The reusable publisher remains Claude's area; coordinate changed file counts and fresh candidate evidence rather than editing old pins.

## Delivery and review gates

These are shared contracts, not implementation or acceptance evidence. Merge after independent review/exact checks under HANDOVER's delegation; then implement on successive claimed branches and extend the existing capability matrix, architecture and live run sheet. Workstreams retain the master's order and candidate milestones. Source implementations must not imply new live access has been granted. New candidate version authority, main promotion, tags, releases and publication remain separate. Keep Claude's reusable publisher and tracker ownership intact.
