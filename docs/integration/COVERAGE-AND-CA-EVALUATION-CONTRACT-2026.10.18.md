# Proposed coverage and Conditional Access evaluation contract — INT-127

Status: decision-only proposal for approved expansion B (recommendations 1 and 8). Canonical base: `4cab5de3e71fcb0e17d47f4d725b304bfc2a0ba9`. No adapter, new stored evidence, Graph call, consent or tenant change is implemented by this decision. Independent shared-contract acceptance and an owner-authorised merge must precede dependent code. Existing named Graph/execution/context review gates are retained.

## Purpose and reuse

An engineer needs to distinguish a created candidate, its configured targets, an intended pilot, actual service-reported outcomes and an unresolved verification. None is interchangeable. A second, explicitly selected journey evaluates a hypothetical Conditional Access sign-in against enabled/report-only policies; it never activates a disabled candidate.

Reuse these existing boundaries rather than introduce another execution/authentication system:

- `Engine/Assessment/ApplicationDeploymentAssessment.Evaluate`: current assignment-configuration meaning stays intact. Its established assignment is not installation success.
- `Engine/Execution/ReviewedChangeService`, `Engine/Workflow/JobProjection` and `JobCompletion`: existing reviewed assignment/activation, durable outcomes and stored-assessment citations remain authoritative. This report may link to them, never mark them complete or invoke their actions automatically.
- `Core/Reporting/GraphReportRegistry`, `Engine/Reports/GraphReportService`, `Graph/GraphClient.ForReports` and existing report documents: reusable bounded read/export conventions. Existing report schemas and route sets retain their meaning; a separate versioned registration is required for the new identities.
- `Graph/Auth/MsalAuthenticator` and `SilentRenewalAcquisition`: existing verified account, protected cache, scope-change invalidation and one silent 401 renewal. No second cache, JWT-claim guessing or interactive retry.
- Native existing presentation tickets, busy/cancellation handling and unsaved-original recovery. Report results never become complete deployment before-evidence.

## B1 — coverage request and interpretation

The request freezes one selected standard/control, original catalogue release/digest, tenant/account, mapped collection and exact object ID, optional explicitly selected pilot object IDs, and the source evidence identities/digests. Mapping/ownership must already be established by the existing provenance rules; matching names do not establish ownership. Pilot IDs are a report scope, not an assignment approval. Preserve configured targeting/exclusions even for disabled CA candidates and unassigned Intune candidates.

Show separate dimensions, with original timestamps and source:

1. Candidate existence and material state: absent, disabled/unassigned, active/assigned, unsupported or unknown. Reuse existing safety/material-state interpretation; never change the object.
2. Assignment configuration and intended pilot: exact returned targets, intent, exclusions and filter IDs, plus any explicitly selected pilot. An intended pilot is not deployed just because its group exists.
3. Observed membership: supported direct user/device member IDs and their types, only where all required pages and properties were returned. This is a point-in-time observation, not Microsoft's effective targeting evaluator.
4. Observed service results: raw service row identity/status/time and a conservative presentation bucket. `installed`, compliance and configuration-success observations have different meanings and remain separate. Unknown/future/missing statuses never become zero, success or not applicable.
5. Stored operation/readback state: retain accepted, rejected, not attempted and uncertain meanings from existing classifiers. Linking a successful read report does not clear an unresolved write or satisfy job completion.

No coverage percentage or estate-wide success denominator is derived from an incomplete or incompatible population. Built-in all-users/all-devices assignments are labelled as those configured populations, not an enumerated complete estate. A selected known subject may be reported as observed in a supported group; absence is only established within a completely enumerated supported membership set. Dynamic/hidden/nested/mixed-type groups, assignment filters, unsupported exclusions or ambiguous user/device matching stay explicitly unable to establish effective targeting. Do not run a transitive directory traversal and call it Intune nested-group support. No name/UPN/display-name joins.

Existing managed-device `id`, `azureADDeviceId`, directory object `id`, app-status `deviceId`, policy-status row `id` and user object `id` are distinct identity namespaces. Store them under explicit names. Do not infer that two GUID-looking values have the same namespace. Where the documented result shape lacks an authoritative subject identifier or mapping, show the original row and status separately, and leave per-subject coverage unknown. A result-row ID must not be relabelled a device ID. Missing timestamps or a result earlier than the selected change/pilot are stale/unknown, not verification of that change. Later timestamps alone do not prove causation.

