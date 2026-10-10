## Registered report implementation — PR #44

| Feature | Source implementation | Evidence / remaining gate |
| --- | --- | --- |
| Users/licences | Added separate report: identity-based assigned SKU/service-plan details, explicit partial reads; existing inventory reused | Synthetic identity/duplicate/malformed/access/cancellation tests; desktop/CLI and live acceptance outstanding |
| Device/MFA/log reports | Added registered typed read-only Graph adapters, bounded range/output/time, unknown fields, minimal sensitive data | Synthetic failures, pagination, cancellation, context/refusal tests; AuditLog access proposed, not granted; retention/human/live unverified |
| Report storage/export | Added strict immutable separate store and HTML/CSV/JSON/Excel documents | Strict shape/identity/integrity, overwrite, explicit-null, escaping/formula tests; Reports page not implemented |
| Mailbox/100 GB and scripts | Not implemented in this PR; existing fixed Exchange capture retained | Dedicated read-only mailbox adapter, verified entitlement rules and manifest library still needed; integrated Exchange mutations remain blocked |

This extends the existing comparison evidence; it does not replace the capability table or close unrun acceptance.

## Portable CLI implementation — PR #45

| Feature | Earlier limit | Source result / validation gate |
| --- | --- | --- |
| Portable offline CLI | Developer/CI CLI omitted from ZIP | Existing managed CLI hosted by the sole packaged application; early dispatch, bdit.cmd and stdout/stderr/exit-code bridge. Existing offline conformance tests retained. Native fresh-package checks exercise reports/jobs/spacing/redirection and refusal; exact Windows evidence required, no live capability implied. |

# Preview.18 approved product-completion work — PR #19

| Area | Earlier limit | Implemented result and evidence |
|---|---|---|
| Definition exports | Separate engineer documents; no integrated exact JSON/defaults set | One set with source-byte JSON/manifest, printable settings/defaults HTML, Markdown, manual references and capability/access scope. All eleven historical releases export; unknown catalogue metadata and checksums round-trip. StandardDefinitionExportTests. |
| Assessment reuse | Desktop supplemented Exchange, CLI accepted only primary input | AssessmentContext shared existing inputs; explicit CLI supplemental file. Actual CLI JSON findings/summary match direct desktop engine on combined fixtures; missing/malformed/cross-tenant/changed/oversize evidence is refused. AssessmentContextTests. |
| Support | Raw local details/logs required individual selection | Exact allowlisted generated metadata preview and ZIP; no workspace records/settings/logs/cache/path reads. Seeded-private-data test checks absence. WorkspaceTransferTests. |
| Continuity | Manual complete-folder procedure without tested transfer helper | Bounded byte backup and separate verified restore; retains unknown fields/original digests and real Unknown write blockers, excludes MSAL cache, refuses traversal/duplicates/changes/links/unsupported types. Exact-limit round-trip and fresh/old plan rejection. WorkspaceTransferTests. |
| Packaging | Hand-maintained dependency versions and stale entry guides | Resolved NuGet/runtimepack inventory, supplied notices, source-cleanliness field and negative inventory verifier; 19 shipped operator guides with closed local links. Windows execution pending exact CI. |
| Product workflow | In-memory advice and control-keyed historical checks | New immutable job/observation/disposition/cutover/lineage contracts in decision PR #20, independently reviewed. Producers/consumers wait for human merge. |

All live service/device effectiveness, final support commitments and production publication remain separately approved and unrun. Synthetic checks are not production capability acceptance. Current exact checks are recorded in COMPLETION-REGISTER; the earlier comparison below is retained historically.

## Historical Preview.17 comparison

# Preview.17 continuation comparison — PR #19

