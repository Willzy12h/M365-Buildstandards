# Astra source handoff — 11 October 2026

Read this delta packet rather than replaying historical prompts. Refresh actual heads and the issue83 inbox before acting. It is a source/validation record, not approval to merge, authenticate or publish.

## Exact baseline and composed source

- Integration: `1e06ccfdb156c759c40cba88589f2a7cc9048a63`, unpublished1.1.0-preview.19, standard2026.09.30. #89 corrected head6c3df85 was independently reviewed (6103265777); both exact Windows runs38094412980/38094416500 passed before merge. Actual integration push38095690739 failed with zero steps in secrets/build/standard; no new package proof exists.
- Last fully verified integration: `0c963e2e649d219a026f62fb8bc5f2a97b77fb3b`, push38094369170. Original CI ZIP SHA256 `479fc88bc54a9f46741939754aeba8af7472e71a755047b839f0b87cb869ba00`. It certifies those older bytes only.
- Combined source: #92 `03a3d930cb6b55540e4db28b7efc1d45d2103126`; validation archive `c14d923bb0ac5a737653b1bf52293a5b0f293e78`. Only operator/diagnostic/claim documentation differs between them; src/tests/build/.github bytes are identical. #92 composes current Astra source parents without changing Claude branches. It is not an integration candidate.

## Claude review queue — delta first

| PR/head | Review required / reusable prior evidence |
|---|---|
| #73 `f6fbad53d6e30b174e9c5526c252a2ca9f96f581` | CLA-101 configured sign-in budget forwarding. Prior guard/broker/preview reviews retained. Root36 related cases passed, five-minute mutant failed12; exact Windows38094747844/38094750687 passed. Confirm this narrow delta; UI design is not a dependency. |
| #78 `9fc317fb69e39a966469e4204e523ffa72a5de33` | Already independently cleared unchanged regressions (6103161871), Windows38094314220/38094317174 passed. No fresh broad review requested. Actual merge check will still be required. |
| #79 `8dfeb0204136098e5bc19b148f3a74536d369750` | CLA-110 lease unavailable wording; whole-history strict refusal unchanged. Actual28 engine checks passed; two held-lease App cases compiled, not executed. Prior CLA-97–99 clearance retained. |
| #80 `0140e93a72930929bcdc2ee52fab927cfda26a7f` | CLA-102 now explains original sign-in timestamp versus silently renewed reads; CLA-103 natural two-second control retained. Current #73/integration composition adds no replacement auth. Earlier review6103164701 and Windows runs are historical, not current-head proof. |
| #81 `2f2487d0bdf8bf5fa7463f02b82c1fe37c49d0fa` | Current #80 composed; source auto-merged and documentation retained. Prior client/evidence review6103150953 remains; verify relevant composition, not a full redo. |
| #84 `ceeeac3d90ead41d3c61407b2748ac244533202d` | CLA-120–122 source labels, displayed-tenant refusal and visible accessible names. 23 pure tests passed; original negatives fail. Current #81 composed. Native Windows execution/screenshots outstanding. |
| #87 `0883a58f776a98c73edc9abf3231fe02515d8fa1` | CLA-124–126 source/section/owner accounting and exact saturated budget Partial.144 delegated/84 root cases passed; guard mutants fail.32MiB cap unchanged. |
| #88 `aa0fe00614f01d0b566df3322967cb01ab4413e2` | CLA-130/131 inventory fidelity/private refusal plus new collapsed Settings diagnostics.102 portable cases passed; three stale/private/cancel mutations fail; App/native compiled. Inspect coordinator ownership/sanitisation and Settings binding; no tenant/host execution. |
| #90 `822525ff7ceb116c0f5efcaac1376ee175813a08` | Current #88 composed, actual early CLI dispatch/cancellation plus mandatory exact-package doctor Ready gate.12 assertion negative controls/two guard removals pass/fail as intended. Original0c2ebdd Windows38094581151/38094583189 success cannot certify this head. Temporary base #88 becomes integration after its merge. |
| #91 `68f5cfdbba3227c3f5b89f0cb9987f2459b4c876` | Preserve all required job names, PR/integration/main/manual checks, pinned actions and publisher. Reduce duplicate feature-push runs; superseded PR cancellation only, unique integration/main/manual groups. Move unchanged catalogue assertions to Linux; executed assertions pass. No protection/settings change. |
| #92 `03a3d930cb6b55540e4db28b7efc1d45d2103126` | New application page/browser/typed horizon/nested tables/full original exports and paired-schema CLI; exact package synthetic export/refusal assertions. Inspect the #79 CompleteConnectionAsync composition: retain #73 authorisation-invalidation subscription and replacement unsubscribe (two new App regressions compiled). AST-25 linked-parent actual-CLI refusal fixed. |
| #76 fresh head | Compact records/own rows and this packet; all34 Claude claim rows retained exactly. Review records delta, not historical bodies. No broad acceptance closure. |

