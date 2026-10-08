# Read-only execution and Exchange report evidence — proposed INT-088, PR #61

Status: decision-only, pending independent review and merge. No dependent source is implemented by this PR. This refines settled INT-071/072 and the merged INT-080 Copy library; it does not reopen Exchange integrated writes. Baseline: integration `14e62275ca212b6f34ecd3b17d676f2445e39845`. Review the actual strict readers, not just the earlier high-level contract.

## Why this decision is needed

The registered report reader presently accepts only `reportEvidence` schema 1, resource `Graph`, and the exact Graph routes/sections/row types. Its evidence store is useful but cannot accept arbitrary CSV or a PowerShell exit code as a collected Exchange report. The bounded configuration `ExchangeCapture` schema 1 has separate meaning and a 2 MiB limit; expanding it would silently change historical evidence. The merged ten schema 1 script items emit projected CSV, not a strict execution envelope. Their copy wrappers do not enforce a process timeout. They therefore remain Copy/Save, with no Run.

#58 supplies a pure mailbox capacity evaluator, not collected evidence or a complete report. #59 protects the existing fixed configuration-capture account boundary, not a general runner. This decision must merge before a new execution manifest, strict Exchange report reader or general runner is added. #55 separately governs experimental tenant changes and is not permission to run Exchange writes.

## Compatibility and ownership

- Preserve the existing Graph report reader/store, Graph report IDs, historical files and configuration/Exchange captures byte-for-byte. Never upgrade or relabel an old CSV as a complete live report. Strict readers refuse other kinds and versions.
- Introduce a separate `exchangeReportEvidence` schema 1 reader and immutable tenant-partitioned `exchange-report-evidence/<id>.json` records using the existing atomic/create-only evidence conventions. Backups/transfers may carry this directory only through explicit protected evidence handling; support bundles do not silently include mailbox/user rows. Import verifies containment, bounds, tenant, exact registered row schemas and integrity. Source claims/digests are not signatures.
- Keep schema 1 script manifests and copied bodies supported as manual items. New runnable entries use manifest schema 2 and a separately pinned runner-output contract. Do not silently enable Run for schema 1. The existing library remains one registry; version dispatch is explicit and strict, not a second catalogue/cache. The body and manifest pins are updated only for newly reviewed bytes.
- Shared collection, strict readers, identity checks, rendering and evaluation stay outside WPF. CLI remains offline: it may copy, import, inspect and export evidence; this decision adds no CLI sign-in or execution. Desktop Run is only for registered read-only adapters.

## Execution manifest schema 2

Schema 2 retains the existing typed parameters, rules, description, prerequisites, module/runtime/role requirements, read-only/change mode, hashes and limits. It adds a required `execution` member with:

| Field | Contract |
|---|---|
| `adapter` | Registered `exchangeOnline` or `purview` read-only adapter; never an arbitrary executable/resource/command |
| `reportId`, `reportSchemaVersion` | Exact registered report and row version; mismatches refuse before launch |
| `outputKind`, `outputSchemaVersion` | `scriptReadResult`, `1`; the reviewed body returns strict typed sections through the owned wrapper |
| `runnerTemplateSha256` | Pin of the owned wrapper bytes, verified with the catalogue/body before launch |

Copy-only change entries cannot have executable permission: Run refuses their mode even if a modified form/manifest asks otherwise. Schema 2 admission tests must explicitly refuse `CopyOnlyChange` execution. Initially the library's schema 1 entries are manual; runnable replacements are separately reviewed schema 2 entries with strict output and honest unverified live status. Existing copied output cannot imply runner acceptance.

Graph licence/device/MFA report forms reuse the registered Graph engine and current verified connection, rather than sending Graph tokens to PowerShell. A Graph PowerShell resource or manifest adapter is not admitted by this decision. This still permits read-only seed forms to invoke shared engine reporting where valid, with the same review and output semantics. It does not allow arbitrary uploaded scripts or editable command text.

## Verified context, authentication and prerequisites

Run requires a current verified tenant GUID, known signed-in account object GUID and confirmed valid UPN, resource/adapter, manifest/body/wrapper pins, exact typed parameters and a newly generated run GUID. Historical evidence or a client profile alone is not a current connection. The preview shows tenant/account, read-only purpose, inputs, module/role requirements and separate Microsoft authentication boundary; the engineer confirms before launch. Changing any reviewed value invalidates that approval.

The existing Graph broker/cache implementation remains the sole Graph authentication implementation. Exchange/Purview use their module's supported authentication in a fresh owned process, with the confirmed account hint. No Graph token/cache/credential is copied into an argument, request file, script, transfer or report. Microsoft CA/MFA/consent may prompt; a Graph sign-in is not proof of Exchange access. The initial adapter does not promise to share another process's module session. A future persistent module process requires its own reviewed lifecycle contract.

Before every registered read and after the final read, the owned wrapper requires exactly one usable connection for the expected resource, matching observed tenant GUID and observed UPN (trimmed, ordinal case-insensitive). Missing identity, ambiguity, expiry, changed account/tenant or resource failure stops collection. No interactive retry during a collection and no automatic rerun. The module's own read retries cannot justify writes. Purview has its own resource validation and cannot borrow Exchange success.

