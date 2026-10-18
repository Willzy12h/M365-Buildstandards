# BuildStandard — vendor capability comparison and recommended additions

**Researched:** 18 October 2026. **Source:** `Willzy12h/M365-Buildstandards`, canonical main at `2e4a0635ddfdf28330f8271942d53fb2a8ca3b0a`.

## Recommendation

BuildStandard already has substantial assessment, standards and controlled-deployment functionality. Its most valuable gaps are **operational breadth and evidence that a deployment covers the intended users/devices**, rather than another configuration engine or an interface rewrite.

Borrow Lighthouse's coverage-oriented deployment journey, CIPP's practical administration tasks, CoreView's configuration reuse, AdminDroid's reporting depth, Maester's scenario testing and SkyKick/ConnectWise's guided workflows. Keep the existing native Windows application, shared engine and explicit approval/evidence boundaries.

This review excludes hosting, billing, resale and platform infrastructure. It recommends additions; it does not authorise or implement them.

## Evidence and limits

I fetched current main and inspected a separate source archive without moving the active checkout. The repository API showed no open PRs. I inspected source registries, UI routes, standards, collection/deployment boundaries and guidance, and read current official vendor and Microsoft documentation. No tests, builds, CI, tenant authentication, Graph/Exchange operations or client-domain DNS lookups were performed. Repository files and GitHub metadata were unchanged.

Vendor capabilities below are **documented offerings, not independently tested claims**. Public documentation may lag availability or depend on edition/licensing. BuildStandard capabilities marked present are implemented in source; this is not fresh human/live acceptance.

The old SkyKick Cloud Manager/Security Manager pages were inaccessible. ConnectWise's acquisition announcement and current Cloud Automation, SaaS Security and Cloud Services pages provide the comparison instead. The old Simeon landing page was uninformative; the configuration comparison uses current CoreView Configuration Resilience documentation, without assuming exact equivalence between historical editions.

“Missing” means no corresponding complete workflow was found in the inspected routes, registries, implementation and guidance. It does not assert that no related helper or historical script exists anywhere.

## What is already present

The current `2026.10.15` catalogue contains **93 controls**. The script registry contains **33 entries: four registered read-only Run entries and 29 standalone Copy/Save entries**. Counts describe coverage, not live validation.

| Area | Current source capability |
|---|---|
| Connection/access | Quick Connect, WAM/MSAL, protected cache, tenant/account binding, assessment/deployment separation; GDAP sign-in with an explicitly selected client. |
| Assessment/discovery | Configuration capture, full HTML configuration inventory, versioned assessment, scoped checks, naming/ownership audit and licence-scope checks. |
| Planning/standards | Immutable catalogues, lineage, upgrade impact, client parameters, deviations, selected findings into plans and limited supported policy imports. |
| Execution/recovery | Complete before-evidence, exact approvals, single-use plans, uncertain-write blocking, readback and controlled recovery. Separate CA candidate creation/report-only/activation and Intune assignments already exist. |
| Engineer workflow | Jobs, observations, legacy dispositions, cutover/completion history, drift, protected evidence transfer and support exports. |
| Graph reports | Users/licences, Intune devices, MFA registration, sign-ins, directory audit, application credential expiry and observed application consent. Access/data limits remain explicit. |
| Exchange/Purview | Integrated read-only configuration capture, offline import, DKIM/DMARC observations and inert reviewed manual change proposals. |
| Registered mailbox reports | Inventory/size/quotas/archive, protocol settings, forwarding settings and hold settings, with typed forms, bounded processes and local evidence/exports. Experimental/live-unverified. |
| Mailbox capacity | Observed quotas and size separated from licence eligibility, including 100 GB primary-mailbox reporting. |
| Documents/CLI | Engineer/client reports, standard documents, JSON/CSV/Excel exports, offline CLI and contained WebView2 catalogue reference. |

Do not rebuild these as new features. In particular, baseline deployment, reporting, versioned standards, deviations, GDAP sign-in, script forms and mailbox quota reporting are already present.

Important limits:

- The CLI is deliberately offline: it does not authenticate or run tenant scripts.
- GDAP sign-in does not list partner customers or manage delegated relationships.
- Most Exchange script families are Copy/Save only; only four have registered Run adapters.
- Exchange mutations remain outside integrated execution. A script button does not resolve that contract.
- Assignment configuration is not proof of effective membership, installation or protection. Filters and additional targeting can require manual review.
- Deviation owners, approvers, reasons and review dates already exist. Consolidated review is the opportunity, not a second exception register.
- Supported policy imports are deliberately narrow, not arbitrary tenant cloning.
- macOS deployment remains unsupported and blocked, as requested.

