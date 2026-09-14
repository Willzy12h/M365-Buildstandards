# Codex work claim

## Active phase — engineer workflow

- Branch: `astra/engineer-workflow`, based on baseline PR #1 commit `3db6c5a3b3426869ac6803c1472db057563818a9`.
- PR: https://github.com/Willzy12h/M365-Buildstandards/pull/2, target integration. Baseline dependency is explicit while #1 remains unmerged.
- Scope: user-authorised executor/evidence/authentication fixes, delegated enterprise-app setup, account/exclusion lookup, one-time connections, WPF design, selective policy recovery and licence overview/user-scope checks.
- Published preview parent: `d5c132dc649159d71c50f67394f8a5a136771f8f`. Further recovery/licensing work stays on this feature branch; no merge or production release is implied.
- Pre-flight found PR #1 only; source branches and Claude's coordination work remain preserved.
- No live tenant operations. See TESTING-EVIDENCE.md and HANDOVER.md.

## Previous phase — preserved baseline claim

- Agent: Astra/Codex
- Branch: `astra/claude-baseline-reporting`
- Base: `integration` at `81b18f5240bb8c36e3b090cb599c6decf1f06d61`
- Scope: evidence-based baseline review, selective Claude application import, recovery of missing reporting source, Windows build/tests and portable packaging.
- Expected files: integration documentation, root README/ignore rules, Claude application/source/tests/build/packaging and its documentation.
- Authority: user explicitly selected Claude as primary and authorised this work. No live tenant actions are authorised.
- Existing work: Claude's `claude/github-repo-access-ygieh2` coordination proposal remains untouched. No open pull requests were returned for the three repositories at pre-flight.
- Pull request: https://github.com/Willzy12h/M365-Buildstandards/pull/1
- Status: implementation, local checks and GitHub CI complete; review pending. See TESTING-EVIDENCE.md and HANDOVER.md. Do not merge or treat this build as live tenant acceptance without the outstanding checks.
