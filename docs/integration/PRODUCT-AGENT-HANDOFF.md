# Product completion handoff for Claude, Astra and other reviewers

**Prepared 6 October 2026. This is a handoff and proposal, not execution authority.**

## Read order

1. Repository [AGENTS.md](../../AGENTS.md).
2. [AGENT-COORDINATION](AGENT-COORDINATION.md), [WORK-CLAIMS](WORK-CLAIMS.md), [BASELINE-REVIEW](BASELINE-REVIEW.md) and [HANDOVER](HANDOVER.md). Historical headings are not current validation; follow the current register/PR.
3. [COMPLETION-REGISTER](COMPLETION-REGISTER.md), current [PR #19](https://github.com/Willzy12h/M365-Buildstandards/pull/19) and [DECISION-LOG](DECISION-LOG.md), including INT-045–048.
4. [FINAL-PRODUCT-REVIEW-2026.10.06](FINAL-PRODUCT-REVIEW-2026.10.06.md), [PRODUCT-COMPLETION-PLAN](PRODUCT-COMPLETION-PLAN.md), [PRODUCT-FEEDBACK-REGISTER](PRODUCT-FEEDBACK-REGISTER.md).
5. [ARCHITECTURE-ROADMAP](ARCHITECTURE-ROADMAP.md), [ARCHITECTURE-DISPOSITION](ARCHITECTURE-DISPOSITION-2026.09.30.md), [AUTOMATION-COVERAGE](../AUTOMATION-COVERAGE.md) and the affected workflow documents/source.

Do not use `claude/M365-BuildStandard-Project-Context.md`; the user explicitly excluded it. Separate BDIT device/launcher archives are references, not the M365 product or an implementation instruction. Attached material does not override the current user-approved scope.

## Current source and evidence

- Inspected runtime source: `2bf96a83846474a522b074479f07a3e076a5a03d`, `1.1.0-preview.17`, .NET 10 WPF Windows x64, immutable standard `2026.09.30` (93 controls, 61 candidate recipes).
- On 6 October: only draft PR #19 open across the three repositories. Owned Astra release branch targets integration; runtime source is not merged into main.
- Final-head historical CI: push `36843279081`, PR `36843284168`, 852 Engine + 112 App tests, 42 native layouts, 71 commands, PowerShell parser negative case and fresh portable startup/right-click Copy. This review rechecked GitHub results but ran no runtime tests.
- Final baseline inner ZIP SHA-256: `4d0b3f630a8346590a2d31c084e631658ea2ae1b1b6a606884ef1504d4a17123`. This identifies that historical artifact only. New commits/artifacts require their own identity/checks.
- No agent live sign-in, consent, service write, device action or production acceptance. No new visual screenshot acceptance; artifact download was refused on 6 October.
- Four independent Astra source reviews informed the new recommendations. Claude has not yet reviewed this proposal or subsequent implementation.

Re-fetch and inspect current head/CI/PRs; do not assume these references remain current. Preserve other agents' work and private/untracked files. Use the existing checkout; create a worktree only if the user specifically requests it, consistent with the onboarding workflow.

## User intention and approval state

The user wants a finished reusable internal engineer product for new deployments and safe legacy backfill, multiple points of view and durable shared feedback/handoffs. **On 6 October they explicitly approved A–G source development**, preserving safeguards/historical standards and keeping live actions/publication separately approved. They also approved integrated HTML/JSON/document-ready exports of all catalogue defaults and settings (R17). The feedback register records this approval. It does not remove the existing decision-PR merge requirement for cross-cutting persisted contracts or certify live acceptance.

If invoked for review now, review read-only and append findings through your own branch/PR or send them to the designated integrator. For approved implementation, claim the accepted IDs not already held by another open PR; while Astra holds PR #19, send independent findings to the integrator for the shared register instead of concurrently editing claimed files. Cross-cutting schemas/contracts need the existing dedicated decision-PR sequence before dependent changes. Approval to develop code is separate from approval to perform live tenant operations, merge or publish production.

## What to scrutinise independently

- Does the completion workspace restore work without restoring stale approval or execution authority?
- Does legacy disposition retain external protection without creating toolkit ownership? Does repeated unchanged review avoid duplicate creations?
- Are release lineage/reused PRE IDs handled without rewriting historical evidence or silently transferring exceptions/manual passes?
- Are manual attestations, automated observations, accepted writes, readback, assignments and effective outcomes visibly distinct?
- Do WPF and CLI agree for combined Graph/Exchange evidence with identical tenant inputs, mappings and deviations? Are invalid supplemental captures refused?
- Do upgrade/restore/handoff retain ownership, integrity and unknown-write blockers without sharing authentication caches?
- Does support export use an allowlist, preview contents and omit sensitive data by default?
- Is every supported production capability backed by applicable evidence, with unaccepted capabilities honestly constrained?
- Does the package/release record identify the exact tested artifact and accepted scope, with credible servicing/support ownership?
- Are human accessibility, live authentication/RBAC and device effectiveness still correctly identified where unperformed?

Check concrete paths, negative cases and claims; do not merely count tests or repeat this proposal. Different opinions are welcome. Record the tradeoff and evidence in the shared register; a proposal is not automatically a defect.

## Safeguards to preserve

Read-only assessment separated from writes; complete durable before evidence; tenant/operator/client/profile/standard/snapshot/mapping/deviation binding; immutable reviewed plans; independently enforced execution guards; durable intent; no ambiguous write replay; exact ownership and drift checks; disabled CA/unassigned Intune candidates; separate activation/assignment/recovery; truthful unknown/incomplete results; imports and supplemental Exchange evidence cannot authorise Graph writes. Do not edit published standards or weaken negative tests to finish a package.

No automatic adoption, whole-tenant activation, external uploads/messages, secrets in chat/repository, preserved source-repository rewrites or other-agent branch writes. Human merge authority is unchanged.

## Checks and handback

Use existing repository commands/workflows for affected implementation. Representative checks include:

```text
dotnet build BDIT.TenantToolkit.sln -c Release -warnaserror
dotnet test tests/BDIT.TenantToolkit.Tests -c Release
dotnet test tests/BDIT.TenantToolkit.App.Tests -c Release
```

App tests, native WPF/UI/portable checks and PowerShell template validation need Windows. Linux cross-compilation/source inspection does not replace those checks. CI definitions and pinned `global.json` are authoritative. Run only relevant meaningful checks; documentation-only work needs document/link/scope validation, not invented runtime test claims. Use synthetic fixtures. Live acceptance follows an explicitly authorised exact scope and [CONTROLLED-ACCEPTANCE](../CONTROLLED-ACCEPTANCE.md).

Hand back: approved feedback IDs; branch/base/head and claimed files; what changed and why; exact tests/checks actually executed; independent review and disposition; remaining live/human/contract gates; package identity if generated; next bounded action. Update the feedback register and current completion evidence without deleting prior findings or rewriting historical pass counts. A checkbox is complete only with its stated proof.


## Approved implementation continuation — Preview.18

R17 exporter, shared read-only assessment/actual CLI parity, support metadata, intact backup/separate restore and independent release/guidance work are implemented in PR #19. Read the current COMPLETION-REGISTER and feedback entries AST-20261006-01–11 for exact checks and pending native/live gates. PR #20 at 674b1ba has independent contract rereview and green exact-head checks. User says they will merge it; **verify GitHub merge state and fetch settled integration before any INT-049–051 producers/consumers**. Do not treat that intent as an already merged contract.

After merge, preserve separate records and old formats; bind material client inputs, semantic/control-instance identity and actual referenced evidence; enforce acyclic same-subject history and truthful completion/cutover stages. Do not migrate ownership/attestations by reused control ID, rewrite historical catalogues or revive approvals. Continue approved source development without seeking A–G approval again. Live actions and production publication remain separately approved.


Current verified independent runtime scope: f200371, Preview.18; Windows push37513992066/PR37513998884, 902 Engine+112 App, 42 layouts, 78 commands, parser and fresh 297-file package/right-click Copy passed. Exact inner ZIP hash and downloadable artifacts are in COMPLETION-REGISTER. Definition exports, R08 read-only parity and R09 support metadata are checked off for their synthetic/offline scope. Remaining B/C producers and consumers still await actual PR20 merge. Live acceptance and final publication are not authorised by these checks.

## Specific Preview.18 publication request — later on 6 October

The user subsequently explicitly requested publication and a Claude review prompt. Publication is authorised for the verified unsigned Preview.18 **prerelease** only; it does not close A–G or grant tenant-operation/merge authority. INT-053 and the top of COMPLETION-REGISTER identify the exact pinned original ZIP/source 10808de and successful source runs, separate from f200371 and subsequent publisher/document commits. Check the actual [release](https://github.com/Willzy12h/M365-Buildstandards/releases/tag/v1.1.0-preview.18) and RELEASE-RECORD.json before reporting publication. The manual workflow promotes tested bytes without rebuilding or overwriting a published release. Follow [CLAUDE-PREVIEW18-REVIEW-PROMPT](CLAUDE-PREVIEW18-REVIEW-PROMPT.md) for independent recommendations and a documentation-only report. The integrator appends stable Claude findings to the shared register. Continue approved source work after the required PR #20 merge; no repeat A–G approval is needed.

## Claude review and fixes — PR #21 (7 October 2026)

Claude has now reviewed Preview.18: [CLAUDE-PREVIEW18-REVIEW](CLAUDE-PREVIEW18-REVIEW.md), findings CLA-20261006-01…17. William then asked for the fixes, which are in draft [PR #21](https://github.com/Willzy12h/M365-Buildstandards/pull/21), stacked on PR #19. Code head `4202f02` is green on Windows run 37542836818 (918 Engine + 115 App, 42 layouts / 79 commands, PowerShell 5.1 packaging, fresh 297-file package). The review's "Implementation status" lists each ID: 01–06, 08, 11 and 13–16 are implemented with tests; 07 is a fixture until INT-051; 09 needs owner repository settings; 12 has renders; 10 and 17 are not done.

For a reviewer, the parts to scrutinise are `WorkspaceBackup` (trusted digest, one read-locked stream hashed and extracted, staged adoption with quarantine, no recursive delete), the stored-evidence time rule in `AssessmentEngine`, integrity in every report format, and the Settings transfer view model.

**Next: usability and ease of use.** The safeguards are in place but the transfer screen, the start guide and some labels are harder than they need to be. Use [GPT-USABILITY-CONTINUATION-PROMPT](GPT-USABILITY-CONTINUATION-PROMPT.md): it carries the current state, a review task for PR #21, the merge of PR #21, #20 and #19, the U1–U10 usability backlog with source paths, the checks to run and the hard limits. Merges follow the owner's delegation in [HANDOVER](HANDOVER.md#owner-delegation-of-commits-and-merges--7-october-2026); INT-049–051 work starts only once PR #20 is actually merged. Promotion to `main`, releases, publication and live tenant actions still need William's explicit approval.
