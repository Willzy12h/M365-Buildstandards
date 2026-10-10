# Engineer and live acceptance run sheet

Prepared on 8 October 2026 from integration `8f0285a`. This is a test procedure, not authorisation and not a completed acceptance record. All checks below start **Not run**. Select the exact candidate first; an older preview's evidence does not validate changed bytes.

Use [CONTROLLED-ACCEPTANCE](CONTROLLED-ACCEPTANCE.md) for approval boundaries and the small inert-candidate scope, [OPERATOR-START](OPERATOR-START.md) for current buttons, [APPLICATION-SETUP](APPLICATION-SETUP.md) for registrations/permissions, and the existing [capability coverage](AUTOMATION-COVERAGE.md). This document does not replace that matrix or redefine supported/manual/experimental/blocked capability.

## Before starting

William or an authorised engineer runs these checks. Agents must not sign in, grant consent, call tenant services or change endpoints. Use a separately approved disposable test tenant and pilots. Keep real identities, exports, tokens, object IDs and evidence paths in the local secure acceptance record, outside chat and Git.

Record package version, exact source SHA, ZIP SHA-256, standard release/digest, extracted-package verification, tested Windows version/scaling and session time in UTC. Verify the ZIP against the independently trusted fingerprint and organisation's application-control policy. A blocked package needs security-owner approval; do not change execution policy or choose another host to bypass it.

Record which phases are approved: read-only capture; registration/consent/engineer assignment; candidate creation; pilot activation/assignment; recovery/retirement. Approval of one phase does not approve another. Experimental actions stay off for production. Resolve missing prerequisites before a phase; record **Blocked** rather than improvising a replacement workflow.

Use these outcomes throughout: **Pass** (observed expected result), **Fail** (observed incorrect result), **Blocked** (a named prerequisite or safety boundary prevents the check), **Not run** (no observation). Cancelled/failed/partial reads are never successful empty results. A service readback does not establish user/device effectiveness.

## A. New client — read-only journey

1. **First launch.** Extract the exact ZIP into its intended approved location and launch normally. Expected: the correct application/version/standard appears, no tenant operation starts automatically and errors identify the actionable prerequisite. Record fingerprint/extraction and startup evidence separately.
2. **Readability and access.** Walk Connect, Configuration, Assessment, Plan changes, Jobs and completion, application setup and evidence/support pages with the keyboard, then Narrator. Repeat on a physical display at 125% and 150% scaling using existing display settings. Expected: meaningful accessible names, visible focus, readable buttons/fields, reachable scrolling and an explanation/next step for disabled actions. Native harness screenshots are supporting evidence; they do not pass this human check.
3. **Connect deliberately.** Use Quick Connect with the client's own approved account. Verify the displayed tenant, primary domain, identity and permission mode. Expand connection details and copy identifiers into the local record. Expected: no silent selection of an ambiguous account, clear assessment/deployment registration identity and visible write-capable access. Record actual WAM/browser interaction and its reason; Microsoft CA/MFA/consent may require interaction.
4. **Existing application/setup guidance.** Inspect detected registrations and grants before creating anything. Expected: identify which application is assessment versus deployment, what is missing and the exact next step. If creation/repair or consent is required, stop at its preview for separate approval. A browser completion page alone does not prove the registration/grants work. Do not grant proposed future report permissions merely because this run sheet mentions reports.
5. **Capture and cancellation.** Take a read-only Configuration capture, inspect collection status and details, then perform a separately approved read cancellation test. Expected: successfully empty, partial, failed, unattempted and cancelled collections are distinguishable. A cancelled capture remains incomplete, not deployment evidence. Preserve it, then obtain a fresh capture for subsequent checks; do not edit its JSON to change the status.
6. **Assess and inspect.** Re-run assessment of the intended capture/standard. Inspect a match, gap, manual result and unable-to-check result where available. Expected: findings cite actual properties and collection limitations; blank/unknown is not silently compliant. Licence scope and prerequisite wording explain what the engineer must check. If the fixture tenant lacks a needed case, mark that case Not run rather than changing production protection to manufacture it.
7. **Reports and saved evidence.** Export the existing engineer/client assessment and configuration JSON/CSV/Excel. Reopen the stored evidence. Expected: reports match the visible findings and timestamps, historical evidence is labelled stored/offline, and incomplete evidence remains incomplete. Compare source capture and report provenance; an assessment report is not a full raw configuration inventory.
8. **Jobs and completion.** Open a job, select a requirement and record a Check result, Decision about existing protection or Replacement stage as appropriate. Cite the stored assessment/capture; retain exact IDs locally. Expected: friendly names lead, details remain copyable, history/citations persist and records alone neither perform manual checks nor authorise writes. Missing/unverified requirements remain outstanding.

## B. Legacy client — preserve protection and backfill safely

