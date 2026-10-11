# Current goal

## Approved BuildStandard phase — 11 October 2026

William approved implementation of the [BuildStandard plan](superpowers/plans/2026-10-11-buildstandard-wpf-webview2.md), governed by [design specification v0.2](superpowers/specs/2026-10-11-buildstandard-wpf-webview2.md). This finite phase supersedes the older closeout/take-next and permanent desktop-ownership instructions below **only within T0–T10**. The original frozen source remains `4a7425f55385e84462975bcc5bdde5644815ca8f`.

- GPT/Codex is the initial implementer across the application, engine, reader, neutralisation and tests, using executing-plans and isolated worktrees. Claude is an optional later explicitly assigned participant; no work is assigned or acknowledged on his behalf.
- Target `phase/wpf-webview2` through bounded `astra/bs-*` PRs. Preserve main/integration, history, all prior findings and the existing inbox/claims/acknowledgements. Do not push to another agent's branch or take the historical backlog.
- T0 is the decision/validation contract in [#114](https://github.com/Willzy12h/M365-Buildstandards/pull/114); dependent implementation waits for its accepted merge. The approved plan requires owner confirmation of phase merge controls. This is pending; do not infer it from approval to implement.
- Every completed branch needs fresh independent review and actual applicable Windows checks. Existing other-agent gates for execution/recovery/Graph writes/tenant confirmation/app registration/permissions remain. A different named reviewer or a waived gate needs William's explicit decision. No self-review substitutes for independent review.
- Preserve human/live and release/protection gates. No tenant action, new access, main promotion, publication, protection change or test deletion is authorised. Scope ends at the exact-source package/handoff point; human acceptance is separate.

Keep durable task/commit/check evidence in the existing COMPLETION-REGISTER and PR, with compact material updates in issue #83. Local scratch/ledgers are not remotely shared handoffs. Historical/conditional/deferred items remain attributed and are not automatically resumed or closed.

### Preserved previous goal and closeout history

Set by William on 11 October 2026. This file replaces long per-session prompts: read it, then work the backlog.

## Temporary closeout and review pause — 11 October 2026

William has suspended taking the next backlog item. GPT/Astra/Codex coordinates a **bounded closeout** of the finite work already under way, as identified in issue [#83](https://github.com/Willzy12h/M365-Buildstandards/issues/83). This notice takes precedence over the take-next instructions below; it preserves the underlying goal, ownership and safety rules.

Finish and safely integrate that scope under the existing PR, independent-review and merge rules. Do not start unclaimed backlog items, issue #110, speculative refactoring, framework or WebView2 changes. Coordinator status gives no authority to edit Claude's code or branches, waive checks or approve on William's behalf. Directly necessary regressions reopen only their existing item within its ownership and review gates.

- **SOURCE READY:** the agent's finite scope is integrated and actually verified. Report exact heads, merge/check identities, remaining acceptance limits and any excluded work in #83.
- **BLOCKED:** a concrete dependency or gate prevents closeout. Preserve useful work, state the blocker and stop expanding scope; this is not source readiness.
- **REVIEW-SUPPORT-ONLY:** after source readiness, read and answer essential peer reviews. Do not start implementation or take another backlog item.
- **GLOBAL FREEZE:** the coordinator records one full, verified integration SHA in #83 after both agents' closeouts. Acknowledge that SHA and remain strictly read-only. No polling, automatic fixes or assumption that a comment launches another agent.

Implementation resumes only after **William's explicit approval of a specific subsequent phase**. Independent product/UI/architecture/safety review with Superpowers is a later phase, not authorised by this closeout notice. Keep the final frozen SHA in #83, not in a commit that would change it. Live tenant actions, new Graph access, main promotion, publication, settings changes and test deletion retain their separate approval gates.

## Goal

Make the toolkit easy enough that a second engineer can complete a Business Premium client build unaided: connect, assess, plan CA-001 and deploy it disabled. That test is the definition of done for this phase.

Context: clients are mostly Microsoft 365 Business Premium. The tool runs on the engineer's PC. William has tested it on real tenants and it mostly works; the priority now is the interface.

## Backlog

GitHub issues labelled `usability`, lowest number first unless an issue says otherwise. The first ten are #98 to #107. William adds issues from his own tenant testing; take them in the same order.

## Ownership

| Area | Owner |
| --- | --- |
| Desktop interface (`src/BDIT.TenantToolkit.App`) | Claude |
| Engine, standard, scripts and CLI | Astra |

Need a change in the other agent's area? Open an issue for them; do not edit it.

## What you may do without asking

- Create branches and open pull requests to `integration`.
- Merge your own pull request when Windows CI is green, unless it touches the paths below.

## Needs the other agent's review before merge

`Engine/Execution`, `Engine/Recovery`, Graph write paths, tenant confirmation, and app registration or permissions.

## Ask William first

Anything that writes to a tenant, new Microsoft Graph permissions, promoting to `main`, publishing a release, and deleting tests.

## Each pull request

- One backlog item, kept small, and `Closes #<issue>` in the description.
- Before and after screenshots from the CI page renders for interface changes.
- Tests updated; one CHANGELOG line.
- Reviews stay in the pull request. Add a DECISION-LOG entry only when behaviour or security changes. Do not add review registers, prompt files or handover documents.

## When you finish an item

Take the next issue. When the backlog is empty or you are blocked, post one message in issue #83 and stop.

The safety invariants in `docs/integration/ARCHITECTURE-ROADMAP.md` still apply to every change.
