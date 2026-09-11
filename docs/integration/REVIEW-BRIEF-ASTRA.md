# Review brief: Astra/Codex on the Claude toolkit

For Astra/Codex. This is a scoped review with a defined output, not an open reading of someone else's code.

## Why you are being asked

INT-002 in `DECISION-LOG.md` proposes building the integrated product on the Claude toolkit, with the console you maintain becoming reference and port source rather than a merge input.

That proposal was written by Claude, about Claude's own implementation. It is recorded as **Proposed**, not Accepted, and it stays that way until you have answered it. You are the only reviewer positioned to find what a self-assessment missed, and a review that simply agrees is worth very little here. If the proposal is wrong, this is where that gets established.

Nothing is seeded into `integration` until this review lands.

## What is actually being decided

Not which agent writes better code. The two implementations are different platforms:

| | Astra/Codex console | Claude toolkit |
|---|---|---|
| Commit | `020c69b` | `337c8e6` |
| Stack | Node 22, PowerShell Graph workers | C# / .NET 8 |
| Interface | Local HTTP service, browser | WPF native window (`net8.0-windows`) |
| Tests | 54 passing | 89 verified at last full build; 99 expected, unverified at the pinned commit |
| Standard | `2026.09.1`, `2026.09.2` | `2026.09.3`, schema v3 |

Nothing merges across that boundary. Whichever is chosen, everything kept from the other is **ported by hand**. So the question is: which base costs less to complete, and what must be carried across before the other is archived.

## Start here

`docs/MIGRATION-FROM-TENANT-CONSOLE.md` in the Claude toolkit is the case against your implementation, written in detail. It analyses "Tenant Console 0.3.2-rc.1" — which is your repository, at the commit above. It contains a ten-item parity checklist, concedes one item outright (`Initialize-Applications.ps1`, which the toolkit cannot do at all), and argues the rebuild-versus-merge decision.

Read it as the thing you are answering. Then `docs/REVIEW-REPORT.md` sections 5 and 9 for the decisions already argued through, `AGENTS.md` for the invariants the toolkit is built around, and `docs/CLAUDE-HANDOFF.md`, which declares its own unverified areas.

## What to produce

A pull request into `integration` from a branch under your own prefix (`astra/…`), containing three things.

**1. Your half of `COMPARISON-MATRIX.md`.** Fill the Astra/Codex column for every area, and propose a Selected approach with evidence. Cite files and tests at the pinned commits. An area you cannot assess is marked as such, not guessed.

**2. A direct answer to the parity checklist.** For each of its ten items: is the status honest, and is the reasoning sound? Then — the part only you can supply — **what does the console do that the checklist does not list at all?** An incomplete checklist is the most likely way INT-002 is wrong, because retiring your implementation depends on it being complete.

**3. Every area where the console should win**, with the reason and the evidence. Item 7 is already conceded; that one is settled and needs no argument. What else? Each becomes a per-area carve-out in INT-002, which can be accepted with exceptions rather than wholesale.

## What is most worth your attention

- **Anything validated against a live tenant.** Every Graph recipe in the toolkit is reasoned but unproven — see its `docs/LIVE-VALIDATION.md`, which its own authors call the largest open risk in the product. A payload your console has actually created successfully in a real tenant outranks a carefully designed one that has never run. Say which of yours those are.
- **Controls lost between `2026.09.2` and `2026.09.3`.** Compare control IDs directly. A control that silently disappeared in the port is a real regression and would not show up in any test.
- **Settings-catalogue definition IDs.** The toolkit reports these as undraftable for `CFG-WIN-*` and `SEC-WIN-*` because the GUIDs are opaque. If your implementation has real ones, they are immediately valuable.
- **The safety claims.** Read/write separation before any request, Conditional Access candidates created disabled, Intune objects unassigned, no retry on ambiguous writes. These are argued and unit-tested, not proven in a tenant. Test the reasoning; say where you think it breaks.
- **Client-facing report wording** that has already been in front of a client and worked.

## Ground rules

- Review at the pinned commits. Both are frozen for this purpose.
- Do not push to the Claude source repository. Findings go in your pull request here, or as issues there for its owner to action.
- Follow `AGENT-COORDINATION.md`: open your pull request as a draft first, add your row to `WORK-CLAIMS.md`, and claim the areas you are reviewing.
- Disagreement is the point of this exercise. State it plainly and back it with a file, a test or a tenant result.
- Do not describe untested Microsoft 365 behaviour as verified, on either side. That rule protects your findings as much as anyone's.
