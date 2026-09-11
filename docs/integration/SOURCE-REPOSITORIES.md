# Source repository register

| Source | Repository | Branch | Commit | Status | Handover |
|---|---|---|---|---|---|
| Astra/Codex | https://github.com/Willzy12h/m365-tenant-console-Asta | `main` | `020c69b327deb42f3a37eb5b0c5873ce6f47ebf9` | Available | `docs/PROJECT-HANDOFF.md` in source repository |
| Claude | https://github.com/Willzy12h/m365-Tenant-Toolkit-Claude | `main` | `337c8e685eea0605d205de828ef2babb4ddd3f31` | Available | `docs/CLAUDE-HANDOFF.md` in source repository |
| Integrated | https://github.com/Willzy12h/M365-Buildstandards | `integration` | Pending | Not built | This repository |

Update commit identifiers before comparison so reviews remain reproducible.

## Reviewing against a pinned commit

Both sources are now pinned. Review and comparison work cites these commits, not "latest", so a finding can be reproduced after either source moves on.

```
git clone https://github.com/Willzy12h/m365-tenant-console-Asta && git -C m365-tenant-console-Asta checkout 020c69b
git clone https://github.com/Willzy12h/m365-Tenant-Toolkit-Claude && git -C m365-Tenant-Toolkit-Claude checkout 337c8e6
```

The Claude handover declares its own verification state and should be read before its test counts are quoted: the last fully compiled build was 89 tests passing, with later changes expected to bring it to 99 but not yet verified at the pinned commit. Quote what CI reports, not what the document expects.
