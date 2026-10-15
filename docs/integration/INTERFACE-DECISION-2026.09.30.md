# Preview.15 interface decision

Retain the WPF shell on .NET 10. The selected implementation adds control detail/manual guidance, a persistent next action and shared before/requested/after evidence in deployment and history. Existing authentication, tenant state, approval and execution services remain the authority.

## Representative comparison

[local-html-review.html](../ui/local-html-review.html) is a working, standalone comparison using four fictional controls and a fixed synthetic tenant. It supports inspecting versus ticking, area changes, a reviewed queue, typed confirmation, synthetic outcomes and copying recorded fields. It has no .NET bridge, tenant connection, external resource dependency or service mutation. Its restrictive content policy blocks external access. It is maintainer material and is not packaged with the application.

Chromium automation passed **26 checks**, including selection isolation, filter clearing, manual-only refusal, wrong-tenant refusal, separate acceptance/configuration/functional outcomes, missing after evidence, clipboard feedback, four viewport sizes, catalogue exports and printing. There were zero JavaScript exceptions and zero external requests. This evidence demonstrates browser presentation; it does not validate a browser hosted inside .NET.

| Criterion | Improved WPF | Local HTML comparison and embedded-host implications |
| --- | --- | --- |
| Workflow | Real application selection, review, evidence and command paths use the current engine | Representative sequence works with synthetic in-memory data; no real engine integration |
| Usability | Detail/prerequisites stay beside control selection; shared result panel and persistent next action | Responsive CSS accommodates 940px width; neither option has a completed human usability study |
| Accessibility | Existing native harness exercises accessible names, keyboard reach, contrast, clipping and table widths | Semantic controls and labels, automated interaction and overflow checks; no equivalent full keyboard/screen-reader acceptance |
| Portability | Existing self-contained Windows x64 package; no browser runtime added | An embedded host needs a supported runtime strategy, distribution/update policy and offline provisioning validation |
| Offline operation | Catalogue exports, saved evidence and planning prerequisites remain in existing services | Prototype needs no external requests; a production host still needs tested local navigation and content isolation |
| Authentication | Existing MSAL/WAM and browser fallback retained; live .NET 10 acceptance outstanding | No hosted sign-in or WAM integration tested; cannot claim parity from a Chromium prototype |
| Safety | Engine guards remain independent of UI; regression suites retained | A future host would require a constrained, typed bridge with navigation/origin, message and engine-authorisation tests |
| Maintenance | Shared result presentation removes duplicated interpretation without replacing orchestration | CSS can simplify some layout work, but adds a host/bridge and a second application test surface |
| Migration | Bounded additions to the current implementation | Full workflow/state/accessibility migration is unimplemented; demonstrated presentation benefit does not justify it for this release |

The user's default preference is improved WPF unless the alternative demonstrates a worthwhile benefit. The comparison supports retaining that default. No embedded host is claimed complete, and no future shell migration is prohibited by this choice.

## Source references and limits

The supplied device handoff was integrity-checked separately: rc.15 runtime, Preview 23 supporting components and snapshot `5d4f1b6b5da8ad1e23162ba48e22a1bc0417b975`. Earlier rc.7/rc.9 and rc.1 source were available as historical context. The available Engineer Console source is rc.1; the mentioned rc.5 and latest laptop state were not supplied. No Console operational workflow was run.

Useful patterns adapted are clear stages, row detail distinct from check selection, nearby requirements, selected-action review, honest partial/unknown results, ticket-copy style summaries and reopening saved evidence. M365 retains tenant identity, separate permissions, exclusions and durable approval/evidence requirements. Device runtime code was not imported or modified.

## Reproduce the browser checks

Use catalogue-only exports, never client reports. From **Build Standard → Export engineer standards and manual guide**, export both HTML files and place copies named `BuildStandard.html` and `ManualGuide.html` in a separate input folder. The native UI harness also produces these engine exports. The cloud review generated them directly with `EngineerStandardDocuments` after `StandardsManifest.Verify` and assigned the verified digest before rendering.

With Python, Playwright/Chromium and Poppler's `pdftotext` available:

```text
python build/Review-Browser.py --documents <catalogue-export-folder> --output <separate-review-folder> --chromium <chromium-executable>
```

Omit `--chromium` to use Playwright's installed browser. This is an optional maintainer check, not a runtime dependency. It serves only copies of the supplied catalogue exports and the synthetic prototype on a temporary loopback server, closes that server afterwards, and writes screenshots, PDFs and `verification.json`. No user-facing cloud localhost preview is required.

Tested viewports: 1480×940, 1180×760, 940×660 and 1180×640. These are browser viewport dimensions, not physical DPI acceptance. Native WPF uses its own three-size harness. Chromium generated tagged A4 PDFs with 132 Build Standard pages and 323 Manual Guide pages. All 93 control IDs occur in both PDFs; internal HTML links resolve and catalogue identity is present. Representative screen, introduction and long JSON payload pages were visually inspected for legibility and wrapping. Exact procedures and payload detail are retained despite the document length.

The saved [browser result](../ui/browser-verification.json) records the 26 checks. Actual WPF images come from the Windows `synthetic-ui-review` artifact and must be labelled with their source commit. Never present this HTML prototype as a .NET application screenshot. Human Narrator, physical DPI, real-screen keyboard and live authentication acceptance remain open.
