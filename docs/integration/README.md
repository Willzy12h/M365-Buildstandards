# Integration workflow

## Repositories

| Name | Role | Editing rule |
|---|---|---|
| `m365-tenant-console-Asta` | Preserved Astra/Codex source | Maintain independently; do not rewrite during integration |
| `m365-Tenant-Toolkit-Claude` | Preserved Claude source | Claude uploads and documents its complete version |
| `M365-Buildstandards` | Master integrated product | Both LLMs contribute through isolated branches and pull requests |

## Master repository branches

| Branch | Purpose |
|---|---|
| `main` | Approved integrated baseline |
| `integration` | Active shared integration target |
| `astra/<feature>` | Astra/Codex contribution based on `integration` |
| `claude/<feature>` | Claude contribution based on `integration` |
| `fix/<issue>` | Isolated correction |
| `docs/<change>` | Documentation-only work |

## Initial build

1. Complete the Claude source repository and handover.
2. Record the exact source commits in `SOURCE-REPOSITORIES.md`.
3. Compare both implementations in `COMPARISON-MATRIX.md`.
4. Agree the canonical application and build-standard schemas.
5. Record material choices in `DECISION-LOG.md`.
6. Build the selected implementation on `integration`.
7. Run automated, Windows and controlled tenant tests.
8. Promote `integration` to `main` through a reviewed pull request.

## Concurrent work

Astra/Codex and Claude should not make unrelated direct commits to `integration`. Each creates a feature branch from the same current integration commit and opens a pull request. This keeps changes attributable and allows the other implementation to review the diff.
