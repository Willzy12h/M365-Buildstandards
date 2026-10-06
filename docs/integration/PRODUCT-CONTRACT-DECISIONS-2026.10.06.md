# Product workflow and evidence contracts — decision PR

Status: **proposed for review and human merge**, 6 October 2026. User approved product work packages A–G and default/settings exports; live operations and production publication remain separately approved. Runtime reference is PR #19 / Preview.17. This decision-only PR branches from integration; no application implementation or published catalogue change is included.

The coordination protocol requires a cross-cutting schema/shared contract to be carried in a dedicated decision PR and merged before dependent implementation. The following contracts enable R01–R06. They do not change the current snapshot, plan, run, mapping, deviation, manual-check or catalogue schemas. Existing records remain authoritative for their original purposes.

## INT-049 — Versioned tenant job and observation records

Add separate tenant-partitioned `jobs/<id>.json` and immutable `observations/<id>.json` files. Schema version 1 contains tenant ID, record ID, standard release and verified digest, created/updated/recorded UTC time, actor, intention (`NewBuild`, `LegacyBackfill`, `RepeatReview`), owner, notes and referenced control instances/evidence IDs. Job state is a projection of current evidence and durable work intent, never authority to execute.

Observations preserve manual history: control ID and semantic identity, status (`Pass`, `Fail`, `Unknown`, `Pending`), evidence reference(s), observed scope/object IDs and digests, actor, reason, recorded/review-due time, standard identity and superseded-observation ID. A missing/changed referenced object, standard or evidence produces `NeedsReview`; it does not silently rewrite the observation or automated assessment. Old manual-check records remain readable and are displayed as legacy/unbound attestations, not migrated passes. No additional writes occur solely because an old observation is read.

Use existing tenant validation, atomic replacement and integrity conventions. Refuse malformed schema versions, duplicate IDs, cross-tenant links and a missing actor/reason for an asserted outcome. New record readers reject unknown members; compatibility for future additions requires a deliberate version decision. A job stores no session/token, acknowledged capture, plan approval or executable request. Its reference to a plan is review-only. Resume still requires the existing live/fresh/integrity/ownership checks and newly reviewed approval.

## INT-050 — Legacy disposition and cutover cases

Add separate `dispositions/<id>.json` records: schema version, ID, tenant, standard/digest, source control semantic identity, observed snapshot IDs, object IDs/settings digests, decision, actor/owner, reason, recorded/review-due time and optional case reference. Closed decisions are immutable; a revised decision supersedes the original.

Decisions: `RetainExternalCoverage`, `ApprovedDeparture`, `AddMissingCandidate`, `Investigate`, `ManualWork`, `ProposeReplacement`. Retention is an engineer disposition, not an automated compliant finding. It never creates a ManagedObjectMapping or permission to update/delete an external object. Existing deviation approvals stay distinct; references require actual same-tenant records and cannot silently create exceptions.

Cutover cases record old/new exact IDs, observed settings, overlaps, real pilot population, prerequisites, functional criteria, recovery limits and stage evidence. Stages are `Review`, `CandidateCreated`, `PilotReviewed`, `EffectivenessVerified`, `RetirementReviewed`, `Closed`. Completing an API operation does not automatically advance a functional stage. Unsupported scenarios stop with an owner and escalation path. Stored cases confer no activation, assignment or retirement approval and cannot replay requests. Whole-tenant targeting cannot be represented as pilot scope by a state-only operation.

## INT-051 — Release lineage and requirement impact

Use a separate immutable, digest-verified lineage asset defining source release/control, target release/control and relation (`Unchanged`, `Renamed`, `Changed`, `Replaced`, `Retired`, `Added`), stable semantic identity, reason and requirement evidence. It supplements published catalogues; historical catalogue bytes/digests remain unchanged.

Initial delivery is read-only source-to-target reconciliation. In `.9/.10 → .30`, explicitly handle PRE-001/002 → PRE-009/010, PRE-003 → PRE-004, PRE-004 → PRE-005 and PRE-005 → PRE-008 office instances. Unknown lineage produces `NeedsReview`. Reused control ID alone never rebinds a mapping, exception or attestation. Retired requirements are reported, not deleted from the tenant. The existing control-keyed mapping schema remains unchanged; no automatic ownership migration is introduced.

An upgrade impact report assesses one unchanged capture under source and target catalogues and presents requirement changes separately from the existing two-capture tenant drift report. New missing collection evidence stays unknown. A future local-reference migration requires an explicit reviewed proposal and preservation of originals; it is outside this initial contract.

## Common identity, validity and history rules (independent review clarification)

**Catalogue identity:** use `(release, verified SHA-256 of exact source bytes)` for a shipped catalogue. A release string or canonicalised model alone is insufficient. Local import candidates remain separate identities and cannot masquerade as a published release. Each lineage endpoint pins its catalogue identity; the lineage asset itself has a SHA-256 entry in a separately reviewed shipped integrity manifest. Its trusted package/source is recorded by the release process. Hashes are consistency checks, not publisher signatures.

