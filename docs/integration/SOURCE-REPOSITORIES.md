# Source register

Inspected 13–14 September 2026. [PR #1](https://github.com/Willzy12h/M365-Buildstandards/pull/1) targets integration. No open PRs were returned across the three repositories at initial pre-flight; recheck before parallel work.

| Repository / ref | Verified commit | Interpretation |
|---|---|---|
| Claude main | 337c8e685eea0605d205de828ef2babb4ddd3f31 | Selected code baseline; matches previous review |
| Claude claude/github-repo-access-ygieh2 | 21d8fead7ad4299a3e471b583dcf9117d1f7a487 | Only AGENTS.md/CLAUDE.md changed; no product fixes |
| Asta main | 020c69b327deb42f3a37eb5b0c5873ce6f47ebf9 | Behavioural reference; matches previous review |
| Asta claude/github-repo-access-ygieh2 | 84f32c24bf18e207b4e7c0d9d03483cef8341ee2 | Two coordination/documentation commits; no product changes |
| Master main / integration / docs/claude-primary-baseline | 81b18f5240bb8c36e3b090cb599c6decf1f06d61 | Documentation-only. Named baseline branch has no additional committed work or PR |
| Master claude/github-repo-access-ygieh2 | 55820d7a6a373592c8f6d9aff355fc80a44afb31 | Unmerged coordination protocol, work claim and proposed INT-002; preserved separately |

Repositories: [master](https://github.com/Willzy12h/M365-Buildstandards), [Claude](https://github.com/Willzy12h/m365-Tenant-Toolkit-Claude), [Asta](https://github.com/Willzy12h/m365-tenant-console-Asta).

## Integrated preview

The engineer-workflow feature branch and [PR #2](https://github.com/Willzy12h/M365-Buildstandards/pull/2) start from the verified baseline import `3db6c5a3b3426869ac6803c1472db057563818a9`. New setup, safety, account lookup and UI work is authored in the master repository. It does not represent a newer Claude or Asta source upload. Both PRs target integration and remain subject to review.

## Report recovery provenance

Six files existed as untracked local source in the original Claude checkout under src/BDIT.TenantToolkit.Engine/Reports/. They were absent from both inspected remote refs. They are recovered source, **not attributable to a published source commit**. Copied without alteration; these SHA-256 values describe the original bytes (Git may normalise line endings).

| File | SHA-256 at recovery |
|---|---|
| CsvWriter.cs | a4b31e0de6b122e6e82fbe47fe05bb1f6dd2d152c45b4931457121f6e70d5b0f |
| HtmlReports.cs | fd78834caad0837164471e715da009f4ecdda16a9819e7139ce233c8533ff877 |
| MarkdownReports.cs | b0a7ac2089c5243a5ab409682ed2367e4d84d53d73a73ce6ae28103f58559402 |
| ReportExporter.cs | 34e54ab1dddda3dabb4d98ce7224263b7350b5b6a161e09d24615dae18146ed1 |
| TabularReports.cs | 5374b336fb491f04ed01d594fb9722d3f058af04cadaa34853a669b23167840e |
| XlsxWriter.cs | 5ae7b2cb59262a8a460d8cde7b0ed204b16c11e8b8366a626971510c32a7fe5c |

git check-ignore with case-insensitive matching confirms source .gitignore:25 (reports/) excludes src/BDIT.TenantToolkit.Engine/Reports/ReportExporter.cs. This proves the ignore defect, not every historical reason for the incomplete upload. Master uses /reports/ to exclude root output only.

## Import boundaries

Imported tracked application/tests/standard/configuration template/build/packaging and source docs. Preserved master instructions and PR template; source README and instructions are retained as docs/CLAUDE-BASELINE-README.md and docs/CLAUDE-SOURCE-INSTRUCTIONS.md. Excluded machine-specific Willz.lnk. No SDK, output, local configuration, token cache or tenant evidence was imported. Neither source repository nor another contributor's branch was changed.