## Vendor comparison

| Product | Relevant documented features | Best lesson for BuildStandard |
|---|---|---|
| **CIPP** | User templates/offboarding, staged baselines and cross-tenant alignment, CA gap analysis/What If, licence optimisation, GDAP relationships, mailbox/Teams/SharePoint administration. [C1–C11] | Task-oriented workflows and a broader practical administration/reporting surface. |
| **Microsoft 365 Lighthouse** | SMB baselines, deployment tasks showing affected-user coverage, account management, device compliance, threats and service health. [M1–M4] | Make it clear who is targeted, excluded, unlicensed, non-compliant or still unknown. |
| **Microsoft Partner Center** | Customer directory/export, delegated access, user/licence administration, service management and support requests. [M5–M7] | Customer import, tenant selection, GDAP readiness and correct portal/support links. Keep ordering/billing outside the toolkit. |
| **SkyKick / ConnectWise Cloud Services** | Cloud Automation offers reusable PowerShell workflows, point-and-click tasks and activity logging; SaaS Security offers discovery/remediation workflows and monitoring. Backup/migration are separate products. [S1–S4] | Curated task recipes and per-step results, reusing the current registry rather than introducing arbitrary execution. |
| **CoreView** | Configuration templates, drift, configuration backup/restore, Dev/Test/Prod change management, user lifecycle, licence optimisation and SharePoint governance. [V1–V4] | Reviewed pack promotion, explicit tenant remapping, dependency handling and honest restore coverage. |
| **AdminDroid** | Detailed usage/licence reporting, group-based licensing, audit and SharePoint reports. [A1–A2] | Join actual usage, access and configuration into a decision an engineer can make. |
| **Octiga** | Customisable baselines, deviations, security monitoring, remediation and client reports. [O1] | Prioritised review cases and recurring review packs; not unattended enforcement while the local app is closed. |
| **Maester** | Security tests, CA scenarios, compliance mappings, readable reports and remediation links. [T1] | Scenario validation and optionally importing separately labelled external test evidence. |
| **Microsoft365DSC** | Configuration-as-code, snapshots, comparisons, cloning and drift across broader workloads. [T2] | Selective format mapping/reference, not a second unrestricted deployment engine. |

Microsoft 365 Lighthouse is not Azure Lighthouse, and Partner Center is not an M365 policy deployment engine. Their roles overlap but are not interchangeable.

## Recommended additions

Effort is relative: **S** mainly presentation/import over existing evidence; **M** a bounded new collector/schema or substantial projection; **L** multi-workload orchestration or a new execution contract. These are judgements, not delivery dates. Read-only features can still need new consent/RBAC and data-handling review.

### 1. Deployment coverage and guided rollout — highest fit (L)

**Gap:** creation, assignment, activation and readback are present, but there is no comprehensive view proving coverage across the intended user/device estate.

**Add:** intended population, observed targeting/exclusions, unlicensed users, unresolved filters/membership and available per-user/device results. Connect existing candidate → review → pilot → verification → broader rollout operations through jobs and cutover records.

**Benefit:** answers “the policy exists, but did we finish this client?”—especially useful for legacy backfill. Lighthouse's deployment plan explicitly distinguishes excluded, not targeted, not licensed and non-compliant users. [M2–M3]

**Guardrails/acceptance:** unsupported targeting remains unknown; service propagation is visible. Never advance because a timer expired. Preserve disabled CA candidates and exact exclusions, and unassigned Intune candidates. An engineer must be able to distinguish configured, assigned, read back, observed effective outcome and not checked.

### 2. Integrate the existing Exchange report families (M–L per family)

**Gap:** mailbox/calendar permissions, message trace, inbox rules, groups and policy reports often exist as standalone scripts, not complete Run/report workflows.

**Add first:** mailbox/calendar permissions, inbox-rule/forwarding investigation and message-trace search, then group membership and selected policy reports. Reuse typed forms, owned processes, result recovery and local exports.

**Benefit:** answer “who can access/send as this mailbox?” or “why did mail not arrive?” without manually assembling PowerShell.

