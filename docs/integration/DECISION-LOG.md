## Proposed product workflow contracts — 6 October 2026

INT-049 (job/attestation records), INT-050 (legacy dispositions/cutover cases) and INT-051 (release lineage/impact) are proposed in PRODUCT-CONTRACT-DECISIONS-2026.10.06.md. User approved A–G source development. The dedicated decision PR must merge before dependent implementation; this entry records a proposal, not a settled contract or live authorisation. Existing snapshot/plan/run/mapping/catalogue schemas and historical bytes remain unchanged.

## Approved Preview.17 continuation — PR #19, 1 October 2026

The user approved the source-reviewed QoL proposal with “Action these changes”. This supersedes the presentation/lifecycle restrictions below only in the stated areas; no live tenant operation is authorised.

- **INT-045 — retain Quick Connect authentication:** request the selected standard's assessment read scopes at the initial sign-in, keep the authenticator transiently in memory, then reverify the same tenant/operator at confirmation without another interactive request. The pending result is single-use, expires after five minutes, and is bound to the standard digest. Cancellation, changing client/standard and shutdown release it. A configured different dedicated client still needs its own authentication; explain why. No discovery token may authorise deployment.
- **INT-046 — reviewed policy approval without GUID retyping:** replace policy deployment's typed-ID dialog with the connected organisation/domain/operator, immutable plan summary and exact write list, plus an explicit approval tick and deliberate deploy button. Pass the approved exact tenant ID to the unchanged executor validation. Keep fresh complete live evidence, session/plan identity, durable intent, inert candidates and no ambiguous retries. Other activation/recovery confirmations retain their contracts.
- **INT-047 — integrated read-only Exchange/Purview capture:** run the embedded allow-listed delegated read script in an owned Windows PowerShell process with a preinstalled supported module. Check each service's observed tenant before collection. No module installation, arbitrary scripts or write commands. Use the verified profile domain as a reference, discover accepted mail domains and offer selection for domain-specific checks. Keep the existing strict schema and imported provenance; process ownership is additional runtime evidence, not a signed export. Exchange evidence is independent of Graph evidence and cannot enable writes. Imports remain supported and offline. Authentication/consent may still require Microsoft interaction for this separate service.
- **INT-048 — guided setup and workflow clarity:** carry the connected tenant into setup, offer Quick setup with discovery of existing registrations and explicit exact-ID selection, and read back configuration, actual grants and operator assignment. Names alone never adopt an application. Explain disabled plan selection and prerequisite action ownership, align readiness labels with navigation, and expose failed collection details and copy controls. Published catalogue bytes remain unchanged.

Windows broker, service RBAC and live Microsoft responses require engineer acceptance; offline and native UI tests cannot establish them.

# Integration decisions

## User-requested usability continuation — Preview.16

- **INT-041 — tenant discovery and account reuse:** add a separate transient organisation/account discovery entry using the existing Microsoft Graph PowerShell assessment fallback and only User.Read/Organization.Read.All. Discovery requests the organisations authority; normal setup/assessment/deployment continue to require a pinned tenant. Confirm the discovered organisation/account before the subsequent pinned assessment connection, reverify both identities, and never copy another profile's inputs or escalate mode. Partner/GDAP keeps an explicit customer and account chooser. An unchanged verified connection or matching cached account can be reused; navigation remains local and mid-operation renewal remains silent only. This adds no persisted schema or Graph write scope.
- **INT-042 — explicit setup approval without repeated GUID entry:** the user found creation hidden beneath permission tables and explicitly requested no second tenant-ID entry. Show the required creation action with the verified tenant/plan, fold detailed permissions, and bring approval into view after preview. Replace setup-only retyping with an explicit approval bound to the current verified tenant/operator and fresh issued plan. Preserve service identity/drift/plan checks, complete evidence, durable intent and no ambiguous replay. Do not change policy deployment's typed tenant confirmation. Label browser admin consent separately from WAM sign-in and show application IDs, assignment, configuration and grant guidance. Updated application tests reflect this requested UX contract and reject absent/changed identity, unapproved or stale plans.
- **INT-043 — documented parent and explicit relationship reads:** supplied captures showed authentication-method/passkey list requests failing while parent policy responses contained those objects, and compliance incompleteness isolated to scheduled-action relationships. Implement bounded Graph read projections from the documented policy/FIDO2 GETs and explicit rule/action reads instead of $expand. Require complete embedded arrays, parent identities, unique object identities and allow-listed parent routes; missing data is not an empty collection. Keep catalogue bytes, API versions and write routes unchanged. Store detail errors in the existing collection Error field. Synthetic HTTP tests cover exact routes and failures; a new authorised capture is still required to verify Microsoft service outcomes.
- **INT-044 — text editing in the portable runtime:** the screenshot names Accessibility 4.0.0.0. Local .NET 10 publishing includes that exact DLL and its dependency manifest entry; the user's failing extraction was not inspected. Verify loading before interactive work, fail incomplete publishing and test context-menu Copy in the fresh extracted executable. Do not claim startup-only checks or a shared-runtime harness establish portable text editing. Preserve the self-contained folder distribution.

