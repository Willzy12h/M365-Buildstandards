# Registered read-only Exchange adapter extension — decision proposal

Status: proposed, not accepted or implemented. Approved follow-up task6; base canonical main fd4d84d. Preserve INT-088 and the existing experimental Run workflow. This is a bounded shared-contract extension, not a generic script executor.

## First eligible additions

Add separate schema2 entries for mailbox protocol settings, external forwarding and mailbox holds. Keep all existing schema1 Copy/Save entries and pins unchanged. Each new report has one registered typed mailbox-row section, exact ExchangeGuid identity, per-value read state, nullable values and an explicit error for any incomplete row. No display-name, address or synthetic-GUID identity joins. Missing identity makes the section Partial and excludes the unidentified row, never empty-success. Duplicate identities refuse.

Protocol rows carry POP, IMAP, MAPI, EWS, ActiveSync and OWA boolean settings; missing or malformed values remain unknown. SMTP AUTH has separate mailbox override (Disabled/Enabled/Inherit/Unknown), observed organisation setting, and a derived setting only when the necessary inputs were returned. A returned null means Inherit, whereas an absent property means Unknown. This is configuration reporting, not proof that authentication succeeds: CA, authentication policies and other Microsoft controls can refuse access.

Forwarding rows retain returned target identifiers and addresses separately, with nullable DeliverToMailboxAndForward and explicit read states. No ownership inference, target resolution guess or forwarding mutation. Hold rows preserve observed hold flags and bounded identifiers; do not claim legal/compliance coverage or conflate Litigation Hold with retention policies. Unknown fields cannot become False, NotSet or an empty collection.

## Shared interfaces and boundaries

- Add package-owned row types and report registrations in Core.Models.ExchangeReportEvidence and Core.Reporting.ExchangeReportRegistry. Preserve the meaning of the existing mailbox inventory registration and historical bytes.
- Extend Engine.Reports.ExchangeReportEvidenceSchema with exact ordered columns, strict per-value state consistency and explicit known enum domains for each new row type. Unknown fields/types/versions still refuse. No schema guessing or generic dictionary acceptance.
- New separately pinned manifests/bodies call only the existing private Use-BditRead gate with reviewed Get-* commands and typed parameters. Preserve before/after tenant/account admission, owned process lifetime, limits, cancellation, result admission and original-output recovery.
- Desktop discovers the registered forms through the current catalogue. Existing CLI offline copy/import/export boundaries remain. No CLI authentication or new service permissions.
- Replace test fixtures that assume a single executable item with exact known-ID selection; retain per-item manifest, parser, identity, refusal, cancellation and strict-envelope coverage.
- Existing capacity evaluator consumes only exo-mailbox-inventory. New protocol/forwarding/hold reports cannot satisfy deployment before-evidence or licence eligibility.

## Not included in this first extension

Mailbox/calendar permissions and log rows need exact composite/object identity contracts; do not fabricate canonical GUIDs to fit the current reader. Domain/policy reports need verified service identity rules. They remain clearly Copy/Save-only until separately specified. Graph licence/device/MFA reports continue through the existing Graph engine, not a new PowerShell token handoff. All Exchange mutations remain blocked from Run.

## Required evidence before acceptance

Meaningful RED/GREEN tests for missing/null/false distinctions, absent versus empty collections, inherited SMTP settings, duplicate/malformed/mismatched identity, truncation, per-mailbox read failure and cancellation. Synthetic PS5.1/7 bodies must pass existing owned-runner AST and command restrictions, return strict registered evidence, and refuse wrong tenant/account with zero report reads. Each manifest must pin its exact body and existing engine-owned wrapper. Run source-matched Windows/App/native/hosted/portable checks on composed source; no live behaviour or RBAC claim follows from synthetic tests. New entries stay experimental/live-unverified.

## Review gate

AGENT-COORDINATION requires shared schema/contract decisions to merge before dependent implementation. This proposal requires the applicable independent runner/evidence safety review and authorised merge; no self-review or prior candidate acceptance waives it. No changes to repository protection, permissions, live access or publishing.

## Primary documentation inspected 15 October 2026

- https://learn.microsoft.com/en-us/powershell/module/exchangepowershell/get-exocasmailbox?view=exchange-ps — explicit properties and identity parameters.
- https://learn.microsoft.com/en-us/exchange/clients-and-mobile-in-exchange-online/authenticated-client-smtp-submission — returned null inherits the organisational setting; the mailbox override takes precedence.

Further field/property-set and role verification is required during the adapter implementation; no live module output was inspected.