## B2 — exact read surface

Initial supported reads use the current verified public-cloud Microsoft Graph context, existing authorised read permissions and package-owned paths. No catalogue-provided URL, user-provided Graph query, `$expand`, arbitrary function or service-principal token is admitted. Object/group IDs are canonical GUIDs from the frozen selection or returned typed assignments. MacOS remains inventory-only, unsupported/default-off/blocked for deployment.

| Read | Exact path/API | Existing scope and meaning |
|---|---|---|
| Selected CA policy and evaluation inventory | `v1.0 /identity/conditionalAccess/policies` and `/{id}` | `Policy.Read.All`; preserve material state and targeting; do not write. |
| Selected application/assignments | Selected existing registered `/deviceAppManagement/mobileApps/{id}` and `/assignments`, same API as the source collection | `DeviceManagementApps.Read.All`; supported object shape only. |
| Application device results | `beta /deviceAppManagement/mobileApps/{id}/deviceStatuses` | `DeviceManagementApps.Read.All`; Microsoft currently documents this result shape in beta. Keep beta and experimental labels; no silent v1/beta fallback. Do not add the unrelated nested user-status route. |
| Classic compliance policy results | `v1.0 /deviceManagement/deviceCompliancePolicies/{id}/deviceStatuses` | `DeviceManagementConfiguration.Read.All`; original status-row identity, not assumed managed-device identity. |
| Classic configuration policy results | `v1.0 /deviceManagement/deviceConfigurations/{id}/deviceStatuses` | `DeviceManagementConfiguration.Read.All`; same identity limitation. |
| Selected classic policy/assignments | Corresponding selected registered policy `/{id}` and `/assignments` | Existing collection's `DeviceManagementConfiguration.Read.All`; preserve actual API and object type. |
| Assignment group metadata | `v1.0 /groups/{id}?$select=id,displayName,groupTypes,membershipRuleProcessingState,visibility` | Existing `Group.Read.All`; null/limited metadata cannot prove a supported static population. |
| Direct members for an admitted static group | `v1.0 /groups/{id}/members` | Existing `Group.Read.All` is documented as supported. Retain exact returned type/ID; no hidden-member scope or Directory.Read.All fallback. |

A source collection or result family outside this table (including settings-catalogue/endpoint-security effective result evaluation) remains specifically unsupported in this adapter, with the existing manual portal route/next step. Do not create a generic report endpoint to disguise that limit. Existing assessment/collection support for those controls is preserved. Adding a result family requires an explicit contract amendment, not a URL supplied by a script.

Bound each selected-control capture to five minutes, 5,000 returned rows total, 100 pages total, 32 assignment groups and 5 MiB stored evidence. On truncation/deadline/cancellation, retain admitted earlier rows with explicit per-source read states and limits; never convert to a smaller complete set. Pagination must remain HTTPS on the exact Graph host/API/object relation, with loops/repeated pages/duplicates and unexpected bodies refused. Reuse safe existing read retry semantics within the total budget; no extra interactive renewal. Complete means requested supported reads finished, not effective deployment or tenant-wide discovery. Licence/access failure is a recorded failed source, not a permission upgrade or empty list.

## B3 — typed read-only Conditional Access evaluation

Add a dedicated typed boundary, for example `IConditionalAccessEvaluator.EvaluateAsync(ConditionalAccessScenario, CancellationToken)`, and a private request builder for exactly:

`POST https://graph.microsoft.com/v1.0/identity/conditionalAccess/evaluate`

Do **not** implement it by adding a generic assessment-mode POST exception, calling `IGraphClient.CreateAsync`, setting deployment mode, constructing an arbitrary Graph path/body, or calling the script runner. `GraphClient`'s generic write guard and assessment-mode refusal tests remain unchanged. This operation is a Microsoft-documented evaluation read whose HTTP method happens to be POST; it provides no write authority.

Initial supported scenario is one existing user object ID and one application ID, using `#microsoft.graph.userSignIn` and `#microsoft.graph.applicationContext`. Require explicit supported device platform and client-app type. Support Windows, iOS and Android; macOS is not offered. Guest/external-tenant identities, service principals, user-action/authentication-context scenarios and unknown future types are unsupported in this slice. Do not claim their policies were tested. Do not invent a different identity to make an evaluation succeed.

