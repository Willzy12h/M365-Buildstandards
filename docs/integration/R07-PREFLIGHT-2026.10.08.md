# Readiness reads and next steps

Astra source slice, 8 October 2026, branched from integration `ef5d61c5180f3d87bd506a93b178c56158a2ad82` after fetching and checking open PRs in all three repositories.

## Claim and scope

Review and correct the existing `ServiceReadinessService` and add synthetic regressions for failed or malformed reads. A failed read must remain Unknown, with a practical next step; it must not imply a missing configuration. Observe emergency-account identities and properties strictly, without treating malformed fields as a successful check. Keep configuration observations distinct from operational acceptance.

No new permission, registration, live call, authentication flow or write route is introduced. Claude's #46 script library and #47 review register remain owned by Claude. This slice does not touch their source or findings. Shared ledger additions are limited to this Astra claim/decision and CHANGELOG entry.

## Validation

Implemented source corrections in `ServiceReadinessService`. The first 25 synthetic regression cases produced 19 failures against the pre-fix source and all 25 passed after the correction. Additional known-unsuitable-account cases retain Action required. Full Linux engine/CLI suite: 1,248 passed, zero failed/skipped. Strict solution cross-build: zero warnings/errors. Exact-head Windows CI and independent review remain pending. Microsoft responses, roles, consent, modules and human/live acceptance remain unverified.

## Engineer meaning

- **Configuration observed**: required returned fields were interpretable and matched this narrow check. It is not a functional or operational pass.
- **Action required / Review required**: a completed, interpretable read found a concrete condition to review.
- **Unknown**: the read failed, identity did not match, or necessary fields could not be interpreted. Follow the row's Next step; do not deploy on an assumption that configuration is absent.

Use the existing assessment application and reviewed read permissions. A 403 asks the engineer to validate the registration/grants and their service role; it does not request a new scope or grant consent automatically. A 404 needs the object/service/access checked before setup changes are proposed. The live module, RBAC, WAM and consent journeys in R07 remain acceptance gates.

## CLA-20261008-30/31 response — 9 October 2026

A concrete unsuitable emergency account remains Action required when another identity is malformed or its read fails. The row retains the unknown identity/read and its safe next step; it is not a complete successful check. Disabled accounts are labelled explicitly. No service error payloads are displayed, and caller cancellation still propagates. Per-account read failure no longer discards a previously observed problem.

The official [androidManagedStoreAccountBindStatus beta reference](https://learn.microsoft.com/en-us/graph/api/resources/intune-androidforwork-androidmanagedstoreaccountbindstatus?view=graph-rest-beta) was retrieved over verified HTTPS on 9 October 2026 and lists unbinding (3), alongside notBound, bound and boundAndValidated. This returned state now says Review required with in-progress guidance, without suggesting another bind/consent while unresolved. Unknown/unreturned states retain Unknown. This enum reference is not proof of live timing or successful unbinding.

Three new regression cases failed against the previous source (28 passed, three failed). Final local and Windows results are recorded on the PR. Microsoft, RBAC, consent, operational recovery and human/live acceptance remain open.