**Subject identity:** separate stable semantic requirement ID from catalogue control ID and instantiated control key. Record `(semanticId, controlId, instanceKey)` plus catalogue identity. The instance key is the engine's actual instance identifier (for example `PRE-008-<location.Key>`), not a display name. Lineage explicitly declares one-to-one, one-to-many, many-to-one, added or retired cardinality; duplicate/conflicting endpoints or unexplained reused IDs are rejected. A historical singleton PRE-005 cannot be attributed to several current office instances automatically: present the relation and require explicit instance review. No cardinality copies mappings, exceptions or attestations into a new identity.

**Reviewed client scope:** jobs, observations, dispositions and cutover cases bind the profile ID and a canonical digest of its reviewed material inputs/scope. Initially use the full validated profile digest, excluding only documented volatile update timestamps; do not exclude targeting, offices, intended values or instance membership. Store referenced evidence IDs/digests and a separate digest of the relevant material observations. An unchanged recapture may preserve validity only when catalogue identity, subject, input digest and relevant material evidence match and review due time has not elapsed. It may update a job's review reference without manufacturing a new decision. Lost evidence, changed inputs/population/instance membership, changed requirements or material observations project `NeedsReview`; original records remain immutable.

**Supersession:** observations and dispositions contain an explicit optional `supersedesId`. Links must reference an existing intact record of the same tenant, job, semantic subject/instance and decision scope. They cannot supersede another control or unrelated scope. Links are acyclic; records never point to a future missing predecessor. Select the current record by this validated graph, never by the most recent timestamp. One valid current head is required; competing successors/forks are `NeedsReview`, never an accepted outcome. Originals remain readable and are never deleted. An atomic immutable-record create is followed by atomic job/reference replacement. If interrupted before attachment, the new record is visibly unattached; the prior attached history remains current. Orphan/fork repair is a deliberate recorded review, never silent overwrite.

**Cutover persistence:** immutable `cutovers/<id>.json` records use schema 1, tenant/record/job identity, actor/owner, UTC times, catalogue/subject/profile-input bindings, integrity and same-tenant evidence/reference validation. Each stage revision explicitly supersedes the prior revision under the same graph rules. Keep review context, old/new object IDs/digests, genuine pilot scope, dependencies, functional criteria and recovery limits in the record. Reopening restores work context only.

**Cutover transitions and closure:** `Review` requires intact old protection/scope evidence; `CandidateCreated` requires supported run/readback evidence and does not imply activation; `PilotReviewed` requires a separately approved real pilot scope and prerequisites but is not proof that the pilot ran; `EffectivenessVerified` requires recorded actual service and user/device/sign-in criteria results; `RetirementReviewed` records the separately approved proposal or an explicit decision to retain coexistence. Closure additionally records either performed-and-verified retirement or approved retained coexistence, fresh assessment and residual exception references. Approval alone cannot stand in for execution or successful verification. Unsupported transitions, failed criteria, missing evidence and uncertain writes remain open.

**Completion projection:** every applicable requirement needs a current accepted outcome with intact referenced evidence or an explicitly approved, same-scope, in-date permitted exception/deferral. Pending, Fail, Unknown, NeedsReview and unresolved operations never satisfy a requirement. The initial default disallows a completed-job claim with deferrals. If the business owner later approves a deferral closure policy, report `CompletedWithExceptions`, keep deferred work visibly outstanding, and record policy/approver/expiry; never display it as full verification. Exemptions do not grant ownership or tenant-write rights. A job never completes solely because candidates exist or a cutover stage was approved.

## Required acceptance before dependent implementation is complete

- Persist/reopen a job without restoring live evidence, acknowledgement or execution authority; cross-tenant/unknown-schema references fail closed.
- Observe/revise manual results while preserving original actor/date/standard and supersession history; changed evidence produces review-needed, never a fresh pass.
- Retain an external equivalent policy without mapping creation or tenant writes; same names grant no ownership.
- Reconcile reused/retired controls without rewriting historical records or implying tenant deletion; repeated unchanged reconciliation adds no duplicate decisions.
- Retain inactive-owned NoChange, active/assigned protection preservation and unresolved-write blocking under the existing planner/executor.
- Reject same-release changed bytes, ambiguous singleton/multiple-instance attribution, conflicting lineage, cross-subject supersession, forks, cycles and changed client inputs. Unchanged recapture adds no duplicate decisions.
- Interrupted attachments preserve prior history; candidate/pilot/retirement approval alone never closes a migration. Restored jobs confer no execution authority.
- A real cutover requires separately approved supported operations and evidence of actual functional outcomes; no stored checkbox authorises it.

Business owners still select named custodians, retention/access arrangements and whether overdue exceptions block programme sign-off. Defaults for implementation: overdue/changed observations prevent a completed-job claim but do not alter automated assessment or create new write rights. Secure local storage and deliberate handoff remain the initial operating model; concurrent shared editing is not promised.

## Independent work permitted before this decision merges

R17 exports use the existing catalogue schema and preserve exact verified bytes. Read-only desktop/CLI parity consumes existing snapshot/profile/mapping/deviation/Exchange types. Current guidance, scope/permission tables, acceptance forms, allowlisted diagnostics, documented whole-folder upgrade checks and release preparation can progress without implementing these new persisted contracts.

No runtime record producers/consumers for INT-049–051 are implemented until this decision PR is merged and relevant contributors pull the settled contract. Human merge authority remains unchanged. This decision itself is not live acceptance or a production release.
