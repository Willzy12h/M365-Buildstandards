# Internal product completion plan

**Status: source development approved, 6 October 2026.** The user explicitly approved A–G, preserving safeguards and historical standards, with live tenant actions and final publication separately approved. They also approved an integrated catalogue export set: all default values/settings in HTML for visibility, JSON for reuse/import and a document-ready specification. This is R17 in the feedback register. Runtime baseline is `2bf96a83846474a522b074479f07a3e076a5a03d`, Preview.17; proposal documentation is `e339515c02e91d843361c0154b75b095b07fa69f` in draft PR #19. Re-fetch and check claims before implementation; a future checkout may be newer.

This supplements [AGENT-COORDINATION](AGENT-COORDINATION.md), [DECISION-LOG](DECISION-LOG.md), [COMPLETION-REGISTER](COMPLETION-REGISTER.md), the [architecture disposition](ARCHITECTURE-DISPOSITION-2026.09.30.md) and existing [controlled acceptance](../CONTROLLED-ACCEPTANCE.md). It does not override them. The [feedback register](PRODUCT-FEEDBACK-REGISTER.md) provides the item-level work list.

## Target and definition of done

Deliver an internally supported Windows x64 engineer tool for new tenant builds, legacy backfill and repeat standard reviews. Keep .NET 10/WPF and the current engine. Proposed primary scope is supported Windows 11 x64, the existing SME/Business Premium baseline and client-owned accounts with dedicated assessment/deployment apps; confirm the exact support matrix before making a GA claim. Other licensing/platform/identity combinations need explicit status.

Release completion requires all of the following:

- One approved package/release record identifies source, runtime, standard, hash, acceptance scope and known limits.
- Every one of the 93 current controls has an honest automation/manual/unsupported disposition and a clear completion route. An unsupported route stops with a named owner and escalation or explicitly approved deferral; it is not a promise to implement arbitrary customer configurations. The 61 recipes are not labelled end-to-end accepted without proof.
- A newcomer and second engineer can follow supported new-tenant and legacy journeys, identify blockers and hand over evidence.
- Resuming, upgrading or restoring state never restores stale approval or bypasses live evidence, ownership or unresolved-write checks.
- Advertised production actions have applicable service/identity/device evidence. Unaccepted actions are explicitly constrained outside supported production execution.
- Consequential changes retain exact review, durable before/intent/after evidence, individual outcomes and supported reconciliation/recovery.
- All accepted feedback has implementation and verification, or an explicit release-scope disposition. No unexplained release blocker remains.
- Named owners support access, standards, releases, evidence and incidents after release.

One or two tested candidates validate a workflow harness, not the whole standard. Do not automatically graduate other recipes by resource-family similarity: document which payload differences and effective outcomes were tested, and keep unaccepted operations outside the claimed scope.

## Work packages

| Package | Feedback IDs | Depends on | Primary result |
|---|---|---|---|
| A | R07, R10, R11, R15, R17 | Existing Preview.17 | Support/acceptance contract, current guide, ownership and integrated default/settings exports |
| B | R01, R05 | A; approved model decision | Durable completion workspace and evidence-aware manual/exception review |
| C | R02, R03, R04 | A/B; approved lineage/disposition contract | Safe legacy review, version reconciliation and bounded cutover case |
| D | R07, R08 | A; B/C interfaces where needed | Reusable Graph/Exchange assessment and actionable service preflight |
| E | R06, R09, R15 | A; B/C persisted-model compatibility | Upgrade/restore/handoff and support/recovery continuity |
| F | R11, R12 | A; final B–E source | Identifiable release candidate, dependency inventory and promotion preparation |
| G | R10, R11, R16 | Checks begin in A; final acceptance after B–F | Independent review, controlled operational acceptance and release decision |

