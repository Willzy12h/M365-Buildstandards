# Integration plan

## Entry conditions

- Astra source commit recorded.
- Claude source commit recorded.
- Claude handover completed.
- Applicable source tests executed.
- No secrets or real tenant evidence committed.

## Sequence

| Stage | Scope | Required result |
|---|---|---|
| 1 | Source inventory | Reproducible commits and handovers |
| 2 | Architecture and schemas | Canonical repository, configuration and standard format |
| 3 | Authentication and permissions | Explicit delegated and application modes |
| 4 | Collection and snapshots | Complete outcomes and mandatory pre-change evidence |
| 5 | Comparison | Semantic matching and incomplete-data handling |
| 6 | Planning and dependencies | Selective, reviewable and resolved plan |
| 7 | Deployment | Safe creation, independent results and retry behaviour |
| 8 | Evidence and reporting | Complete before/after outputs |
| 9 | Interface | Clear workflow, tenant identity and risk state |
| 10 | Packaging | Reproducible portable Windows release |
| 11 | Acceptance | Automated, Windows and controlled tenant validation |

## Promotion gate

The integrated implementation remains on `integration` until tests, documentation and agreed acceptance checks pass. Promotion to `main` uses a reviewed pull request.