**Guardrails/acceptance:** permission rows need principal/object composite identities; logs need date-range, pagination, retention and truncation semantics. Preserve every partial/error state and exact tenant/account/resource binding. `exo.sharing` is Exchange sharing, not SharePoint file access. No integrated Exchange mutation is implied. [C1]

### 3. Broader domain/email readiness (M)

**Already present:** accepted domains, actual DKIM selectors and DKIM/DMARC DNS observations.

**Add:** review all selected domains, MX/provider expectations, SPF syntax and lookup-budget analysis, enrolment/registration CNAMEs and a DNS-owner change worksheet.

**Benefit:** catch common new-client/legacy setup gaps and make manual DNS work explicit. [C8]

**Guardrails/acceptance:** a non-Microsoft MX may be a legitimate security gateway. Records do not prove real-message signing/alignment. No automatic DNS changes or blanket DMARC-reject recommendation. Any later external lookup needs its appropriate authorisation.

### 4. SharePoint, Teams, OneDrive and guest-access inventory (L)

**Gap:** no integrated collaboration permissions/sharing report is registered. App consent and mailbox permissions do not cover it.

**Add:** site/Team ownership, guests, broad grants, broken library inheritance, ownerless groups and selected sharing links. Start at site/library level; make costly item/link scans explicit and separate.

**Benefit:** access reviews at takeover, leaver handling and before wider Copilot adoption. [C6–C7, V4, A2]

**Guardrails/acceptance:** exact identities; per-site coverage/failure timestamps; no document content collection by default. Selected-site access cannot be called tenant-wide. Direct grants are not complete effective access: membership and other access paths matter. New workload access requires review. Cleanup should initially produce proposals.

### 5. Licence usage and entitlement review (M–L)

**Already present:** assigned products/service plans, scope checks and mailbox capacity/eligibility.

**Add:** workload activity windows, inactive/disabled-account review, direct versus inherited licences, overlapping products and evidence-backed reclaim/downgrade suggestions. Consider mailbox/archive/hold requirements and required security capabilities together.

**Benefit:** a practical review queue, rather than only a licence list. [C5, V3, A1]

**Guardrails/acceptance:** lack of app activity does not prove a licence is unnecessary. Business Premium security/management entitlements matter even when Office apps are unused. Shared mailboxes may legitimately require licences. Respect privacy-obscured report identities; do not automatically change privacy settings. Show report refresh date and missing evidence. No automatic licence removal, purchase or invented savings.

Microsoft documents `Reports.Read.All` plus an appropriate delegated role for usage reports. Downloads use a short-lived preauthenticated URL: a new safe download path must avoid forwarding Graph credentials or logging the URL. Existing licence reads do not automatically authorise this access. [M8–M9]

### 6. Client portfolio and partner readiness (S–M initially)

**Already present:** client profiles, per-client evidence and GDAP sign-in.

**Add first:** local client list with last assessment, standard version, incomplete reads, outstanding changes and overdue deviations. Preview/import Partner Center's customer CSV, deduplicating by tenant ID rather than name. [M5–M7]

**Add later:** read-only GDAP expiry/status/role inventory, explaining missing access for a selected operation. [C9]

**Benefit:** less spreadsheet work and less retyping, with an obvious next client to review.

**Guardrails/acceptance:** imported records are unverified until connected. A reseller relationship does not prove GDAP, and GDAP alone does not prove application consent. Each live refresh is tenant/account-bound. A local cached summary is not continuous fleet monitoring or bulk-write authority.

### 7. Joiner, mover and leaver workflows (L; material expansion)

**Gap:** there is no complete user-lifecycle workflow. Existing group/policy operations and jobs do not constitute one.

**Add:** named templates, typed inputs, dependency preview and per-step outcomes. Joiner: identity, location, approved licence, groups/access. Mover: exact additions and removals. Leaver: ownership/access inventory, retention and mailbox decisions, reviewed session/sign-in actions, handover and licence review.

**Benefit:** repeatable daily work for a second engineer with fewer missed dependencies. [C2–C3, M4, V2]

**Guardrails/acceptance:** distinguish synchronised and cloud-managed users; never copy another user's access indiscriminately. Check hold/archive/capacity before licence removal. Exchange steps remain manual/copy-only until their execution contract changes. Partial execution must reconcile per step, never offer blind “re-run everything”. Start with discovery and a generated reviewed checklist, then approve individual write actions.

### 8. Conditional Access scenarios and coverage (M–L)

