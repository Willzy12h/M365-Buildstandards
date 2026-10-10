# Astra review findings and cost-effective release assurance — 10 October 2026

This is a review recommendation, not release acceptance or a guarantee of fault-free Microsoft behaviour. Use [CURRENT-STATE](../CURRENT-STATE.md) for orientation, then fetch integration and run the coordination pre-flight. Changed bytes need fresh applicable verification.

## Confirmed findings from this pass

| Finding | Observed behaviour | Current disposition |
|---|---|---|
| Native layout regression, #73 | A wrapping experimental write-access badge increased header height and pushed native pages into unwanted scrolling. | Corrected at 3ea8485; refreshed source 198cd9d passes full Windows/native/package validation. Independent review remains required before merge. |
| #67 test scheduling assumption | Immediate request-count assertions relied on authentication completing synchronously. | Corrected at 381deb0. A deliberately yielding fake authenticator reproduces the old failure; cancellation mutations still fail and the restored suite passes. Further Claude review found missing successful page-two retry, actual cancelled report rows and explicit renewal-count checks. These are corrected at 5ee67b3 with targeted mutations; delta review pending. |
| AST-20261010-01, #74 | Empty/whitespace required size, quota and mailbox type values can claim Collected. | Posted with four original failing-first reproductions, two additional required-type refusals and 37-case combined scratch repair proof. Corrected by Claude, independently re-reviewed and merged through #74/#75; exact Windows and actual integration push checks passed. Implemented, live-unverified. |
| AST-20261010-02, #75 | Explicit null result collections/elements throw ordinary exceptions outside the sanitised refusal path. | Posted with three failing reproductions and 24-case scratch repair proof. Corrected by Claude, independently re-reviewed and merged through #74/#75; exact Windows and actual integration push checks passed. Implemented, live-unverified. |
| Enrolment import gap, #57 | Current ENR-002 could not satisfy authored naming because its family had no rule. | Implemented after merged INT-091 at 665fa2d; 87 focused and 1,464 full restored tests plus Windows checks pass. Core CLA-34 independently resolved; historical compatibility documented; #57 merged as 5ef5470 and actual integration push 38057154787 passed. Naming desktop/live acceptance remains. |
| AST-20261010-03, #75 | Expected tenant/account with an observed expired Exchange token can still perform three reads and seal Collected. | Posted with a real-wrapper/synthetic-module reproduction, official field reference and five-case scratch repair proof. Corrected by Claude, independently re-reviewed and merged through #74/#75; exact Windows and actual integration push checks passed. Implemented, live-unverified. |
| AST-20261010-04, #77 | Promotion description misstates write behaviour, CA targeting, script count and validation. | Promotion PR converted to draft and its description corrected by Claude. Final candidate and William’s promotion approval remain pending. |

Scratch repairs are experiments outside the checkout. They are not merged code, original-head proof or live acceptance. Shared feedback attribution stays with the reported findings; do not mark broad R items complete from these tests.

## Recommended review sequence

1. Resolve the confirmed findings and review their narrow deltas. Each refusal fix needs a failing-first case, a valid positive control and fresh exact-head Windows CI. Add explicit null/blank/missing distinctions, changed identity/expiry during reads, cancellation, output overflow, non-zero exit with plausible output and descendant-held pipes where relevant.
2. Once authentication, evidence and the owned runner settle, use one dedicated Astra high or xhigh review of those paths and their integrations. Pin the source SHA and state the invariants and known limitations. Ask for confirmed, reproducible defects with file/line and a failing case; keep hypotheses separate. Xhigh is useful for these interacting boundaries; it adds less value to routine copy, documentation and formatting changes.
3. Use an independent Claude review for the final integration delta. Different reasoning can expose assumptions the author missed. Do not run several unfocused full-repository passes or repeat an unchanged full master prompt as a substitute for tests.
4. Verify each fix with a targeted regression, then run one final full Windows/native/extracted-package suite on the exact candidate bytes. Repeat a broad suite when a new change, failure or unresolved concern justifies it. Keep the original ZIP, independent fingerprint and source/check identities together.
5. Keep William's live and human acceptance gates open. WAM/CA/MFA, consent, Graph API behaviour, Exchange module/RBAC/retention and real accessibility/scaling cannot be established by offline fixtures. Experimental/manual gating remains required for unaccepted features; Exchange writes stay copy-only.