## Implemented behaviour and focused evidence

Reports offers five registered Graph reports plus application expiry and observed consent. Expiry horizon1–365 defaults30; consent has no log dates/horizon. Reads use existing verified authorisation and route-restricted providers, with experimental acknowledgement/context invalidation; page navigation/export cannot sign in or grant access. Application.Read.All metadata-purpose reuse is already acknowledged; no new Directory.Read.All consent. Missing existing delegated-grant access stays NotAttempted.

The schema-aware browser strictly pairs schema/report ID and checks saved filename identity. Tables lacking a read-status column explain the unavailable filter. Filters never reduce original exports. Thirteen delegated/browser pure tests passed; original mixed-history browser fails.37 Windows App cases and native three-size paths are authored/compiled, not executed.

The actual offline CLI exports both schemas through the shared bounded UTF-8/tenant/digest reader. At7f1f066 Program.cs:411/423, valid schema1 evidence under a linked parent exportedexit0; repaired code refusesexit2, no stdout/export and unchanged source.52 delegated actual-process/conformance cases passed; root47 CLI cases passed (the root filter omitted five HeadlessRunnerTests). Preserve AST-20261010-25 attribution in #92. No replay/auth/evidence resealing.

Settings uses the same offline engine/text formatter, workspace exclusivity and an owned cancellable operation. Ready clears before reruns; late Ready after cancellation refuses; private faults never enter output/logs. Run/Cancel/Copy availability explains the next step. No script, installer, repair, policy bypass or tenant call.

Fresh-package source tests now require actual extracted launcher doctor Ready/all mandatory RequiredPass before native startup, and both application families through packaged executable HTML/launcher JSON. Exact status/times/nested metadata/supplied notice and original JSON bytes must survive; tamper/wrong tenant refuse without output/mutation. Two strict fixture readers, four SDK export assertions/four refusals and PS7 parser pass. ASCII [char]0x00B7 comparison assembly fixes a demonstrated PS5.1 UTF8-no-BOM/Windows1252 mismatch. These synthetic/SDK checks do not execute the actual Windows package or PS5.1.

## Final combined offline validation

| Check at c14d923 / byte-identical03a3d93 | Actual outcome |
|---|---|
| Full unmodified Engine/CLI suite | 1,945 passed,30 failed,0 skipped of1,975; all30 are fixed-location PowerShell MemberData discovery failures |
| Sequential platform-available suite | 1,891 passed,0 failed/skipped; explicitly excludes ExchangeReportRunnerTests (54 passes/30 missing-data failures from full run) |
| First filtered run, preserved | 1,890 passed,1 failed because a concurrent App build rewrote the real CLI runtimeconfig. Retest waited for builds; no source/test change |
| Strict Release App.Tests/UiReview crosscompilation | Both passed,0 warnings/errors; neither Windows runtime executed |
| PS7 parsing/standard manifest | Both package scripts parse;11 historical releases/1 lineage verified |
| Registry/publisher/tracked-source guards |30 registered items; existing pin valid;19 publisher safeguard tests passed; evidence/template assertions passed |

[Structured validation summary](COMBINED-VALIDATION-2026.10.11.json) pins result hashes; full logs/TRX remain outside source under the recorded local evidence location.675 archived tracked files match the exact commit after restoring SDK-normalised cmd line endings. No host symlink or discovery/test edit. The full suite remains failed for unsupported local runner cases; the filtered result does not validate them. Actual Windows App/native/PS5.1/extracted package/bdit.cmd checks remain unrun.

## Remaining ownership and gates

Claude: Run UI, additional schema2 read-only Exchange adapters and derived mailbox-capacity/entitlement presentation; preserve the B task-centre/A persistent context when integrating navigation. The redesign does not block source development. #89 source is merged and independent review closed; no further Astra review repair requested there. Reconcile only your own stale claim rows and handle preserved PR-less records/runner branches; Astra has not changed them. Exchange changes remain Copy/Save only.

Astra: respond to new reproducible delta findings, then refresh/merge eligible source sequentially under HANDOVER once independent dispositions and exact Windows checks permit. Current #92 is the composed source for the eventual milestone run, not a waiver of individual merge checks.

External: Actions execution/capacity must become available and actual integration1e06ccf push validation must pass before further merges. We cannot establish the billing cause through accessible APIs; no blind rerun. Next-preview exact-source ZIP/hash, native/PS5.1 package proof, candidate version/pin and exact promotion PR follow reviewed integration; old bytes cannot substitute. No version bump/publication/live action done.

William: separately approved human/live acceptance, interactive Windows bdit.cmd, client colours/named owners/evidence policies, distribution/security and repository settings, main/tags/publication/final1.1.0 approval. Blank approvals stay blank. This handoff exhausts current independent Astra implementation slices, not the product's remaining obligations.