Typed optional conditions: an explicit IPv4/IPv6 address, two-letter country, sign-in/user risk from documented supported values, and nullable device-compliance state. Omitted, explicitly false and explicitly none are distinct. Show precisely which fields are omitted and that Microsoft may default missing conditions; never silently encode unknown compliance/risk as false/none. Request `appliedPoliciesOnly=false` so exclusions/nonapplication can be inspected. Do not expose arbitrary `deviceInfo`, authentication flow, OData subtype or extensible JSON. Any extra condition needed to evaluate a policy is a visible unsupported/missing-condition limitation, not inferred from the engineer's machine.

Freeze the request bytes/digest, tenant/account, session generation, Graph audience/resource and returned scope set before starting. Recheck verified current context after token acquisition, before each actual send and after the response. Reuse the existing provider's tenant/account and scope-change refusal; do not parse opaque tokens or trust a scope label in imported evidence. Require the current authorised `Policy.Read.All` access (Microsoft documents it as supported); no new `Policy.Read.ConditionalAccess` consent is required just because it is the documented least permission. Do not request additional access in the report. A missing scope directs the engineer to the existing deliberate connection/setup process.

Allow at most one silent token renewal after 401, with the identical frozen scenario and all context checks repeated. No automatic retry after timeout, 429, 5xx, malformed output or cancellation; report the outcome as could not evaluate and permit only a fresh deliberate read later. The evaluation is not an uncertain tenant write and must not create a write-intent/replay record. Use a 120-second overall budget (including acquisition/renewal), 64 KiB request bound, 5 MiB response/report bound and at most 1,000 policy-result rows. Never follow redirects or opaque next links for this exact action; a response claiming further pages is explicitly incomplete. A cancellation after a synchronous token return still prevents the send at the final boundary.

## B4 — evaluation results and evidence

Strictly admit a 200 JSON object with an explicit array of typed `whatIfAnalysisResult` rows. Empty body, missing array, null rows, duplicate policy IDs, unexpected subtype, coerced bools or oversized fields are not an empty success. Preserve policy ID/name, raw state, nullable `policyApplies`, original `analysisReasons` and requested-condition coverage. Unknown/missing bool or state is unclassified, not false/disabled. A service-returned false is labelled “Microsoft reports this policy does not apply to this supplied scenario”, with reason/limitations; it is not a blanket protection or access result. Store selected useful condition/grant/session metadata only as bounded typed evidence, never tokens, secrets or request headers.

Microsoft's What If evaluation includes enabled and report-only policies, not disabled candidates, and does not expand service dependencies (for example Teams does not implicitly test Exchange). Show those limitations beside the result. Compare any separately captured policy inventory by exact ID and timestamp; inventory/evaluation are not an atomic transaction. Disabled candidates are **not evaluated**, with a separate offline targeting/configuration explanation. Do not activate or change targeting/exclusions to obtain an evaluation. Report-only applicability is not enforcement, and applicability is not a real sign-in/MFA result. Business Premium/P1 does not prove P2 risk-feature entitlement; unavailable/unsupported risk evaluation stays unknown.

Create two separate strict evidence kinds (proposed `deploymentCoverageReport/1`, `conditionalAccessEvaluationReport/1`) rather than reinterpret existing `ReportEvidence`, `TenantSnapshot` or historical Exchange captures. Both include schema/adapter/toolkit identity, tenant/account/resource, requested scope/conditions, collection interval, per-source states, source catalogue/control/object digests where applicable, original IDs/raw known values, limits, cancellation/deadline reason and integrity digest. Preserve original supplied bytes; imported evidence is historical/unverified even when internally consistent and resealed. Reject incompatible kind/schema, null/duplicate/coerced fields and contradictory complete coverage. No import can grant permission, rebind a tenant, approve a plan, clear uncertain writes or complete a job.

## B5 — native, CLI and operational behaviour

Add focused Reports journeys for the selected control's coverage and an explicit CA scenario. Reuse the current native command centre/context, exact-ID details, shared read-only export writers and local unsaved-result handling. Navigation/selection never starts a Graph request or sign-in. New live-unaccepted capture actions remain visibly experimental/default-off and require the existing deliberate read acknowledgement; deployment approval remains separate and native.

Show progress/source states, Stop, historical import, original-result recovery and HTML/CSV/original-JSON export. Context or parameter A–B–A invalidates current-client admission/export but retains original unsaved evidence for acknowledged recovery, without requerying or changing its identity. No new report may silently overwrite prior files or be saved under a newly selected client. Read failures remain inspectable alongside useful earlier results. Large collections are projected off the UI thread with bounded virtualised rows.

