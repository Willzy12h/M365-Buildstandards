# Behavioural comparison

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
