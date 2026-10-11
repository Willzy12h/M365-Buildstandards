# Mailbox capacity presentation — PR #94

Root-tested source **6d4f1bc1e5c60ff0b389319113b0c8e7da25bdfa** composes current Run/task-centre/report branches. Documentation-only updates retain those source bytes. Temporarily targets #93, keeping review focused on this new offline page rather than replaying its parents. Claude explicitly handed off the unstarted presentation in CLAUDE-20261011-003.

**Reports → Mailbox capacity** pairs strict existing `exo-mailbox-inventory` and `users-licences` evidence for the selected expected tenant. Saved and supplied sources keep independent original IDs, timestamps, states, digest, account claims and limitations. No sign-in, live collection, new domain prompt, licence assignment or quota change is performed.

Results show name/SMTP, observed 100 GB quota, verified-plan entitlement, finding and read state. Details preserve exact identity, raw primary size/warning/send/send-receive quota and archive size/quota, including Unlimited and unknown reasons. A licence label does not establish configured capacity or entitlement; failed reads never become zero, false or an empty successful list. This is historical derived review, not a new complete capture or deployment authority.

Search/entitlement filters affect presentation only. HTML/CSV ZIP/JSON/Excel export the full review, raw measurements and source provenance; JSON includes both original records and labels the document `mailboxCapacityDerivedReview`. No evidence is rewritten, upgraded or resealed. Source/context changes invalidate results and export attribution. Stored filename/record-ID, bounded UTF-8/32 MiB, tenant/digest/link and partial inventory checks are retained; malformed or inaccessible listings explain their limits.

## Actual validation

Isolated new-file patch SHA-256 `9c2a987eb5f5ec0fe1c8aa9b2b6bef4151f501690d4b348f06a28434c83bf8a4`: 27 focused cases pass (19 new plus 8 #89 controls); removing the filename-ID guard fails its regression and leaves a valid-source control passing. Isolated strict App.Tests/harness builds pass, zero warnings/errors; eight new Windows App cases compiled, not executed.

Root performed reachable Shell/template/harness wiring, preserving both Run and mailbox command registrations and all earlier groups/steps/pages. The navigation contract now requires exactly 21 pages. Every page is included in generic three-size snapshots, and the mailbox partial adds populated saved/supplied/Partial/archive-unknown/Unlimited/filter/full-export/provenance native journeys without changing size thresholds or clipping assertions.

[Composed validation JSON](release-readiness/COMPOSED-READ-REPORTS-VALIDATION-2026.10.11.json) records exact source, commands/counts and log/TRX hashes. **Full unmodified 2,056:** 2,026 passed, 30 failed, 0 skipped; all failures are the existing fixed-location PowerShell MemberData discovery cases. **Sequential available suite:** 1,972 passed, 0 failed/skipped, explicitly excluding ExchangeReportRunnerTests (which also had 54 passing cases in the full run). No test/host-discovery alteration, host symlink or hidden exclusion. Strict composed App.Tests and UiReview crosscompile pass with zero warnings/errors. Local evidence `/workspace/bdit-validation/2026-10-11-composed-read-reports/`.

Actual Windows App/native renders, PS5.1/runner module execution, fresh extracted-package checks, physical accessibility and live behaviour are **unrun**. Claude independent review and exact-head Windows/integration checks remain mandatory. CLA-20261011-010 on parent #87 remains unresolved and needs decision #95; passing tests here do not close that finding. No candidate, version bump, publication or broad completion claim.
