# Architecture and developer guide

## Stack

- .NET 10, C# 12, WPF (`net10.0-windows`), published self-contained for `win-x64`. No installer, no elevation, no machine-wide changes.
- Dependencies: `Microsoft.Identity.Client` (MSAL) and `System.Security.Cryptography.ProtectedData` (DPAPI). Everything else is the base class library: `System.Text.Json` for models and canonical hashing, `System.IO.Compression` for XLSX and CSV bundles.
- No DI container, no ORM, no database. Evidence is versioned JSON under `data\`.

## Projects

| Project | Responsibility |
|---|---|
| `BDIT.TenantToolkit.Core` | Models (profile, standard, snapshot, assessment, deviation, plan, run, mapping, drift, session), the `IGraphClient` contract, canonical JSON (`CanonicalJson`), the safety rules (`ConditionalAccessSafety`, `WritePayloadGuard`), paths, settings, logging and secret scrubbing. No I/O beyond files and no network. |
| `BDIT.TenantToolkit.Graph` | `MsalAuthenticator` (Windows WAM pop-up with system-browser fallback, tenant-pinned authority, DPAPI cache), `GraphRouteAllowList`, `GraphClient` (v1.0/beta roots, bounded read retries, no write retries, payload guard), `TenantConnectionService` (verify organisation and operator, access check). |
| `BDIT.TenantToolkit.Engine` | `StandardsLoader` and `StandardsManifest`, `TenantCollector`, `AssessmentEngine` and `NameResolver`, `DeploymentPlanner`, `DeploymentExecutor`, `DriftAnalyser`, `EvidenceStore`, reports (`HtmlReports`, `MarkdownReports`, `TabularReports`, `CsvWriter`, `XlsxWriter`, `ReportExporter`). |
| `BDIT.TenantToolkit.App` | WPF shell. `Workspace` is the composition root and state machine; page view models are thin; views are XAML only. |
| `BDIT.TenantToolkit.Tests` | xUnit tests around every safety boundary with a scripted `FakeGraphClient` and a scripted `HttpMessageHandler`. |

Dependency direction: App → Engine → Graph → Core. Tests reference Core, Graph and Engine.

## Integrated engineer workflow (1.1 preview)

`Graph/Setup/ApplicationSetupService` owns a separate, in-memory privileged setup identity and narrow transport. It resolves delegated scope IDs, previews two registrations, creates approved registrations/service principals or repairs explicitly selected dedicated tool IDs and validates actual configuration, consent and direct engineer assignment. Consent uses Microsoft's browser flow; no directory roles, grants or secrets are written by the service. Normal assessment/deployment routes cannot create applications. See [application setup](APPLICATION-SETUP.md).

`Engine/Identity/AccountResolver` resolves explicit UPN/object-ID/display-name searches. `TenantProfile.ExclusionAccounts` stores tenant-bound identity, purpose, reason, time and selecting operator. Dedicated emergency IDs remain distinct from additional approved exceptions; both are merged into reviewed CA candidates with the delegated creator. Profile and exclusion changes invalidate plans.

The executor reloads complete, integrity-checked before evidence plus current mappings and deviations, rebuilds reviewed write rows, freezes inputs and checks durable state before each request. Plans bind profile, operator, app, standard file/content, snapshot, mappings and deviations. Updates retain ownership, inactive-state and drift checks. Write acceptance and configuration verification are separate; historical records without acceptance display Unknown without changing their original digest.

`Workspace` serialises operations, owns cancellation and awaits active work before async shutdown. Policy writes stop at safe action boundaries; setup completes the current app/SP pair and after evidence. Exports run on a worker task. The shell shows tenant/account/access, including setup and broader shared-fallback token scopes. See [design system](DESIGN-SYSTEM.md).

## Graph routing

`Engine/Recovery/RecoveryService` derives single-use recovery plans from intact deployment records and fresh complete evidence. A dedicated Graph recovery method permits bodyless DELETE, guarded restoration PATCH or state-only CA disablement on three supported v1.0 policy routes plus the beta device-configuration route used for BitLocker. Normal writes do not gain general deletion capability. Exact returned IDs are saved before ownership/readback steps. `EvidenceStore` shares a per-tenant process lock between policy deployment and recovery; both retain ambiguous outcomes. See [recovery](RECOVERY.md).

`Engine/Assessment/LicenceInventoryService` performs paginated read-only subscription/user capture and derives counts, searchable assigned users and conservative direct-user scope checks. `OverviewViewModel` exposes these without inferring unsupported group, guest, role or device eligibility. Captures remain tenant-local. See [licensing](LICENSING.md).

- A collection in the standard declares `api` (`v1.0` or `beta`), `path`, `scope`, optional `write`, `assignments`, `children`, `relationship`, `singleton`, `nameProperty`.
- `GraphRouteAllowList.FromStandard` turns collections into read routes and, where `write` is present, write routes. Six fixed diagnostic routes are added (`/organization`, `/me`, `/roleManagement/directory/roleAssignments`, `/subscribedSkus`, `/users`, `/groups`).
- `GraphClient` refuses any path that is not on the list, and any write that is not the collection root (POST) or root/`{guid}` (PATCH) of a writable collection.
- Beta and v1.0 are separate roots chosen per call. `@odata.nextLink` must keep the same host, API version and route; otherwise pagination stops with an error and the collection is marked incomplete.
- Reads retry up to five attempts on 429, 500, 502, 503, 504 and transport errors, honouring `Retry-After` up to `maxRetryAfterSeconds` (300 by default). Writes are never retried; timeouts and gateway errors surface as `AmbiguousWriteException`, which stops the run.
- 403 becomes `PermissionException` with the collection's declared scope as the hint. 401 triggers one silent token renewal, then `AuthenticationRequiredException`; there is no interactive prompt mid-operation.

## Catalogue schema (version 3)

```json
{
  "schemaVersion": 3,
  "release": "2026.09.3",
  "status": "...", "description": "...", "publishedOn": "...", "owner": "...",
  "collections": { "<key>": { "api", "path", "scope", "write?", "assignments?", "children?", "relationship?", "singleton?", "label", "nameProperty?" } },
  "parameters": [ { "key", "label", "type": "guid|guidList", "required", "description" } ],
  "controls": [ {
    "id": "CA-001", "name", "category", "severity": "Critical|High|Medium|Low|Informational",
    "purpose", "desiredState", "businessImpact", "engineerAction", "documentationNotes",
    "licence": { "servicePlans": [ "AAD_PREMIUM" ], "note" },
    "collection": "<key or null>",
    "assessment": { "mode": "settings|manual", "manualInstructions", "ignoreProperties": [], "partialMatchThreshold": 0.5 },
    "expectedProduction": { "state", "assignment", "notes" },
    "safeDeployment": { "state", "assignment", "notes" },
    "payload": { ... Graph body template with {{parameter}} placeholders ... },
    "dependencies": [], "references": { "microsoft", "cis", "cyberEssentials" }
  } ]
}
```

Validation rules enforced by `StandardsLoader.Validate`: schema version, unique control IDs of the form `AAA-000` or `AAA-BBB-000`, known collections, `api` without a version in `path`, a payload only for settings-mode controls, a name property in every payload, no `assignments` in any payload, and for Conditional Access recipes `safeDeployment.state == "disabled"` and no payload state other than `disabled`.

Integrity: `standards\manifest.json` lists a SHA-256 digest per release file. `StandardsLoader.Load` refuses to load a file that is unlisted or modified. This is an integrity digest; it is not a signature. Release lineage (`standards\lineage\lineage-<target>.json`, INT-051/057) records how each control of an earlier release relates to the target; it pins every catalogue by its manifest digest, is listed in `standards\lineage\manifest.json`, and is used only to explain earlier-release ownership records during assessment. The tool is internal and unsigned; publish the ZIP checksum and, where application control is in use, allow the executable by path or hash.

## Assessment

`AssessmentEngine.Assess(snapshot, standard, profile, mappings, deviations)`:

1. Tenant binding is checked across every input.
2. Per control: not-applicable deviation, licence requirement (from the `licences` collection), manual mode, collection usability, template resolution (missing profile parameters produce *Requires manual review*, never *Missing*).
3. `FindCandidates` compares the resolved recipe with every object in the collection: identity and metadata keys are ignored, `@odata.type` and `grantControls` act as prefilters, every remaining leaf is compared with `CanonicalJson.IsSubset`. Objects that match fully, match at least the partial threshold, or share the standard name are returned with property-level differences and enforcement state.
4. Status: `Compliant` (settings match and enabled or assigned), `SettingsMatchNotEnforced`, `PartialMatch`, `Missing`, `UnableToAssess`, `RequiresManualReview`, `LicenceUnavailable`, `NotApplicable`, `CompliantWithDeviation`. A deviation never overrides `UnableToAssess`.

## Registered partial checks

`CheckSelection.ForArea` and `ForControl` resolve the verified catalogue's existing control instances and required collections, including equivalence/licence dependencies and schema-specific assessor dependencies. Selection pins the catalogue, reviewed client scope and actual registered definitions; unknown selectors or changed routes are refused before collection. New modules belong in a new catalogue release, with their dependency routing covered by collector and assessor tests; arbitrary URLs are not modules.

`TenantCollector.CollectScopedAsync` reads only those dependencies. `AssessmentEngine.AssessSelected` evaluates only the chosen controls. Both always mark results partial, including an area containing every control or a filter over complete historical evidence. Missing, interrupted and failed reads stay explicit and cannot be reported as empty successes.

`ScopedCheckService` binds live results to the verified tenant/account, or derives historical reviews without inventing an account or refreshing capture time. The INT-070 `scopedCheck` wrapper is stored immutably under `data/tenants/<tenantId>/scoped-checks/<id>.json`, outside the snapshot/assessment/plan stores. Its strict bounded reader validates identity, registered dependencies, partial flags and digests. Digests detect changes, not authenticity. Ordinary snapshot readers reject wrappers; deployment still requires complete ordinary before-evidence. Desktop and offline CLI hosts must use this service rather than filtering a completed live capture and calling it scoped collection.

The Assessment page exposes live scoped reads and explicitly selected historical filtering without replacing the full workspace assessment, snapshot or plan. Its requirement picker uses the shared control-instance registry; older releases reuse `ControlAreas.For` without changing catalogue bytes. Offline `bdit check --snapshot <file> --area <area>` (or `--control <id>`, with optional `--exchange-snapshot <file>` as for `report`) writes the strict historical wrapper to standard output; it does not write installation evidence, authenticate or acquire live data. The portable CLI host remains a separate INT-074 implementation gate.

## Planning and execution

`DeploymentPlanner.Build` produces rows with actions Create, Update, NoChange, Blocked, Conflict, Drift, Manual, Deviation. Conditional Access payloads are forced to `disabled`, the emergency accounts, optional exclusion group and the verified operator are injected exactly once, and any earlier operator exclusion recorded in the mapping is preserved. Existing objects are matched by ownership mapping and live readback only; same-name or overlapping unmanaged objects are conflicts.

`DeploymentPlanner.Validate` recomputes the plan digest and checks tenant, session mode, account, application, profile, standard, snapshot, mapping, snapshot age, plan age, acknowledgement and the write guard on every row.

`DeploymentExecutor` runs one write at a time: preflight re-read (name and settings collisions for creates; ownership, drift, state and assignments for updates), journal the intent and payload digest, write, map, read back and compare, then capture the after-change snapshot. Ambiguous failures stop the run with status *Review required*; nothing is retried. `WaitForCompletionAsync` lets the host block exit until the in-flight write and evidence complete.

## Evidence layout

```
data\profiles.json
data\tenants\<tenantId>\snapshots\<stamp>-<id>.json      integrity digest
data\tenants\<tenantId>\assessments\<stamp>-<id>.json
data\tenants\<tenantId>\plans\<stamp>-<id>.json           plan digest
data\tenants\<tenantId>\runs\<stamp>-<id>.json            integrity digest
data\tenants\<tenantId>\runs\journal-<id>.jsonl
data\tenants\<tenantId>\managed-objects.json               ownership mapping
data\tenants\<tenantId>\deviations.json
data\tenants\<tenantId>\manual-checks.json
data\tenants\<tenantId>\msal-<mode>.cache                  DPAPI-protected, removed on disconnect and exit
```

Every load checks the embedded tenant ID against the requested tenant folder.

## Adding a control

1. Add the control to the release file with a new ID. For manual controls set `assessment.mode` to `manual` and write `manualInstructions`. For automated controls set `mode` to `settings`, name a collection and supply a `payload` template using only properties Graph accepts on create.
2. For Conditional Access recipes keep `state: "disabled"` and use `{{emergencyAccountIds}}` (or `{{emergencyAndGuestIds}}`) in `excludeUsers`.
3. Add controls only to a new, unpublished release file; published releases never change. Run `build\Update-StandardsManifest.ps1`, review and commit the manifest diff, then the tests (`StandardsTests.Shipped_standard_release_is_valid` parses every shipped release). The portable build only verifies the committed manifest (`build\Test-StandardsManifest.ps1`) and fails on any mismatch. Once a release is published, add its digest to `Release20260930Tests.Historical_standard_bytes_match_the_published_baseline_digests`. A new release also needs a lineage file from the earlier releases into it: list every control whose ID now means something else, retired controls and cardinality changes, then add its digest to `standards\lineage\manifest.json`. `ReleaseLineageTests` checks the file against every catalogue it names.
4. Validate the payload against a test tenant before enabling deployment for that control; recipes are not proven by unit tests.

## Adding a collection

1. Add a `collections` entry with the correct API version, path, delegated read scope and (only if the toolkit should write to it) a write scope.
2. The collector, allow-list, access check and reports pick it up automatically. Name resolution uses the `groups`, `users`, `namedLocations`, `apps` and `licences` keys; keep those keys stable.
3. If the collection needs per-object detail, declare `assignments`, `children` or `relationship`.

## Testing

`dotnet test tests\BDIT.TenantToolkit.Tests -c Release` covers canonical JSON, catalogue validation and integrity, tenant binding, assessment statuses, planner safety, plan integrity, Graph client retry and guard behaviour, executor reconciliation, idempotency, ambiguous failures, stop and shutdown, evidence integrity, secret scrubbing, formula-safe exports, deterministic reports and drift classification. Tests never touch the network.

## Packaging

`build\Build-Portable.ps1` restores, builds, tests, verifies the committed standards manifest (failing on any mismatch), publishes the App self-contained, stages `standards`, `config`, `docs`, launchers, `README.md` and `CHANGELOG.md`, writes `VERSION.json` (versions, runtime, NuGet packages) and `SHA256SUMS.txt`, and zips the result with a `.sha256` file alongside. Only the build machine needs the .NET 10 SDK.


## Registered read-only reports (INT-071 / PR #44)

`Core/Reporting/GraphReportRegistry` owns stable report IDs, typed row schemas, fixed Graph routes, required access and limitations. `GraphClient.ForReports()` shares the existing verified connection's HTTP/token context while replacing the route allowlist with read-only report routes. Only registered collection reads and exact `/users/{guid}/licenseDetails` are accepted. This is not a general Graph query or script executor and does not authenticate.

`Engine/Reports/GraphReportService` reuses `LicenceInventoryService` for subscription/user inventory and collects actual per-user licence details by identity. It produces independent strict `reportEvidence`, with explicit partial/failed/cancelled/not-attempted sections. Five-minute/5,000-row/31-day-query bounds are read limits, not retention or entitlement claims. Unknown fields are null; failed reads never become successful empty results.

`ReportEvidenceSchema` rejects missing/unknown/wrongly typed fields, unregistered rows, duplicate keys/IDs, inconsistent status, invalid identity/ranges and changed digests. `EvidenceStore.SaveReport/LoadReport` uses a dedicated immutable folder and the existing atomic create writer, preserving required null fields. Ordinary snapshot readers reject wrappers. Reports cannot authorise a write. `RegisteredReportDocuments` and `ReportExporter.ExportReport` share HTML/JSON/CSV/Excel generation with the existing safe writers; desktop/CLI navigation remains pending.

To add a report, review any new shared contract/access first, register its stable ID, typed row and exact read routes, then implement an engine adapter with dependency/access checks, bounded reads, context continuity and truthful failures. Add synthetic failure/empty/unknown/cancellation/import/export tests. Document permissions and limitations in APPLICATION-SETUP before live consent; do not change historical captures or conceal new access behind a report button.

## Portable offline CLI host (INT-074 / PR #45)

The App references the existing CLI assembly (`bdit.dll`, `UseAppHost=false`; compiled as a library when the parent publishes self-contained, from the same CLI source), so the self-contained ZIP keeps one application executable. `App.OnStartup` recognises `--cli` only as the first argument and returns through the existing CLI before Workspace/settings/logger/broker/cache initialisation. `PortableCliHost` preserves inherited standard handles, attaches to an existing parent console only when necessary, writes UTF-8 and returns the CLI exit code. Only stdout is required: without stderr, error text shares stdout. The handle choice sits behind `IStandardHandles` and is unit-tested for every stdout/stderr combination. It creates no new console or alternate process host. `bdit.cmd` forwards arguments without changing the working directory. Developer `dotnet bdit.dll` usage and offline conformance safeguards remain unchanged.

`Test-Portable.ps1` verifies the extracted dependency, sole executable, help, pipe/file redirection, launcher failures, exact codes, spaced evidence paths and actual synthetic inventory/jobs/assessment reports, before existing desktop startup/shutdown checks. It refuses desktop startup-log/auth-cache side effects in CLI mode. It also runs piped stdout with stderr inherited from the calling process and records that handle's type. These native package tests run on Windows; Linux cross-build is not runtime proof. CI has no interactive console, so an interactive `bdit.cmd` run (help, a refusal and `| findstr`) remains a manual Windows gate. The protected publisher and reviewed historical pins remain unchanged.
