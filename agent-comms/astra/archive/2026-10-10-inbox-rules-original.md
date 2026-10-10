# Initial inbox rules — archived snapshot

Historical context; do not load this archive during routine inbox checks. Current actions/status are in [issue #83](https://github.com/Willzy12h/M365-Buildstandards/issues/83). This copy does not establish receipt, approval or completion.

- Original: https://github.com/Willzy12h/M365-Buildstandards/issues/83
- Original updated at: 2026-10-10T21:11:40Z
- Original body SHA-256: `cc0c0fb60184fc60a372fa3b101a8abd12c3142ace0230fb72a0b0868dcd46c4`
- Captured: 2026-10-10T21:19:14Z

<!-- Original body begins; exact UTF-8 body follows. -->
# Shared Astra and Claude inbox

William requested a central GitHub location for agent instructions, messages and handoffs on 10 October 2026. This issue belongs to the repository and is accessible from any branch. It is the working inbox; no per-message PR is needed. Neither agent's feature branch is the shared source of truth.

## Start and check

Read this issue and new comments at session start, between meaningful work slices and before claiming work/review/merge. Use `gh issue view 83 --repo Willzy12h/M365-Buildstandards --comments`. Check at safe boundaries about every 15–30 minutes during an active waiting session; no busy-loop or interruption of tests. An inactive external chat must still be started once by William. Posting is not proof of delivery, receipt or work underway.

## Send and reply

Prefix each comment with **From: Astra / Claude**, **To: Astra / Claude / both**, a unique message ID, and **In reply to** when applicable. Cite exact source/PR/check identities; include the requested action, current claim, dependencies and a clear completion condition. The recipient acknowledges explicitly, records its own claim/PR before implementation, and replies with in-progress/blocked/completed/declined and truthful validation. Corrections and updates use new comments; do not edit or delete the other agent's messages. Do not treat a sender's unchecked task as the recipient's acknowledgement.

Keep messages short, linking to existing evidence rather than copying the whole history. Use session-local last-read comment IDs to read new/unresolved threads. Findings and exact review dispositions still belong on the actual PR/shared register; link them here. The proposed permanent `agent-comms/` folder in PR #82 holds reusable rules/templates and optional durable summaries once reviewed and merged into integration. Working messages do not depend on that merge.

## Authority and safety

AGENTS.md, AGENT-COORDINATION, HANDOVER, actual source/claims and explicit William instructions remain authoritative. This issue does not transfer branch ownership, replace required independent review or CI, authorise a merge/publication, or grant live access. Use only already authorised scope. No tenant authentication, consent, live Graph/Exchange/DNS calls, endpoint changes, main promotion, version/tag/release/publication or repository-setting changes are authorised here. Exchange changes remain Copy/Save only. Never post private tenant/client identifiers, credentials, tokens, secret values or private exports.

The first Astra comment carries the current review/implementation queue. Refresh actual integration and PR heads before acting. Source, synthetic/native checks, human acceptance and live Microsoft behaviour remain distinct.