| Area | Before | Approved result and evidence |
| --- | --- | --- |
| Quick Connect | Discovery auth disposed before confirmation; second sign-in | One-use retained auth, standard/tenant/time guards and reverified operator. Preview17AuthAndProcessTests plus existing discovery guards; live WAM prompt count pending. |
| Setup | Direct navigation lacked tenant; existing IDs entered manually | Verified tenant prefill, Quick setup checks, explicit existing-ID picker, configuration/grants/assignment validation. Preview17WorkflowTests plus existing setup HTTP/approval guards. |
| Policy approval | Repeated tenant GUID transcription | Verified tenant/domain/account, exact changes, explicit tick and deliberate deploy. Native dialog checks and changed-context negative tests; engine immutable approval unchanged. |
| Planning/readiness | Ambiguous ownership, grey selections, unrelated numbering | Tool/engineer guidance, reason/remedy, matching navigation pages and collection error details. No eligibility/evidence gate loosened. |
| Exchange | Engineer script export/run/import replaced Graph snapshot | Owned embedded read process; module/tenant checks, progress/cancel, domain discovery, independent evidence. ExchangeWorkspaceTests protects Graph state and wrong-tenant refusal; strict import/DNS/proposal guards retained. Windows PowerShell 5.1 parses the generated template and rejects an injected write without executing it. |
| Packaging | Validated Preview.16 | Preview.17 strict builds passed; 852 engine/112 app tests, 42 native page/size checks and the freshly extracted portable package/context-menu checks passed. Exact implementation evidence is in COMPLETION-REGISTER. Catalogue bytes/persisted schemas unchanged. |

# Behavioural comparison

## Preview.14 / standard 2026.09.12 - PR #19

The C#/.NET 8 baseline remains authoritative. Neither preserved source repository was changed. This increment implements the maintainer's NEXT-RELEASE-PLAN; it does not reopen the completed PR #17/#18 review.

| Area | Result | Evidence / remaining checks |
| --- | --- | --- |
| Standard and schema | Add .12 only, schema 5 optional null-omitted metadata, 96 controls / 61 recipes; older bytes preserved | Release20260912Tests; complete manual metadata and historical export tests |
| Offices and candidates | Stable per-office identity; all reserved/non-public CIDR classes refused; empty groups/untrusted locations; new CA disabled and Intune objects unassigned | Release20260912Tests, creation guards, planner and executor regressions; AndroidStoreReadinessTests detects removal of either Play prerequisite guard |
| Reviewed identity changes | Six constrained new actions, typed confirmation, complete durable before evidence, preserved unrelated settings, exact ownership, intent-before-request and no retry | ReleaseIdentitySafetyTests runs each new kind through refusal, persistence failure, drift and uncertain transport cases |
| Assessment | New identity and Exchange/Purview observations; malformed/incomplete evidence remains unknown | ReleaseIdentityAssessmentTests (red/green malformed cases); ExchangeEvidenceTests, fake DNS only |
| Exchange architecture | Supported delegated engineer capture, strict inert import, no app-only credentials; selected commented proposals instead of automatic writes | INT-030/035/036, ExchangeEvidenceTests and ExchangeProposalTests; module/SPF trust and all service effects unverified |
| Engineer documents | All 96 controls in both HTML/Markdown exports, generated from catalogue only | EngineerStandardDocumentsTests, four local harness commands and CI download; browser appearance unverified |
| Interface | Area filters with isolated selection, three Configuration tabs, expanded document exports | App tests; full keyboard, names, contrast, clipping and command harness at three sizes |
| Packaging | Preview.14, two extra operator guides, four generated document downloads | Strict build, both suites, portable package/link checks and exact-head CI recorded in release ledger |

See [release validation](RELEASE-2026.09.12-VALIDATION.md) for counts and failures corrected, [coverage](../AUTOMATION-COVERAGE.md) for every control and [handover](HANDOVER.md) for manual items/decisions. All evidence is synthetic/local or CI; none is live Microsoft acceptance.

## Confirmed F1–F5 follow-up

The integrated baseline now checks CA material additions against verified evidence, uses the documented enrolment envelope, blocks unsupported Autopilot group assignment/removal, preserves beta recovery validation, and separates observed app assignments from proven deployment scope. Only .11's two CA array caveats change operator. [Implementation and synthetic evidence](SAFETY-REVIEW-2026-09-21.md); neither source repository was changed and live acceptance remains unverified.

## Preview.6 extension

The source comparisons below remain historical. The integrated code now provides 37 candidate recipes, explicit tenant-setting/activation/assignment actions, supplied-package publication and external-service readiness checks. See [coverage by control](../AUTOMATION-COVERAGE.md) for client inputs, recovery limits and the four remaining human workflows. Release compilation is confirmed; these added behaviours have not been tested against Graph or through the GUI.

## Integrated workflow update — 14 September 2026

This update supersedes the baseline gaps below where named implementation and checks now exist. Source comparisons remain historical evidence.

