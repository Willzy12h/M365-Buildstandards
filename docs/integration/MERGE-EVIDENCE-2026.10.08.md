# Reviewed integration merges — 8 October 2026

Astra tracker reconciliation branched from integration `865e1a1e14d4d857d83f99d184cae2b67cb31e66` after fresh fetch and pre-flight across all three repositories. Only Astra-owned claims, decisions and current continuation records will change. Claude's #46 script work and #47 findings register remain under Claude's ownership; their historical findings will be referenced, not rewritten.

## Scope

Record actual exact-head and merge-commit Windows run identities for #48–#51 and #42/#44/#43/#45. Reconcile Astra's INT-070–074 decision state and current continuation against merged source; retain all historical publication identities and pending human/live gates. No source, catalogue bytes, permissions, release version or publisher change.

## Validation

GitHub PR states and actual checks will be read before recording each result. Windows/native results are CI evidence read by Astra, not locally executed Windows checks or live Microsoft acceptance. #45 merge-commit CI was running at claim time and subsequently passed. #46's conflicts and attribution were corrected by Claude; it merged after final exact-head checks, with merge Windows run `37832476624` now passed; #53 readiness source is independently reviewable on its own branch.

## Fix merges into Astra branches

The exact fix heads were reviewed before merge. Each resulting branch merge had both push and PR Windows runs read as successful; no other-agent branch was pushed.

| Fix PR → parent | Branch merge SHA | Passing push / PR runs |
|---|---|---|
| #51 → #42 | `7b5f0f00434175cc1ed3042214618bdb64a7552c` | 37826204559 / 37826213729 |
| #49 → #44 | `27ff140125432b3445d99cfa10db4a8cd6746efb` | 37826214058 / 37826223104 |
| #48 → #43 | `3d020d6822f59b8c8ecb595d1adcff40131a66e3` | 37826224254 / 37826232007 |
| #50 → #45 | `f0eac0b8389a5266d8a40291609dac8e4c95c841` | 37826233795 / 37826241334 |

## Integration merges in the requested order

All actual build/standard/secrets checks passed on each exact head before a merge commit; all corresponding integration push runs passed afterwards. Append-only metadata conflicts retained both sides. #44 included #42, #43 included #44, and #45 included #43 plus the new release-readiness documents.

| PR | Exact reviewed head | Passing head push / PR runs | Integration merge SHA | Passing merge run |
|---|---|---|---|---|
| #42 | `dd878c97632cfdf396fbd27c3e871b7ae042c97a` | 37826338058 / 37826344849 | `1c8a0d6c3bd4cdbde07da36fb9186eb65bc180da` | 37826999034 |
| #44 | `0efce56dcd95e727f74cc2a15729f0d6ac935e5b` | 37827438308 / 37827444433 | `bd7bcc7064f78ef23b0a9cf7899c7c02e39b82bc` | 37828021628 |
| #43 | `ed9045417becbcd08454e1a89e7e894dc4513f51` | 37828204017 / 37828210100 | `994c767f3d01b4b1bd87d88a9452dd50631b384d` | 37828940200 |
| #45 | `0384d1af1bcfec638c7c9d054df260e870d56bd0` | 37830266874 / 37830272919 | `865e1a1e14d4d857d83f99d184cae2b67cb31e66` | 37831138250 |

The #45 integration run's actual check annotations record 1,220 engine/CLI and 143 Windows app tests, all executed/passed with zero failures/skips; 45 page/size renders, 85 commands, zero binding issues and tenant calls; fresh 302-file extraction, matching stage/checksums, actual startup/shutdown/context-menu Copy and portable CLI help/launcher/inventory/jobs/reports/exit/output checks without desktop/auth initialisation. Its original ZIP SHA-256 is `0a970f9f3a68b5bda00afb4703e04320c3f87e19441e4c48adc3df36f6f09b0c`. This identifies that source/package only: source is still unpublished `1.1.0-preview.19`, standard `2026.09.30`. It is not proof for later changed bytes or a publication pin. Current renders were generated/structurally checked in CI, not visually inspected here. The manual interactive Windows console test remains open.

## Continuation against the 7 October path

- Independent #21–#33 post-merge review and confirmed safeguards were implemented in #36, with exact/merge checks. Claude's independent #42–#45 review yielded 19 findings and fix PRs now integrated. Human/live gates remain open.
- Reusable publisher: already merged in Claude's #34 (`c04a7ba`, INT-066); no duplicate publisher is needed. New candidates need their own reviewed pins.
- Astra claims #19/#20/#36–#45 are merged. Claude owns #46 and #47; Astra owns the reviewable readiness source #53 and this reconciliation #54. Claude was asked to update only its own stale rows in #47.
- Remaining source: R07 module/role/access polish, R10 current guidance, runtime experimental/manual gating, Reports/navigation/authoring exposure, extended mailbox/entitlement reporting and controlled script/report execution. #53 addresses a confirmed R07 read-status gap. No broad feedback item is marked finished while required human/live gates remain.
- Next preview preparation is authorised by the current goal; final 1.1.0 version/promotion/publication requires William. Owners/evidence/deferral policy, signing/distribution, repository settings and servicing ownership remain business decisions.

## Claude library #46 — independently re-reviewed

Astra reviewed original `3d993c9`, then code/doc fixes `27c40b0`/`d9a24aa`, and finally `b9aceaed6222a7697a39d6015363b79c4105d19b` after both integration merges. AST-20261008-05–09 are closed at source/synthetic level; read limits/unknowns/nullable flags/exact account and denied/ambiguous permissions now retain truthful results. Attribution correctly separates Astra's source inspection/CI reading from Claude's local fail-first PowerShell 7 runs. Final Windows push/PR runs **37831467460 / 37831474759** passed (1,270 engine/CLI + 156 App, 48 layouts/88 commands, no bindings/tenant calls, PowerShell 5.1 synthetic Copy checks, fresh desktop/CLI package checks). Original exact-head ZIP hash: `2bf063c5e8b891c21cde34dee4d87a480b61e0cf142e4810655478c96ac9fd80`.

