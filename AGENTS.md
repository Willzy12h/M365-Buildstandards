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
- Keep Conditional Access creation disabled and unassigned until reviewed.
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
- Make write operations previewable, selective, independently reported and safe to retry.
- Update tests and documentation when behaviour changes.
- Run the applicable automated checks before opening a pull request.
