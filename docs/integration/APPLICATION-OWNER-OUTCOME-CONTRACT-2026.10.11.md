# INT-096 — Versioned application owner outcome consistency

Status: proposed, decision-only PR #95. No dependent source implementation before this decision is reviewed and merged. This amends the expiry report portion of INT-094/#69/#87, preserving the consent report, access scopes and all read-only boundaries.

## Proposed exact contract

New application-expiry captures use envelope schema 2, report revision 2 and a newly registered adapter revision. The current revision-1 adapter identity is never rebound. Each registered application/service-principal owner source requires `retainedOwnerOutcomes`, with exactly five required integer fields: `collected`, `partial`, `failed`, `cancelled`, `notAttempted`. Unknown members, duplicate fields and missing values are refused. Other registered sources do not carry this owner-only field. The source routes and existing size/attempt limits remain unchanged.

The vector records effective outcomes for retained exact-identity parents, once per parent after metadata refinement. It is checked against the existing parent rows with the bounds and attempt interval below. It does not include a root-pagination marker, claim inventory completeness or authenticate an unsigned record.

Revision-1 expiry evidence is available only through explicitly labelled limited historical inspection/export. Its original bytes, digest, adapter and revision remain intact; it cannot enter the revision-2 owner-consistency-validated path. UI, CLI and all exported views display “Historical owner outcome consistency unverified”. Ordinary current-revision loading does not silently downgrade or upgrade a record. The consent report keeps its current revision and admission semantics.

Implementation must define distinct strict current and explicit historical admission paths and carry their assurance limitation to every presentation/export. Merely allowing the previous revision through the old unlabelled path does not close CLA-20261011-010. No derivation, resealing, automatic rewrite, source substitution or release/version change is authorised.

[Published inert reproduction and actual results](release-readiness/proofs/CLA-20261011-010.md).

## Evidence and rationale

