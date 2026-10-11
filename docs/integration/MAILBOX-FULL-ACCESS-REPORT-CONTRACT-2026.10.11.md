# INT-097 — Read-only Full Access report registration

Status: proposed shared decision; no dependent adapter implementation before reviewed merge. INT-088 already authorises successive read-only report registrations. This records the new strict row/identity contract required by the next small slice; it does not make a Copy script runnable by changing its manifest alone.

## Scope

Add `exo-mailbox-full-access`, report revision 1, resource ExchangeOnline, section `permissions`. It reports Full Access grants for one explicitly supplied mailbox only. It does not report Send As, Send on Behalf, calendar rights, effective group membership or AutoMapping. Existing inventory and all 29 manual items retain their original contracts/bytes. Exchange changes remain Copy/Save only.

Use the existing `exchangeOnline` adapter, owned runner, exact tenant/account context and sequential `Use-BditRead` gates for `Get-EXOMailbox` then `Get-EXOMailboxPermission`. No new outer evidence/manifest/runner-result version, wrapper fallback, secret transport, module installation or Graph permission. Register the new body and manifest with exact hashes; do not rebind the current mailbox adapter or wrapper pin.

## Inputs and identity

Required scalar `MailboxGuid` is Text, accepting only a canonical non-empty GUID. `MaxRows` is Integer, default 500, allowed 1–5000, bounding retained permission entries. Registry semantic validation must reject invalid values before process launch, in both UI and copied/supplied requests. No arrays, interpolated command strings or unbounded all-mailbox loop.

A typed `MailboxPermissionReportRow` includes required base identity/name/read-state/error and required properties: exact `mailboxGuid`; observed `primarySmtpAddress`; fixed `permissionKind:FullAccess`; nullable `principalObjectId`; observed nullable `principalIdentifier` and `principalName`; explicit `principalResolution`; nullable `isInherited` and `deny`. Only actually returned fields are recorded; there is no directory lookup or inference of effective access.

A returned canonical principal GUID is observed metadata, not independently resolved directory identity. The resolution label must say `ObservedGuid` or `Unresolved`; display names/SIDs never become invented object GUIDs. Missing identity/flags stay unknown with a Partial row and reason. Missing target is distinct from a failed permission read and a successful empty result.

The existing schema's universal canonical-GUID row-ID rule is preserved for inventory. For this report only, use a deterministic composite ID when a non-empty exact principal identifier is returned: `grant:v1:` plus unpadded base64url of UTF-8 canonical compact JSON `[mailboxGuid,principalIdentifier,isInherited,deny]`. GUIDs use lower-case canonical D; the observed principal identifier retains its exact case/characters, and flags are JSON booleans/null. Bounded identifier/name strings and total ID size are checked before encoding/admission; no truncation or hash masquerading as an object ID. Recompute and compare on strict read. If no usable principal identifier is returned, ID stays null and resolution Unresolved; no invented ID. Duplicate composite entries make the collection Partial and are not merged or guessed.

`principalObjectId`, if returned, must be a canonical non-empty GUID. A non-empty GUID identifier alone must not populate that field without the corresponding returned GUID property. Never expand a group or infer a physical/user identity from its name. Preserve whether flags were unavailable rather than turning null into false.

## Truthful coverage and strict admission

Target mailbox lookup must verify exact returned mailbox GUID before the permission request. Missing, malformed or mismatched target prevents that request. Only FullAccess entries are projected; missing/ambiguous AccessRights remain unknown/partial rather than evidence of no Full Access. Source command identity/provenance, timestamps, requested target, cap and retained count remain explicit.

A failed/cancelled permission command is not an empty success. A completed zero-grant result may be Collected only after successful exact-target and permission reads. Row limit, cancellation, output truncation, missing required properties and duplicate identities retain Partial/Cancelled outcomes and reasons. The global existing time/output/evidence limits remain; nested/unbounded permission arrays are not introduced.

Register the row type and dispatch row-ID/semantic validation by the exact report registration. Strict admission refuses unknown fields, missing required properties, report/section/source mismatch, incorrect composite identity, foreign target and malformed/null required members. Existing inventory remains unchanged. Store/export uses existing strict envelope and immutable evidence; this report is never complete deployment before-evidence.

## Required implementation and tests after merge

Add the typed model, exact registry definition and narrow schema dispatch; new pinned schema-2 PowerShell body/manifest/registry entry; semantic request checks; stand-in module command/property behaviour; operator/access docs. Reuse #93's generic Run discovery rather than editing its forms, and do not touch #94's supplied capacity review.

Synthetic tests must cover two principals on one mailbox, allow/deny/inherited distinctions, unknown flags/identity, exact target mismatch, invalid input before launch, successful empty versus failed read, cap/duplicates/cancellation, wrong account/tenant with zero subsequent reads, forged composite IDs, unknown/missing fields, strict immutable-store/export round trips and inventory compatibility. Copy/change items remain refused for Run. Run each supported PowerShell pairing using stand-in modules only; actual Windows/native/live access remains separate.

Candidate module requirement remains ExchangeOnlineManagement >=3.7.0 with documented cmdlet/RBAC prerequisites, not a claim that a role name guarantees effective access. No role assignment or live consent. New adapter status is implemented/live-unverified only after source lands and tests execute; this decision itself implements nothing.

Calendar, message trace and unified audit need separate exact registrations and coverage rules. Purview needs reviewed resource/authentication handling because the current wrapper accepts Exchange only; no adapter can bypass that rejection.
