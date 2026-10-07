# Continuation prompt — review Claude's follow-up, then continue the product

**Prepared 7 October 2026 by Claude for the next agent (ChatGPT/Codex, working as Astra).** Paste the block below as the task. William has delegated commits and merges to the agents; the delegation and its conditions are in [HANDOVER](HANDOVER.md#owner-delegation-of-commits-and-merges--7-october-2026). Nothing else in this prompt widens what the agent may do.

```text
You are continuing work on Willzy12h/M365-Buildstandards (BDIT M365 Build Standard tool:
.NET 10 WPF, Windows x64, portable; CLI "bdit"). You are the Astra/Codex agent: astra/*
branches are yours, claude/* branches are Claude's. Read AGENTS.md,
docs/integration/AGENT-COORDINATION.md and docs/integration/HANDOVER.md on `integration`
first and follow them. HANDOVER records William's 7 October delegation: agents commit and
merge under stated conditions; William advises and approves and does not want to commit
or merge by hand. Use UK English. Ignore claude/M365-BuildStandard-Project-Context.md
(the user excluded it).

CURRENT STATE (re-fetch and verify; do not trust these references blindly)
- integration @ d39eb73. PR #19 (Preview.18 work), PR #20 (INT-049..051 contracts) and
  PR #21 (Claude's Preview.18 review fixes + usability pass) are merged. main @ 81b18f5
  is untouched. Source version 1.1.0-preview.19, unpublished.
- Exact-head CI for PR #21 (a515e6a, run 37593722744): 928 Engine + 119 App tests, native
  WPF harness 42 layouts / 79 commands / 0 binding issues / 0 tenant calls, PowerShell 5.1
  packaging, fresh 297-file package. Check the integration push run for d39eb73 too.
- What is now on integration (CHANGELOG "1.1.0-preview.19" lists it; the feedback register
  has every CLA-20261006-NN with its status):
  * Evidence integrity in every assessment result; modified snapshots refused.
  * Stored evidence judged as of its capture (Graph, Exchange, DNS; capped at now).
  * Capability matrix from the engine's real read routes; manifest verified at build.
  * Verified evidence hand-off: SHA-256 fingerprint sent separately (or a logged
    acknowledgement), adopt into an empty workspace with in-place re-verification and
    quarantine, `bdit verify-restore`. Settings is a guided hand-off/receive flow.
  * Opt-in support sections (OS/scale, recent Graph errors with request IDs, collection
    status, timeouts); export equals preview; no tenant/account/object names.
  * Usability: task-specific progress titles; results with next steps and Open folder;
    Quick Connect notice when the header shows an already-open session; more room in plan
    review at 1180x640; first read-only assessment path on Overview and OPERATOR-START.
- OPEN: Claude's follow-up PR from claude/m365-buildstandards-preview-review-mjr04x into
  integration:
  * Release lineage into 2026.09.30 (INT-051 first slice, recorded as INT-057):
    standards/lineage/lineage-2026.09.30.json + its manifest, verified by
    build/Test-StandardsManifest.ps1 and ReleaseLineage.Load; LineageReview annotates
    shared assessments (desktop and CLI) with an explanation for each earlier-release
    ownership record whose control ID now means something else, and flags records from
    releases without lineage. Nothing is rebound, moved or dropped; no finding status or
    plan changes.
  * ReviewedClientScope.Digest (INT-056, CLA-17): only company, notes, createdAt and
    updatedAt are non-material.

TASK 1 — REVIEW AND MERGE CLAUDE'S FOLLOW-UP PR
Review it independently: lineage relations against the catalogues (PRE-001..005 in .9/.10,
PRE-008 cardinality, retired controls, CFG-WIN-007/APP-WIN-008), the "unlisted keeps the
same requirement" rule, loader refusal of changed/unlisted/mis-pinned files, that
AssessmentContext ignores lineage for a non-published catalogue, that annotations never
change status/ownership/plans, and the INT-056 field list. Record findings as
GPT-20261007-NN. Fix small confirmed defects on your own branch (never push to claude/*)
or ask Claude via a PR comment. Merge it yourself under the HANDOVER delegation once your
review has no open safeguard finding and CI is green on the exact head.

TASK 2 — CONTINUE THE APPROVED PRODUCT WORK (each in its own PR, in this order)
1. Upgrade impact report (rest of INT-051): assess one unchanged capture under the source
   and target catalogues and present requirement changes separately from two-capture
   tenant drift. Missing collection evidence stays unknown. Desktop and CLI.
2. INT-049 versioned jobs and observations (see
   docs/integration/PRODUCT-CONTRACT-DECISIONS-2026.10.06.md); bind
   ReviewedClientScope.Digest; old manual checks shown as legacy/unbound, never migrated.
3. INT-050 legacy dispositions and cutover cases; retention never creates ownership or
   write permission; stages need recorded evidence; approval alone never closes.
4. Usability from real engineer feedback, if William provides it. Keep the native harness
   passing and add renders for new screens.
5. GitHub Actions still on Node 20 (checkout, setup-dotnet, cache, upload-artifact): CI
   forces them to Node 24 with a warning. Upgrade to current releases pinned by SHA in one
   reviewed PR, including publish-preview18.yml, and confirm CI.

HOW TO WORK
- Claim in docs/integration/WORK-CLAIMS.md and with a draft PR. Never push to claude/*
  branches or the source repositories.
- Logic changes need engine or view-model tests; UI changes need the command registered
  in the UiReview harness and renders at all supported sizes. App tests and the harness
  run only on Windows CI: read the logs; do not claim them from Linux.
- dotnet build BDIT.TenantToolkit.sln -c Release -warnaserror;
  dotnet test tests/BDIT.TenantToolkit.Tests -c Release; Windows CI for the rest.
- A new standard release needs a lineage file into it (docs/ARCHITECTURE.md).
- Remove dead code and stale comments you touch.
- Hand back: merges done (PR, merge SHA, verifying CI run), IDs addressed,
  branch/base/head, what changed and why, exact checks and results, what is still open,
  next bounded action. Update HANDOVER, COMPLETION-REGISTER and the feedback register.

HARD LIMITS
- Merge only under the HANDOVER delegation conditions. No approvals on William's behalf.
  Promotion to main, tags, releases, running the publish workflow and repository settings
  need William's explicit approval each time.
- No live sign-in, consent, tenant or device operation, module installation or external
  upload.
- Synthetic fixtures only; never commit tenant exports, credentials, tokens or secrets.
- Preserve every safeguard (read-only assessment, immutable reviewed plans, execution
  guards, no ambiguous write replay, unknown-write blockers, verified transfer with a
  fingerprint or logged acknowledgement, no restored authority, no ownership by reused ID
  or similar name). Do not edit published standards or weaken, skip or delete tests.
```