Exact source: `0883a58f776a98c73edc9abf3231fe02515d8fa1` (#87). No production changes were made. The reproduction patch is test-only and deliberately leaves one red test; it is not a merge-ready fix.

## Executed evidence

`source /workspace/bdit-validation/activate-dotnet.sh && dotnet test tests/BDIT.TenantToolkit.Tests/BDIT.TenantToolkit.Tests.csproj -c Release -warnaserror -m:1 -nodeReuse:false --filter FullyQualifiedName~ApplicationOwnerOutcomeReviewTests --logger 'trx;LogFileName=owner-outcomes-before.trx' --results-directory /tmp/application-owner-0883/proofs`

Result: 1 failed as intended, 2 passed, 0 skipped. Compilation succeeded with warnings treated as errors. TRX: `proofs/owner-outcomes-before.trx`; log: `proofs/before.log`. The fixture supplies an in-memory HTTP handler and synthetic token provider; no live tenant, credential, Graph or remote CI action occurred.

1. Two exact applications, successful empty owners for the first, HTTP 403 for the second. Capture correctly records the second owner read as Failed and aggregate ownership source as Partial. Modify only the second parent's `ownersReadStatus` and `readStatus` to Collected, clear its two reasons, and recompute the digest. `ReportEnvelopeDispatcher.ReadFile` accepts it. The expected-refusal assertion fails.
2. Two exact applications on the first root page, HTTP 403 on the next root page, both retained applications' owner queries successfully empty. Capture and ReadFile correctly preserve Partial root/owner sources and Collected retained parent/owner rows. This positive control passes.
3. Compare every registered source route, structured status, retained row count, attempt count, section status and complete retained row JSON between the two histories after the first is tampered. They are identical. This proof passes; source error prose differs.

## Why existing structured fields are insufficient

- `ApplicationReportEvidenceSchema.cs:139–148` accounts attempts and constrains Collected, NotAttempted and Failed owner sources, but cannot derive the origin of Partial/Cancelled.
- `ApplicationReportService.cs:204–211` collapses multiple child outcomes into one aggregate status; the individual outcome list is not persisted.
- `ApplicationReportService.cs:222–223` legitimately adds an incomplete root outcome to the ownership source, even if every retained parent's owner read succeeded.
- `ApplicationReportService.cs:319–322` also refines the root source to Partial when a returned parent has incomplete owners. Thus the final root state cannot tell which direction caused the partial outcome.
- `ApplicationReportSource` (`Core/Models/ApplicationReportEvidence.cs:45–55`) contains only aggregate status/reason/counts, with no structured discriminator.

Rejecting every Partial owner source with all retained owner rows Collected rejects the legitimate pagination control. Treating error phrases as machine-readable provenance introduces an undocumented language-dependent contract. Changing the collector alone cannot repair already recorded evidence. Unsigned digests authenticate neither current nor proposed source claims.

## Proposed smallest decision

Approve a **new explicit expiry report revision** containing a required, bounded source-level count vector for effective retained-parent owner outcomes: Collected, Partial, Failed, Cancelled, NotAttempted. Use the existing `reportSchemaVersion` revision mechanism if approved, preserving envelope schema 2 and expiry report ID; bind the adapter/revision registration explicitly. Consent remains unchanged. The exact field name and wire representation must be decided before source implementation. Do not make the field silently optional in the old revision, infer it on load, or add an unversioned free-form source property.

Count each retained exact-identity parent once, after its actual owner outcome and metadata refinement are known. Do not reuse `_childStates`: that list can contain multiple refinements per parent, cap markers, and the root incompleteness marker. The new counts describe retained-parent outcomes, **not transport attempts or inventory completeness**. Capture the outcomes before writing the final row JSON; strict read/export checks compare the persisted vector against rows. Root pagination failure must never become an additional failed child in the vector.

Required invariants:

- Each count is an integer from zero to the existing 5,000-row section bound; the checked sum equals the exact-identity retained-parent count. Missing/ambiguous parents stay excluded and retain their current NotAttempted/no-query rules.
- Collected + Partial + Failed is no greater than `objectsAttempted`, which is no greater than that sum + Cancelled. Cancellation before a query and cancellation during a query remain distinguishable only within this existing truthful interval.
- Existing combined 256 attempted-child cap remains. NotAttempted and pre-query Cancelled counts may exceed 256 because up to 5,000 parents can be retained. Do not impose the attempted-child cap on all outcome counts.
- Every bucket equals the corresponding retained exact parents' `ownersReadStatus`; existing per-owner metadata, parent state/reason, owner-row count and identity checks continue. Limited/malformed/duplicate retained owners refine their parent's bucket to Partial, even after successful transport.
- Source Partial/Cancelled may still reflect incomplete root inventory independently of these buckets. No exact overall-state equality between the vector and source is imposed. This preserves real root failures/truncation and cancellation.
- Counts add consistency evidence, not authentication or per-parent attestation. They detect the reported promotion with unchanged source counts. A coordinated rewrite of counts/rows and digest remains forgeable; swapping which parent carries a failure can preserve counts. If the requirement is independently binding outcomes to specific parent IDs, a bounded per-parent outcome ledger is a different, larger decision. A digest alone cannot authenticate either design.

## Historical schema-2 semantics must be decided explicitly

Existing `schemaVersion:2/reportSchemaVersion:1` files lack the new evidence and cannot truthfully acquire it by derivation from potentially rewritten rows. Preserve their original bytes, identity, digest, version and source claims; never auto-upgrade, reseal or overwrite them.

The recommended decision retains them through an explicitly limited historical inspection path, naming that per-parent ownership outcome consistency is unverified. They must not enter the new revision's validated-owner-success path. New captures and strict current-revision imports require the new vector; old revision readers continue to reject unknown new fields. A deliberate old-revision downgrade must not bypass this distinction. Historical export must retain the original revision and display the same limitation, rather than claim the new guarantee.

If the product instead requires all existing ReadFile/normal export paths to reject CLA-20261011-010 without a historical mode, the decision must approve refusing ambiguous old-revision ownership evidence, including the legitimate partial-root control. That is a deliberate compatibility change. Merely continuing to admit old files unchanged while adding counts to new captures does **not** close the original finding for old-schema imports.

## Acceptance after the decision merges

Use the red capture/tamper test adapted to the new revision, and preserve the real partial-root positive control. Add missing/duplicate/negative/overflow/mismatched counts, wrong revision/adapter, silent downgrade, mixed successful/failed zero-owner queries, per-parent retained-page failure, malformed/duplicate owner metadata, bounded/unattempted fan-out, and cancellation before/during child reads. Confirm strict file admission and all four export formats enforce the same guarantee and historical policy. No live acceptance or Windows execution is established here. Decision/source merge and required CI remain blocked by the stated budget constraints; this artifact grants no waiver.