## When to use the final xhigh prompt

William asked for a paste-ready Astra xhigh prompt once the integration is ready. Prepare it when the required source slices and independent review corrections have merged, and fresh Windows CI has passed on that exact integration revision. It must pin that SHA, identify the actual remaining scope and use this compact packet rather than replaying the master conversation. Selecting Astra xhigh is an app/model setting; text in a prompt does not itself change the selected reasoning effort. The prompt will request read-only review first, with confirmed findings, regressions and shared-file/PR output before follow-up implementation. It grants no live action, publication or safeguard change. Current source has pending independent reviews and unfinished Reports/adapters/navigation/doctor slices, so it is not the final review candidate yet.

## Token and context discipline

The practical default is one focused high-effort review plus one independent integration review and narrow repair verification. More whole-repository passes consume repeated history and can generate duplicate or hypothetical findings. No measured token-price comparison is available here; this is an efficiency recommendation, not a numerical cost claim.

Give reviewers a compact packet: current source SHA, selected diff/files, contract references, open findings, exact validation summaries and unresolved gates. Read CURRENT-STATE first; retrieve historical decisions only when relevant. Preserve historical records but keep them out of the routine orientation packet. Store findings and proof in shared files/PRs, cite existing results and inspect concise CI summaries rather than repeatedly emitting full logs. Do not put credentials, caches or private tenant/user data into any reviewer packet.

## Release position

Passing checks reduce demonstrated risks; they cannot promise flawless operation. A release candidate requires settled source, truthful capability/validation status, exact-source package verification and an approval record with blank human/live gates. Main promotion, tags, releases, publication and security-policy changes remain separately approved. This pass has authorised none of them.

## Findings retained after the latest independent review

#65 merged with CLA-20261009-01 still open despite a conflicting ready comment. #78 adds the missing partial/capped-reason round-trip and HTML/CSV/XLSX test, as well as explicit BOM/line-break tests for #68; full Windows checks are green, independent review/merge pending. #73's green helper tests did not prove production silent-renewal wiring or setup/consent invalidation; CLA-20261010-40/41 are implemented at 28a30a0 with actual production-path mutations and a passing full Windows push; delta review remains. These are demonstrated coverage gaps, not proven Microsoft failures. #69 ownership/routing/replication wording and #76 snapshot/tracker claims have explicit review responses.

The runner's PowerShell 5.1 cooperative-stop timing failure and missing dedicated final-close-only negative control remain narrow follow-ups. Real Exchange token fields/RBAC/modules are live-unverified. A possible direct-export disk-full partial file/concurrent-mutation case is a hypothesis; no reachable concurrent caller was established. Do not present a surviving mutation or unrun live check as closed because a different full run was green.
## Dedicated continuation performed — 10 October 2026

William has now requested the focused Astra xhigh continuation. [The pinned result](XHIGH-REVIEW-2026.10.10.md) records confirmed startup-integrity and owned-process cancellation defects plus the permission-preview timing correction, with reproductions and narrow repair checks. This supersedes the earlier suggestion to wait for a new master prompt. Review the pending corrections and future integration deltas; repeat a broad pass only when new interacting source or a demonstrated regression warrants it. The final candidate and human/live gates remain open.

## Delegation calibration

William subsequently authorised a bounded parallel Astra/Sol 6.1 xhigh comparison and a first delegated implementation. [DELEGATION-2026.10.10](DELEGATION-2026.10.10.md) records the actual results, AST-20261010-08 discovery and repair, one-writer policy and effort routing. Both models passed the shared process grading; Sol found the independently confirmed deadline defect. Reliable token/cost counters were unavailable, so neither speed nor this small sample establishes a cost winner. Correctness, usability and total task/review/rework efficiency take priority over speed; Medium/Low have not been benchmarked here.