9. **Discover without adoption.** Capture the separately approved legacy test tenant and inspect existing protection before planning. Expected: matching names alone do not establish toolkit ownership; equivalent/stricter objects and unknown evidence stay distinguishable. Existing protection remains in place during this read-only phase.
10. **Record dispositions.** For suitable test requirements, record retention, proposed replacement or an approved departure through the existing workflow. Expected: a departure depends on the existing valid approved deviation and review date; expired/missing approval does not become completion. Save ownership, reason and next step in the secure record.
11. **Preview backfill.** Select only the agreed missing controls, review exact plan IDs/payloads/exclusions and inspect readiness blockers. Expected: placeholders, incomplete before-evidence, foreign identity and uncertain writes block approval/execution. A filter or partial check does not satisfy complete before-evidence. Similar names do not permit overwriting/adopting an object.
12. **Create inert candidates — only after separate approval.** Follow Stage B of CONTROLLED-ACCEPTANCE for the actual reviewed CA-001 and Windows time-zone candidates, or record this phase Blocked/Not run. Expected: CA remains disabled with exactly reviewed targeting/exclusions; Intune remains unassigned. Preserve durable intent, accepted response and readback separately. Do not clear CA targets to manufacture an “unassigned” candidate. Candidate creation does not approve activation, assignment or retirement.
13. **Replacement and effectiveness.** Follow separately approved Stage C where supported. Record staged approval and actual effective sign-in/device results separately from service readback. Expected: an unperformed stage or missing prerequisite cannot be ticked into a successful replacement; retained legacy protection is not retired automatically. If exact reviewed narrow CA pilot targeting is unsupported, leave activation unperformed.
14. **Uncertain-write handling.** Inspect any existing safely prepared synthetic/offline unresolved-write acceptance workspace; do not cause a lost live response deliberately. Expected: unresolved reviewed changes/LAPS operations block tenant-wide completion, and unresolved writes on selected controls block their jobs, including relevant historical runs. A new job, capture or plan does not clear uncertainty. Follow [UNRESOLVED-WRITES](UNRESOLVED-WRITES.md); no replay or automatic retry. Record synthetic evidence separately from live behaviour.

## C. Repeat review — history and authentication

15. **Move between pages and review a plan.** Within the verified session, move between read-only pages and return to a saved plan review. Expected: page changes alone do not request sign-in. Record actual prompt count, action, registration, resource and interaction reason without token/cache data. Graph authentication does not prove Exchange/Purview access.
16. **Deliberate read-to-write switch.** Only if approved, switch visibly to deployment access using the verified tenant/account, then review the exact plan. Expected: necessary write access is requested at the deliberate switch; final exact approval remains required. Verified deployment context is reusable where the existing session permits it. Cancellation or identity mismatch leaves no usable write context. Do not revoke real grants or manufacture a Microsoft 401 to test this.
17. **Reconnect and sign-out.** Exercise supported reopening/reconnect, disconnect and explicit sign-out according to the application's current cache-retention contract. Expected: known-account reconnect can try silent acquisition; ambiguous accounts need deliberate selection; sign-out/disconnect semantics are honoured. Record real interaction-required outcomes rather than promising zero prompts. Never include caches in evidence, transfers or support exports.
18. **Drift versus upgrade.** Reassess a new capture against the pinned standard, then inspect the existing release-lineage/upgrade workflow using an actually available verified release. Expected: tenant drift and standard upgrade impact are separate; historical reports keep their original standard/input bindings. A missing lineage or changed source is a review/blocker, not an automatic rebind. Mark the upgrade comparison Not run if no suitable release pair exists.

## D. Second-engineer handoff

19. **Prepare a protected transfer.** Use existing backup/restore/adoption previews and secure custodians. Record the trusted archive fingerprint independently. Expected: no token/cache transfer, truthful file/evidence verification and quarantine/refusal for modified evidence. Test tampering only on a copied synthetic workspace, never the sole original evidence archive.
20. **Second engineer resumes.** On an approved second Windows context, adopt/restore through the supported workflow and establish their own authenticated identity. Open the job/history/citations. Expected: company/tenant/standard/owner/remaining work are clear; prior authentication and approvals are not inherited. Missing cited evidence or conflicting history requires review, not a fabricated clean result.
21. **Completion and next action.** Compare Jobs with the available offline CLI job projection where the candidate actually exposes it. Expected: same stored requirement/history/blockers, clear outstanding human tasks and no write authority from a completion claim. Portable CLI hosting merged in #45 and is present in the current integration source, with extracted-package synthetic checks. Run parity only when the selected exact candidate contains `bdit.cmd` and its recorded checks passed; an older published package may still exclude it. If unavailable in that candidate, record Blocked — candidate unavailable. Do not install a field SDK as a workaround. The human interactive-console check remains Not run until an engineer records it.
22. **Preview support export.** Inspect the support preview before exporting. Expected: local bounded outputs, disclosed exclusions, no private tenant export or cache included implicitly. Keep detailed handoff material with its authorised custodian; share only the sanitised response below.

## E. New feature gates — run only when present in the exact candidate