Primary references for INT-043: [authentication methods policy](https://learn.microsoft.com/en-us/graph/api/resources/authenticationmethodspolicy?view=graph-rest-1.0), [FIDO2 GET including passkeyProfiles](https://learn.microsoft.com/en-us/graph/api/fido2authenticationmethodconfiguration-get?view=graph-rest-1.0), [scheduled rules](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfig-devicecompliancescheduledactionforrule-list?view=graph-rest-1.0). Read on 1 October 2026 UK time. Supplied exports remain outside the repository; only independently authored synthetic fixtures are committed.


## Release continuation — 30 September 2026

- **INT-038 — .NET 10 LTS:** the user authorised migration for support lifetime. Retarget the Core/Graph/Engine/CLI, WPF application and both test/harness families to .NET 10; use official SDK 10.0.401, retaining the existing C# 12 source contract. Keep MSAL 4.89.0 and its broker together; verify them under the new Windows runtime rather than changing authentication architecture. Update ProtectedData to a supported 10.0 package, bootstrap, CI and self-contained win-x64 packaging. Tests, WPF inspection, extracted startup and WAM/live acceptance remain distinct. Microsoft's release metadata gives .NET 10 end of support as 14 November 2028. No Graph permission, schema, approval or deployment mechanism changes are introduced by the framework migration. The separate PowerShell/WinForms device toolkit is a design reference.

- **INT-039 — approved recommendation completion:** continue PR #19's existing Astra/Codex claim; do not duplicate or overwrite its release work. The user supplied the recommendations as the intended direction: retire classic ENR-003/004 and Defender EDR onboarding SEC-WIN-002 from a new immutable default standard, retain independent SmartScreen, Business Premium and ESET package/licensing requirements, keep Exchange manual and SPF bypass disabled/audit-only pending trust/alignment acceptance, and investigate supported native Windows/mobile/passkey mechanisms. Historical standard bytes and evidence compatibility are preserved. COMPLETION-REGISTER records the current implementation and acceptance evidence. Missing project-context documentation is explicitly no longer a prerequisite.

- **INT-040 — retain WPF for Preview.15:** implement the selection/detail and shared before/requested/after presentation within the existing .NET application. A standalone, inert HTML comparison exercises selection, queue review, typed synthetic confirmation, outcomes and copying, with 26 browser checks. It demonstrates responsive presentation but does not establish an embedded browser host, authentication integration or safe engine bridge. Existing WPF authentication/orchestration and passing native checks support retaining the default. The browser comparison is maintainer material, excluded from the portable runtime. [Interface decision](INTERFACE-DECISION-2026.09.30.md) records the evidence and limits; [release goal](RELEASE-GOAL-2026.09.30.md) defines acceptance.

## Release completion - PR #19, phase 7

INT-030 through INT-037 remain the release decisions. Preview.14 ships .12 as the default without modifying any older published catalogue. The portable allow-list gains the engineer document and Exchange/Purview guides; CI publishes the four catalogue-only generated document files separately from synthetic interface evidence. Final audit malformed-identity cases now remain unknown; this tightens the existing invariant without adding a schema, permission or write mechanism. The maintainer recommendations in HANDOVER remain proposals, not newly implemented decisions.

## Engineer documents - PR #19, phase 6

- **INT-037 - catalogue-only engineer exports:** generate The Build Standard and Manual implementation and verification guide from the loaded catalogue in HTML and Markdown. The API accepts no profile, snapshot, client name or connection; client values remain named placeholders. Group by the recorded Entra/Intune/Exchange/Purview area. Include exact candidate settings, intended production scope, licence/edition notes, required inputs, sources and unverified limits. The manual guide requires non-empty INT-031 before, ordered portal steps, PowerShell and after/pass sections for every control; refuse an incomplete guide instead of silently omitting content. PowerShell references are for separately reviewed engineer use, not toolkit execution or proof of deployment. Shared delegated preflight guidance does not claim to replace complete evidence, durable intent, selective approval or service verification. Keep the existing client document and historical catalogue behaviour unchanged. All four exports are local commands in the interface harness.

## Exchange write fallback - PR #19, phase 5

- **INT-036 - selected, inert PowerShell proposals:** implement INT-030's authorised fallback by exporting one explicitly selected control at a time from Configuration. Require a matching typed tenant confirmation, an intact saved imported capture, fresh complete observations for the selected action and the explicitly entered accepted domain. Render the selected control's catalogue manual instructions and PowerShell as line comments, with an unconditional refusal if the file is invoked. The export is a review artefact, never an executable deployment script or an approval to make changes. Show before observations, the proposed settings, separate after checks and the uncertainty/reconciliation instructions. No authentication or Exchange write transport is added. DKIM enabling instructions are included only when both fresh DNS CNAME answers match the actual captured targets; an absent configuration receives only a disabled-creation proposal followed by recapture. The SPF bypass proposal remains disabled and in audit mode because header trust/alignment is unverified; it never includes activation. Do not adopt existing custom rules by name. The manual catalogue fields introduced by INT-031 hold these instructions so the phase 6 guide uses the same reviewed content. No new schema or Graph permission is needed.

Maintainer proposal: retain manual, change-controlled Exchange operation until Microsoft documents a supported single-attempt delegated write route. Keep the own-domain bypass disabled until the trusted SPF/From alignment boundary is demonstrated. These are verification and implementation limits; the maintainer's standard outcomes are unchanged.

## Exchange/Purview evidence contract - PR #19, phase 4

- `exchangeDomain` uses a schema-5 `mailDomain` parameter type: canonical ASCII DNS domain validation, no URL/wildcard/IP/command text, and no default. It is mandatory when the three domain-dependent controls are selected, and before capture export/import. Saving an empty required domain is refused with its field name.

- **INT-035 - inert external captures:** add a null-omitted `TenantSnapshot.ExchangeCapture` record with schema version, source capture ID/time, module version, separate observed Exchange/Purview tenant IDs, delegated provenance, an explicit domain, allow-listed collection statuses/projections and separately timestamped DNS answers. Import is bounded, rejects duplicate/unknown JSON fields and cross-tenant provenance, and never interprets scripts. It creates an offline snapshot with no Graph collections, clears approvals and cannot satisfy `SnapshotRequirements`, even if its complete flag is altered. Missing fields/collections and read errors stay unknown. The domain must be explicitly entered and validated; domain-dependent checks also require captured accepted-domain membership. DNS uses `IDnsLookup`, with a fixed, bounded Windows `Resolve-DnsName` adapter and fake test answers; no tenant authentication or configuration writes. The toolkit never launches the generated Exchange capture script. Source claims in an imported file are not authenticated proof of Microsoft responses.

## Interface sections - PR #19, phase 3

- **INT-034 - area filters within the existing workflow:** keep the existing flow pages and add All areas, Entra, Intune, Exchange and Purview filters on Assessment and Plan. Assessment combines area with result, category and search. Plan selection is confined to the displayed area; changing area clears the selection, with that behaviour stated beside the filter. The exact-change preview continues to show the whole built plan. Schema-5 controls use their recorded area; older releases retain a presentation-only ID-family fallback. Expanded office controls retain their stable identities and prerequisite guidance. No connection, write command or persisted approval is created by changing a filter. The offline harness uses .12 and checks the new controls at all three existing sizes.

## Standard 2026.09.12 investigation — PR #19, 25 September 2026

- **INT-030 — Exchange/Purview route and phase 5 gate:** use documented Exchange Online PowerShell delegated authentication for engineer-run, allow-listed read-only capture, with strict tenant-bound import into toolkit evidence and fakeable DNS queries. Do not execute Exchange writes from the toolkit: the supported module documents internal retries after network delays and no documented single-attempt write switch was found. Generate selected reviewed implementation PowerShell instead, clearly labelled as an engineer-run proposal; export/import never runs it. No undocumented REST, app-only authentication, certificates, secrets, module installation or execution-policy changes. This is the fallback authorised by NEXT-RELEASE-PLAN. The SPF-header/From-domain trust boundary is unverified and is a further blocker to automatically deploying the bypass rule. See [primary-source investigation](API-INVESTIGATION-2026.09.12.md).
- **INT-031 — additive release/profile schema:** before implementation, approve schema 5 with optional, null-omitted `ControlDefinition.Area`, `RepeatFor`, `Implementation` (before, ordered portal steps, PowerShell and after/pass checks) and optional collection `PublicIpRangesOnly`. Profile parameters gain null-omitted `OfficeLocations`, containing a stable key, display name and CIDR list; required-input metadata gains optional `RequiredForControls`. Repeatable controls derive a stable instance control identity from the reviewed catalogue control plus the validated client key; never from a display name or list position. Bind expanded rows to the existing catalogue/profile/plan digests and preserve exact-ID ownership. Reject duplicate keys/names, non-public ranges and ambiguous instances. Schema 3/4 and published bytes/historical digest semantics remain supported. Exchange/DNS evidence will use additive typed records with explicit provenance/status; it cannot authorise a Graph or Exchange write. Full per-control document metadata is mandatory for .12 by phase 6, without retrofitting older releases. Tests must fail on missing metadata, invalid inputs, altered expansion or cross-tenant imports.
- **INT-032 — new delegated permissions:** `Application.Read.All` resolves the documented Intune Provisioning Client by its fixed Microsoft application ID, never name. `Policy.ReadWrite.Authorization` disables default-user self-consent while preserving other permissions. `Policy.ReadWrite.ConsentRequest` enables the reviewer-bound workflow. Existing `Policy.Read.All` reads both consent policies; there is no new consent read scope. Add these only to their appropriate assessment/deployment permission sets, document them in setup and changelog, and state that both applications need administrator consent again after updating their configured permissions. No consent is performed during this task. Exchange capture uses separate read-only Exchange RBAC; no Exchange write permission is added to the Graph apps.
- **INT-033 — manual and pending items:** use native documented CSP settings through the existing OMA transport; do not add uploaded ADMX, arbitrary templates or script deployment. Where public native catalogue IDs or a complete supported mechanism could not be confirmed, retain an explicit manual control with exact engineer steps and an unverified note. Keep ENR-003/004 and SEC-WIN-002 visible as deferred/manual references in .12 while requesting the documented retirement decisions; no older release is changed. Retain CFG-WIN-004 SmartScreen and ask the maintainer to confirm it independently of ESET. Fast-startup `Hiberboot` disabled is not a force-off setting, and an enforcing taskbar XML is not a starting layout. These are manual/proposal paths. ESR management moved to Windows settings backup and restore in July 2026: use the documented `SettingsSync/EnableWindowsBackup` native CSP and retain manual device verification. The conflicting Pro applicability statements for consumer Copilot removal remain unverified. These qualifications implement the plan's fallback rules rather than changing its target outcomes.

## Confirmed safety review F1–F5 — 21 September 2026

- **INT-025:** CA enable/report-only uses bounded material equality against intact verified, exact-ID write evidence, not the outgoing payload's subset alone. Inadequate baseline or material additions block preview/execution. No global subset rewrite or historical state adoption. Emergency disablement is preserved.
- **INT-026:** enrolment uses its documented request envelope. Autopilot generic group assignment/removal is blocked pending a separately reviewed workflow; this narrows INT-019's generic assignment/containment description. Historical accepted envelopes are interpreted read-only, never replayed.
- **INT-027:** retain the collection API version during recovery re-verification; beta-only EDR guards remain unchanged.
- **INT-028:** application compliance needs required intent and established scope. Optional, null-omitted assessment metadata in .11 declares expected population/exclusion prerequisite; unresolved group/exclusion/filter scope remains manual-review. No membership inference or historical catalogue rewrite.
- **INT-029:** correct only .11's two CA array-count caveats. Keep numeric operators numeric. Exclusion compatibility and Windows Hello defaults remain acceptance questions, not newly proven bypasses or device behaviour.

Authority: the human approved implementation of the proposed F1–F5 plan on existing PR #9, without tenant operations, consent, application changes or merging. [Detailed evidence and acceptance proposal](SAFETY-REVIEW-2026-09-21.md).

## Preview.6 automation and follow-up review (14 September 2026)

- Complete implementation paths across the 45 existing controls: 37 candidate recipes, four reviewed tenant-action workflows and four readiness/engineer workflows. Preserve historical standard releases; ship the new catalogue as 2026.09.6.
- Tenant-wide settings, activation, assignment and Autopatch category enrolment use separate immutable previews, explicit approval, durable intent, exact target resolution and read-only verification. They are never ordinary candidate creation. Device-side rollback is not promised.
- Accept reviewed client inputs and narrowly supported Graph exports for settings that cannot be inferred. Store-to-Win32 import is supported for the four optional application controls when the app is unavailable through Intune's Store source. Package publication consumes an existing encrypted .intunewin file; installer execution, package creation and vendor credentials remain external.
- Apple/Google ownership, emergency-access validation and privileged-role decisions retain human steps. Readiness checks do not imply those requirements are complete. Autopatch enrolment does not implement ring creation or OS release approval.
- The user requested code completion without running tests. Compile the solution and review the diff; preserve existing CI. New tenant and UI behaviour remains unverified. See [review dispositions](FOLLOW-UP-REVIEW.md) and [coverage](../AUTOMATION-COVERAGE.md).

## Preview.5 policy-code decision (14 September 2026)

User requested code first, with UI integration later, and authorised SME defaults informed by Cyber Essentials/NIST. Add LAPS, Defender Antivirus, firewall and EDR candidates without activating or assigning them. Restrict import to supported Graph policy shapes; reject arbitrary settings and tenant onboarding blobs. Entra LAPS enablement is a separate approved tenant-wide operation using the documented full PUT, preserving all registration settings, with durable before/intent/after evidence and read-only re-verification. Do not expose a generic singleton write or automatic LAPS disable/rollback. All new live behaviour remains unverified; see [API handover](../POLICY-AUTOMATION-CODE.md).

| ID | Date | Decision | Reason / authority | Status |
|---|---|---|---|---|
| INT-001 | Original | Preserve source repositories; develop shared product in master through reviewed PRs | Existing accepted decision and current user instruction | Accepted |
| INT-002 | 2026-09-14 | Claude C#/.NET 337c8e6 is primary; Asta 020c69b is a selective reference | Explicit current user direction; evidence in comparison. Replaces the pending proposal on Claude's unmerged coordination branch | Accepted |
| INT-003 | 2026-09-14 | First implementation imports baseline, recovers reports and repairs build/package with focused tests | Restore a reviewable baseline before expanding functionality | Accepted |
| INT-004 | 2026-09-14 | Preserve CA disabled state and stored targeting/exclusions in this import | Targeting and enforcement differ. Historical "unassigned" semantics must be agreed before a targeting change or live test | Accepted for baseline preservation; final rollout semantics unresolved |
| INT-005 | 2026-09-14 | Complete durable evidence, dependency checks, plan binding, ownership/drift checks and truthful terminal states remain requirements | Current user safety requirements override incomplete implementation; see testing evidence and handover | Implemented with synthetic regression tests; live validation pending |
| INT-006 | 2026-09-14 | Defer app-only, cloud browser UI and extra workloads; app provisioning now handled by INT-007 | Current user explicitly requested integrated enterprise application setup | Partially superseded |
| INT-007 | 2026-09-14 | Add isolated delegated setup for two single-tenant public-client applications and enterprise apps; explicit creation preview, separate browser consent and Graph validation | Current user instruction; toolkit app setup is the stated assumption. No app-only credentials or automatic directory-role assignment | Implemented; live validation pending |
| INT-008 | 2026-09-14 | Resolve and explicitly select tenant-bound exclusions with purpose/reason; never infer an exemption from admin-style names | Current user account-lookup request and existing emergency/creator safeguards | Implemented; synthetic tests |
| INT-009 | 2026-09-14 | One-time connection is default; saving a profile is optional, but local audit evidence remains durable | Selective Asta workflow integration | Implemented |
| INT-010 | 2026-09-14 | Green means verified/pass; amber marks write access or warnings; blue indicates collection/planning; grey means unknown/pending | Current user requested coherent design for engineering decisions | Implemented; see DESIGN-SYSTEM |

## Recovery and licensing decisions — 14 September 2026

- **INT-011:** user-approved selective recovery for recorded policy creations and supported inactive updates, plus state-only CA disablement. Durable ownership and before/after evidence, fresh preview, typed tenant and explicit drift review are mandatory. Unknown outcomes, assigned Intune objects and unsupported cleanup remain blocked or manual. Implemented with synthetic tests; live acceptance pending.
- **INT-012:** user-approved licence dashboard and on-demand assigned-user search. Direct-user scope checks use captured service-plan assignments; unresolved group/role/guest/device scopes stay unknown. Do not infer entitlement from seat totals or assign licences automatically. Implemented with synthetic tests; tenant-specific acceptance pending.
No decision in this PR authorises tenant writes, application registration, consent, subscriptions or a release to production. Source snapshots and digests are evidence, not signatures or automatic rollback.

## Independent review decisions — preview.3

- **INT-013:** explicit transport boundary: only confirmed pre-send failures become NotAttempted. Unknown writes are not resolved through name searches or retries. Accepted outcomes are retained even when follow-up reads fail.
- **INT-014:** accepted-unverified writes can be re-verified using reads only. Separate source-linked evidence records and safe local mapping finalisation preserve the original audit trail. Read-only assessment access is sufficient.
- **INT-015:** do not assume no real 1.0.0 evidence exists. Require acknowledged reconciliation of completed, absent-acceptance records against the latest exact ownership/payload and current safe settings. Preserve historical bytes; insufficient evidence remains blocked.
- **INT-016:** Stop cancels deployment reads immediately but lets an in-flight policy write reach its configured timeout/result. Readback and after-capture each have a 60-second budget; truncated evidence remains explicitly incomplete. Storage/OS hangs are outside these network budgets.
- **INT-017:** preserve the localhost registration pending a real admin-consent redirect test. UI and documentation explain Stop waiting / Validate setup fallback; no automatic application recreation or consent inference.

## Preview.4: authentication and device automation (14 September 2026)

User testing reported localhost redirect mismatch and confusing setup permissions. Keep separate assessment/deployment tokens, replace random-port admin consent with an exact registered web callback, and prefer WAM for desktop sign-in. Existing-ID repair requires explicit preview/approval and rejects unrelated application configurations. Engineer app assignment may be included explicitly; directory roles and consent grants are never written. Generalise visible product branding while retaining internal namespaces and original historical standard bytes. Add only user-specified BitLocker settings and optional long paths in 2026.09.4. The beta device-configuration route is isolated and receives the same recovery gates; no other beta recovery route is enabled. User clarified unspecified recovery choices remain not configured. Live server defaults and device effects are acceptance work.

## Independent review implementation — 21 September 2026

The following entries document existing reviewed exceptions plainly and the additional safeguards in PR #9. They do not change AGENTS.md or authorise a live tenant operation.

- **INT-018 — CA activation:** a separate reviewed workflow may PATCH only the state of an exact-ID toolkit-created CA policy. Deployment access, immutable approval bound to tenant/operator/client/standard/profile/snapshot/mappings, fresh before evidence and unchanged ownership/settings are required. Enabling/report-only requires an inactive candidate, two distinct emergency accounts plus additional exclusions and the operator retained, and confirmed matching default-capable client inputs. After evidence records acceptance separately from verification. `Conditional_access_activation_is_state_only_on_a_v1_policy_addressed_by_id` covers the payload boundary; confirmed-input tests cover the new input gate. These are not a claim that every service gate has independent mutation coverage.
- **INT-019 — Intune assignment:** a separate approved POST to the resource's `/assign` action may assign an exact-ID toolkit-created, unassigned candidate. The same session, approval, before/after evidence, ownership/drift and confirmed-input gates apply. Groups must resolve in this tenant. All users/All devices are explicit preview choices only on supported device-configuration, compliance, Settings Catalogue and mobile-app routes. Unsupported resources require groups; containment removes every assignment with no replacement. `Group_assignment_targets_only_resolved_group_ids_and_removal_takes_no_replacement_targets` and `Built_in_population_is_exactly_previewed_and_cannot_be_tampered` cover target/payload gates. Device-side reversal is not promised.
- **INT-020 — selective deletion:** existing recovery may DELETE a supported toolkit-created candidate only from its intact accepted creation record and exact-ID mapping, following a fresh approved recovery plan, deployment session, typed tenant, ownership/drift checks and before/after evidence. Assigned Intune policies cannot be deleted through this path; unexpected active CA requires explicit containment/drift review. No generic DELETE or directory prerequisite cleanup is added. `Wrong_tenant_read_only_or_unapproved_recovery_never_writes`, `Missing_ownership_and_name_matches_cannot_authorise_removal`, `Changed_review_inputs_block_recovery`, `Assigned_Intune_object_is_not_silently_unassigned_or_deleted` and `Corrupt_run_or_incomplete_snapshot_blocks_preview` protect these boundaries.
- **INT-021 — prerequisite identity and creation-only routes:** preserve historical catalogue bytes; .11 uses fresh exclusion IDs and restores stable .8 identities elsewhere. Recorded purpose must match before auto-injecting an exclusion. Groups/named locations are creation-only at planner, executor and Graph boundaries because their changes can affect active controls. `A_reused_control_ID_cannot_exclude_the_former_managed_users_group` and `Directory_prerequisite_PATCH_never_reaches_HTTP` protect the known defects. Existing conflicts need human reconciliation; no name adoption or automatic evidence migration.
- **INT-022 — historical verification compatibility:** recognise the exact old model digest of unchanged known .3–.8 catalogues only for read-only accepted-write verification. Preserve historical evidence and absent plan properties. Never apply this compatibility to new execution approval. `HistoricalDigestTests` covers unchanged, tampered, unknown-acceptance and new-metadata cases.
- **INT-023 — truthful assessment and defaults:** array counts are explicit schema-4 operators; failed reads are unknown, directory material settings matter, and matching group metadata does not prove intended membership. Shipped reviewable defaults permit inert candidates with warnings, but cannot establish compliance or authorise activation with missing/different client values. Identity/targeting defaults are rejected. `AssessmentReviewRegressionTests` and confirmed-input regressions cover these distinctions.
- **INT-024 — prerequisite guidance:** data-driven tooltips/expandable guidance distinguish reviewed toolkit actions from manual tenant/device/consent prerequisites. Guidance never implies successful validation. Typed input round trips and merged profile saves preserve existing data; reports describe proposed scope rather than completed rollout. `PolicyInputParserTests`, `BuildStandardDocumentTests` and offline UI checks cover this presentation contract.

## Maintainer decisions for the next release — 25 September 2026

- **INT-025 — guided application setup (PR #18):** one administrator sign-in, one reviewed approval with the typed tenant ID, then creation, both administrator consents in turn and a read-back of actual grants. The separate tick before sign-in is removed because the toolkit writes nothing at sign-in and Microsoft's own prompt asks for the setup permissions. Assigning the engineer running setup is the default. Each stage runs only if the previous one finished cleanly; nothing is retried after an uncertain write and consent is never inferred from the browser.
- **INT-026 — scope of standard 2026.09.12 and the Exchange and Purview sections:** the maintainer's decisions on identity, office locations, devices, Windows configuration, ESET as antivirus and EDR, compliance, Autopatch, mobile apps, Exchange and Purview, and what is excluded or deferred, are recorded in [NEXT-RELEASE-PLAN.md](NEXT-RELEASE-PLAN.md). They are settled for that release. Schema changes and the Exchange connection route it calls for still need their own entries here when they are made.


## INT-052 — Catalogue specification exports and existing-format continuity (PR #19)

Approved A–G/R17 work reloads the manifest-verified catalogue source, verifies unchanged in-memory identity and preserves exact JSON bytes. HTML/Markdown distinguish fixed settings, reviewable defaults and unresolved client identity; manual requirements receive no invented API defaults. The full set adds compatible integrity metadata, current capability/access scope and complete existing manual references. This is reusable definition data, not tenant evidence or deployment authority; published catalogues remain untouched.

Desktop and headless reporting share existing profile/mapping/deviation/snapshot/optional Exchange assessment inputs. Offline supplemental input is bounded and tenant/integrity validated. No new authentication, write transport or persisted schema is introduced.

Support export contains only previewed generated technical metadata. Sensitive backup preserves current JSON/JSONL/NDJSON data bytes, refuses unsupported types/links, excludes authentication caches and restores only into a new separate directory after bounded checksum validation. It neither rewrites historical evidence nor reauthorises old plans. Upgrade remains a deliberate complete-package transfer with fresh authentication, preserved unresolved records and trusted handoff.

Resolved package/runtime inventory comes from published dependencies with supplied licence notices. Source cleanliness is recorded, preview/live limits are explicit, and production promotion uses an independently approved exact artifact. INT-049–051 producers remain gated by decision PR #20; this decision does not bypass that merge gate.

## INT-053 — Authorised Preview.18 publication and independent Claude review (PR #19)

The user explicitly requested “publish and give me a prompt for claude to review this and update me with recomendations” on 6 October 2026. Publish the already validated unsigned Preview.18 as a GitHub prerelease, pinning source 10808de and push run 37515288607 with inner ZIP SHA-256 9155fad428fe6c93ae8b72a6695a8ee5622f533348165d00167bcd577f3495cc. This supersedes the earlier publication hold for this specific preview, not live operations or GA/support acceptance.

The cloud proxy refuses direct Azure artifact downloads. A manual-only GitHub Actions path therefore retrieves the original successful run's assets and checks source/job status, original ZIP identity, clean self-contained metadata and portable evidence. It stages a draft, verifies uploaded asset digests and an unchanged source tag before publishing. It never rebuilds the application, modifies main/integration, merges branches or overwrites a published release. Later tooling/review-document commits are distinct from the released binary source.

Provide the full independent review prompt in the repository and as a release asset. Claude reviews source, journeys, standards exports, support/release controls and unfinished approved A–G work; it records recommendations with stable IDs and exact evidence in its own documentation-only branch/report. Astra integrates shared-register updates, preserving previous findings. PR #20's actual human merge is still required before INT-049–051 implementation. Publication neither closes that work nor substitutes for independent/human/live acceptance.

## INT-054 — Evidence integrity in results, stored-evidence time and verified transfer (PR #21)

Claude's Preview.18 review (CLA-20261006-01, -04, -05, -06, -10) led to these additive contracts. Published standards, existing record formats and historical bytes are unchanged.

- **`AssessmentResult.SnapshotIntegrity`** is an optional field: `intact`, `modified` or `notRecorded`. A result written before it existed reads as absent, not as a failure. A primary snapshot whose integrity digest does not match is refused for assessment by the CLI and the desktop; a modified stored Exchange snapshot is refused when reopened. The digest check relies on the snapshot model serialising identically, so a future model change must keep that, or integrity will read "modified".
- **Stored evidence is judged as of its capture:** the reference time is the latest of the Graph capture, Exchange capture and DNS query times, capped at now. Live work keeps the wall-clock rule.
- **Evidence transfer** checks a SHA-256 fingerprint received separately from the archive, or an explicit logged acknowledgement that none exists. Adoption only fills an empty `data/` folder, re-verifies every file in place and quarantines on failure. No sign-in, approval, plan or session is transferred.
- **Support metadata** may include four opt-in, previewed sections. Graph failures are kept in memory only, reduced to the declared route root, status, error code and GUID-shaped correlation IDs.

## INT-055 — Owner delegation of commits and merges (7 October 2026)

William delegated commits and merges to the agents, keeping approval of promotion to `main`, tags, releases, publication, repository settings and live tenant actions. The conditions are in [HANDOVER](HANDOVER.md#owner-delegation-of-commits-and-merges--7-october-2026). On 7 October he approved Claude merging PR #21, #20 and #19 once CI is green.

## INT-056 — Non-material client profile fields for reviewed-scope validity (CLA-20261006-17)

Follow-up to INT-049/050 now that PR #20 is merged. Their records bind a digest of the reviewed client scope. A digest of the whole profile would mark every observation and disposition `NeedsReview` after a harmless relabel, which invites rubber-stamping. The digest (`ReviewedClientScope.Digest`) therefore leaves out only `company`, `notes`, `createdAt` and `updatedAt`. Every other field stays material: tenant, domain, application overrides, parameters, offices, policy inputs, group IDs, emergency and exclusion accounts, including exclusion display names. Adding a field to the non-material list needs its own decision. A label-only edit keeps the digest; any material change alters it (tests in `ReviewedClientScopeTests`). Amended by INT-059: exclusion `resolvedAt` and `selectedBy`, and the order of the account lists, are not material.

## INT-057 — Release lineage, initial read-only slice (INT-051, CLA-20261006-07)

`standards/lineage/lineage-2026.09.30.json` records how each control of 2026.09.9, .10, .11 and .12 relates to 2026.09.30. It pins every catalogue by the digest in `standards/manifest.json` and is itself listed in `standards/lineage/manifest.json`; the build and the loader refuse a changed, unlisted or mis-pinned lineage file. A control not listed under a source keeps the same requirement under the same ID; a test fails if that rule would cover a control that left 2026.09.30 or whose name changed. Shared assessment (desktop and CLI) adds a "Release lineage" explanation to the limitations and to the finding of each control whose ownership record came from an earlier release and means something else now; a record from a release with no lineage is flagged for review. Nothing is rebound, moved or dropped, and no finding status or plan changes. The upgrade impact report (one capture assessed under both catalogues) is the next slice.

## INT-058 — Upgrade impact report (INT-051, second slice)

`UpgradeImpactAnalyser` assesses one stored capture under a source and a target release with the same client inputs, ownership records and deviations, then traces each source requirement through the verified lineage. Each row is Same requirement, Renamed, Changed, Replaced, Retired, Added or Unknown, with its status under both releases; repeated office instances are summarised per status. With no verified lineage between the two releases, every source row is Unknown and the report says so. The report is presented separately from two-capture drift, is available on the Assessment page and as `bdit upgrade-impact`, and writes only the exported report: no assessment, mapping, exception or plan is saved or changed.

## INT-059 — Review fixes for release lineage and reviewed client scope (INT-056, INT-057)

A post-merge review of PR #22 found four defects, fixed in one pull request.

- **Reviewed scope, an amendment to INT-056.** Each exclusion account's `resolvedAt` and `selectedBy` are now non-material, and the order of the exclusion accounts, `emergencyAccountIds` and `additionalExclusionAccountIds` no longer counts. The account picker restamps `resolvedAt` and `selectedBy` whenever an engineer resolves the same account again, so INT-056 would have sent every INT-049/050 record to review for an action that changes nothing. The lists are sets: the profile validator de-duplicates them and drift compares them without regard to order. Display name, UPN, object ID, purpose and reason stay material, as INT-056 decided. No record bound the old digest yet, so nothing needs migrating.
- **Unreadable lineage never stops work.** `ReleaseLineage.Load` and `Parse` now report any failure to read or validate lineage as an `IntegrityException`, including a malformed lineage manifest and explicit nulls. Assessment then records the limitation and continues, as INT-057 intended.
- **Skipped lineage is explained.** When the loaded catalogue's bytes differ from the catalogue the lineage describes, assessment records why it did not use the lineage. The per-record message now says "no verified lineage … is available", which is true in both cases, where it used to say none was recorded.
- **Validator.** A relation is keyed by its source control, and an added requirement has none, so `Added` is refused with a reason instead of a generic error. `Retired` relation and `Retired` cardinality must now appear together.

## INT-060 — Job and observation records, first implementation (INT-049)

Implements the persistence and read-only projection of the INT-049 contract (PR #25). Jobs live in `jobs/<id>.json` and immutable observations in `observations/<id>.json` under the tenant's folder. Readers refuse schema versions other than 1, unknown members, a tenant or ID that does not match the folder and file, and a failed integrity digest; listings report such files as unreadable rather than skipping them silently. An observation is created once (an existing ID is never replaced) and then attached by replacing its job; only the job's owner, notes, evidence references, review-only plan reference and attachment list may change, and attached observations cannot be detached. A requirement keeps one line of history: a new record for a requirement that already has one must supersede its current record, of the same semantic ID, control and instance key. Each observation binds the catalogue identity (release and verified SHA-256), the profile ID and `ReviewedClientScope.Digest`, the pinned capture's integrity digest and a SHA-256 of exactly the observed objects as captured. A Pass or Fail needs a named actor and a reason. The projection selects the current record from the supersession graph, never by time, and reports `NeedsReview` for a changed standard, client inputs, evidence or observed objects, a passed review date, a missing predecessor, a cycle or a fork; only an unchallenged, in-date Pass counts as accepted. Old manual checks are listed as legacy, unbound attestations. Computing the projection writes nothing. Evidence references cover saved captures only; assessment references, desktop and CLI surfaces and the completion claim are later slices. Choices made where the contract was silent: superseding an already-revised record is refused at write time (a fork can still be read from tampered or concurrent history and is then `NeedsReview`); observed objects must be present in the referenced capture.

## INT-061 — Legacy dispositions, first implementation (INT-050)

Implements the disposition half of the INT-050 contract (engine only). Immutable dispositions live in `dispositions/<id>.json` under the tenant's folder. They use the INT-060 conventions: schema 1 strict readers, create-never-replace, the same tenant, ID and integrity checks, and attachment by replacing the job. They also share INT-060's bindings: the catalogue identity, the profile ID and `ReviewedClientScope.Digest`, the pinned capture, a digest of exactly the named objects, and an explicit `supersedesId` with one line of history per requirement instance. The projection selects the current decision from the graph and reports `NeedsReview` on the same grounds as an observation. It also reports `NeedsReview` when an approved departure's deviation has gone, changed control or passed its review date. Only an unchallenged retention or approved departure counts as settled. Investigate, manual work, add missing candidate and propose replacement always leave work outstanding.

A disposition never creates a managed-object mapping, a deviation, or any right to change or delete a tenant object.

Choices made where the contract was silent:
- Every decision needs an owner, an actor and a reason.
- Retention must name at least one observed object in a capture.
- `ApprovedDeparture` must cite an existing, in-date `ApprovedDeviation` record for the same control in the same tenant; `NotApplicable` records do not qualify. Only that decision may cite a deviation.
- Repeating the current decision writes nothing and returns the existing record. This applies when the decision, owner, deviation, object set and material digest are unchanged and the current record still stands, including against an unchanged recapture. A revision is otherwise required.
- The job gains an optional `dispositionIds` list that is omitted until the first disposition. Jobs written by PR #25 therefore keep their bytes and digests.
- The contract's optional case reference is carried as `caseId`. INT-062 enables it.

## INT-062 — Cutover cases, first implementation (INT-050)

Implements the cutover half of the INT-050 contract (engine only). Each revision of a case is an immutable `cutovers/<id>.json` record using the INT-060 conventions. A case is the line of revisions sharing a `caseId`, one requirement instance in one job. Each revision carries the full reviewed state:
- old and new object IDs and a digest of the old objects as captured;
- overlaps and the pilot groups;
- the pilot and retirement approvals (who and where recorded);
- prerequisites and functional criteria with their actual results;
- recovery limits;
- residual deviations;
- any unsupported-scenario escalation.

A revision must supersede the case's current revision. Stages move forward one step at a time and may step back. A closed case is never reopened.

What each stage requires:
- **Review:** the old objects present in the referenced capture.
- **CandidateCreated:** a saved, intact deployment run with an accepted write and a passing readback for every new object, pinned as a new `run` evidence kind. This kind is allowed on cutovers only.
- **PilotReviewed:** pilot groups that are group objects in a capture with collected groups, a recorded approval and every prerequisite met. Whole-tenant targeting is not a group ID, so it cannot be a pilot.
- **EffectivenessVerified:** at least one criterion, and every criterion `Passed` with what was actually observed.
- **RetirementReviewed:** `Retire` or `RetainCoexistence` with a recorded approval.
- **Closed:** a complete capture taken after retirement was reviewed. It must show the new objects present and, for `Retire`, every old object gone. For `RetainCoexistence` it must show every old object still present.

Any residual deviations must be existing, in-date approved deviations for the same control. An escalation stops forward movement until a revision at the same stage clears it. A closed revision cannot carry one.

The projection reports `NeedsReview` in these cases:
- a changed standard or client inputs, or missing evidence;
- old objects changed before closure, or reappearing after a retirement;
- new objects missing from the current capture;
- a passed review date, or a residual deviation that has lapsed;
- a missing predecessor, a cycle or a fork.

`Closed` holds only for an unchallenged closed revision. A disposition's `caseId` must name a case attached to the same job for the same requirement instance, and a lost case projects `NeedsReview`.

Histories in the projection are now listed in supersession order, then by time.

Choices made where the contract was silent:
- Retirement is performed outside the toolkit and proven by a fresh capture.
- The "fresh assessment" at closure is that capture; stored assessment references remain an INT-049 follow-up slice.
- Residual exceptions are limited to the case's own control.

## INT-063 — Workflow surfaces and the completion projection, first slice (INT-049/050)

Adds the completion projection to the engine and the first desktop and CLI surfaces over INT-060 to INT-062. Surfaces add no authority. Nothing on them reads or writes a tenant, and a complete job grants no permission to plan or deploy.

The completion projection (`JobCompletion`) lists every applicable requirement instance of the current standard and client inputs. Each one is in one of four states:
- **Verified:** a current, in-date, unchallenged Pass that relies on stored evidence, with no open decision or cutover case.
- **Approved departure:** a current, settled approved-departure disposition. It is counted and named as an exception, never as verification. The claim reads "Complete with approved departures … (not verified)".
- **Not applicable:** an in-date `NotApplicable` entry in the deviation register, for a requirement with no records in the job. It is left out of the count.
- **Outstanding:** everything else. This covers Pending, Fail, Unknown, `NeedsReview`, an unsettled decision and an open, escalated or unreadable cutover case.

A job is complete only when nothing is outstanding and nothing blocks the job as a whole. The job is blocked by any of these:
- a job-level review reason;
- an unreadable attached record;
- an unreadable deviation register;
- records for an instance that is no longer a requirement.

No deferral closure policy exists, so deferrals never complete a job, as the contract's initial default requires.

Surfaces:
- **Desktop:** a **Jobs and completion** page. It opens a job, shows each requirement's state, reasons and history, and records an outcome or a legacy decision through `JobWorkflow`. A revision automatically supersedes the current record. When a requirement has history but no single current record, the page refuses and says a deliberate review is needed.
- **CLI:** `bdit jobs` and `bdit job` project jobs read-only. `--snapshot` names the current capture. Without it, the material check is skipped and the output says so. The conformance tests now forbid `JobWorkflow` and every workflow writer in the runner.

Choices made where the contract was silent:
- Retaining external coverage is a decision, not an outcome. The requirement still needs an accepted Pass.
- A Pass with no stored evidence does not verify a requirement.
- A new record's semantic ID is the existing history's, or else the one the shipped lineage into the release declares for the control, or else the control ID.
- Recording cutover revisions from the desktop, and references to stored assessments, are the next slices. Cutover cases are shown on the page and in the CLI but are not yet recorded there.

## INT-064 — Recording cutover revisions from the Jobs page (INT-050 surfaces, second slice)

The Jobs page records cutover revisions through `JobWorkflow.Cutover`, so every INT-062 stage rule applies unchanged. For the selected requirement, an engineer starts a new case at review or picks an existing case. The form is then filled from the case's current revision and offers the next stage. A stopped or closed case stays at its stage. Each revision carries the full reviewed state. The capture in view is pinned when it is saved for this client, and a saved run is chosen for the candidate stage. A case with no single current revision is refused until it has been deliberately reviewed.

Choices made where the contract was silent:
- Prerequisites and functional criteria are typed one per line. A met prerequisite starts with `[x]`. A criterion is `result | description | what was observed`, and the result must be Passed, Failed or NotRun; anything else is refused rather than read as not run.
- An approval or escalation with every field blank is recorded as none.
- The CLI keeps showing cases read-only. References to stored assessments remain the next slice.

## INT-065 — Outcomes cite stored assessments (INT-049 assessment references)

An observation may now cite one stored assessment as evidence, alongside the capture it pins. INT-060 left assessment references as a later slice. The reference is `{ kind: "assessment", id, sha256 }`, where the digest is the SHA-256 of the stored assessment file as canonical JSON, because assessments carry no integrity digest of their own. Any later change to that file, or its removal, sends the outcome to review with `EvidenceMissing`, as a changed capture already does. The desktop Jobs page offers the stored assessments of the capture in view under the loaded standard, each labelled with its finding for the selected requirement, and `bdit job` names a cited assessment.

Choices made where the contract was silent:
- A cited assessment must be of an intact capture (`snapshotIntegrity: intact`), under exactly this standard release and catalogue digest, and must hold a finding for the requirement instance. The capture it assessed is pinned with it. If the request also names a capture, the two must agree.
- A Pass may cite an assessment only when its finding for that instance is Compliant or RequiresManualReview. A finding of missing, partial, not enforced, unable to assess, licence unavailable, compliant with a deviation or not applicable would contradict the claim, so the citation is refused and nothing is written. Fail, Unknown and Pending may cite any finding. An attestation still cannot turn a failed collection into verified configuration.
- Dispositions and cutover revisions keep their existing evidence kinds. Citing an assessment changes no completion rule: a Pass still needs stored evidence and acceptance to verify a requirement.
- Older assessments written before `snapshotIntegrity` existed cannot be cited. Reassess the capture instead.

## INT-066 — One publisher for every release, pinned per version (R12, CLA-20261006-09)

`publish-preview18.yml` and `build/Publish-ValidatedPreview.py` hard-coded Preview.18's source, run, SHA-256, file count and notes, so every later release needed a new workflow. They are replaced by `publish-release.yml` and `build/Publish-ValidatedRelease.py --version <version>`, which read the same identities from a reviewed pin, `build/releases/<version>.json`, with notes in `build/releases/<version>.md`. Every check the Preview.18 publisher made is kept: the run is a successful original-repository `build.yml` push of the pinned commit on the pinned branch, with successful build/standard/secrets jobs and unexpired review artifacts; the ZIP matches the recorded SHA-256 and its sidecar; extracted-package evidence and `VERSION.json` identify the same source, version, standard, clean tree and .NET 10 runtime; published releases and tags are never replaced or repointed; uploaded assets are verified by server digest.

Choices:
- A pin is still a reviewed source change, so publishing a new version cannot skip review. Pins are validated strictly (unknown or missing members, malformed SHAs/run IDs/versions, Markdown paths outside the repository) in CI, with negative tests.
- Dispatches are refused unless they run from `main` or `integration`, so an unreviewed pin on a feature branch cannot be published even if the `release` environment has no reviewers yet.
- The version input reaches the script through an environment variable, never an expression inside `run`, so input text cannot execute.
- Prerelease is derived from the version (`-preview.` publishes as a prerelease, not latest); a version without it publishes as the latest release.
- Generated notes always end with the ZIP SHA-256, source, standard and run, and with the unsigned internal distribution route William chose on 7 October: compare the separately published fingerprint, and have the security owner allow the build by policy (normally a file-hash rule). No application-control bypass.
- Preview.18's pin is kept as a record. The publisher refuses to touch a published release, so it cannot republish it.


## INT-067 — Completion must account for unresolved writes and conflicting requirement identities

PR #36 enforces INT-049 completion and stable requirement-history rules after reproducing AST-20261008-01/02 against integration `8f0285a`. It does not introduce a persisted schema, execution authority or reconciliation override. Completion reuses the existing strict evidence guards for the job's requirement controls, plus the existing tenant-wide reviewed-change/LAPS uncertainty guards. Existing deployment plan replay checks retain their ordering and scope. An unreadable or modified run remains a blocker, not empty success.

A second semantic/control identity for an already recorded instance is refused within its outcome or decision history. Previously persisted conflicting groups block completion without rewriting originals. The claim remains separate from deployment authority. Cross-record-kind identity and cutover evidence review remain in the ongoing post-merge review; these two fixes do not claim that the complete review or human/live gates are finished.

### INT-067 follow-up — predecessor cutover evidence

AST-20261008-03 reproduces a closed case retaining its claim after its candidate run is deleted. PR #36 therefore verifies all attached stage references against their original digests while projecting a case. Current-stage freshness/material/due checks remain separate: historical overdue times do not themselves invalidate a newer reviewed stage, but missing/modified stage evidence requires review. No new record schema, migration, write or execution permission is introduced.

### INT-067 follow-up — truthful requirement rows

The projection's duplicate identity groups now explicitly need review. Completion does not choose an authoritative outcome/decision from those groups. Conflicting histories and unresolved tenant write history produce Outstanding rows as well as a blocked job claim; a hidden first group or an old Pass must not leave a misleading Verified row. Existing immutable records and their raw status remain available for review, and no reconciliation is performed by reading. The two additional row assertions were observed failing before correction.

### INT-067 follow-up — readable refusal on inaccessible evidence

Responding to Claude's independent review of e433513, completion catches IOException and UnauthorizedAccessException from write-history reads and returns a visible blocked/outstanding projection. An exclusively locked run was observed raising IOException before the fix; the regression also verifies completion resumes once the file is unlocked. No automatic retry, broad exception swallowing, reconciliation override or execution-boundary change is introduced.

## INT-068 — Measure the existing explicit-connect acquisition policy

PR #37 isolates the existing silent-first choice into an internal test seam used by MsalAuthenticator itself. It owns no cache, session, registration or resource and is never called by mid-operation renewal. Only MsalUiRequiredException may lead from silent acquisition to interaction during an explicit connect; cancellation and other failures propagate without interactive retry. More than one matching cached account requests an explicit chooser while retaining the known hint. Existing identity verification, Quick Connect freshness, DPAPI cache retention and disconnect contracts remain unchanged. Prompt evidence in AUTHENTICATION-FLOW distinguishes inspected before behaviour, tested after policy and unrun Microsoft/live behaviour. No permission or persisted-schema change.

## INT-070–072 — Proposed scoped-check, report-evidence and script contracts

Decision-only PR #39: [MODULE-REPORT-SCRIPT-CONTRACTS-2026.10.08](MODULE-REPORT-SCRIPT-CONTRACTS-2026.10.08.md). Pending review/merge; not settled or implemented. INT-070 defines shared registered dependency routing and distinct partial scoped-check evidence that cannot authorise writes. INT-071 separates large strict report evidence from historical configuration/Exchange captures and distinguishes observed quotas from verified entitlement. INT-072 defines reviewed typed script manifests and bounded read-only execution, with Exchange changes unconditionally copy-only. No existing evidence/catalogue bytes, permissions, cache contracts or blocked write boundaries are changed by this document. New report access remains proposed/ungranted. Dependent code starts only after the coordination contract-merge gate.


## INT-074 — Portable offline CLI hosting (settled: PR #39 merged; implementation is INT-079)

PR #39, merged as `3f946ae` (see HANDOVER), settled dispatching the existing read-only CLI through the single portable application executable, before desktop workspace/session initialisation, with a `bdit.cmd` launcher and no second executable. When it merged, the package intentionally excluded the developer/CI CLI; a candidate must not be described as shipping `bdit` until it contains the INT-079 implementation and has its own exact-source package evidence. Settling the contract is not native, human or live acceptance. Preserve no-authentication/no-write boundaries, redirected output and actual exit codes, and fail rather than selecting another host if application control blocks execution. Require native extracted-package tests and fresh candidate proof. This shared packaging/hosting change needs independent review and merge before implementation; publisher ownership and publication authority remain unchanged. Details and acceptance checks: [hosting contract](MODULE-REPORT-SCRIPT-CONTRACTS-2026.10.08.md#int-074--portable-offline-cli-through-the-existing-application-host-settled-by-pr-39).

## INT-069 — Engineer wording without changing workflow authority

PR #38 uses Check result, Decision about existing protection and Replacement stage as display labels for the existing outcome/disposition/cutover keys. Stored enum values, IDs, approval semantics and engine services are unchanged. Requirement names lead the table and copied details retain exact IDs. Disabled recording actions show their selection/busy prerequisites and next step. The page explicitly identifies prerequisite/effectiveness input as engineer-recorded evidence, not automated execution. OPERATOR-START no longer calls the merged Jobs surface future work. No human/live acceptance gate is closed by a render or source test.


### INT-069 follow-up — requirement viewport on smaller windows

Visual review of the actual PR #38 native artefacts at `c384947` found that the fixed 150px Jobs list left only one requirement row visible at 1180×640. A populated-job native check now counts fully visible requirement rows, intersecting their bounds with table and page viewports; it must inspect all three sizes and fail below two rows. The Jobs list fits its contents within a 64–100px bounded viewport rather than reserving unused space, and the page introduction is shorter. Requirement names and exact IDs remain accessible; long job lists retain scrolling. OPERATOR-START also explains that unresolved tenant-wide reviewed/LAPS operations or historical control writes can block completion, following Claude's PR #36 review. Native validation is recorded separately from pending newcomer, Narrator and physical-scaling acceptance.

Native regression proof: Windows push run `37709019773` at `cc124ca` failed specifically with “jobs 1180x640 · 0 fully visible requirement rows”; the other two sizes passed that check. The layout fix is being validated against the same unchanged assertion. Do not count the expected failing run as candidate acceptance.


### INT-069 follow-up — safeguard warnings must not displace requirements

Combined Windows run `37710984578` at `ab323d3` reproduced another cramped viewport: new unresolved-write blockers displaced requirements at 1180×760 and 1180×640. The same minimum-two-full-rows assertion is retained. Full job blockers now lead the independently scrolling details pane in a bounded, selectable read-only text field; no reason is removed or truncated from the underlying text. The harness verifies that a blocked job exposes exactly the full view-model blocker text, visibly and read-only. This is a layout fix, not a weaker completion or evidence rule.

## INT-073 — Full HTML inventory of existing captured data

PR #40 adds a shared HTML configuration renderer and offline bdit inventory command using the existing TenantSnapshot schema and ReportExporter. It introduces no report-evidence schema or permission/collection route and therefore does not depend on the proposed INT-070–072 wrappers. Friendly names lead; all returned raw properties/assignments, capture provenance and collection limitations remain visible. Domain/user/device fields absent from the current capture are explicitly unknown, not inferred. Existing Exchange schema/tenant validation preserves separate resource/time boundaries; no DNS call occurs. The CLI uses the existing primary integrity reader, rejects a release override and remains offline with unchanged no-authentication/no-write conformance checks. Desktop Reports integration is separate. A source inventory guide records the current developer-only CLI exposure; portable hosting/desktop integration still need their own reviewed work. Claude's publisher is untouched.


## INT-075 — Numbered acceptance without expanding execution authority

PR #41 derives docs/LIVE-ACCEPTANCE-RUNSHEET.md from CONTROLLED-ACCEPTANCE. It preserves separately approved setup/read/candidate/pilot/recovery stages and prepares new-client, legacy, repeat-review and handoff journeys. Expected results and a sanitised response template distinguish source/synthetic/native/human/live evidence, service readback and effectiveness. New report/scoped/script/navigation steps remain blocked until present in the exact candidate; all unrun gates stay open. No second capability matrix, source contract, permission grant or publication authority is introduced. Current portable CLI exclusion is explicit. Agent development never performs these live steps.


## INT-079 — Portable CLI implementation under merged INT-074

PR #45 implements the approved single-executable hosting contract: managed CLI dependency with no apphost, early --cli dispatch before desktop context, inherited output-handle preservation and bounded parent-console attachment. No console allocation, alternate host, authentication or writes. The launcher retains the caller's current directory and exit code. Existing CLI logic and conformance tests are reused, not reimplemented. Fresh extracted-package verification requires actual help, pipe/file output, spaced synthetic inventory/report/job inputs, failure codes, and no desktop startup/cache side effects. The unchanged desktop first-launch tests remain required. Packaging changes need fresh exact-source proof; existing publisher pins are immutable. No version bump or publication is authorised by this source work.


INT-079 build correction: Windows PR run `37722388394` at `eaa4402` failed NETSDK1067 because the self-contained application publish propagated SelfContained=true to the executable CLI project while its apphost was disabled. During that publish, compile the same CLI source as a managed library exposing Program.Main; ordinary developer builds retain the executable DLL entry point. Do not disable SDK reference checks or introduce a second executable. The same extracted-host/exit/output/CLI conformance assertions remain required; the failing run is not acceptance.

Local Windows cross-publish confirms the sole toolkit application apphost plus the .NET runtime's existing `createdump.exe` diagnostic, recorded in the official runtime pack's RuntimeList.xml. Preserve that runtime component and exact stage/checksum verification; deny any additional application executable. This clarifies “one application executable”; it does not introduce a second CLI host or permit using the runtime diagnostic as a bypass. Cache absence checks inspect the tenant-local data tree, not shipped MSAL runtime DLL filenames.

### INT-079 follow-up — Claude review corrections (CLA-20261008-14 to -19)

Responding to Claude's review of `37d9d56`. The console bridge now decides handle selection through a small `IStandardHandles` seam: it attaches to a parent console only when stdout or stderr is missing, restores inherited valid handles afterwards, and refuses (exit 3) only when no stdout exists after that attempt. A missing stderr no longer refuses the command; error text then shares the stdout writer so no refusal is lost, and the CLI's own exit code is kept. App tests cover all four valid/invalid stdout/stderr combinations, with and without a parent console. Package verification adds piped stdout with stderr inherited from the calling process (recording that handle's type), requires the synthetic tenant name and ID in both generated HTML files, matches each printed path to its generated file, requires empty stderr for successful commands, and allows only `app\createdump.exe` besides the application executable. The CLI project refuses a standalone self-contained publish rather than silently producing a library; the App's self-contained publish, which only builds it, is unaffected. An interactive console run of `bdit.cmd` remains a manual Windows gate: CI has no interactive console, so these checks do not prove display in an engineer's console window.
