# Controlled acceptance proposal

First prepared for Preview.15; it applies unchanged to later previews. Record the exact package from the release's `RELEASE-RECORD.json` (currently `v1.1.0-preview.18`).

This is a prepared test scope, not authorisation. No tenant, operator, pilot device or live credentials have been supplied for this development task. Use the exact final package commit/checksum and standard 2026.09.30; record each outcome and its evidence. Keep tenant identifiers and exports outside the source repository.

Use the [numbered engineer/live run sheet](LIVE-ACCEPTANCE-RUNSHEET.md) for new-client, legacy/backfill, repeat-review and second-engineer handoff checks and the sanitised response template. It preserves the approval stages below; future report/script checks remain blocked until present in the exact candidate.

## Information required before a live session

Identify a disposable tenant by ID and primary domain; a named consenting administrator and engineer; two distinct emergency accounts; licensed pilot users; an enrolled disposable Windows device; and existing assessment/deployment registration IDs if available. Record Business Premium service eligibility, Windows edition and any separate ESET entitlement. Identify one pilot group and the approved office CIDRs only if the selected tests need them. Supply identifiers locally in the toolkit, never secrets in chat.

Agree an acceptance window and recovery limit. Record who may approve registrations, consent, engineer assignment, candidate creation and later pilot activation/assignment. These are distinct consequential actions. Stop if the actual tenant differs, evidence is incomplete, ownership is ambiguous or an outcome is uncertain.

## Stage A: setup and read-only acceptance

1. Record package commit/version/runtime/standard/checksum; verify extracted files and first launch. Check keyboard navigation, Narrator and physical 125%/150% scaling through existing display settings under human control.
2. In the identified test tenant, prefer validating the explicitly supplied existing registrations. If creation or repair is needed, capture the application's exact registration/permission preview and proposed engineer assignment before requesting approval. Use the documented wizard and [application setup](APPLICATION-SETUP.md); do not assume consent from a browser success page.
3. Exercise Windows broker sign-in and supported fallback for the assessment application. Confirm tenant, primary domain, account and read-only access. Verify actual grants and entitlement against Microsoft data. A successful token is not proof that every collection can be read.
4. Capture the tenant. Require complete collection evidence for any later writes. Exercise Stop during a read; cancelled collections must remain incomplete/unknown. Reconnect and take a fresh complete capture.
5. Assess and inspect findings, missing prerequisites and licence/user checks. Export HTML/JSON evidence, reopen the stored capture and confirm it is marked stored/offline and cannot itself authorise execution. Compare report content with the visible findings.

Record actual WAM/consent and Microsoft responses separately from the existing synthetic tests. Do not revoke production permissions or alter a real user's licence to manufacture a negative test.

## Stage B: two inert candidates, separately approved

The proposed initial write set is deliberately limited:

| Control | Proposed change | Required state after creation |
| --- | --- | --- |
| CA-001 — Require MFA | Create one uniquely prefixed, toolkit-owned policy from the current catalogue through a fresh reviewed plan. Preserve the exact planned targets and resolved emergency/operator exclusions | `state=disabled`; no activation or report-only transition |
| CFG-WIN-011 — UK time zone | Create one uniquely prefixed Windows configuration candidate using the current catalogue's native time-zone payload | Unassigned; no device receives the policy |

Before approval, save the actual generated plan with resolved IDs, exact request paths/payloads, unique names, tenant/operator binding, before snapshot ID and digests. Review existing objects to avoid name-based adoption. Unresolved placeholders cannot be approved. The table defines the bounded intended change; the generated preview is the exact proposal the human must authorise.

Execute only the approved rows. Record durable intent, returned IDs, write acceptance and readback separately. Confirm the CA policy is disabled and exclusions match; confirm the Intune candidate has no assignments. Inspect the per-action before/requested/after panel and exported run, then reopen the saved evidence. If a response is lost or ambiguous, stop and reconcile by read-only queries; never press Execute again as a retry.

## Stage C: pilot effectiveness and supported recovery

Candidate creation does not authorise this stage. Obtain separate approval of the exact policy ID, pilot target, exclusions, intended state/assignment and recovery preview. CA-001's default broad targets must not be enabled merely because its disabled candidate passed. A pilot CA policy must already have separately reviewed narrow targets supported by the current workflow; otherwise leave CA activation unperformed.

For the Intune time-zone candidate, use the toolkit's separate reviewed assignment workflow to target only the agreed pilot group. Record the exact assignment preview, service readback, device check-in and effective device value. Keep configuration readback and device effectiveness as separate results. Assignment removal can stop future delivery; it does not guarantee reversal of an applied device setting.

Before cleanup, recapture and verify exact-ID ownership and unchanged material state. Use supported reviewed containment/removal, then delete only the owned candidates whose current state permits it. Never delete unrelated objects or adopt a changed object. Retain before/after snapshots, mappings, run and recovery journals. A failed cleanup or unresolved write remains an open gate.

## Further standard acceptance

After the small candidate workflow is accepted, schedule separately scoped checks from [live validation](LIVE-VALIDATION.md) and [native settings/mobile procedures](NATIVE-SETTINGS-AND-MOBILE.md): device preparation, ESET deployment, Windows settings, mobile Outlook configuration, passkey-profile migration and functional authentication/recovery. Keep Exchange manual; SPF bypass activation remains blocked until its trust/alignment checks are demonstrated. Do not infer broader standard acceptance from two candidates.

