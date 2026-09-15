# Working with two agents

This is the operating protocol for Claude and Astra/Codex working the same product at the same time. It applies to all three repositories, not just this one.

It exists because the two agents cannot see each other. Neither holds the other's context, neither is notified when the other pushes, and each works from whatever it read when it started. Everything below is a substitute for that missing awareness. Follow it literally; a rule that looks like ceremony is usually the one preventing silent loss of work.

## The three repositories

| Repository | Role | Who writes here |
|---|---|---|
| `m365-tenant-console-Asta` | Preserved Astra/Codex source | Astra/Codex maintains it. Claude reads it. |
| `m365-Tenant-Toolkit-Claude` | Preserved Claude source | Claude maintains it. Astra/Codex reads it. |
| `M365-Buildstandards` | Integrated product | Both, through branches and pull requests. |

Neither source repository is rewritten to match the other. They are the provenance record. The final product is built here, on `integration`, and promoted to `main` by reviewed pull request.

Reading the other agent's repository is encouraged. Writing to it is not: propose the change in an issue there, or raise it in the integration pull request, and let its owner make it.

## Identity and branches

Each agent owns a branch prefix and writes only to its own.

| Agent | Prefix | Example |
|---|---|---|
| Claude | `claude/` | `claude/deployment-retry-semantics` |
| Astra/Codex | `astra/` | `astra/graph-collection-batching` |
| Either, by agreement | `fix/`, `docs/` | `docs/permission-matrix` |

- Branch from the current tip of `integration` (in this repository) or `main` (in a source repository). Say in the pull request which commit you branched from.
- Never commit directly to `main` or `integration`.
- Never push to, rebase, amend or force-push a branch carrying the other agent's prefix. If that branch needs a change, ask for it in its pull request.
- One change per branch. A branch that grows a second unrelated change should be split.

## Before you start: the pre-flight check

Do all four. It takes under a minute and is the only thing standing between two agents and the same file.

1. `git fetch origin` and confirm you are branching from the current tip, not from what you cloned an hour ago.
2. List open pull requests across all three repositories, and read the changed files of any that are open.
3. Read `WORK-CLAIMS.md` in this directory.
4. If anything open touches the files or feature area you need — stop. Say so, name the pull request, and either wait for it to merge or pick different work. Producing a conflicting change and discovering it at merge time is the failure this protocol exists to prevent.

## Claiming work

Open a **draft pull request as your first act**, before the work is finished — as soon as you have one commit, even an empty scaffold commit with the plan in the description.

The draft pull request is the claim. It is the only signal the other agent reliably sees, because it is visible immediately and needs no merge. A claim held only in your own context, or in an unpushed commit, is not a claim.

The title states the feature area. The description states which files you expect to touch. Mark it ready for review when it is done.

`WORK-CLAIMS.md` is the durable record of who did what and why, for the human and for later. Add your row when you open the draft. It is not the live signal — the draft pull request is.

## Feature areas

Work is split by area, because two agents in one area will collide however carefully they behave. These are the areas from the integration plan:

authentication · collection and snapshots · comparison · planning · deployment · evidence and reporting · interface · packaging · build standard data

One agent holds an area at a time. Hold it until your pull request merges, then release it. The human assigns areas; if you have not been assigned one, claim the narrowest area your task needs and say in the draft that you have done so.

Cross-cutting changes — a schema, a shared contract, a rename that touches every area — are not claimable this way. Raise them first as a decision in `DECISION-LOG.md` with a pull request of their own, get it merged, and let the other agent pull it in before either of you builds on it.

## Merging

- First pull request to be ready merges first. The second brings the new base in (`git merge origin/integration`) and resolves conflicts on its own branch, never by pushing to the first.
- Resolve conflicts by reading both sides and deciding which behaviour is correct. Never resolve by taking your own side wholesale because it is yours. If both sides changed the same logic and either choice loses behaviour, stop and ask the human.
- A merge conflict in a safety-relevant file — anything enforcing an invariant — is resolved by keeping the stricter behaviour, then testing that it still holds.
- Re-run the applicable checks after resolving. A merge is a change; it is not verified because both sides were.

## Reviewing each other

The other agent's pull request is the natural place to catch what its author could not see. Review it: read the diff, check it against the invariants, say what you think is wrong and why.

Review comments are proposals, not instructions. The branch owner decides and pushes. If a review claims an invariant was weakened, that claim gets answered explicitly before the pull request merges, either by fixing it or by showing why it is not so.

Do not approve or merge the other agent's pull request. A human does that.

## Recording decisions

Anything material — a schema choice, an implementation selected over the other, a permission change, a rejected approach — goes in `DECISION-LOG.md` with its reason and the pull request that carried it. `COMPARISON-MATRIX.md` carries the evidence behind the choice.

A decision recorded there is settled. If you disagree with one, raise it as a new decision row with the argument, or as an issue. Do not quietly reverse it in a later pull request; that is how a product loses its reasons and reacquires a bug it already fixed once.

## When to stop and ask the human

Stop rather than guess when:

- Both agents have open work touching the same files and neither can proceed without conflicting.
- A conflict cannot be resolved without losing behaviour from one side.
- A safety invariant appears to be in the way of the task.
- The other agent's pull request contradicts a decision in `DECISION-LOG.md`.
- You are about to do anything on the other agent's branch or repository.

## Never

- Push to `main` or `integration` directly.
- Push to, force-push, rebase or amend the other agent's branch.
- Rewrite the other agent's source repository to match yours.
- Merge or approve the other agent's pull request.
- Change a test to make a suite pass, on either side of a merge.
- Claim something was verified that you did not run. "Tests pass" means you ran them and watched them pass.
- Commit tenant evidence, client identifiers, tokens or secrets — this does not relax because a merge was awkward.