| Area | Claude baseline | Asta reference | Integrated recommendation | Evidence / remaining checks |
|---|---|---|---|---|
| Evidence/execution | Partial boundary validation, incomplete terminal states | Stronger workflow safeguards worth retaining | Executor reloads complete durable evidence, rebuilds write rows and binds all inputs; unresolved writes cannot be replayed | Executor/Planner regressions; live failure checks pending |
| Account exclusions | Raw IDs and creator safeguards | Friendly identity/parameter workflows | Explicit tenant-bound lookup, purpose/reason, names plus IDs; emergency and creator safeguards preserved | AccountResolver/planner tests; live lookup pending |
| Connections | Always saves profile | One-time connection option | One-time default, opt-in saved profile, durable evidence retained | WPF implementation; interactive sign-in pending |
| App setup | External registration setup | Onboarding reference only | Separate privileged delegated setup, permission preview, explicit creation, browser consent and Graph validation | ApplicationSetupTests; live setup/consent/assignment pending |
| UI/reports | Dense panels, collection/create colours implied success | Useful inspect/export/progress workflows | Shared semantic colours, persistent identity/access, structured plan/results and background exports | Build and offline rendering; human acceptance pending |

## Original source comparison

Source evidence uses the pinned revisions in [SOURCE-REPOSITORIES.md](SOURCE-REPOSITORIES.md). Claude paths are relative to the imported baseline; Asta paths refer to its source repository. "Implemented" below means code inspection; checks actually run are separate in [TESTING-EVIDENCE.md](TESTING-EVIDENCE.md).

| Area | Claude implementation | Asta implementation | Recommended state | Evidence / remaining checks |
|---|---|---|---|---|
| Architecture/dependencies | C# Core, Graph, Engine, WPF; net8.0 and net8.0-windows; MSAL/ProtectedData | Node >=22, local HTTP server, PowerShell Graph worker | Select Claude architecture | .sln, .csproj; Asta package.json, server.js. No blanket runtime merge |
| Packaging | Self-contained Windows publish, launchers, hashes; source omission prevented builds | Bundled runtime design and local browser launcher | Restore Claude portable pipeline | build/Build-Portable.ps1; Asta runtime/build files. Package/GUI checks tracked separately |
| Auth and permissions | Delegated MSAL; tenant-pinned browser sign-in; separate read/write registrations and protected cache | Delegated and certificate app-only request modes, setup mode | Keep Claude delegated scope; defer app-only/provisioning | Graph/Auth/MsalAuthenticator.cs, TenantConnectionService.cs; Asta engine/connections.js |
| Saved/one-time connections | SaveProfile is called when connecting from form; saved-profile selection exists | API accepts ephemeral profile and reports oneTime separately | Candidate: explicit one-time mode in WPF | App/ViewModels/ConnectViewModel.cs:194; Asta server.js /api/connect |
| Tenant identity | Returned tenant/operator checked; header/details expose identity, mode and scopes | Session organisation/account; tenant-checked snapshots and access report | Retain Claude; verify GUI visibility and fallback write-scope notice | TenantConnectionService, ShellViewModel:155; Asta connections/collector |
| Collection/pagination | Bounded Graph pagination; per-object assignment/settings/relationship failures | Pagination origin/loop/count guards; detailIncomplete flags | Retain Claude collection model | GraphClient; TenantCollector; Asta collector.js. Live endpoint details unverified |
| Object names/IDs | Shared NameResolver; searchable/filterable configuration grid and rendered setting values | Group-name resolution plus configuration navigation | Retain resolver; selectively port useful navigation | Engine/Assessment/NameResolver.cs; ConfigurationViewModel; Asta collector.js, ui/workspace.js |
| Missing/unknown/null | Unusable captures block affected rows; absent member can match expected null | Error/detailIncomplete is distinct from missing; comparison has manual fallback | Fix null semantics without weakening unknown handling | Core/Json/CanonicalJson.cs:130; Asta comparison.js |
| Comparison/equivalence | Setting-level comparisons, enforcement states, equivalence signals/caveats; not automatically compliant | Semantic overlap candidates independent of names; manual cross-policy judgement | Select Claude; preserve evidence/caveats in exports | AssessmentEngine, EquivalenceEvaluator, EquivalenceTests; Asta comparison.js |
| Deviations/manual work | Structured deviations and manual-check register; unknown handling explicit | Profile exceptions plus manual-control and check workflows | Retain Claude structure, review policy for exceptions | Workspace; Core models; Asta planner.js, comparison.js |
| Drift/backfill | Mapping-based drift; selected controls from versioned standards; inactive owned PATCH | Mapped ownership, last-applied comparison, newer release planning | Retain Claude; describe limited updates accurately | DriftAnalyser, DeploymentPlanner; Asta planner.js. Not a general migration engine |
| Plan binding/staleness | Tenant/profile/standard/snapshot/mapping/operator/app digests; age/ack checks | Profile/standard/snapshot/plan hashes and age/ack checks | Prefer Claude binding; enforce at executor boundary | DeploymentPlanner.Validate:290; Asta planner.js validatePlan |
| Snapshot/dependencies | Durable capture; affected-row completeness checks; dependencies are warnings | Durable capture and affected-row blocking; no proof of full snapshot gate | Implement full completeness/dependency requirements | Workspace, DeploymentPlanner:123, Validate; Asta planner/collector/server |
| Safe candidates/ownership | CA disabled with exclusions/targeting; Intune unassigned; no adoption by name; inactive owned updates | Similar collision/drift checks and creator exclusions | Retain stricter checks; clarify CA targeting before live work | Core/Safety, planner/executor; Asta planner.js |
| Execution/retry | Graph writes have no retry; records acceptance/readback, then after capture; unexpected InProgress gap | Serial worker writes, per-row catches, post-capture and result log | Retain Claude layering; fix truthful terminal states | GraphClient, DeploymentExecutor:218; Asta server.js:49–53, execution tests |
| UI/progress/cancellation | Async command guards, exclusive operations, deployment pause/stop; synchronous analysis/export; no wired read/sign-in cancellation | Progress/log controls, explicit auth cancellation, browser lifecycle leases | Port cancellation UX selectively, not browser server lifecycle | Commands.cs, Workspace, App.OnExit; Asta server.js, lifecycle.js, ui/workspace.js |
| Evidence/exports | JSON evidence/digests/journal; recovered HTML/Markdown/CSV/XLSX incl equivalence; export tests | JSON evidence, browser report/export workflows | Restore Claude reports; review prose/format edge cases | EvidenceStore; recovered Reports; EvidenceAndReportTests. No automatic rollback |
| Coverage/live validation | 45 controls, 12 recipes; source marks live validation outstanding | Broader app setup/auth workflows exist, but code is not live proof | Stabilise current scope, defer extra workloads | standard 2026.09.3; docs/LIVE-VALIDATION.md; Asta connection/setup paths |
| Automated verification | Original CI fails; recovered local solution/test evidence recorded separately | Main and latest coordination-branch CI succeeded | Use both as evidence with limits | CI links in testing evidence. Neither proves GUI or live Graph |

