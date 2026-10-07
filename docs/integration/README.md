# Integration workflow

## Current product completion proposal

The [6 October multi-perspective review](FINAL-PRODUCT-REVIEW-2026.10.06.md) proposes a finished internal engineer product for new builds, legacy backfill and repeat reviews. Read its [completion plan](PRODUCT-COMPLETION-PLAN.md), [shared feedback register](PRODUCT-FEEDBACK-REGISTER.md) and [Claude/Astra handoff](PRODUCT-AGENT-HANDOFF.md). These are recommendations awaiting scope approval; they do not replace the coordination rules or certify live acceptance.

## Current continuation

Claude's [Preview.18 review](CLAUDE-PREVIEW18-REVIEW.md) and its fixes are in draft PR #21, stacked on PR #19. The next agent should start from [GPT-USABILITY-CONTINUATION-PROMPT](GPT-USABILITY-CONTINUATION-PROMPT.md), which focuses on usability and ease of use.

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

The operating protocol for two agents working at once is `AGENT-COORDINATION.md`: pre-flight check, claiming work by draft pull request, branch ownership, feature areas, merge order and conflict resolution. Read it before starting. Current claims are in `WORK-CLAIMS.md`.

Astra/Codex and Claude should not make unrelated direct commits to `integration`. Each creates a feature branch from the same current integration commit and opens a pull request. This keeps changes attributable and allows the other implementation to review the diff.