#46 merged as `b755dbb3b8e1b958c12825ec296fee4e319e9ae4`; its merge Windows run `37832476624` passed after separate readback. The actual merge annotations record 1,270 engine/CLI and 156 app tests, 48 page/size renders, 88 commands, zero binding issues/tenant calls and fresh 302-file desktop/CLI startup checks. Its distinct original ZIP SHA-256 is `c02fd686160fb66c91090f1e2d7b8b6b003b81a344756ed5eb10517ed2bc3ef8`. No Run button or Exchange mutation route was added. Entitlement, archive reporting and broader report/runner scope remain unimplemented; Microsoft behaviour is live-unverified. Claude's #46 claim/INT-080 row remains its owner's row and needs reconciliation by Claude, not Astra.


## Claude findings record #47 — actual merged state

Claude's final record head `2dc3ca533d768cff0ba04229c0a3fd85919fc06c` passed push/PR runs 37832182434 / 37832189410. It merged as integration `14e62275ca212b6f34ecd3b17d676f2445e39845`; actual merge Windows run **37833184420**, build check **113503487129**, passed. Annotations record 1,270 engine/CLI and 156 App tests, 48 layouts/88 commands, zero binding issues/tenant calls, PowerShell 5.1 synthetic Copy and fresh 302-file desktop/CLI startup checks. Original ZIP SHA-256: `ed63e94754666c657075248febef69f7df5028defb2ec97a3323c394c7e9dc18`. This is an unpublished Preview.19 CI artifact, not a release or proof of later source. Claude owns the record and its feedback rows.

## Exact-source pending review evidence

All rows below are open source/decision PRs. Their push and PR checks were read successful; no review has been supplied or merge performed. Tests are synthetic/offline, not live Microsoft acceptance.

| PR | Exact source | Passing push / PR Windows runs | Engine / App tests | Native layouts / commands | Original push ZIP SHA-256 |
|---|---|---|---|---|---|
| #53 | `293f1ea2bf8ca611eb671116250da0874f86d605` | 37837717580 / 37837724041 | 1298 / 156 | 48 / 88 | `0f9047d58d91a1443bcb34371232130bf696ed269359bc79e29511708638c1cb` |
| #56 | `e74d44386e0bc3f00ed973e26ce7124d81134035` | 37838495956 / 37838503127 | 1270 / 157 | 48 / 89 | `5362e97a955a5bd61b4bae2cdde10f68844f8a5e9b62bf288b28f309710653af` |
| #57 | `5a9a27181c78023076b3e4094e41eae991b4fd5b` | 37838335710 / 37838341849 | 1292 / 156 | 48 / 88 | `97b1501810a1bc1e0efa7ce21b74b27b488be1883651c9244bb2df878210f463` |
| #58 | `7adff06384e7835f089293b2636d83cc67c18585` | 37839811425 / 37839818933 | 1299 / 156 | 48 / 88 | `c8f97eea2fa5245c831a6405998ef2186f23ee1ee57ea753dc2469394452d71b` |
| #59 | `96632ae3df1a4bdebb12d64037646953f9932eda` | 37840902811 / 37840910436 | 1277 / 156 | 48 / 88 | `dffe6b5597c6c0b45752b056749a13c34b968e76b3a1f08f50a99e73312e626b` |
| #60 | `60e19e12a4c902abb8f7a1e0c1c4d9f127b2756b` | 37842433877 / 37842441949 | 1273 / 156 | 48 / 88 | `e104bf83c7ca70e81a9c8f2db1c40036a9e6b8a8b3b2aefe1cb4d46911a2d11c` |

Each passed fresh 302-file extraction/startup/context-menu Copy/portable CLI and blank settings/evidence checks. #59 also executed all 17 PowerShell 5.1 account/tenant synthetic refusal/positive checks. #55's decision-only head `6534c4972332a8d2a63fa577abde22b27d4a94ce` passed 37833394549 / 37833401753; its runtime gate is not implemented.

Astra downloaded and visually inspected real WPF Configuration/Scripts renders from #56's exact e74 source at all three sizes. Initial clipping failures and their corrections are retained on the PR. Human readability/Narrator/physical-scaling acceptance remains unrun. These original ZIP hashes must not be reused for a new candidate or changed source.

#60 first failed the strict xUnit2031 analyser at `d0a85a0`; the filtering overload preserves the same assertion, and the exact 60e19e1 runs above passed. #61 head `98e3c2207cc9407eb34fc0fffcfa80e67bc84219` has mixed outcomes: push 37843036623 passed, PR 37843042207 failed the existing ExecutorTests outer WaitAsync deadline (1,269 passed, one failed). #63 proposes isolated scheduling with all deadlines/assertions unchanged. Do not merge #61 while any exact required check is red. #62 exact head `acb30b9a0899e6fad20950d3ff3aa4d4f8def135` passed push/PR Windows 37843953170 / 37843960978: 1,292 engine/CLI + 156 App tests, 48 layouts/88 commands, zero bindings/tenant calls and fresh 302-file checks including actual registered-report Partial export and wrong-tenant refusal. Original push ZIP SHA-256: `bf72638db29dfc1e0f601f6bf27dd9cc5c40859a7359f83504ee6e68ebbc3c31`. #63 checks remain pending. One diagnostic rerun of #61's failed job was requested after reading/isolating the outer timeout; source is unchanged and no assertion is relaxed. The original failure remains historical evidence, and a successful retry would not prove the timing problem fixed.