The matrix above records source-baseline behaviour. The integrated preview now adds one-time connections, cancellation, isolated delegated app provisioning, explicit exclusions and the redesigned WPF workflow. App-only remains deferred. Recovery and licensing additions are new integrated work, rather than evidence that either source previously implemented them.

| Integrated area | Recommended state implemented | Evidence / remaining checks |
|---|---|---|
| Policy recovery | Exact-ID register; selective deletion, inactive-update restoration and reviewed CA disablement | RecoveryService, RecoveryTests; live acceptance and uncertain-write reconciliation remain outstanding |
| Licence overview | Subscription counts, assigned-user search, direct-user scope results | LicenceInventoryService, LicensingTests; unsupported scope remains unknown |

## Preview.4 additions

Preview.5 adds four data-driven Windows candidates and a restricted Graph-export importer on the existing Claude engine. A separate Entra LAPS service preserves the full registration policy during approved enablement. No Asta runtime or UI rewrite is introduced. All four candidates are covered by synthetic deployment/removal tests, and LAPS by actual HTTP-adapter tests with synthetic responses. UI integration and live device/Graph acceptance remain pending; see [policy code handover](../POLICY-AUTOMATION-CODE.md).

| Area | Recommended current behaviour | Evidence / remaining check |
|---|---|---|
| Desktop identity | WAM pop-up, browser fallback, tenant/operator binding retained | Builds and offline guards; live WAM/GDAP required |
| App setup | Exact consent callback, explicit-ID repair, reviewed engineer assignment, actual scope counts | Synthetic transport/evidence/repair tests; live consent/branding required |
| Device automation | Supplied BitLocker and optional long paths; unassigned candidates and selective recovery | Shipped-payload deployment/recovery tests; portal defaults, escrow and device effects required |