**Already present:** property-level assessment, targeting/exclusions and controlled CA operations.

**Add:** scenarios for administrators, ordinary users, guests and emergency identities across relevant Windows/mobile access conditions. Display policy IDs/state, applicability, exclusions, licence limits and missing inputs. CIPP and Maester provide useful examples. [C4, T1]

**Current Microsoft API:** Graph v1.0 documents `POST /identity/conditionalAccess/evaluate`, using `Policy.Read.ConditionalAccess` or higher `Policy.Read.All`. BuildStandard already names the latter for some collection routes; actual consent still needs checking. [M10–M11]

**Important limits:** the existing transport needs a dedicated reviewed allowlisted evaluation contract, not permission for arbitrary assessment POSTs. Microsoft's What If evaluates enabled/report-only policies, **not disabled candidates**, and does not evaluate service dependencies. It is not proof of a real sign-in or a simulation of arbitrary uncommitted JSON. Any offline candidate projection must remain distinctly labelled. Preserve all write guards.

### 9. Easier standards-pack authoring/comparison (M–L)

**Already present:** catalogue releases, lineage, upgrade impact, client parameters and restricted policy import.

**Add:** supported-setting editor, side-by-side comparison of captures/policies, parameter/prerequisite validation and explicit base-standard/client-override presentation.

**Benefit:** turn a useful client configuration into a reviewed reusable internal standard without copying raw JSON and foreign IDs. [C10, V1, T2]

**Guardrails/acceptance:** no arbitrary code imports or ownership-by-name. Configuration equality is distinct from assignment equality. Historical catalogues remain immutable; changes need new releases/lineage. Expose conflicting packs/overrides. Prefer supported Windows/Business Premium families over breadth. Do not add Microsoft365DSC as an automatic second execution engine.

### 10. Recurring review and handover pack (S–M)

**Already present:** reports, history, jobs, deviations and credential-expiry reporting.

**Add:** one bundle showing changes since last review, newly unknown checks, expiring credentials, overdue deviations, unresolved outcomes and next actions; short client summary plus detailed engineer evidence.

**Benefit:** repeatable periodic reviews and engineer handover with less manual assembly. [C11, O1]

**Guardrails/acceptance:** preserve differences in standard version and collection scope when comparing history. Changes alone do not identify their author. Show matched, accepted deviation, unknown and not applicable separately. “Due when opened” is feasible; notifications while the app is closed are a different architecture.

### 11. Service health and escalation context (S for links; M for API)

**Gap:** prerequisite diagnostics are not a Microsoft service-incident feed.

**Add:** relevant incidents/advisories, tenant-specific portal links, exact failure timestamps/request IDs and sanitised escalation export.

**Benefit:** helps distinguish a service outage, missing access and configuration error before making changes. [M1, M5]

**Guardrails/acceptance:** matching an advisory does not prove causation. API reads require `ServiceHealth.Read.All`, which is not established by existing access. Safe portal links provide value first. [M12]

### 12. Selected security findings and incident context (M–L; later)

**Gap:** sign-in logs/MFA registration do not provide Defender incident triage, Secure Score mapping or complete identity-risk analysis.

**Add selectively:** read-only incident context/portal links, high-priority identity/application findings, versioned benchmark mappings and optionally separate Maester evidence import. Reuse existing application-consent/expiry reports.

**Guardrails/acceptance:** full Identity Protection risk APIs require Entra ID P2 according to Microsoft; Business Premium/P1 alone is insufficient. Defender product entitlements and the client's actual protection stack matter. Do not silently replace the current ESET-oriented standard. Scores, registration, enforcement and observed protection are separate. Incident-response writes are additional scope. [M13, T1, O1]

## Useful peer features to defer or keep external

