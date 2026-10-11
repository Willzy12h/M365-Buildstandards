# Current goal

Set by William on 11 October 2026. This file replaces long per-session prompts: read it, then work the backlog.

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
