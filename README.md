# Microsoft 365 Build Standards and Tenant Toolkit

This repository is the master integration and continuing-development repository for the Microsoft 365 tenant build standard, assessment tooling and controlled deployment workflow.

## Source implementations

| Implementation | Repository | Purpose |
|---|---|---|
| Astra/Codex | [m365-tenant-console-Asta](https://github.com/Willzy12h/m365-tenant-console-Asta) | Preserved Astra implementation and current tested baseline |
| Claude | [m365-Tenant-Toolkit-Claude](https://github.com/Willzy12h/m365-Tenant-Toolkit-Claude) | Preserved Claude implementation and handover |
| Master | [M365-Buildstandards](https://github.com/Willzy12h/M365-Buildstandards) | Compared, integrated and collaboratively maintained implementation |

The source repositories remain intact as evidence of each implementation. Selected components are integrated here after review rather than copied without comparison.

## Status

Repository initialisation is in progress. Application source will be added after the Astra and Claude implementations have been compared and the canonical structure has been recorded.

## Branches

- `main` — approved integrated baseline and releases.
- `integration` — active shared integration branch.
- `astra/<feature>` — Astra/Codex contributions.
- `claude/<feature>` — Claude contributions.
- `fix/<issue>` — isolated corrections.
- `docs/<change>` — documentation-only work.

See [Integration workflow](docs/integration/README.md).