| Feature | Recommendation |
|---|---|
| Bulk users/password resets/group administration | Valuable after a lifecycle contract; start with exact single-tenant previews and per-object results, not cross-tenant bulk writes. |
| Device sync/reboot/retire/wipe and recovery-secret retrieval | Richer status and portal links first. Wipe/reset and LAPS/recovery-key retrieval need separate security/custody decisions. Keep endpoint repair separate; macOS remains blocked. CIPP documents the broader actions. [C12] |
| Tenant-wide configuration restore | Existing evidence backup and controlled recovery are narrower. Publish a recoverability matrix before promising a complete tenant rewind. Not every ID/effect is recreatable. [V1] |
| M365 content backup and migration | Use specialist products. Toolkit configuration snapshots do not back up mailbox/SharePoint/OneDrive contents. A coverage/restore-test reference can fit the handover. [S4] |
| Teams Phone/call queues | Add only for a concrete recurring service need. |
| DLP/retention/sensitivity-label writes | Require policy-owner decisions, licences and reviewed execution/recovery semantics; existing observations do not make them ready to enable. |
| Continuous monitoring/auto-remediation | A closed portable app cannot provide this. Keep deliberate refresh and reviewed changes unless the product architecture is separately expanded. |
| Generic no-code workflow builder/arbitrary PowerShell | Avoid now. Curated registered operations are more supportable and preserve the execution boundary. |
| Billing/orders/PSA platform | Outside the requested scope. |

## Recommended sequence

**Reuse first:** local client review list/CSV import; integrated mailbox/calendar permissions and message trace; recurring review pack; clearer supported deployment coverage; wider domain readiness.

**Then bounded new read-only workloads:** licence usage, site/library/guest inventory, CA scenarios, GDAP expiry/roles and optional service health.

**Then separately approve new writes:** joiner/mover/leaver and selected administration operations, plus wider deployment adapters where the business need is clear.

Deployment coverage has the highest strategic fit for build/backfill. Existing report integration and local portfolio/handover views are the best reuse opportunities. User lifecycle has high recurring value but is a substantial execution expansion.

For each addition, require that a second engineer can choose the correct client, understand evidence age/access limits, complete a bounded task, and leave a result distinguishing success, partial completion, not attempted and could not check.

Do not silently replace agreed policies with vendor defaults. SSPR, endpoint security, CA targeting, retention and supported platforms require explicit standard decisions. No cost saving, delivery date or product-wide parity claim is made.

## Sources

The following official pages were successfully retrieved. The working research directory retains page text, URLs and content hashes. Product marketing is treated as a description of offerings, not measured effectiveness.

### CIPP

