# Read-only Run workflow — PR #93

Source implementation `9347319a942914055b9c78949cd9ebb476c6c673`, built on the existing INT-088 runner and current report/task-centre branch. Temporarily targets #92's branch so review is limited to this slice; retarget integration only after reviewed parents merge. Claude explicitly handed off the unstarted Run screen in CLAUDE-20261011-003, and William authorised completion.

**Scripts → Run reports** offers only registered schema-2 read-only items; currently the mailbox size/quota/archive adapter. The 29 schema-1 Copy items retain their forms and execution boundaries. No general PowerShell executor, new cache, module installation, consent or Exchange mutation is introduced.

The engineer chooses an approved installed runtime, reviews prerequisites and enables a default-off connection-scoped read opt-in. A new exact review pins verified tenant/account, profile/connection, resource, manifest/body/wrapper hashes, runtime, typed values and limits. Separate approval and Run are required. Changing inputs/context invalidates review; review is consumed before the single transport call. Changing pages never starts authentication. Exchange authenticates separately and may require Microsoft CA/MFA/consent.

The pure controller reuses the existing catalogue, request binder, owned Exchange runner and immutable evidence store. Workspace exclusivity and owned cancellation prevent another task from starting while stopping. Refused/missing/malformed/partial/cancelled/timed-out results remain distinct. Only strict context/input/hash-bound evidence is admitted. Copy outcome/JSON and explicit local Save are available after completion; save errors say persistence is unconfirmed, because an existing immutable disk record may remain. Save cannot overwrite evidence or repeat a run.

One runnable manifest's stale “Run not offered” limitation is corrected, with its exact registry manifest pin. Body/schema/limits/runtime/module requirements and every Copy entry pin remain unchanged. All collection/authentication/mutation remains outside WPF.

## Actual checks and remaining gates

Isolated patch base ece29493; patch SHA-256 `dd0adcd29b08308e5aedcff47e85fb216d82d64c0c93535520f69c20ca097c9a`. Final 62 pure workflow tests passed; earlier related 122-case set passed. Six independently compiled guard mutants fail as intended. Actual duplicate-ID store refusal preserves original bytes across automatic/explicit Save, with one runner call. Isolated App.Tests and UiReview crosscompile with zero warnings/errors.

Root composed source 6d4f1bc includes both Run and mailbox review: full unmodified suite 2,026 pass/30 fixed-host MemberData failures; sequential available suite 1,972 pass, excluding ExchangeReportRunnerTests explicitly. Strict composed App.Tests/harness crosscompilation passes. Structured results live in the #94 validation record; these are source/synthetic/crossbuild proofs, not Windows execution.

The harness includes setup/exact-review/Partial-outcome/cancellation/copy/save checks at all three unchanged sizes, accessible names, keyboard and explicit command categories with zero real tenant calls. It is compiled, **not executed**. Claude independent review, exact-head Windows/PS5.1/module/native/fresh-package validation and William's live/human acceptance remain pending. No candidate, release/version or broad feedback completion claim.
