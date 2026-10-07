# Final internal product review — 6 October 2026

**Recommendation:** finish this as a supported Windows engineer product, retaining .NET 10, WPF and the existing guarded engine. Make new-tenant builds, legacy backfill and repeat reviews coherent, resumable and evidence-backed. Complete a bounded release rather than starting a platform rewrite.

This is a proposal for the user's review. It authorises no additional implementation, consent, tenant action, merge or production publication. The user requested this review and the shared documents; runtime changes await scope approval. Four independent Astra source reviews informed the engineering, migration, architecture and release recommendations. These are analytical viewpoints, not interviews, a Claude review, a formal security audit or live acceptance.

Read this first, then the [completion plan](PRODUCT-COMPLETION-PLAN.md). The [feedback register](PRODUCT-FEEDBACK-REGISTER.md) tracks individual recommendations. The [agent handoff](PRODUCT-AGENT-HANDOFF.md) lets Claude or another agent review and continue without conversation history. Existing [coordination rules](AGENT-COORDINATION.md), decisions and claims remain authoritative.

## 1. Where the product is now

Reviewed source: **`2bf96a83846474a522b074479f07a3e076a5a03d`**, version **1.1.0-preview.17**. On 6 October, fetch and GitHub inspection confirmed that draft [PR #19](https://github.com/Willzy12h/M365-Buildstandards/pull/19), `astra/release-2026-09-12` → `integration`, is the only open PR across the three project repositories. It remains unmerged. `integration` is `5cadd3a070b0e308bdffc327dfc7954bd69b9e83`; `main` is `81b18f5240bb8c36e3b090cb599c6decf1f06d61`. The preview branch is the reviewed product, not an already released main-branch product.

| Area | Existing capability | Practical limit |
|---|---|---|
| Application | Self-contained .NET 10 WPF Windows x64 package; Core/Graph/Engine/App/CLI separation | Actual supported OS, module and identity combinations need a release contract |
| Standards | Immutable default 2026.09.30; 93 controls, 61 candidate recipes; generated standard/manual guides | Recipes are not 61 accepted end-to-end automations; manual and service/device checks remain |
| Connect/setup | Quick Connect; retained authentication through confirmation; tenant-aware setup; exact app selection, permission/grant and assignment checks | Real WAM/MFA, dedicated-app transitions, consent propagation and GDAP acceptance remain open |
| Assessment | Read-only collection, property differences, enforcement distinctions, equivalence evidence, licence/readiness information | Incomplete or unsupported reads must remain unknown; existing custom protection needs an engineer disposition |
| Changes | Selected guarded candidates, complete before evidence, digest-bound approval, durable intent, per-action outcomes | CA candidates remain disabled; Intune candidates remain unassigned; activation is separately reviewed |
| Recovery | Exact-ID ownership, drift checks, supported restore/delete/containment and accepted-write re-verification | No universal rollback; uncertain writes cannot be retried; device effects may need manual recovery |
| Exchange/Purview | Integrated delegated read capture, accepted-domain/DNS checks, separate evidence, import/export and inert proposals | Preinstalled supported module; live RBAC/authentication acceptance open; service writes are not integrated |
| Engineer continuity | Profiles, snapshots, mappings, deviations, manual checks and run history | No unified durable job/disposition ledger; state and binaries share a portable root |
| Reuse | Headless offline reports, catalogue exports and guarded imports | CLI cannot currently reproduce desktop assessment with Graph plus a separate Exchange capture |
| Release | Strict Windows CI, synthetic UI checks, exact package metadata/hashes and extracted launch/context-menu checks | Review artifacts are not an approved durable release channel; preview is unsigned |

The exact reviewed head has green [push CI](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/36843279081) and [PR CI](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/36843284168): **852 Engine + 112 App tests**, 42 native page/size checks, 71 commands, PowerShell parser negative control, and fresh portable startup/shutdown/right-click Copy. These were previously executed CI checks, rechecked through GitHub on 6 October; this review did not rerun them. The [completion register](COMPLETION-REGISTER.md) retains historical implementation evidence; the PR identifies the final-head package.

No agent live tenant/device acceptance is established. A fresh attempt to retrieve the synthetic UI artifact on 6 October was refused by artifact storage, so this review adds no visual screenshot acceptance. Existing UI checks are structural/automated evidence. Private user captures and the separate BDIT device/launcher archives are not committed, and that separate product is outside this review. The old `claude/M365-BuildStandard-Project-Context.md` remains excluded by the user's instruction.

## 2. What “finished” should mean

The proposed target is **M365 BuildStandard 1.1.0 GA for internal engineers**, with a named owner and an explicitly accepted capability set. An engineer should be able to:

1. Build a new client tenant to the applicable current standard through supported automation and clearly owned manual steps.
2. Review an established tenant, retain effective custom protection, backfill genuine gaps and document any separately approved migration.
3. Return later, understand changed requirements versus tenant drift, and resume outstanding work using fresh evidence.
4. Hand the job to another engineer without losing ownership, exceptions, unresolved outcomes or the reasons behind decisions.
5. Obtain one identifiable supported package, upgrade safely, and raise a reproducible support request.

A job is complete when every applicable control has a disposition, an owner and the required outcome evidence. A successful API write is one stage; enabled protection, application installation and device/sign-in effectiveness are separate stages. Unknown or untested outcomes remain visible. Manual completion is acceptable when it has a supported procedure and evidence, rather than a hidden instruction or an unsupported script.

GA does not mean every future Microsoft change is already implemented. It means a bounded product with support, servicing and trustworthy limits. Unaccepted actions must be labelled experimental/manual/unsupported and excluded from supported production execution. Any capability gate added to achieve that needs a reviewed contract; source inspection or two successful candidates cannot certify the whole catalogue.

## 3. Different points of view

### Internal engineer: “Help me complete the job, not remember twelve screens.”

**Recommendation R01:** make Overview a tenant completion workspace, with starting intentions **New tenant build**, **Review/backfill existing tenant** and **Repeat review**. Show one outstanding queue: tool action, engineer action, client/vendor dependency, blocked evidence, awaiting approval or functional check. Reuse existing pages and readiness calculations rather than replace the UI.

Save job intent, notes and evidence references. Reopening should restore the work to review, never restore old execution authority. An engineer should see why a row is untickable, who resolves it and the next relevant screen. A created disabled policy must still show activation and effective verification outstanding.

**Insight:** the biggest simplification is fewer decisions to rediscover, not fewer deliberate approval boundaries. See the current in-memory advice in [ShellViewModel](../../src/BDIT.TenantToolkit.App/ViewModels/ShellViewModel.cs#L178) and readiness projection in [DeployViewModel](../../src/BDIT.TenantToolkit.App/ViewModels/DeployViewModel.cs#L135).

### Senior migration engineer: “Preserve working protection while closing gaps.”

**Recommendation R02:** add a legacy disposition register: retain proven existing coverage; approved departure; add missing candidate; investigate conflict/unknown; manual work; propose a separate replacement. Bind each decision to tenant, standard, observed object IDs/settings, snapshot, engineer, reason and review date. Retained external objects are not managed-object mappings.

**Recommendation R03:** add source-to-target standard reconciliation and an upgrade-impact report. Current drift compares two captures against one standard. A standards upgrade must also compare the same capture against old and new requirements, identifying added, changed, retired and renumbered controls. Historical PRE IDs changed meaning; matching a reused ID must not carry ownership, a manual pass or an exception into a different requirement.

**Recommendation R04:** use a bounded cutover case for replacements. Record old/new protections, overlaps, real pilot scope, dependencies, functional success, recovery limits and separately approved retirement. Do not disable legacy protection when a candidate is merely created. CA state-only activation cannot invent a pilot target when its stored targeting is broad.

**Insight:** backfill is a convergence programme, not a bulk-create operation. The existing planner correctly blocks unmanaged overlap and active/assigned updates. Extend its review context; preserve its refusals. See [planner](../../src/BDIT.TenantToolkit.Engine/Planning/DeploymentPlanner.cs#L241), [mapping model](../../src/BDIT.TenantToolkit.Core/Models/Mapping.cs#L9), [drift](../../src/BDIT.TenantToolkit.Engine/Drift/DriftAnalyser.cs#L30) and [historical renumbering](../AUTOMATION-COVERAGE.md#retired-and-deferred-controls).

### Tenant administrator: “Explain the access I am approving.”

**Recommendation R07:** derive a concise workflow access matrix from the loaded standard and implementation: assessment, candidate deployment, app setup, activation/recovery, Exchange and Purview. Show required scopes, applicable roles/licences, exact app names/IDs, grants, engineer assignment and who supplies consent. Explain WAM sign-in versus browser administrator consent and any genuinely necessary additional token audience.

Accept a primary identity path first: client-owned account and dedicated assessment/deployment registrations after setup. Keep shared Quick Connect an explicit discovery/bootstrap choice. Accept partner/GDAP only for tested combinations. Add a preflight that reports missing module/version, assignment or permission prerequisites with an actionable next step; do not automatically install modules or escalate permissions.

**Insight:** “connected” must not imply “every collection is readable” or “deployment is configured.” Existing [setup](../APPLICATION-SETUP.md) is a strong basis. Reduce redundant prompts where tokens can legitimately be reused; make no guarantee about Microsoft MFA policy.

### Product/business owner: “Make this repeatable across the team.”

**Recommendation R15:** assign a product owner, standard owner, release owner, acceptance lead and evidence/support custodian. One person can hold several roles. Establish supported client assumptions, exception approval, retention/storage, support escalation and a modest servicing cadence. Record engineer time, blocked prerequisites, repeated incidents and overdue reviews after pilot use; do not invent productivity figures.

Use tenant profiles for client inputs and approved exceptions; use versioned standards for policy requirements. Avoid cloning a whole toolkit per client or silently editing the published standard. Start with the existing SME/Business Premium baseline and document licence/platform variations. Additional product presets need reviewed applicability and settings, not arbitrary assumptions.

**Insight:** a reusable internal business product includes operating responsibility and repeatable handover, not just a ZIP. Team adoption should be tested with a second engineer who did not author it.

### Support/on-call engineer: “Tell me what failed and what I can safely do.”

**Recommendation R09:** provide a previewable support bundle and copyable diagnostic details. Default contents should be package/runtime/standard identity, operation IDs/status, dependency checks and scrubbed diagnostics. Exclude token caches, credentials, profiles, tenant captures and full reports by default. Deliberate private-evidence inclusion needs content preview and the organisation's approved sharing channel; no automatic upload.

**Recommendation R15:** add an incident decision tree for not attempted, accepted but unverified, unknown/missing ID and damaged evidence. Identify who can reconcile and when independently approved portal/service recovery is needed. Keep original journals. Clearing evidence or retrying an uncertain write is not a recovery route.

**Insight:** recovery limits must be explainable by the second engineer. [Unresolved-write rules](../UNRESOLVED-WRITES.md) and [recovery](../RECOVERY.md) already supply the safety model.

### Security/change reviewer: “Show the scope, provenance and consequence.”

**Recommendation R05:** make manual outcomes and exceptions release-aware, with evidence references and renewal rules. Preserve historical attestations; distinguish automatic observations, engineer attestation and approved exceptions. A changed standard/object, reused control ID or overdue review must trigger visible re-review. An attestation cannot turn a failed collection into verified configuration.

**Recommendation R06:** prove upgrade/backup/handoff preserves tenant ownership and unresolved-write blockers. Keep authentication caches local to the engineer's identity; tenant evidence has different storage/retention needs. Define a designated workspace custodian or explicit checkout/handoff initially. Existing file locks do not establish safe concurrent editing of every record on a shared drive.

**Insight:** a checksum proves consistency with a reference, not publisher identity. DPAPI token protection does not encrypt all evidence. Preserve engine-enforced tenant, identity, integrity, freshness and ownership gates. This is a product-safety perspective, not a claim of a completed security scan.

### Release engineer: “Give engineers one release they can identify and service.”

**Recommendation R11:** maintain a capability/acceptance matrix per advertised control/action: assessment, create, activate/assign, effective verification and recovery. Include OS/module/identity/licensing scope, evidence, owner and status. Every production-supported action needs applicable live proof; manual, experimental, blocked and not-run states must be explicit.

**Recommendation R12:** prepare a durable approved release channel, with source commit, package SHA-256, runtime/dependency inventory, standard digest, CI evidence, acceptance scope, known limits, approver and previous release. Promote the tested immutable artifact; rebuilding requires a new identity and checks. Prefer Authenticode signing using an organisation-managed signing method; an approved controlled unsigned/hash-pinned internal route can be a bounded alternative. Do not bypass SmartScreen or application control.

A documented full-package upgrade is sufficient initially; an automatic updater is optional. Servicing the bundled .NET runtime/MSAL and module compatibility needs an owner and cadence. Retain accepted binaries/evidence beyond expiring CI review artifacts.

**Insight:** existing packaging is substantial. The missing layer is approval, support scope, distribution and servicing, not another build script. See [portable builder](../../build/Build-Portable.ps1) and [CI](../../.github/workflows/build.yml).

### Maintainer/integrator: “Keep one engine and small, tested extension points.”

**Recommendation R08:** restore desktop/CLI combined-evidence parity through a shared read-only assessment input/context and an optional Exchange input. WPF passes separate Exchange evidence; CLI currently passes a single snapshot. Test output parity using identical profile, mappings, deviations and Graph/Exchange fixtures; reject cross-tenant supplemental data. This is a concrete reuse gap, not a reason to rewrite the architecture.

**Recommendation R13, later:** improve future standard authoring/release diffs, then consider a build-time compiler if repetition warrants it. Keep published flat JSON and historical bytes immutable, with release-pinned sources and reproducible output. Do not make write transports executable from data.

**Recommendation R14, later:** extend the existing offline CLI with a selected-client batch/report roll-up. Show evidence age, incomplete clients, unresolved work and exceptions. No unattended credentials are needed for this first version. Add one import adapter only after choosing an actual team-used format and synthetic examples. Ticket/change integration can start as a local export containing plan digest and evidence references, without connecting or sending to an external system.

**Insight:** use existing Graph and Exchange boundaries. Broad Workspace refactors should follow a demonstrated seam, starting with the parity fix. A hosted portal, mandatory database, plugin runtime and live multi-tenant scheduler add a different operating model and are not GA prerequisites. The existing [roadmap](ARCHITECTURE-ROADMAP.md) and [disposition](ARCHITECTURE-DISPOSITION-2026.09.30.md) remain relevant.

### Newcomer, accessibility reviewer and independent agent: “Let me verify this without the author's memory.”

**Recommendation R10:** reconcile current instructions and test an unassisted engineer journey, keyboard, Narrator, display scaling, error recovery and interruptions. The shell still asks for typed confirmation although Preview.17 policy approval uses reviewed-tenant confirmation; current entry points mix Preview.15/16 labels. Dynamic announcements are a human-test hypothesis, not an established Narrator defect.

**Recommendation R16:** use one shared feedback register with stable IDs, evidence, decision, owner, implementation and independent verification. Claude reviews on its own branch; Astra answers each finding and integrates accepted corrections through reviewed PRs. Disagreements remain recorded with their rationale. A checked box requires proof of the stated acceptance, not agreement with the idea.

**Insight:** keep historical reports, but give people and agents one current read order. Avoid repeatedly copying obsolete counts and completion claims. Existing source coordination remains the authority; these documents supplement it.

## 4. Opinions that conflict, and the recommended decision

| Tension | Recommended resolution |
|---|---|
| Engineer wants one-click completion; change reviewer wants deliberate scope | Automate discovery, preflight, plan assembly and evidence; keep exact consequential approval and fresh engine validation |
| Migration engineer wants reuse; planner refuses unmanaged adoption | Retain verified external coverage through a disposition; do not grant ownership or patch permission |
| Business wants a final release; QA has outstanding live gates | Define supported actions and prove them; explicitly constrain unaccepted actions instead of calling everything production-ready |
| Team wants shared history; current storage is local JSON | Start with secure custodian/backup/handoff and tested upgrades; add central concurrent storage only for a demonstrated requirement |
| Maintainer wants structural cleanup; users need a release | Fix combined-evidence reuse and necessary workflow seams; defer wholesale refactoring/compiler work |
| Operations wants Exchange convenience; service writes are consequential | Accept integrated read capture first; retain reviewed manual procedures and inert proposals until a separate write design is approved |

## 5. Recommended implementation sequence

1. **A — Release contract and current guidance:** support/acceptance matrix, owners, primary identity path, corrected current instructions and recorded acceptance plan.
2. **B — Completion workspace and attestations:** resumable job state, clear ownership/next actions, version-aware manual outcomes and exceptions.
3. **C — Legacy backfill and upgrade impact:** dispositions, release lineage, requirement-change versus tenant-drift reports and bounded manual/supported cutover cases.
4. **D — Reusable evidence and integration polish:** combined Graph/Exchange parity, clear service/module preflight and reviewable change export.
5. **E — Upgrade, continuity and support:** safe tenant workspace/backup/restore/handoff, support bundle and incident/reconciliation runbook.
6. **F — Production release preparation:** reproducible release record, resolved dependency inventory, signing/distribution preparation and servicing process.
7. **G — Independent and operational acceptance:** negative regressions, two engineer journeys, actual Microsoft/device checks for supported scope, human usability and release decision.

Detailed dependencies and completion tests are in [the plan](PRODUCT-COMPLETION-PLAN.md). R13/R14 compiler, additional adapters and offline portfolio reporting are useful follow-ons, not dependencies that should keep this release perpetually open.

## 6. Autonomy, approval and collaboration

After scope approval, agents can autonomously inspect source, prepare bounded contract proposals, implement approved local work, add meaningful tests, build review artifacts, fix reproducible defects, update documentation and submit owned draft PRs. They should continue through ordinary failures and reviewer feedback within that scope.

Business decisions still need an owner: support scope, named responsibilities, evidence location/retention, access model, exception-renewal policy and signing/distribution method. Proposed defaults are internal Windows x64, existing SME/Business Premium baseline, dedicated client registrations, local secure workspace with explicit handoff, human-approved writes, and a full-package upgrade. These are proposal assumptions, not approved customer configuration.

Code approval does not authorise tenant login/consent/writes, production pilot activation, DNS/device changes, publication or merging. Prepare each exact consequential preview first, then obtain the applicable approval. An authorised engineer can perform live checks while agents continue independent code/document work. Agents do not need customer secrets in chat.

Claude and Astra can collaborate through repository files and PRs; this review did not actually contact Claude. New findings go into [PRODUCT-FEEDBACK-REGISTER.md](PRODUCT-FEEDBACK-REGISTER.md), with unique author-prefixed IDs to avoid collisions. Each agent uses its own branch and claim, preserves other findings, records disagreements and cites evidence. A human retains merge authority. No agent should approve its own implementation merely because another model generated it.

## 7. Short approval scope

**Recommended:** approve work packages **A–G** to complete a supported internal M365 BuildStandard 1.1.0 release: coherent engineer workflow, safe legacy backfill, release-aware evidence, Graph/Exchange reuse, upgrade/support continuity, release preparation and independently recorded acceptance. Preserve current safeguards and historical standards. Defer compiler expansion, broad adapters, hosted portals, unattended remediation and central storage.

This approves source/product development when the user explicitly accepts it. Live tenant actions, consequential pilot/recovery steps and final publication remain separately scoped approvals. No approval is inferred from this document.

Suggested short desktop goal after approval:

> Finish the internal M365 BuildStandard release using docs/integration/PRODUCT-COMPLETION-PLAN.md.
