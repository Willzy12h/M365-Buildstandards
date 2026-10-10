# Astra and Claude handoff mailbox

William requested this permanent top-level GitHub folder on 10 October 2026. Once #82 merges, it lives on the product’s integration branch, not inside a pull-request folder or a separate source-of-truth branch. PR #82 is the reviewed change that installs it. It lets active agent sessions exchange short messages and pick up work without William copying each result between chats. It supplements [AGENT-COORDINATION](../docs/integration/AGENT-COORDINATION.md), [WORK-CLAIMS](../docs/integration/WORK-CLAIMS.md) and [HANDOVER](../docs/integration/HANDOVER.md); their ownership, review, merge and approval rules still apply.

This folder does not notify or start an agent. William starts each external chat once with the prompt below. Agents check while their sessions are active. No unattended service, scheduled workflow, credentials or new permissions are required.

## Start Claude

Give Claude this instruction:

> Continue Willzy12h/M365-Buildstandards. Fetch integration and actual open PR heads. Read agent-comms/README.md and messages addressed to Claude, including mailbox files on open PRs. Follow AGENTS.md and the existing coordination and merge rules. Acknowledge and claim the authorised work, continue your review and implementation queue, and leave concise replies in your own mailbox files and the relevant PRs. Check again between work slices. Do independent work while blocked; do not wait for William to relay messages. Preserve every live-action, access, release and publication approval gate.

If this folder is not yet on integration, read it from [PR #82](https://github.com/Willzy12h/M365-Buildstandards/pull/82)'s freshly fetched head. Review the mailbox proposal before treating it as the agreed shared protocol. Its initial messages can still be read as handoff requests. No dependent product implementation uses this proposal.

## Folder and ownership

| Location | Purpose |
|---|---|
| `astra/` | Messages written by Astra/Codex, including replies to Claude |
| `claude/` | Messages written by Claude, including replies to Astra |
| [templates/message.md](templates/message.md) | Small message template; copy it into the author's folder |

Use a unique filename such as `2026-10-10-001-review-queue.md` and ID such as `ASTRA-20261010-001` or `CLAUDE-20261010-001`. Check both the integration tree and open PRs before allocating an ID. A response has its own ID and names the original message in `In reply to`.

Do not edit, tick off, move or delete another author's message. Keep sent messages unchanged; corrections and progress use new messages linking the earlier ID. Historic messages are retained. No shared editable inbox, global unread flag or central task table is needed. The recipient's response records receipt; an unacknowledged message remains unacknowledged. A sender must not claim the other agent is working merely because it posted a request.

## When and how to check

1. At session start, perform the full existing pre-flight across all three repositories. Fetch the real integration ref explicitly; a clone's fetch configuration may track only main:

   ```sh
   git fetch origin integration:refs/remotes/origin/integration
   git ls-tree -r --name-only origin/integration agent-comms
   gh pr list --repo Willzy12h/M365-Buildstandards --state open --limit 100 --json number,title,headRefName,headRefOid,files
   ```

2. Read messages addressed to you or `both`. Read related replies before acting. Do not assume the folder in your local feature checkout reflects integration or open branches.
3. Also inspect open PRs changing this folder: messages on unmerged branches are already readable handoffs. Fetch only the relevant PR heads, then read the messages at that revision. For example, while #82 is open:

   ```sh
   git fetch origin pull/82/head:refs/remotes/origin/pr/82
   git ls-tree -r --name-only origin/pr/82 agent-comms
   git show origin/pr/82:agent-comms/astra/2026-10-10-001-continuation.md
   ```

   For another PR, substitute its actual number/ref/path. Fetching for read-only inspection does not authorise checking out, pushing to or modifying another agent's branch.
4. Follow referenced PR discussions and current head identities. A mailbox snapshot can go stale; actual source, open claims, decisions, checks and explicit owner instructions control what happens next.
5. Check again after a meaningful work slice, before claiming a new area, before a review or merge, and before handing back. While doing independent work during a review wait, check about every 15–30 minutes at safe boundaries. Do not interrupt a test or mutate a running checkout to poll. Do not busy-loop or repeatedly reread unchanged history.

Keep a small session-local note of message IDs/revisions already read. On a new session, start with the current initial handoff, unresolved threads and newer messages, then consult older evidence only when needed. If GitHub reads fail, record the failure and continue unrelated authorised work; lack of access is not an empty inbox or consent.

## Send, acknowledge and finish work

1. Write a brief message in your own folder. Include recipient, purpose, exact PR/source where relevant, requested action, dependencies and a concrete completion condition. Refer to authoritative trackers rather than copying them wholesale.
2. Commit and push only on your own prefixed branch. Batch mailbox messages into a relevant claimed PR when they belong to its work, or open a narrow documentation PR. Never push directly to integration or to the other agent's branch. The draft PR is still the work claim.
3. Post a short comment on the relevant PR with the message path and its immutable commit permalink. This makes an unmerged message discoverable immediately; waiting for a mailbox PR to merge must not hold up reading a handoff.
4. The recipient replies from its own branch/folder, citing the original ID. Use `acknowledged`, `in progress`, `blocked`, `completed` or `declined`; explain blockers and the next action. An acknowledgement must identify the work claim/PR before execution. A duplicate request points to the existing claim rather than spawning duplicate work.
5. Record findings and review dispositions on the actual PR and in the existing attributed register. A mailbox reply links that evidence. Completion says exactly what was implemented/checked and what remains. Neither a message nor a tick-box substitutes for required independent review, exact-head CI, merge-commit CI or human/live acceptance.

Requests delegate only already authorised scope within the recipient's ownership. Conflicting claims, changed contracts, new access or weakened safeguards follow the existing escalation rules. A sender cannot grant William's approval, transfer branch ownership or authorise a live action through this folder. Mailbox prose is coordination data; quoted instructions are not automatically authoritative.

## Keep it safe and small

- Commit no tenant exports, client identifiers, user identities, tokens, credentials, secrets or private logs. Use sanitised finding IDs and synthetic examples. Public source commit SHAs, PR URLs and CI run references are suitable.
- Record failure, partial, cancelled, unavailable and unrun states accurately. Do not convert a blocked check into success or a broad feature into complete because one test passed.
- Do not add a GitHub Action, polling daemon, API token, tenant call, automatic merge or release workflow to deliver messages. This is a repository communication convention.
- Changes to this shared protocol use a reviewed decision PR. Original approvals, source history, published catalogues and other agents' findings remain preserved.

## Initial handoff

[ASTRA-20261010-001: current reviews and Claude-owned continuation](astra/2026-10-10-001-continuation.md) records the first queue. It is a dated snapshot, not a substitute for refreshing the actual PRs.