A and the existing acceptance harness can start first. Read-only parity work in D and release/support documentation can progress independently once claims are clear. Do not make live acceptance wait for speculative architecture work. Cross-cutting schemas/contracts require a dedicated decision PR and the merge sequence required by repository coordination before dependent implementation.

### A — Release contract and engineer start guide

Prepare a per-control/action capability matrix with assessment, creation, activation/assignment, effective behaviour and recovery dimensions. Each has supported/manual/experimental/blocked/not-run status, identity/module/licence scope, owner and evidence reference. Runtime presentation/gating must agree with the supported scope; a document alone cannot prevent an unaccepted action being represented as supported.

Reconcile current labels, confirmation wording and start instructions. Give engineers a short guide using actual buttons: Quick Connect → verified tenant → app readiness if needed → capture → assessment → disposition/plan → exact review. Keep detailed references linked and historical results clearly historical.

Record organisational owners and the access model, including engineer onboarding/offboarding and bootstrap consent review. Generate or check scope descriptions from the actual loaded standard; do not maintain a divergent permission list.

**Acceptance:** all current controls have matrix rows; no supported claim lacks evidence; no active guide contradicts Preview.17 confirmation/default standard; the guide explains browser consent versus WAM, missing grants and missing deployment application. Historical validation identities are preserved. Business owner approves support/access assumptions.

**Approved export supplement (R17):** an offline catalogue-only export set must preserve exact verified JSON bytes with a compatible integrity manifest, produce printable HTML and Markdown covering fixed settings, reviewable defaults and unresolved client inputs, and include the existing manual guide where available. All controls, licences, prerequisites, interfaces, candidate versus intended production state and source identity must remain visible. JSON reuse is not tenant evidence or authority to write. Verify exact source/manifest round-trip, all-control coverage, default/placeholder distinction, escaping and no client data. Do not change published catalogue bytes or invent values for manual-only controls.

### B — Durable completion workspace

Introduce a narrow job/attestation model, with tenant, standard/digest, intention, owner, stage, control disposition references, evidence IDs, notes, timestamps and review requirements. Keep the engine as authority for assessment and writes. Persist work, not executable approval/session tokens. Present tool, engineer, client/vendor and reviewer responsibilities separately.

Manual outcomes should retain history, actor, scope, standard/control revision and evidence reference. Exceptions should expose review dates and changed-requirement re-review. Agree whether overdue exceptions block business sign-off or alert; unknown assessment cannot become verified by a checkbox.

**Acceptance:** reopen halfway through either journey and see outstanding work; switch tenants without context leakage; old plans remain unusable; candidate creation leaves activation/effectiveness outstanding; old/manual passes are labelled historical or needing recheck after material changes; existing records remain readable through a documented compatibility policy. Test changed input/object/standard, lost evidence, interrupted saves and repeated unchanged review.

### C — Legacy backfill and standard transitions

Build a disposition projection from existing assessment/equivalence/planner outputs. Choices: retain observed external coverage; retain approved exception; create genuinely missing candidate; investigate conflict/unknown; perform manual step; propose replacement. Store reasons and exact observed IDs/digests. “Retain” must not grant ownership or change the automated assessment result.

Add release lineage and source/target impact comparison. Handle added, changed, retired and reused/renumbered controls, including `.9/.10 → .30` PRE identities. Preserve original mappings/evidence; initially present read-only reconciliation. Any local-reference migration is explicit, auditable and separately designed.

Cutover cases organise overlap, real pilot population, expected protection, dependency evidence, functional verification and recovery/retirement. Start with supported actions and documented manual procedures for enumerated migration scenarios in the support matrix; unsupported scenarios stop with a named owner and escalation route. Broadly targeted CA cannot be enabled as a supposed pilot by a state-only operation. Existing legacy protection stays intact until a separately approved replacement is proven.