The offline CLI may export each registered supplied report through the same strict schema/presentation. It does not acquire tokens, run evaluation POSTs or collect coverage. Coverage “next steps” may navigate to existing native planning/review pages with source citations; they cannot press approval or perform an assignment/activation. The existing pilot/activation/verification workflow is reused, not rebuilt as a second state machine.

`APPLICATION-SETUP.md` must explain the additional purposes of the existing permissions and the beta result read, affected registrations, licence/RBAC limitations and no new consent. No modules/runtime/dependencies, generic scripts, publication or permission expansion are introduced by B. Missing service role/licence/access is explicit; no automatic fallback to broader scope.

## B6 — implementation and acceptance gate

After the shared decision is accepted/merged, refine the existing B task brief in its existing PR/ledger, not another overall plan. Implement serially in one owned feature branch: typed evidence/admission and offline projection; bounded GET adapters; the dedicated typed evaluation transport; native journey/export/CLI and composed verification. New files belong in existing Core reporting/models, Graph, Engine reports and native view/controller layers; no business rule belongs solely in WPF.

Required observed failing/passing cases include:

- Candidate versus assignment versus reported installation/compliance; unsupported filters/groups; missing metadata; hidden/partial memberships; distinct ID namespaces and duplicate display names; stale or missing result time; missing licence/access; delayed/no results; unknown status never success.
- Exact route/method/body admission; wrong tenant/account/resource/scope; renewal changes; context A–B–A; cancelled-before-send and after-await; 401 once only; redirects, error/deadline/oversize/truncated/malformed result; missing conditions and disabled/report-only meanings. Preserve generic assessment POST/PATCH/DELETE refusal, with both narrow and broad returned scopes.
- Strict original-byte/schema/digest import; no authority escalation; stale context/source export; unsafe HTML/CSV; output interruption; unsaved-original retention and truthful recovery; same CLI/native projection counts.
- Native actual three-size renders, keyboard/accessibility names, disabled-action next steps and zero tenant calls; current-source engine/App/hosted/PowerShell/portable checks. Verify actual original ZIP/hash and package identity. Cross-build is not native validation; green CI is not live Microsoft acceptance.

A fresh-context engineering review supplements the required named Graph/context/safety acceptance; do not replace it with self-review. Future main integration requires its applicable owner authority and exact-head/resulting-merge checks. Human accessibility and live service acceptance remain unrun until separately authorised. Rollback removes new report routes/registrations without altering original evidence, historical standards, existing execution guards or stored jobs.

## Primary references inspected 18 October 2026

- [Microsoft Graph evaluate (v1.0)](https://learn.microsoft.com/en-us/graph/api/conditionalaccessroot-evaluate?view=graph-rest-1.0), [scenario conditions](https://learn.microsoft.com/en-us/graph/api/resources/signinconditions?view=graph-rest-1.0), [result](https://learn.microsoft.com/en-us/graph/api/resources/whatifanalysisresult?view=graph-rest-1.0).
- [Conditional Access What If and limitations](https://learn.microsoft.com/en-us/entra/identity/conditional-access/what-if-tool).
- [Application device-status list (beta)](https://learn.microsoft.com/en-us/graph/api/intune-apps-mobileappinstallstatus-list?view=graph-rest-beta) and [result identity](https://learn.microsoft.com/en-us/graph/api/resources/intune-apps-mobileappinstallstatus?view=graph-rest-beta). A v1.0 resource-link request redirected to beta; do not claim v1.0 support from the original URL.
- [Compliance device-status list](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfig-devicecompliancedevicestatus-list?view=graph-rest-1.0) and [row properties](https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-devicecompliancedevicestatus?view=graph-rest-1.0).
- [Configuration device-status list](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfig-deviceconfigurationdevicestatus-list?view=graph-rest-1.0) and [row properties](https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-deviceconfigurationdevicestatus?view=graph-rest-1.0).
- [Direct group members, limited information and hidden membership](https://learn.microsoft.com/en-us/graph/api/group-list-members?view=graph-rest-1.0); [Intune assignment groups and exclusions](https://learn.microsoft.com/en-us/intune/app-management/deployment/assign-groups).

These are documentation observations, not live API, entitlement, role or workstation validation. Source implementation remains gated on this decision; unrelated approved work continues.
