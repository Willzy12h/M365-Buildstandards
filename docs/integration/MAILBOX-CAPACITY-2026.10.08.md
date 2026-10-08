# Primary mailbox capacity evaluation — PR #58

Implements INT-071's shared calculation in the engine. This slice is **source implemented, live-unverified**. It does not collect Exchange data, change a quota, add a report screen, alter schema-1 evidence or run Claude's library. A verified collector/report adapter and UI remain required for end-to-end reporting.

## Actual sources and rules

Astra retrieved the following official Microsoft pages over verified HTTPS on 8 October 2026 (HTTP 200), and inspected the relevant limits and service-plan identities:

- [Exchange Online limits](https://learn.microsoft.com/en-us/office365/servicedescriptions/exchange-online-service-description/exchange-online-limits): commercial Plan 2 primary send/receive capacity is 100 GB; Plan 1 is 50 GB.
- [Shared mailboxes](https://learn.microsoft.com/en-us/microsoft-365/admin/email/about-shared-mailboxes?view=o365-worldwide): over 50 GB requires the shared mailbox's own Plan 2 licence. The documented historical pre-July-2018 unlicensed 100 GB provisioning exception explains why an observed 100 GB quota cannot prove entitlement today. The tool does not reduce or alter such a quota.
- [Service-plan reference](https://learn.microsoft.com/en-us/entra/identity/users/licensing-service-plan-reference): commercial `EXCHANGE_S_ENTERPRISE` = `efb87545-963c-4e0d-99df-69c6916d9eb0`; `EXCHANGE_S_STANDARD` = `9aaf7827-d63c-4b61-89c3-182f06f82e5c`. These are service-plan IDs, not product SKU names.

Only an exact mailbox `ExternalDirectoryObjectId` joined to a unique assigned-licence report user ID can establish the reviewed eligibility. Product display names, SMTP/display-name matches and tenant subscription availability are not joins. Complete per-user products/service plans, a matching ID/name, `appliesTo = User` and `provisioningStatus = Success` are required for Plan 2 eligibility. Missing, malformed, partial, cancelled or unreviewed Exchange variants remain unable to check. Government, education, future and unreviewed mailbox types are not inferred from similar names.

## Capacity and evidence boundaries

All primary size, warning/send/send-receive quota and archive size/quota texts remain separate and are retained verbatim. Only an explicit supported non-negative integer Exchange byte count is interpreted; 100 GB is compared with 107,374,182,400 bytes. `Unlimited`, rounded GB-only text, localised/ambiguous separators, overflow and failed reads never become zero or an affirmative 100 GB value. Interpretation is bounded to 4,096 characters. Zero is accepted only when actually returned as an explicit byte count. Archive, Recoverable Items and local OST/PST limits cannot establish primary capacity or entitlement.

Results separately expose configured 100 GB (true/false/unknown), eligibility and the combination: eligible but lower quota, configured but entitlement unconfirmed/not eligible, configured and eligible, another observed quota, or unable to check. There is no recommendation to buy/assign a licence, expand an archive or increase a quota.

The input assigned-licence report must pass the existing strict schema/digest/tenant checks. Its ID and observed time are cited. Mailbox adapter input has its own exact mailbox identity and UTC capture time; different service reads are not represented as one atomic live observation. This pure evaluator does not authenticate a claimed mailbox source: a subsequent collector must verify tenant/account, bounded reads, per-field read status and immutable separate report evidence before presenting results. It must avoid duplicate mailbox/user rows while representing shared/resource mailboxes independently. Historical evidence remains historical; the calculation is not deployment before-evidence.

## Synthetic validation

The first implementation checkpoint `bad860b` ran 25 dedicated tests: 22 passed, two failed because a known Exchange service-plan ID with an unrelated name was incorrectly treated as not eligible, and one failed because its test fixture put a failed row in a wholly failed section (the existing strict reader correctly refuses rows in failed sections). The source now treats known IDs with mismatched names as unknown; the fixture represents retained user rows in a partial section. All 25 then passed. Four further bounded-input/type cases were added. A completed broad local run before those final four additions passed all 1,295 engine/CLI tests. Final targeted/strict-build and exact Windows outcomes are recorded on the PR; human and live acceptance remain open.

Final local checks: all 29 dedicated cases passed after the bounded/type additions, and the strict solution cross-build passed with zero warnings/errors. The earlier completed broad run (1,295 cases) predates those four added cases; it is not described as the final Windows run.
