# Claude review request — M365 BuildStandard Preview.18

Please independently review the current M365 BuildStandard product and recommend the next improvements. Review the implementation, user journeys, standards, production process and support model. Be sceptical and concrete; this is an internal engineer product intended for repeatable new builds, repeat assessments and safe backfill of legacy tenants.

## Exact review target

- Repository: <https://github.com/Willzy12h/M365-Buildstandards>.
- Published prerelease: `v1.1.0-preview.18`, <https://github.com/Willzy12h/M365-Buildstandards/releases/tag/v1.1.0-preview.18>.
- Released application source: **10808de054a195c73ae742a0ae2f6240a318e421**. Application ZIP SHA-256: **9155fad428fe6c93ae8b72a6695a8ee5622f533348165d00167bcd577f3495cc**.
- Default immutable standard: **2026.09.30**, 93 controls and 61 inert candidate recipes; .NET 10/WPF, self-contained Windows x64.
- Validated source run: <https://github.com/Willzy12h/M365-Buildstandards/actions/runs/37515288607>. Recorded results: 902 Engine + 112 App tests, 42 native page/size combinations, 78 commands, zero bindings/tenant calls, PowerShell 5.1 and dependency-verifier negative checks, fresh extraction/startup/shutdown/right-click Copy. These are synthetic/offline checks, not live or human acceptance.
- Active source claim: [PR #19](https://github.com/Willzy12h/M365-Buildstandards/pull/19), branch `astra/release-2026-09-12`. Later commits may add publisher/review documents; do not attribute their hash or new runtime behaviour to the released ZIP. Record the exact released-source and latest-PR revisions separately.
- Shared-contract decisions: [PR #20](https://github.com/Willzy12h/M365-Buildstandards/pull/20), final reviewed contract head `674b1ba6b638a4cb43da74d3217cfff0aac14bd2`. Check actual merge state. It was open when this prompt was prepared. INT-049–051 producers/consumers are not implemented at the released source; contracts or proposals are not completed features.

Fetch current refs and list open PRs/claims across the integrated repository and preserved Astra/Claude source repositories. Read the PR diff and check annotations rather than relying only on this prompt or counting passing tests. Never overwrite source repositories or another agent's branch. Use the existing checkout and inspect the exact revisions read-only; keep your report on your own `claude/…` branch if repository writes are available.

## Read first

1. `AGENTS.md`, `CLAUDE.md` and `docs/integration/AGENT-COORDINATION.md`.
2. `docs/integration/WORK-CLAIMS.md`, `BASELINE-REVIEW.md`, `HANDOVER.md`, `DECISION-LOG.md` and `COMPLETION-REGISTER.md`.
3. `docs/integration/PRODUCT-AGENT-HANDOFF.md`, `PRODUCT-COMPLETION-PLAN.md`, `PRODUCT-FEEDBACK-REGISTER.md` and `FINAL-PRODUCT-REVIEW-2026.10.06.md`.
4. `docs/OPERATOR-START.md`, `WORKSPACE-CONTINUITY.md`, `INTERNAL-OPERATING-MODEL.md`, `RELEASE-AND-SERVICING.md`, `CONTROLLED-ACCEPTANCE.md`, `APPLICATION-SETUP.md`, `EXCHANGE-PURVIEW.md`, `AUTOMATION-COVERAGE.md` and affected source/tests.

Use repository-root prefixes for files in the same integration directory. Read the newest implementation checkpoint before treating a historical heading as current. The user explicitly excluded `claude/M365-BuildStandard-Project-Context.md`; ignore it. Separate device-tool/launcher archives are references and are not this application.

## What to assess

Give distinct opinions from these perspectives, with concrete consequences and tradeoffs:

- **First-time internal engineer:** discoverability, Quick Connect, assessment versus write-capable tokens, exact app name/ID, actual grants/assignment, WAM versus browser consent, unnecessary authentication, disabled selections, prerequisite ownership, missing/unknown states, copyable details and next steps.
- **Experienced engineer and second-engineer handoff:** progress/resume, fresh capture versus historical evidence, repeat-run behaviour, durable unresolved writes, backup/restore/upgrade, client switching and whether the workflow reduces effort without restoring stale approval.
- **Legacy migration engineer:** equivalence despite different names, stricter/custom settings, partial/incomplete reads, unmanaged ownership boundaries, old/reused PRE IDs, lineage, retention/exceptions, pilot/cutover/effectiveness/retirement and avoidance of duplicate or unsafe changes. Separate current behaviour from pending INT-049–051 implementation.
- **Standards owner:** catalogue authenticity and immutability, all 93 controls, fixed settings versus defaults versus client identities, manual/licence/prerequisite requirements, obsolete defaults, HTML readability/printing, exact JSON/manifest reuse and document-ready Markdown. JSON is definition data; it is not a deployment/evidence import or catalogue installer.
- **Security and tenant administrator:** least privilege, assessment/setup/deployment boundaries, identity/app/profile/session binding, no name-based ownership, exact reviewed previews, fresh durable before/intent/after evidence, disabled CA/unassigned Intune, drift checks, cancellation, ambiguous-write reconciliation, support privacy and credential-cache exclusion. Do not infer Microsoft service behaviour from mock tests.
- **Release/support owner:** original artifact/source/runtime/standard/hash identity, dependency/licence notices, unsigned-distribution limits, supported scope, servicing, upgrade and incident recovery, named ownership and withdrawal/rollback. Binary downgrade is not tenant-change reversal.
- **Maintainer/reviewer:** desktop/CLI parity, strict supplemental evidence parsing, bounded input/storage, failure visibility, interface bindings/layout/keyboard behaviour, useful negative tests, transport consistency, understandable design and durable cross-agent decisions.

Trace high-value concerns through the actual engine boundary rather than only the UI. Prioritise meaningful defects and finish work over speculative rewrites. Check claims in operator guides and release notes against runtime behaviour. Assess usefulness of tests, not just counts.

If you can run checks, use the pinned SDK and repository commands for affected areas. Windows is required for App/native WPF/PowerShell/portable interaction; Linux cross-compilation does not establish native behaviour. Review release-provided screenshots visually when accessible and identify exact image/viewport. If artifact access, a test platform or live acceptance is unavailable, state that limit; do not invent a pass. Use only synthetic data. No live authentication, admin consent, tenant/device operation, module installation or external data upload is authorised by this review.

## Deliverables

1. Lead with your independent verdict: **ready for the stated offline prerelease scope**, **needs corrections**, or **insufficient evidence**, with the three most consequential reasons. Give a separate finished-product/GA verdict; the pending A–G and live/human/business gates prevent silently equating those scopes.
2. Provide a prioritised findings table. Each entry needs a stable `CLA-YYYYMMDD-NN` ID, type (confirmed defect / observed limit / proposal / test hypothesis), priority, exact revision and file/line, user impact, reproducible evidence, recommended change, alternatives/tradeoff, acceptance with a meaningful negative case, scope/package and remaining approval/live/contract dependency. Classify review evidence as source, synthetic, native, human or live. Link duplicates instead of rewriting old findings.
3. Describe the new-client, legacy-backfill, repeat-review and second-engineer handoff journeys. For each, identify confusing steps, missing capabilities and the smallest useful improvement. Keep engineer versus tool versus client/vendor responsibilities explicit.
4. Give a sequenced next-work proposal: release blockers, already approved A–G implementation, low-effort QoL, business decisions and optional out-of-scope expansion. State what an agent can build autonomously under existing approval, what awaits the required contract merge, and what requires separate human/live/publication authority. Do not reopen A–G source approval; do not implement new proposals during this review.
5. Save the full report as `docs/integration/CLAUDE-PREVIEW18-REVIEW.md`. If writing to GitHub is available, claim a narrow **review-documentation-only** draft PR on your own `claude/…` branch targeting `integration`, record your own work-claim row and avoid concurrently editing Astra's claimed files. Give the integrator a paste-ready set of proposed feedback-register entries in your report. The integrator will append them to the shared register; do not delete or overwrite existing IDs/dispositions or tick anything without its evidence. If you cannot write, return the full report in chat and say so.
6. Finish with a short plain-English update for William: your verdict, top recommendations, what is already working, what remains unfinished, and a concise approval summary for genuinely new scope/business choices. Include report/PR links and exact tests you personally executed. Do not merge, publish, change runtime code or perform live tenant actions as part of this review.

Preserve all existing safeguards and historical catalogue/evidence bytes. User approval of A–G authorises bounded source implementation, not automatic compliance, ownership adoption, restored execution permission or live action. Review comments are proposals; the integrator owns implementation and records the independent response and verification in the shared feedback register.