## Independent review corrections

PR #9 retains Claude's WPF/Core/Graph/Engine structure and adds guards rather than importing a second runtime. See [review dispositions](REVIEW-FIXES.md) for behavioural comparisons and regression evidence covering directory prerequisites, older evidence, assessment, typed inputs, targets and reporting.

## Preview.15: current continuation

| Area | Choice and evidence | Remaining limit |
| --- | --- | --- |
| Runtime | Retain Claude's component separation; upgrade all projects to .NET 10, official SDK 10.0.401 and self-contained win-x64 | Live WAM/consent needs Windows tenant acceptance |
| Interface | WPF detail/prerequisite guidance and shared result evidence, keeping UI logic outside the engine. Standalone inert HTML comparison passes 26 browser checks | HTML is not an embedded host; no authentication/bridge equivalence claimed |
| Device references | rc.15 runtime and later supplied snapshot/Preview 23 source inform selection, detail, outcomes and evidence patterns. Available Engineer Console source is rc.1; claimed rc.5 was not supplied | No claim to latest laptop files or integrated device successor |
| Standard | Successor .30 has 93 controls/61 unchanged candidate payloads; ten published catalogues remain byte-identical | Microsoft/device effectiveness remains unverified |
| Evidence | Missing historical fields stay unknown; client switch clears selected result and visible summary; tests cover these boundaries | Actual tenant evidence was not accessed |
| Reports | Engine-generated HTML/Markdown; Chromium checks and representative A4 inspection completed | Physical print and screen-reader experience require human checks |

See [interface decision](INTERFACE-DECISION-2026.09.30.md), [release goal](RELEASE-GOAL-2026.09.30.md) and [completion register](COMPLETION-REGISTER.md).


## Preview.16: user-observed usability issues

| Area | Choice | Evidence / limit |
| --- | --- | --- |
| Connect | Transient Quick Connect plus explicit-customer partner sign-in; preserve pinned normal authentication | Synthetic identity/mode/profile tests; live WAM/GDAP still pending |
| Setup | Visible creation approval bound to verified identity; no repeated setup GUID; clear browser consent, app IDs and access stages | User-requested UX change; preserve policy deployment confirmation and service execution guards |
| Read collection | Documented parent arrays and explicit scheduled-action reads, with strict completeness | User-supplied capture inspected locally; Microsoft documentation and synthetic HTTP cases, no fresh live capture by agent |
| Runtime | Preserve WPF/.NET 10 self-contained packaging; load-check Accessibility and exercise fresh-package context-menu Copy | Local publish includes matching dependency; exact Windows CI result will be recorded in completion register |

See [QoL goal](QOL-GOAL-2026.10.01.md) for the full acceptance contract.

## Scoped checks — PR #42, INT-070/076

| Surface | Choice | Evidence and remaining gate |
|---|---|---|
| Registry | Reuse verified catalogue control instances, registered routes and existing historical area mapping | Dependency/route/scope regression tests; no historical catalogue edits |
| Collection and assessment | Collect only dependencies; skip unrelated controls before evaluation | Synthetic request/assessment/cancellation/identity tests; Microsoft live behaviour unverified |
| Scoped evidence | Strict separate partial wrapper, immutable atomic store, original historical source/time retained | Malformed/tampered/complete-claim/import-refusal tests; no new deployment authority |
| Desktop | Area/requirement picker and separate partial result; explicit stored-evidence option | App state-preservation tests and three-size native harness added; actual Windows outcome recorded on exact head |
| CLI | Offline `check --snapshot --area/--control`, same evaluator, JSON stdout | Real subprocess parity/refusal tests; portable hosting remains unimplemented |
## Astra focused integrity follow-up — 10 October 2026

PR #79 replaces startup's use of the tolerant display loader with the existing strict deployment-history requirements. Original `5ef5470` re-seals an edited Pending result and loses its unresolved-write refusal; the corrected path preserves invalid bytes and refuses before changing any record. Five negative cases fail first and a valid-history control passes; 138 related local regressions pass after correction. No schema, catalogue, permissions or live behaviour is changed. Independent review and exact-head Windows validation remain separate gates; see `INTERRUPTED-RUN-INTEGRITY-2026.10.10.md`.
