# Handover and priorities

## Current work

PR [#1](https://github.com/Willzy12h/M365-Buildstandards/pull/1), branch astra/claude-baseline-reporting, targets integration. The current user selected Claude as primary and authorised source development. Neither source repository nor Claude's separate coordination branch was changed.

This PR imports the selected C# application, recovers six report files, fixes the WPF copy-summary compilation error, preserves equivalence export sheets with meaningful tests, and adds portable packaging plus reporting-source tracking to CI.

See [baseline review](BASELINE-REVIEW.md), [comparison](COMPARISON-MATRIX.md), [source register](SOURCE-REPOSITORIES.md), and [testing evidence](TESTING-EVIDENCE.md). Historical source documents are preserved as claims to check, not a substitute for current evidence.

## Next implementation task

**Enforce complete pre-change evidence at the execution boundary.** Check durable snapshot existence/integrity, overall completeness, required collection/detail coverage and plan bindings before any tenant write. Reject incomplete/missing/tampered evidence without calling Graph.WriteAsync. Add tests covering an unrelated failed collection as well as a failed collection used by a selected control, and a caller invoking the executor directly. Reuse existing models; avoid a schema redesign unless necessary.

This task follows the user's explicit complete-snapshot requirement. Do not silently reduce it to per-control completeness. Separate later activation prerequisites from candidate-creation dependencies.

## Priority queue

| Priority | State | Work / verification needed |
|---|---|---|
| P0 | Addressed in this PR, see testing evidence | Recover reporting, compile full WPF solution, run tests, produce portable review package |
| P1 | Known issue | Enforce complete durable evidence and full plan validation at execution boundary |
| P1 | Known issue | Finalise every result after unexpected exceptions, including InProgress and write-accepted/readback-failed cases; never replay ambiguous writes |
| P1 | Known issue | Force token renewal after a GET 401 while retaining tenant/operator checks; no write retry |
| P1 | Known issue | Distinguish missing properties from JSON null in comparison and verification |
| P1 | Planned | Resolve required dependencies and review supported inactive PATCH scenarios with explicit tests |
| P2 | Known issue | Windows sign-in/collection cancellation, shutdown deadlock/cleanup checks, responsiveness for large exports |
| P2 | Known issue | Correct client-report read-only/no-change wording; review Markdown rendering and export edge cases |
| P2 | Candidate | Port one-time connection workflow and useful log/configuration navigation from Asta |
| P2 | Known issue | Three CA1416 warnings around Windows-only token cache calls in cross-platform Graph project |
| Later | Planned | Explicitly authorised tenant validation of each recipe, including permission/licence limitations |
| Later | Deferred | App-only, app provisioning, cloud browser interface and additional workloads |

## Decisions still needing product input before dependent work

Conditional Access baseline candidates are disabled and retain stored targeting and exclusions. Historical "disabled and unassigned" wording is ambiguous. Do not clear targeting or enable policies until the intended semantics and tenant scope are explicitly agreed. Current source-code approval does not authorise live testing.

## Status vocabulary

Implemented and verified means the named check was actually run. Implemented but unverified means source exists without that verification. Planned is agreed future work; Candidate is under consideration; Deprecated is superseded behaviour; Known issue is an observed defect. Passing tests do not verify GUI interaction, consent, live Graph or automatic recovery.