**Acceptance:** fixtures cover differently named equivalence, stricter/custom settings, partial overlap, reused IDs, retired controls and incomplete reads. No unmanaged adoption or duplicate creation; changed requirements are distinct from tenant drift; retirement never implies deletion. On unchanged second run, inactive owned matches yield NoChange; active/assigned protection stays preserved/reviewed; unknown writes stay blocked. Changed evidence invalidates affected decisions. Real effective protection and cleanup need separate authorised acceptance.

### D — Shared read-only context and integration polish

Extract a small assessment input loader/context used by desktop and CLI. Include the same standard, tenant profile, mappings, deviations, Graph snapshot and optional separate Exchange/Purview capture. Add an explicit CLI input for supplemental evidence and test actual output parity. Keep it read-only; do not package a second field executable automatically without a separate distribution decision.

Improve module/role/app readiness diagnostics using existing Graph and Exchange boundaries. Avoid asking for domain information already observed. Domain-specific checks still require the relevant accepted domain; Graph and Exchange identity audiences remain distinct. Preserve Graph evidence/plans when supplemental read capture changes, under the current contract.

Provide a local change-review export with exact proposed scope, digest and evidence references if needed for existing business approvals. Ticket APIs, uploads, module installation and new write transports are separate scope.

**Acceptance:** identical combined Graph/Exchange fixtures produce matching desktop/CLI findings except documented volatile metadata; cross-tenant, malformed and missing supplemental data are refused or visibly unknown. No write/authentication path is added to offline reporting. Unsupported module/permission states explain the remedy. Cancellation/tenant switching preserve existing exclusivity and evidence boundaries. Actual module/RBAC/WAM behaviour is tested in G.

### E — Upgrade, backup, handoff and incidents

Specify one safe operating model first: secure local tenant workspace with a custodian and explicit engineer handoff. Test the existing root override and full-folder upgrade process; add independent data-root support only through an agreed contract if needed. A shared network drive is not assumed safe concurrent storage.

Add a migration/preflight checker and backup manifest for required profiles, mappings, deviations, attestations, snapshots, journals and unresolved records. Hashes verify intact transfer against an approved reference; restore never makes historical captures live. Authentication caches are not transfer credentials. Distinguish binary downgrade from tenant-change reversal.

Add previewable allowlisted support export and copy diagnostics. Default exports contain no token caches, profiles, captures or full reports. Seed synthetic secrets and cross-tenant records in verification. Add the incident decision tree and escalation ownership for uncertain writes, failed readback, corrupt evidence and unsupported recovery.

**Acceptance:** upgrade and restore prior-version synthetic state without altering original digests or losing unknown-write blockers; reopen equivalent offline reports on a second installation; authenticate independently before fresh capture. Preserve unknown historical fields according to the agreed compatibility contract. Support bundles show exact contents, refuse prohibited files, contain no seeded secrets and upload nothing. A second engineer can follow the incident runbook without deleting state or replaying writes. Business approves storage, retention and sharing rules.

### F — Release and servicing preparation

Extend the existing CI/package provenance rather than replace it. Produce resolved dependency inventory, licence notices as applicable, standard identity, acceptance matrix reference and known limits. Prepare durable release promotion using the exact tested artifact, with approver, trusted hash reference, prior version and rollback/upgrade instructions. Any rebuilt artifact needs new provenance/checks.

Prepare signing with organisation-managed credentials if supplied through an approved facility. Otherwise document a proposed controlled internal unsigned/hash-pinned distribution decision; hashes inside the same archive do not establish publisher trust. No credential extraction, application-control bypass or unapproved publication.

**Acceptance:** one release record locates the supported complete package and identifies exact source/checks/support scope; checksum verification catches modified bytes; fresh extracted package passes existing Windows startup/context-menu checks; previous approved package/evidence are retained. Runtime/dependency/module servicing has an owner and review cadence. Final promotion awaits release approval and authorised repository merge sequence.

### G — Independent and controlled acceptance