These are reserved acceptance steps, not claims that the baseline implements them. Check the merged source/candidate feature matrix before running. Unmerged PR evidence is source/synthetic/native evidence, not live acceptance. Until the feature is present, record **Blocked — implementation/candidate unavailable**.

23. **Full HTML configuration inventory (PR #40).** Once exposed in the selected candidate, export from the same stored capture as step 7. Expected: tenant/capture/standard provenance, all returned object settings/assignments and explicit unknown/missing/partial/error states. Values agree with source evidence; no assumption that unavailable collections contain zero objects. Captured primary domain is not claimed as an exhaustive verified-domain list. It remains separate from assessment and build-standard documentation.
24. **Scoped checks.** Check one area and one control. Expected: desktop collects only required dependencies, assesses selected controls and labels evidence partial. The offline CLI uses existing evidence and says it is a filtered offline review. Scoped evidence cannot authorise writes. Observe collection coverage without exposing request headers/tokens.
25. **User/device/MFA/log reports.** For approved known test identities, compare actual assigned products/plans, device ownership/OS/compliance/last sync and reported authentication registration. Expected: exact identity joins; missing fields are unknown; registration is not enforcement or successful use. For selected log dates, inspect pagination, retention, access/licence limitations and partial results. No report may infer a physical device from a generic authentication-method label.
26. **Exchange mailbox and 100 GB audit.** Compare representative approved test mailboxes against authorised Exchange read results. Expected: mailbox type/identity, actual size, raw primary quotas and separate archive values, preserving Unlimited/read failures. The observed 100 GB list derives from actual primary quota; verified service-plan entitlement is separate. Eligible-but-lower-quota and configured-but-unconfirmed entitlement remain distinct. Business Premium name alone is not proof of 100 GB capacity. Keep primary/archive/Recoverable Items/OST/PST limits separate.
27. **Exchange read reports.** Exercise supported mailbox/calendar permissions, message trace and unified-audit queries within reviewed bounds. Expected: resource-specific authentication/roles, explicit date/range/pagination/module limitations and partial/cancelled results. Large report evidence never masquerades as complete deployment before-evidence. Do not add modules, grant roles or consent as an implicit part of Run.
28. **Read-only scripts.** The merged #46 library currently provides Copy/Save, with no Run. Review those forms and generated script safely; record integrated Run as Blocked — implementation/candidate unavailable. Once a separately reviewed runner is present in the selected exact candidate, review manifest-generated purpose, inputs, tenant/account, runtime/module/access prerequisites and output destination before Run. Expected: owned bounded execution, supported runtime, cancellation, local secret-safe output and verified result identity/read status. A file or exit-zero alone is not proof of successful checking. If application control blocks execution, stop for security-owner approval; no alternative host.
29. **Copy-only changes.** Inspect AutoMapping re-grant or quota proposals only when complete before-evidence/entitlement is available. Expected: **Copy script, no Run**, exact current/proposed values, consequences and approval/recovery limitations; missing evidence gives a blocked/inert proposal. No automatic quota increase, licence purchase/assignment or archive expansion. This run sheet does not approve executing the copied change script.
30. **Coherent navigation.** Once the new shell is integrated, repeat steps 2–3 across Tenant, Build standard, Reports, Scripts and Settings. Expected: visible tenant/domain/identity/permission mode, highlighted write-capable access, historical/offline/incomplete distinctions and no dead ends. Retain actual keyboard/Narrator/physical-scaling results.

## Sanitised response template

Copy this summary into the project thread. Keep the detailed secure record locally. Use anonymous case labels, not tenant/domain/user/mailbox/object/registration IDs, token strings, contact details, evidence paths or private screenshots.

```text
Candidate version:
Source SHA:
ZIP SHA-256 (public package fingerprint only):
Standard release and publication digest:
Windows version / scaling:
Session UTC date:
Approved phases (read / setup / create / activate-assign / recover):

Step number(s):
Outcome: Pass / Fail / Blocked / Not run
Expected result:
Observed result (sanitised):
Evidence type: source / synthetic / native / human / live
Prompt count and reason (no identity/token):
Service readback vs user/device effectiveness:
Cleanup outcome or unresolved operation (anonymous label):
Next step / responsible role:

Other steps remain Not run unless explicitly listed above.
```

Update the existing completion/feedback registers with attributed evidence after review. A passing journey is not approval to promote to main, tag, publish or deploy to another tenant. Keep unresolved human/live gates open and prepare the exact-source promotion record separately.

### Authentication follow-up for the candidate (PR #73 review)

On an explicitly approved disposable-tenant session, record whether a normal silent token renewal keeps the same verified Graph identity/scopes and whether an interactive requirement stops the operation with reconnect guidance. Record only pass/fail, prompt count/reason and whether scope membership changed, without values, tokens or account/tenant identifiers. Check Quick Connect/Connect with another account previews show an unknown account, known-account reconnect previews show the deliberate hint, and setup/consent closes any previous experimental opt-in. A slow access-preview review may exhaust the existing sign-in timeout; a refused/expired request must send no new operation. These steps remain Not run until William supplies acceptance.