## Acceptance record

For every check retain: package source commit and ZIP hash; standard release/digest; approved scope and approver; observed time; expected and actual result; service acceptance/readback; effective device/sign-in result; evidence locations; cleanup outcome; and Pass, Fail, Blocked or Not run with a reason. Avoid storing personal details in distributable screenshots.

Client use requires human code/package review plus the applicable live and physical checks. Until then this release is a tested preview only to the extent supported by its completion register.

## Approved native phase: unrun human checks

After the assembled candidate’s independent review, use H1 to confirm native grouped navigation and all21 routes, current-client replacement, hidden finding detail, last-built-plan versus selection, exact CA-001 review and deliberate approval (Enter must not write), saved reference mismatch/copy/close, and distinct accepted/readback/save/protection messages. Exercise new-client, legacy, repeat and handover journeys. Windows harness checks are logical-pixel/synthetic evidence; newcomer/Narrator/physical scaling and Microsoft live behaviour remain unrun. No live or production approval follows from this document.

## Approved BuildStandard phase verification matrix — implementation candidate

This finite phase follows the unchanged approved plan/specification. Source implementation is provisional, independent review pending. Final T10 exact-source CI/package identifiers are recorded in COMPLETION-REGISTER and the candidate packet; human/live gates below are not passed by source tests.

| Criterion | Implementation/evidence | Remaining acceptance |
|---|---|---|
| AC-01 | T3 TokenCapability/Graph assessment guard regressions; source/runtime admission review deferred | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-02 | T3/T6 native mode/access distinction, all-size synthetic layouts | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-03 | T4 ReviewedWriteAdmission deadline/preflight regressions | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-04 | T4 initial-dispatch/not-sent/uncertain-write regressions; T7 outcome projection | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-05 | T5 current-context and T7/9 callback/selection isolation | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-06 | T5 HomeTaskProjection; T6 shared next-step native journeys | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-07 | T7 existing access-transition/recapture guidance and approval invalidation tests | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-08 | T7 original states, collection gaps and unknown/empty presentation | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-09 | T7 OutcomePresentation tests separate intent/acceptance/readback/save/protection | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-10 | T7 owned native CA-001 Space/exact payload/exclusions/explicit confirmation | H1 human/environment acceptance unrun; independent review pending. |
| AC-11 | T6/7 all-route visible caption and two-row/other native viewport checks | H1 human/environment acceptance unrun; independent review pending. |
| AC-12 | Automated native names/focus/contrast checks only; Narrator/physical scaling not accepted | H1 human/environment acceptance unrun; independent review pending. |
| AC-13 | T2/6/7 guide/routes/readiness numbering and existing route parity | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-14 | Actual synthetic WPF PNGs and original B+A concepts remain separately identified; William visual sign-off pending | H1 human/environment acceptance unrun; independent review pending. |
| AC-15 | T9 actual native View reference/Back/Retry, no tenant/authentication calls | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-16 | T8 immutable descriptor/export; T9 actual hosted and native exact-byte Save | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-17 | T9 lazy runtime/config/cache refusal and WPF/CLI startup guards; actual unavailable managed workstation case unrun | H1 human/environment acceptance unrun; independent review pending. |
| AC-18 | T9 real hostile hosted fixture, scripts/frames/resources/schemes denied; source has no injection/bridge | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-19 | T5/9 generation/late-work guards, owned lease/reparse/concurrent cleanup regressions | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-20 | T9 all-three-size native/hosted Find/zoom/Back/Retry; physical keyboard/copy/Narrator/scaling/workstation policy unrun | H1 human/environment acceptance unrun; independent review pending. |
| AC-21 | Conditional exception refresh/office-instance and unsaved-run changes excluded; historical findings preserved | Outside this approved phase. |
| AC-22 | Second engineer unaided outcome unrun; synthetic CI is not human/live acceptance | H1 human/environment acceptance unrun; independent review pending. |
| AC-23 | T2 neutral defaults/active language plus T6/7/9 generic UI; required historical/developer exceptions preserved | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-24 | T1 namespace/assembly/launcher/resource compatibility; T10 modern and legacy publisher name tests | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-25 | T1/2 preserved data/cache entropy/lock/catalogue bytes; historical regression coverage | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-26 | T2/8 new neutral derivatives/current hashes; historical records/pins unchanged | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-27 | T7 nine-status partition and four Needs review subcounts; no score | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-28 | T9 Deny mapping/fragments and actual non-user refresh; host-origin admission distinct from SDK gesture flag | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-29 | T6 strict saved original pin/client mismatch/collapsed sensitive detail, all sizes | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-30 | T5/9 generation/navigation and owned late callbacks; reused-view Retry regression | H1 human/environment acceptance unrun; independent review pending. |
| AC-31 | T9 neutral per-instance/session cache outside allowlisted evidence/backup/support roots; no cache transfer | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
| AC-32 | T2/6 cold no-client/no-live state; T9 catalogue without connection; demo/fixtures explicitly labelled | Final independent source review pending; live Microsoft behaviour unverified where applicable. |