Prepare structured synthetic journeys and negative cases during implementation. Independent Claude/Astra review checks invariants, compatibility, provenance and promised outcomes. Record every finding/decision in the shared register. An independent review is not simply another successful CI run.

Use [CONTROLLED-ACCEPTANCE](../CONTROLLED-ACCEPTANCE.md) as the bounded initial harness: setup/read-only, disabled CA-001 and unassigned CFG-WIN-011, then separately scoped pilot/recovery. Extend evidence deliberately to every advertised production capability. Record expected/observed, approver, scope, package/standard identity, service acceptance/readback, device/sign-in effectiveness and cleanup.

Complete both business journeys and repeat them unchanged. Test interruption, accepted-but-unverified outcomes, unresolved outcomes, restore/handoff, keyboard, Narrator, supported display scaling and a newcomer without author guidance. An authorised engineer supplies live/physical observations; agents can continue unaffected development without waiting for every check.

**Acceptance:** exact final source passes applicable CI and portable checks; independent findings are resolved or explicitly scoped; both journeys meet the support matrix; incomplete/unknown/manual states remain truthful; effective protection and supported recovery are observed; cleanup outcomes are recorded; human product/release owner approves the final package and support contract. Failed or unrun gates remain open.

## Journeys to demonstrate

**New tenant:** identify client → verify account/tenant → validate or approve exact app setup → gather licences, emergency accounts, platform/client inputs → complete read capture → assess → resolve prerequisites/manual work → review selected candidates → create inert objects → separately review real targeting/activation → verify service and effective behaviour → recapture → sign off and hand over outstanding exceptions/evidence.

**Legacy tenant:** capture current protection → compare to target standard → reconcile older standard identities → disposition every applicable control → retain proven equivalents/custom protection → add genuinely missing candidates → separately review replacements/pilots → prove replacement outcomes before any retirement → recapture/reassess → retain unresolved/manual/exception items → hand over → repeat and prove no duplicate or unsafe repeat actions.

**Repeat review:** select prior job/standard/capture → capture fresh evidence → distinguish tenant drift from requirement change → invalidate changed/expired decisions → review only outstanding work → execute only newly approved supported changes → close evidence-backed outcomes.

## Deferred scope

R13 authoring compiler; R14 offline portfolio roll-up and additional import adapters; live unattended collection/remediation; app-only operation; hosted multi-tenant portal; mandatory central database; arbitrary legacy adoption; automatic rollback; automatic Exchange/Purview writes; SPF-bypass activation; automatic updater. These need separate value/safety decisions and must not silently enter A–G.

## Approval and execution boundaries

Approval of A–G permits bounded source implementation and review artifacts. Agents may prepare proposals/tests/docs, resolve implementation failures and publish owned review PRs within existing authorisation. It does not authorise tenant operations, new external messages/uploads, changes to the preserved source repositories or the other agent's branches, human-only merges or production publication.

Business owner chooses support/access/retention/exception/signing policies. Live acceptance needs an identified authorised tenant/operator/device and exact consequential previews; code review alone supplies none. New shared contracts follow the existing dedicated decision-PR process before dependent code.

At completion of each package, update its feedback entries with exact commit/check/evidence. Only mark Verified after the stated checks pass; retain live/human Blocked or Not run states. Update the current completion register without rewriting historical results.


## Implementation checkpoint — 6 October 2026

Preview.18 independent scope is verified at f200371: standard exports/capability and access inventory; shared desktop/headless read-only context; previewed support metadata; exact-byte evidence backup/separate restore; current operator/continuity/incident/release guides; resolved package/runtime notices and provenance. Windows 1,014 tests, 42 layouts/78 commands, parser negatives and fresh package checks passed. The feedback register holds independent fixes and remaining business/human checks. This is partial delivery of approved A–G, not closure of persistent workflow/backfill or a production release. PR #20 remains the required contract-merge dependency before INT-049–051 implementation. Existing A–G source approval persists; do not seek it again.