The report records both the initiating Graph-verified account object ID and the UPN actually observed by the module, with those distinct provenance labels. It must not claim the module independently returned/verified an Entra object ID when it did not. A caller cannot set initiating identity from arbitrary imported output. Offline evidence records historical source claims and is never treated as a current connection.

Preflight checks the installed approved runtime and supported module version without installing, importing for sign-in or altering controls during source tests. Report command/RBAC requirements are exact per adapter; role display names alone do not prove effective permission. Missing/blocked prerequisites produce actionable unavailable/failed states. New Graph scopes, module commands, RBAC or registrations are documented and presented for William's review before any live consent; source development grants nothing. Existing authorisation is reused where valid.

## Owned process and safe data binding

The parent generates a fresh contained staging directory with create-new request/result names. Launch only the registered approved runtime with `UseShellExecute=false`, `ArgumentList`, `-NoProfile`, and `-File` of the verified owned wrapper. Use no `-Command`, shell command string, `Invoke-Expression`, execution-policy override, module installation, alternate host after an application-control block or executable path supplied by a form. Values are typed JSON data in the request, read strictly and bound with parameter splatting; they never become script source. Copy produces separately reviewed safely quoted literals using the existing literal rules.

The request records run ID, expected tenant/UPN, adapter/report/schema, exact hashes and validated non-secret inputs. It contains no cache, token or credential. The parent refuses unexpected request fields/types. The child cannot choose another output directory or schema. Recheck the body/wrapper pins immediately before launching; never execute an unverified file. Private report outputs stay local in the toolkit's protected evidence workflow, with the same sensitive-data handling as other evidence; no upload is introduced.

Bound every process to the manifest timeout (absolute maximum 1,800 seconds), output envelope (32 MiB UTF-8), depth (48), and report row limit (maximum 10,000 per registered section; lower per-item limits remain authoritative). Bound progress stdout to 1 MiB and diagnostic stderr to 256 KiB. Bounds terminate only the owned process and descendants. No raw module errors/streams enter chat, committed fixtures or support bundles; retain only registered safe reason codes and sanitised local descriptions. Never assume redirected streams cannot contain secrets.

Cancellation first requests cooperative stop, then terminates only the owned process tree after at most five seconds. Timeout, forced kill, output limit, missing/invalid result or process failure cannot be Collected. The parent records a bounded Failed/Cancelled result with explicit reason and no unverified child rows. Verified partial sections can be retained only when a complete strict envelope was returned before termination and passed every identity/pin/schema check; their status remains Partial/Cancelled. Leftover temporary files are never imported on a later run. No automatic retry, output replay or resume is introduced.

## Child output and parent evidence

The child returns a `scriptReadResult` schema 1 envelope. Required fields:

| Field | Meaning |
|---|---|
| `schemaVersion`, `kind`, `runId` | `1`, `scriptReadResult`, exact parent-issued GUID |
| `scriptId`, `manifestSha256`, `scriptSha256`, `runnerTemplateSha256` | Exact registered executed identities/pins |
| `adapter`, `reportId`, `reportSchemaVersion` | Exact reviewed resource/report/row contract |
| `tenantId`, `observedAccountUpn` | Actual module connection identity, matching parent expectation |
| `startedAt`, `endedAt` | UTC run timing, end not before start |
| `runtimeVersion`, `moduleVersion` | Actual supported loaded/runtime provenance |
| `parameters` | Exact normalised typed inputs; unknown/missing/changed parameters refuse |
| `status`, `limitations`, `sections` | Explicit read states, safe limitations and only strict registered typed rows |

Unknown/duplicate fields, null required values, bad identity/dates/pins, unsupported resources/versions, duplicate row identities, unexpected sections and invalid UTF-8 refuse. Exit zero or file existence is not successful checking. A non-zero exit cannot be promoted to Collected even with a plausible output. No caller-controlled URI, executable, raw `AuditData`, raw Graph/Exchange object or module credential/session object is accepted.

The parent validates that envelope against the actual reviewed run, then seals a new `exchangeReportEvidence` schema 1 record with required ID/run ID, report/schema, tenant, initiating account object ID and observed module UPN, `resource` (`ExchangeOnline` or `Purview`), source mode, UTC interval, toolkit/adapter/module/runtime versions, exact script/manifest/wrapper digests, safe typed parameters, registered source commands, status/limitations/sections and canonical integrity digest. Registration fixes allowed parameters, source commands, sections and row schemas; importing does not invent a registry from a manifest supplied in the file. Historical imports retain their original timing/source claims and cannot become authenticated live runs.