- C1: [Overview](https://docs.cipp.app/) and [complete documentation index](https://docs.cipp.app/llms.txt).
- C2: [User templates](https://docs.cipp.app/user-documentation/identity/administration/user-defaults.md).
- C3: [Offboarding wizard](https://docs.cipp.app/user-documentation/identity/administration/offboarding-wizard.md).
- C4: [CA gap analysis](https://docs.cipp.app/user-documentation/tenant/security-simulator/conditional-access.md) and [sign-in situations](https://docs.cipp.app/user-documentation/tenant/security-simulator/situations.md).
- C5: [Licence optimisation](https://docs.cipp.app/user-documentation/tenant/reports/list-licenses/optimization.md).
- C6: [Sharing report](https://docs.cipp.app/user-documentation/teams-share/reports/sharing-report.md).
- C7: [Permissions report](https://docs.cipp.app/user-documentation/teams-share/reports/permissions-report.md).
- C8: [Domain analyser](https://docs.cipp.app/user-documentation/tenant/standards/domains-analyser.md).
- C9: [GDAP relationships](https://docs.cipp.app/user-documentation/tenant/gdap-management/relationships.md).
- C10: [Intune comparison](https://docs.cipp.app/user-documentation/tools/intune-tools/compare-policies.md).
- C11: [Baseline alignment](https://docs.cipp.app/user-documentation/tenant/baselines/alignment.md) and [staged baselines](https://docs.cipp.app/user-documentation/tenant/baselines/template.md).
- C12: [Device management](https://docs.cipp.app/user-documentation/endpoint/mem/devices.md).

### Microsoft

- M1: [Lighthouse overview](https://learn.microsoft.com/en-us/microsoft-365/lighthouse/m365-lighthouse-overview?view=o365-worldwide) and [requirements](https://learn.microsoft.com/en-us/microsoft-365/lighthouse/m365-lighthouse-requirements?view=o365-worldwide).
- M2: [Lighthouse baselines](https://learn.microsoft.com/en-us/microsoft-365/lighthouse/m365-lighthouse-deploy-standard-tenant-configurations-overview?view=o365-worldwide).
- M3: [Deployment plan and coverage](https://learn.microsoft.com/en-us/microsoft-365/lighthouse/m365-lighthouse-review-deployment-plan?view=o365-worldwide).
- M4: [Lighthouse user management](https://learn.microsoft.com/en-us/microsoft-365/lighthouse/m365-lighthouse-users-page-overview?view=o365-worldwide).
- M5: [Partner Center Customers workspace](https://learn.microsoft.com/en-us/partner-center/customers/customers-overview).
- M6: [Customer list and CSV export](https://learn.microsoft.com/en-us/partner-center/customers/see-your-customer-list).
- M7: [GDAP](https://learn.microsoft.com/en-us/partner-center/customers/gdap-introduction).
- M8: [Graph usage report, permissions and download](https://learn.microsoft.com/en-us/graph/api/reportroot-getoffice365activeuserdetail?view=graph-rest-1.0).
- M9: [Usage-report privacy](https://learn.microsoft.com/en-us/microsoft-365/admin/activity-reports/activity-reports?view=o365-worldwide).
- M10: [What If semantics/limits](https://learn.microsoft.com/en-us/entra/identity/conditional-access/what-if-tool).
- M11: [Graph v1.0 evaluation API](https://learn.microsoft.com/en-us/graph/api/conditionalaccessroot-evaluate?view=graph-rest-1.0).
- M12: [Service-health issues](https://learn.microsoft.com/en-us/graph/api/serviceannouncement-list-issues?view=graph-rest-1.0).
- M13: [Identity Protection licensing](https://learn.microsoft.com/en-us/entra/id-protection/overview-identity-protection).

### SkyKick / ConnectWise

- S1: [SkyKick acquisition](https://www.connectwise.com/company/press/releases/axcient-skykick-acquisition).
- S2: [Cloud Automation](https://www.connectwise.com/platform/saas-security/cloud-automation).
- S3: [SaaS Security](https://www.connectwise.com/platform/saas-security).
- S4: [Cloud Services product distinctions](https://www.connectwise.com/platform/bcdr/cloud-services).

### Other tools

- V1: [CoreView Configuration Resilience](https://www.coreview.com/configuration-resilience).
- V2: [CoreView user workflows](https://www.coreview.com/use-case/automate-user-creation).
- V3: [CoreView licence management](https://www.coreview.com/use-case/m365-license-management).
- V4: [CoreView SharePoint security](https://www.coreview.com/sharepoint-security).
- A1: [AdminDroid licence reporting/management](https://admindroid.com/microsoft-365-license-management-tool).
- A2: [AdminDroid SharePoint reporting](https://admindroid.com/microsoft-365-sharepoint-online-reporting).
- O1: [Octiga](https://www.octiga.io/).
- T1: [Maester](https://maester.dev/).
- T2: [Microsoft365DSC](https://microsoft365dsc.com/user-guide/get-started/introduction/).

### BuildStandard source evidence

All paths below refer to [the pinned main source](https://github.com/Willzy12h/M365-Buildstandards/tree/2e4a0635ddfdf28330f8271942d53fb2a8ca3b0a):

- `docs/GOAL.md`; `docs/OPERATOR-START.md`; `docs/EXCHANGE-PURVIEW.md`; `docs/AUTOMATION-COVERAGE.md` (the detailed table retains the historical 2026.09.30 heading; current control count was checked against actual 2026.10.15 JSON).
- `standards/2026.10.15.json`; `scripts/registry.json`.
- `src/BuildStandard.App/ViewModels/RouteCatalog.cs`; `Views/ConnectView.xaml`; `ViewModels/RunReportsViewModel.cs`; `ViewModels/DeviationsViewModel.cs`.
- `src/BuildStandard.Core/Reporting/GraphReportRegistry.cs`; `ApplicationReportRegistry.cs`; `ExchangeReportRegistry.cs`.
- `src/BuildStandard.Core/Models/ReviewedChange.cs`; `TenantProfile.cs`; `Deviation.cs`.
- `src/BuildStandard.Engine/Assessment/ApplicationDeploymentAssessment.cs`; `LicenceInventoryService.cs`; `Standards/DevicePolicyImporter.cs`; `Reports/MailboxCapacity*.cs`.
- `src/BuildStandard.Graph/GraphClient.cs`; `GraphRouteAllowList.cs`; `src/BuildStandard.Core/Graph/IGraphClient.cs`.
- `src/BuildStandard.Cli/Program.cs` (explicit offline CLI boundary).

## Remaining unchecked

No vendor trial or live API behaviour was validated. Existing human/live acceptance is unchanged. Proposed new workloads still need bounded permission/API/identity-schema design and meaningful tests when selected. No new consent, scope, implementation, release or tenant action is authorised by this report.
