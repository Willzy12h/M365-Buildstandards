# Astra review findings and cost-effective release assurance — 10 October 2026

This is a review recommendation, not release acceptance or a guarantee of fault-free Microsoft behaviour. Use [CURRENT-STATE](../CURRENT-STATE.md) for orientation, then fetch integration and run the coordination pre-flight. Changed bytes need fresh applicable verification.

## Confirmed findings from this pass

| Finding | Observed behaviour | Current disposition |
|---|---|---|
| Native layout regression, #73 | A wrapping experimental write-access badge increased header height and pushed native pages into unwanted scrolling. | Corrected at 3ea8485; refreshed source 198cd9d passes full Windows/native/package validation. Independent review remains required before merge. |
| #67 test scheduling assumption | Immediate request-count assertions relied on authentication completing synchronously. | Corrected at 381deb0. A deliberately yielding fake authenticator reproduces the old failure; cancellation mutations still fail and the restored suite passes. Narrow delta review requested. |
| AST-20261010-01, #74 | Empty/whitespace required size, quota and mailbox type values can claim Collected. | Posted with four original failing-first reproductions, two additional required-type refusals and 37-case combined scratch repair proof. Claude-owned fix and fresh re-review pending. |
| AST-20261010-02, #75 | Explicit null result collections/elements throw ordinary exceptions outside the sanitised refusal path. | Posted with three failing reproductions and 24-case scratch repair proof. Claude-owned fix and fresh re-review pending. |
| Enrolment import gap, #57 | Current ENR-002 could not satisfy authored naming because its family had no rule. | Implemented after merged INT-091 at 665fa2d; 87 focused and 1,464 full restored tests plus Windows checks pass. Independent delta review pending. |
| AST-20261010-03, #75 | Expected tenant/account with an observed expired Exchange token can still perform three reads and seal Collected. | Posted with a real-wrapper/synthetic-module reproduction, official field reference and five-case scratch repair proof. Claude-owned fix and fresh re-review pending. |
| AST-20261010-04, #77 | Promotion description misstates write behaviour, CA targeting, script count and validation. | Promotion PR converted to draft; corrections posted. Final candidate and William’s promotion approval remain pending. |

Scratch repairs are experiments outside the checkout. They are not merged code, original-head proof or live acceptance. Shared feedback attribution stays with the reported findings; do not mark broad R items complete from these tests.

## Recommended review sequence

1. Resolve the confirmed findings and review their narrow deltas. Each refusal fix needs a failing-first case, a valid positive control and fresh exact-head Windows CI. Add explicit null/blank/missing distinctions, changed identity/expiry during reads, cancellation, output overflow, non-zero exit with plausible output and descendant-held pipes where relevant.
2. Once authentication, evidence and the owned runner settle, use one dedicated Astra high or xhigh review of those paths and their integrations. Pin the source SHA and state the invariants and known limitations. Ask for confirmed, reproducible defects with file/line and a failing case; keep hypotheses separate. Xhigh is useful for these interacting boundaries; it adds less value to routine copy, documentation and formatting changes.
3. Use an independent Claude review for the final integration delta. Different reasoning can expose assumptions the author missed. Do not run several unfocused full-repository passes or repeat an unchanged full master prompt as a substitute for tests.
4. Verify each fix with a targeted regression, then run one final full Windows/native/extracted-package suite on the exact candidate bytes. Repeat a broad suite when a new change, failure or unresolved concern justifies it. Keep the original ZIP, independent fingerprint and source/check identities together.
5. Keep William's live and human acceptance gates open. WAM/CA/MFA, consent, Graph API behaviour, Exchange module/RBAC/retention and real accessibility/scaling cannot be established by offline fixtures. Experimental/manual gating remains required for unaccepted features; Exchange writes stay copy-only.

## Token and context discipline

The practical default is one focused high-effort review plus one independent integration review and narrow repair verification. More whole-repository passes consume repeated history and can generate duplicate or hypothetical findings. No measured token-price comparison is available here; this is an efficiency recommendation, not a numerical cost claim.

Give reviewers a compact packet: current source SHA, selected diff/files, contract references, open findings, exact validation summaries and unresolved gates. Read CURRENT-STATE first; retrieve historical decisions only when relevant. Preserve historical records but keep them out of the routine orientation packet. Store findings and proof in shared files/PRs, cite existing results and inspect concise CI summaries rather than repeatedly emitting full logs. Do not put credentials, caches or private tenant/user data into any reviewer packet.

## Release position

Passing checks reduce demonstrated risks; they cannot promise flawless operation. A release candidate requires settled source, truthful capability/validation status, exact-source package verification and an approval record with blank human/live gates. Main promotion, tags, releases, publication and security-policy changes remain separately approved. This pass has authorised none of them.
