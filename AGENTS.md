# Master repository instructions

## Purpose

Build and maintain the integrated Microsoft 365 tenant build standard, assessment console and controlled deployment tooling.

## Authoritative sources

- Astra/Codex source: `Willzy12h/m365-tenant-console-Asta`
- Claude source: `Willzy12h/m365-Tenant-Toolkit-Claude`
- Integrated source: this repository

Do not overwrite either source repository while performing integration.

## Integration rules

- Compare behaviour, schemas, tests and security controls before selecting an implementation.
- Record material choices in `docs/integration/DECISION-LOG.md`.
- Update `docs/integration/COMPARISON-MATRIX.md` with evidence.
- Keep `main` releasable. Perform active integration on `integration`.
- Astra/Codex and Claude must use separate feature branches for concurrent work: `claude/…` and `astra/…` respectively. Neither agent writes to the other's branch.
- Merge changes through reviewed pull requests.
- Follow `docs/integration/AGENT-COORDINATION.md` before starting any work. It covers the pre-flight check, how work is claimed, how conflicts are resolved and when to stop and ask. It applies to all three repositories.
- Claim work by opening a draft pull request as your first act, and record it in `docs/integration/WORK-CLAIMS.md`.
- Do not describe untested Microsoft 365 behaviour as verified.

## Product requirements

- Separate read-only assessment, comparison, planning and deployment.
- Capture a complete pre-change snapshot before tenant writes.
- Show tenant identity, primary domain, signed-in identity and permission mode.
- Highlight write-capable access.
- Keep Conditional Access candidates disabled and Intune candidates unassigned. Preserve stored CA targeting/exclusions in the baseline; see INT-004 for preserved baseline targeting and outstanding live rollout acceptance.
- Resolve and display Microsoft Graph object names and IDs.
- Support standard versioning, comparison and backfill.
- Produce structured action results and complete before/after evidence.
- Keep branding and naming configurable.
- Use UK English.

## Security

- Never commit tenant exports, credentials, tokens, secrets, private keys or saved connection data.
- Use least privilege and document Microsoft Graph permission changes.
- Do not expose secrets in logs, tests, reports or examples.
- Use synthetic fixtures for automated tests.

## Engineering

- Maintain Windows portability and avoid mandatory administrator rights.
- Keep tenant collection and mutation logic outside the interface layer.
- Make writes previewable, selective and independently reported. Never automatically retry an ambiguous tenant write; require reconciliation and a fresh reviewed plan.
- Update tests and documentation when behaviour changes.
- Run the applicable automated checks before opening a pull request.

## Current baseline and safety

- Claude's C#/.NET implementation is the primary baseline by current user instruction. Read docs/integration/BASELINE-REVIEW.md and HANDOVER.md before implementation work.
- Open a draft PR early to claim your area; target integration and do not overwrite another contributor's branch. Read existing open PRs before starting.
- A baseline import is not safety acceptance. Fix known evidence/execution/renewal gaps before authorised live deployment testing.
- Test changes affecting safety with a case that would fail if the safeguard were removed. Never weaken a test simply to make a suite pass; explain any correction to a stale test contract.
- Record exact validation: source inspection, build, tests, GUI and live Graph are different evidence.