Reuse `Collected`, `Partial`, `Failed`, `NotAttempted`, `Cancelled` states. A section is Collected only after all required reads/pages completed within bounds and every row's required fields were read. Successful empty is only an actually completed empty query. Failed/unattempted sections contain no rows. Partial/cancelled sections identify the bound/error and attempted coverage; unavailable fields are nullable with per-field/row read state and reason. Overall state cannot exceed section states. The record is created atomically and never overwrites another ID. Fingerprint verification detects change, not truth or Microsoft acceptance. No report record can load as full configuration before-evidence or authorise deployment.

## First Exchange registrations and mailbox identity

Implement successive read-only registrations: mailbox inventory/statistics/quotas including archive, mailbox permissions, calendar permissions, message trace and supported unified-audit query. Each has an exact source-command list, resource/runtime/module/RBAC prerequisites, bounded parameters, registered row type, limitations and live-unverified status. Service-imposed date/retention/page limits are preserved; do not assume a 31-day UI date range proves available retention. Unified audit output is a whitelist projection of known fields, never raw nested AuditData. No mailbox content, message body, contact/recovery detail or secrets are included.

Mailbox inventory uses exact non-empty Exchange mailbox GUID as mailbox identity, and separately records nullable `ExternalDirectoryObjectId`, display name, primary SMTP and actual mailbox type. Shared/room/equipment remain distinct mailbox objects, even when there is no joinable user/licence row. Permission identities are exact mailbox/object plus principal ID when the service provides it; unresolved principal names are explicitly unresolved and not invented GUIDs. Name/SMTP/display-name equality cannot establish ownership or a licence join.

The mailbox row records raw primary total size, raw IssueWarningQuota, ProhibitSendQuota and ProhibitSendReceiveQuota, plus archive existence/read status, raw archive size and raw archive quota separately. Each value has explicit read status and nullable raw text; Unlimited remains Unlimited, failed read never becomes zero. Archive absence may be reported only from a successful explicit archive-state read; a failed archive request is unknown. Primary mailbox, archive and Recoverable Items remain separate; this report makes no OST/PST claim. Preserve enough exact identity to deduplicate repeated objects without collapsing separate mailboxes.

Capacity evaluation reuses #58 after its independent review/merge. It interprets only supported raw byte forms, reports currently configured 100 GB independently of entitlement, and cites exact assigned-user Graph report ID/digest/time plus documented rule version/source date. Exact ExternalDirectoryObjectId joins a unique actual licence-user ID; no UPN/name/SKU-name guessing. Missing, partial, stale or unjoinable source remains explicit. Derived analysis records each source identity/digest/capture interval and its limitations; it does not edit either original report or pretend non-simultaneous observations are fresh. A complete entitlement claim requires the evaluator's explicit supported service-plan evidence; unknown variants are UnableToCheck. Reports cannot turn this into an approved quota change.

## UI, exports, manual changes and acceptance

Reports and Scripts use one consistent tenant/identity/mode banner and shared read services. The UI distinguishes no connection, historical/offline, successfully empty and could-not-check; disabled Run explains the exact prerequisite and next step. Read-only Run is experimental/live-unverified until William accepts its adapter, and is off by default for production use. Its opt-in is visibly distinct from the write gate; it cannot authorise deployment or suppress Microsoft's prompts. Imported historical output remains inspect/export only.

HTML/CSV/JSON and existing Excel routes render registered rows, filters, UTC times, source identity, read states and limits. Neutralise HTML/CSV formula hazards using existing exporter rules. Exact IDs/raw quotas remain copyable in details/export; friendly names lead. Never describe partial output as tenant-wide coverage or completeness. Automated tests use synthetic modules/processes and native UI harness; source/parser/synthetic/native/human/live evidence are separate.

AutoMapping re-grants, quota updates and all Exchange changes remain **Copy script, no Run** at every service/UI boundary. A proposed manual action requires exact current evidence, identity, operations/consequences, verified entitlement when proposing 100 GB, durable intent/approval wording and truthful uncertain-write recovery. Incomplete evidence yields an inert blocked proposal. No automatic retry/replay/assignment/purchase/archive expansion is added. Integration of mutations would require a separate reviewed execution contract and William's approval; this runner is not that contract.

## Required implementation proof and delivery

Failing regressions must cover wrong/missing/changed tenant and account before reads, resource separation, strict version/kind/pin/parameter/row identity, failed/partial/cancelled/empty queries, duplicate/unmapped fields, bounds/timeouts/owned-process termination, child-output forgery, hostile typed input/path/literal cases, unconditional copy-only Run refusal, immutable evidence and refusal as deployment before-evidence. Parse and synthetic execute each claimed PS 5.1/7 pairing without loading Microsoft modules. Native harness checks forms, confirmation, disabled reasons, partial output and export at all three sizes. No live operation is authorised by these tests.

After this decision merges, use separate claimed source PRs: strict models/reader/store; owned runner and fake-process/module regressions; registered Exchange adapters; Reports/Scripts UI integration and capability evidence. Coordinate #55/#56/#58/#59 rather than editing their branches or bypassing reviews. Publisher remains Claude's area. Each candidate needs exact source/Windows/package/hash proof and an unrun live checklist. Do not mark this document or green synthetic CI as implementation, live acceptance or permission to publish.
