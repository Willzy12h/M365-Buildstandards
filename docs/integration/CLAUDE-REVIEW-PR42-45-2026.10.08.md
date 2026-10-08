# Claude independent review of PRs #42–#45 — 8 October 2026

Requested by William on 8 October 2026. This is a model code review of the ready heads, not an approval on William's behalf and not human or live acceptance. Astra owns fixes and merges under delegation; a changed shared contract goes through DECISION-LOG first.

## What was reviewed

| PR | Head reviewed | Diff reviewed | Live checks on that head |
|---|---|---|---|
| #42 scoped checks | `6a511cf07f1f615fdeee0138147d2fcc9c7a53ce` | `692d2d5...6a511cf` | push 37717009564 and PR 37717013690: build, standard, secrets all success |
| #43 naming audit | `f72bba0432b340173d7b0063ed6aaf619479a638` | `692d2d5...f72bba0` | push 37718011272 and PR 37718015982: all success |
| #44 separate reports | `bcd84880c2d80e14f27ea8f27e1dbe9e7e341a7b` | `6a511cf...bcd8488` (stacked on #42) | push 37721968857 and PR 37721972418: all success |
| #45 portable CLI | `37d9d5672af9f4e7bd7c210ea182e49342ff5a40` | `692d2d5...37d9d56` | push 37722895536 and PR 37722898783: all success |

Base: `integration` at `692d2d5` (merge of #41). The heads matched the checkpoint SHAs when fetched.

**Evidence levels.** Source review and Linux engine tests (`dotnet test tests/BDIT.TenantToolkit.Tests`, .NET 10.0.401): #42 1,117 passed, #43 1,098, #44 1,142, #45 1,079, none failed. The App tests, the WPF harness and native Windows behaviour were reviewed from source and from the green Windows CI runs; they were not rerun here. No tenant sign-in, Graph, Exchange or DNS call was made. Two public Microsoft Learn pages were read for #43 and #44.

**Regressions.** The failing tests written for confirmed findings are kept, uncompiled, in [review-evidence/CLA-20261008](review-evidence/CLA-20261008/). Each file drops into `tests/BDIT.TenantToolkit.Tests` at the matching head.

No P0 or P1 defects were found. The severities below follow the register's product priorities.

## #42 scoped checks

**CLA-20261008-01 — P2, confirmed defect (failing test).** Scoped results drop the release-lineage review warning that the full assessment shows.
- `ScopedCheckService.cs:62` calls `AssessmentEngine.AssessSelected` directly. Only `AssessmentContext.Assess` (`AssessmentContext.cs:21`) runs `LineageReview.Annotate`, and that is the path the desktop `RunAssessment` (`Workspace.cs:592`) and `bdit report` (`Program.cs:216`) take.
- Scenario: a toolkit mapping recorded under an earlier release (the INT-051 reused-ID case). The full assessment says "Release lineage: Review needed…", but **Check this requirement** and `bdit check` show the same control without it.
- Regression: `Scoped_check_keeps_release_lineage_warning` fails with ``Expected start: "Release lineage:"``.
- Fix: apply the same lineage annotation to the selected findings, for example an `AssessmentContext` overload that takes a selection, and add a parity test.

**CLA-20261008-02 — P2, confirmed by source trace.** The stored-evidence check ignores separately imported Exchange/Purview evidence.
- `Workspace.cs:567` passes only `Snapshot`; `RunAssessment` also passes `ExchangeSnapshot?.ExchangeCapture`. `AssessmentViewModel.CanCheck` requires `Snapshot`, so an Exchange-only workspace cannot check. The CLI `check` (`Program.cs:162–184`) has no `--exchange-snapshot`, although `report` does (`Program.cs:210`).
- Scenario: after the normal Exchange import, **Check this area** on Exchange or Purview reports every EX/PUR control as UnableToAssess, while the full assessment assesses them. This is conservative, not unsafe, but it breaks the INT-070 parity.
- Fix: pass the separate Exchange capture through `ReviewHistorical` as `separateExchange`, record its ID in the wrapper, allow an Exchange-only source, and add `--exchange-snapshot` to `check`, with parity tests.

**CLA-20261008-03 — P2, mechanism confirmed; real-tenant likelihood is a hypothesis.** Live reads are discarded when the result exceeds the 8 MiB wrapper cap.
- `ScopedCheckSchema.cs:14` caps wrappers at 8 MiB. `ScopedCheckStore.Save` (`ScopedCheckStore.cs:16`) runs the strict reader before writing, and `Workspace.RunScopedCheckAsync` keeps the result only after `Save` succeeds. Ordinary full snapshots have no such cap.
- Regression: 9,000 CA items of about 1 KB each, then `ReviewHistorical` and `Save`, fail with "The scoped evidence exceeds the 8 MiB reader limit." A large Intune area check (settings catalogue children and assignments) may reach this.
- Fix: derive the cap from realistic full-capture sizes, or show the unsaved result clearly labelled "not saved". Add a test either way.

**CLA-20261008-04 — P3, confirmed defect (failing test).** A historical review of a source with no recorded digest reports the derived assessment as `Intact`.
- `ScopedCheckService.cs:61` computes a fresh digest on the derived capture. `AssessSelected` then sets `SnapshotIntegrity` from it, and the engine's "Evidence integrity not recorded" limitation is lost. Only the inserted free text mentions NotRecorded.
- Regression: `Assert.Equal(NotRecorded, record.Assessment.SnapshotIntegrity)` fails with ``Actual: "intact"``. This contradicts INT-076's "A new digest does not authenticate an old source".
- Fix: carry the source integrity state into `assessment.SnapshotIntegrity`, or add a `sourceIntegrity` field, and keep the NotRecorded limitation.

Minor notes for #42, with no action required:
- Scoped captures do not read users, groups or roles, so names display as IDs.
- The derived capture keeps unrelated `BetaCollections` metadata.
- Older scoped wrappers become unreadable once the client-scope digest changes. This may be intended, but it is undocumented.
- A snapshot file containing only `null` now gets the wrapper refusal message.

**Verified correct for #42:**
- **Selection and dependencies:** `ForArea` and `ForControl` work for every area and control in all 11 shipped catalogues, schema 3–5. Degrading every collection outside a control's declared dependencies never changes its full-assessor status, including UPD-001 and ID-004 at schema ≥5. That run used synthetic, mostly empty data. `AssessSelected` equals `Assess` per control over complete evidence.
- **Reads:** only the selected collections and their own details are read.
- **Pinning:** the selection pins release, digests, controls, keys and definitions, and is re-checked before any read.
- **Incomplete evidence:** missing or failed dependencies give UnableToAssess. `Complete` is always false for a scoped capture.
- **Strict reader and store:** strict parsing and atomic, no-overwrite storage. Ordinary readers refuse scoped wrappers.
- **No replacement:** scoped evidence never replaces the snapshot, assessment, plan or acknowledgement. The capture time is preserved and a modified source is refused.
- **CLI:** `bdit check` is offline, stdout-only and stores nothing.

## #43 naming audit

**CLA-20261008-05 — P2, confirmed defect (failing tests).** The published `apps` collection has no naming rule.
- `NamingConvention.cs:27` keys the APP rule on `applications`, which is the beta collection. Every published catalogue defines `apps` (`/deviceAppManagement/mobileApps`, v1.0). APP-IOS-001..003 in 2026.09.12 and .30 use `apps`, and releases .3–.5 have no `applications` collection at all.
- Effects:
  - `CheckAuthored` returns "Unable to check" for `APP - Company portal - iOS`, and `RequireAuthored` throws for every APP-IOS control.
  - For a capture under .3–.5, the audit falsely reports "No capture for this object type".
- Regressions: `R1_apps_collection_controls_can_be_authored` and `R1b_apps_only_capture_is_audited`.
- Fix: add an `apps` rule, then handle the overlap as in -06.

**CLA-20261008-06 — P2, confirmed defect (failing test).** Overlapping collections report a mapped object as both managed and unmapped.
- `NamingAudit.cs:53/61` requires `mapping.Collection == key`. Several collections read the same Graph path:
  - `configuration` and `endpointProtection`
  - `compliance` and `extendedCompliance`
  - `apps` and `applications`
  - `appProtection` and the iOS and Android protection collections
- Regression: object `6666…` is listed as "Toolkit-managed" under `endpointProtection` and as "No toolkit mapping for this object" under `configuration`.
- Fix: do not report Unmapped for an ID that any mapping in an overlapping collection holds; report OwnershipUnknown with "mapped under <collection>", or dedupe by Graph path. Managed still needs the exact collection and object.

**CLA-20261008-07 — P3, confirmed by source trace.** `endpointProtection` cites the settings-catalogue resource.
- `NamingConvention.cs:23` cites `deviceManagementConfigurationPolicy`, but every catalogue reads that collection from `/deviceManagement/deviceConfigurations`.
- The limit is null either way, so only the evidence link and the doc table row are wrong.

**CLA-20261008-08 — P3, confirmed defect (failing test).** Decomposed accents fail the internal character rule.
- `NamingConvention.cs:42` tests single UTF-16 units, so `GRP - Café team` in NFD form, and supplementary-plane letters, are reported Non-conforming.
- This is internal policy, not a Microsoft limit, but the doc says letters are allowed.
- Fix: normalise to NFC, or iterate by rune and allow non-spacing marks.

**Verified correct for #43:**
- **Name limits:** the group `displayName` 256 limit matches Microsoft Learn (read 8 October 2026). The conditional access limit is honestly null, and other families stay null with an "unverified" limitation. UTF-16 counting is conservative.
- **Management needs proof, not a name:**
  - A name never establishes management.
  - Managed requires an intact capture, exactly one mapping, a verified run, `Create`/`Accepted`/`Pass`, matching payload and readback digests and IDs, `IsSubset(readback, lastApplied)` in the correct order, and a write before the capture.
  - Cross-tenant input and a Modified capture are refused.
- **Distinct states:** missing, error, partial and empty reads stay distinct.
- **Read-only:** the audit makes no writes and has no rename path.

## #44 separate reports

**CLA-20261008-09 — P2, confirmed defect (failing tests).** One malformed or duplicate identity discards the whole report instead of marking it partial.
- The service checks only for `null` and uses the lenient `ProfileValidator.IsGuid`, while `ReportEvidenceSchema.Validate` (run by `Seal`, `GraphReportService.cs:52`) also rejects empty or whitespace IDs, duplicate subscription IDs, `Guid.Empty` and empty required text. Specific mismatches:
  - Subscriptions are not deduplicated (`:74,79`).
  - Log rows keep a blank `rawId` (`:149–153`, against `ReportEvidenceSchema.cs:106`).
  - `GuidText` (`:192`) accepts the zero GUID, which `GuidValue` (`:150`) rejects.
  - `ProductsComplete` (`:183–186`) uses a `null` check, but `Validate` (`:124–127`) uses `IsNullOrEmpty`.
- Regressions R1–R4:
  - R1: duplicate SKU ID.
  - R2: a sign-in ID of `""` and an audit ID of `" "`.
  - R3: an empty `servicePlanName` on plan efb87545-963c-4e0d-99df-69c6916d9eb0.
  - R4: the zero user GUID.

  Each throws `ConfigurationException` after the full read, and no evidence is kept.
- Fix: share one identity rule and one required-text rule between the service and `Validate`, dedupe subscriptions, and treat a blank `rawId` as missing.

**CLA-20261008-10 — P2, confirmed by source trace.** Large log or device sets fail with zero rows instead of a partial 5,000.
- `CollectSingle` reads everything with `GetAllAsync` (`GraphReportService.cs:136`) and only then applies `Take(MaximumRows)` (`:140`).
- `GraphClient.GetAllAsync` throws above `MaxItems` = 50,000 (`GraphClient.cs:25,102–109`), so a busy 31-day sign-in range becomes `Failed` with no rows.
- A five-minute timeout inside `GetAllAsync` also keeps nothing, yet the message says "retained rows are partial" (`:166`).
- Fix: add a bounded read that stops at the row cap and records a truncation note, and correct the cancellation message when no rows were kept.

**CLA-20261008-11 — P2, hypothesis from Microsoft Learn; not tested live.** The log query options are not listed as supported.
- `LogPath` sends `ge … and … lt …` (`:187–188`) plus `$select` (`:132–133`).
- Microsoft Learn (read 8 October 2026) lists `eq`/`ge`/`le` for signIn `createdDateTime` and directoryAudit `activityDateTime`, and lists no `$select` for either list endpoint.
- If Graph rejects them, both log reports always return `Failed`.
- Fix: use `le` with the end one tick earlier (the code already flags out-of-range rows locally) and drop `$select`, or prove both in live acceptance.

**CLA-20261008-12 — P3, confirmed defect (failing test).** A trailing newline duplicates a user.
- The `^…$` GUID regex and `Guid.TryParseExact("D")` both accept `"<guid>\n"`, and deduplication groups raw strings (`:86–95`, `:106`). R5 returns 2 rows where 1 is expected.
- Fix: normalise with `Guid.ParseExact(…).ToString("D")`, or use `\z`.

**CLA-20261008-13 — P3, hypothesis (hardening).** `ConnectedTenant.ForReports()` (`TenantConnectionService.cs:28`) returns the unrestricted client when `Graph` is not a `GraphClient`.
- No production path does this today, but a future decorator would silently bypass the report-only route restriction.
- Fix: throw instead.

Operational note for live acceptance, not a defect: per-user `licenseDetails` reads run sequentially inside the five-minute budget, so tenants above roughly 1–2k users will usually end `Cancelled`. That status is reported truthfully.

**Verified correct for #44:**
- **Licence joins:** SKU and service plan come from per-user `licenseDetails` and are joined by exact ID only, never by name. `skuId` and `servicePlanId` stay separate. No quota or entitlement is inferred from licences.
- **Read outcomes:** missing or duplicate users produce a partial row and no query. An empty read is `Collected`, a failed or 403 read is `Failed`, cancellation is `Cancelled`, and the worst section bounds the overall status.
- **Strict evidence:** the reader is strict, keeps required nulls, and stores immutable, separate records with tenant and ID checks.
- **Read-only routes:** no write scope, with a read-only route allow-list that is re-checked on `nextLink`.
- **Permissions:** `Directory.Read.All` is accepted only where Microsoft documents it. `AuditLog.Read.All` is proposed and ungranted: when it is missing the report returns `NotAttempted` and never asks for consent.
- **Export safety:** HTML is fully encoded under a strict CSP. CSV neutralises cells starting with `= + - @`, tab, CR or LF. XLSX uses inline strings only, with no formulas. JSON uses the strict serialiser.

## #45 portable CLI

**CLA-20261008-14 — P2, confirmed coverage gap (Windows behaviour is a hypothesis).** The console-attach branch is never exercised.
- Every CI run redirects both stdout and stderr (`Test-Portable.ps1:76–121`), so `AttachConsole`/`Restore` in `PortableCliHost.cs:21–27` never runs.
- Untested paths include the interactive `bdit.cmd --help`, `| findstr` with stderr on the console, and `2> err.txt`. A regression in that branch (for example removing `AttachConsole`) would pass CI and could leave the user with exit 3 and no visible output.
- Fix: put the handle selection behind a small interface and unit-test all four handle combinations in App.Tests. Add one native case with stdout piped and stderr NULL under a console parent. Record an interactive-console run as manual Windows evidence until then.

**CLA-20261008-15 — P3, confirmed by source trace.** A missing stderr refuses the whole command.
- With stdout redirected, stderr NULL and no parent console, `PortableCliHost.cs:22–25` throws and exits 3 silently.
- `bdit.cmd` always has a console parent, so the impact is low.
- Fix: require stdout only, and point stderr at stdout or at `TextWriter.Null`.

**CLA-20261008-16 — P3, confirmed.** The package assertions are weaker than their messages.
- `Test-Portable.ps1:114` accepts `INCOMPLETE|Incomplete` without the synthetic tenant name.
- `:121` only counts `assessment-*.html` files.
- `:118/:120` do not assert an empty stderr.
- Fix: require the tenant name and ID in both HTML files, require the printed path to match the file, and assert an empty stderr.

**CLA-20261008-17 — P3, hardening.** The `createdump.exe` exemption (`Test-Portable.ps1:71`) matches that file name anywhere in the tree.
- Fix: allow exactly `app\createdump.exe`.

**CLA-20261008-18 — P3, confirmed.** The documents contradict the PR.
- `DECISION-LOG.md:334` still heads INT-074 "(proposed; not settled)", and `MODULE-REPORT-SCRIPT-CONTRACTS-2026.10.08.md:121` says "(proposed)". INT-079 (`:364`) calls INT-074 merged.
- The `Build-Portable.ps1:9–10` header still says bdit is excluded from the package.

**CLA-20261008-19 — P3, confirmed by reasoning from the MSBuild condition; a standalone publish was not run.** A standalone self-contained CLI publish silently produces a library.
- `Cli.csproj:8` makes `dotnet publish src/BDIT.TenantToolkit.Cli -r win-x64 --self-contained` build a library with no entry point. Nothing in the repo does this today.
- Fix (optional): add an `<Error>` for that case.

Low-confidence note, not raised as a defect: the UTF-8 console writer may garble non-ASCII text on an OEM code page console. This needs a Windows check, and may match the existing `dotnet bdit.dll` behaviour.

**Verified correct for #45:**
- **Early dispatch:** dispatch happens before `base.OnStartup`, paths, settings, logger and `Workspace`. Nothing runs earlier, and `startup.log` absence is a real guard (`Test-Portable.ps1:122`).
- **Exit codes:** they propagate through `Shutdown`, and CI asserts 0, 2 and 64 directly and through `bdit.cmd`.
- **Publish output, reproduced on Linux:**
  - The CLI builds as a library inside the self-contained publish, with no `bdit.exe` or `bdit.runtimeconfig.json`, so there is no alternate host.
  - `createdump.exe` is the only other `.exe`.
  - Removing the condition reproduces NETSDK1067.
- **Developer path:** `dotnet bdit.dll` still has its entry point and returns 0 and 64.
- **Process handling:** inherited handles are captured before the console attach. There is no `AllocConsole`, no second process and no sync-over-async in the CLI.
- **`bdit.cmd`:** quotes its paths, keeps the caller's directory, forwards `%*` and returns the exit code.

## Recommendations and merge order

1. **#42 first:** fix -01 and -02 before merging, because they break the desktop/CLI/full parity that INT-070 promises. Decide -03 (cap or "not saved" display) and fix -04 in the same pass, then re-run the exact-head checks.
2. **#44 next, on the merged #42:** fix -09 and -10. Either take -11 now or add it to the live acceptance run sheet as a named gate. -12 and -13 are small and worth taking.
3. **#43 next:** fix -05 and -06 together, because adding `apps` creates the overlap. Merge with the combined base, keep both sides' ledger rows, and re-run checks on the merge result.
4. **#45 last:** -14 is a coverage gap rather than a known failure. Merging with -14 recorded as an open native gate is reasonable if the interactive-console run goes on the manual Windows checklist. Take -18 now. The rest are optional.
5. After each merge: re-fetch `integration`, confirm merge-commit CI, and record the SHAs and run IDs here. Passing standalone checks do not validate combined bytes.

## State at 8 October 2026 (after the fix PRs)

This adds the later state; the reviewed heads, findings and failing-first regressions above remain the record of what was reviewed. At William's request Claude wrote the fixes as single-commit PRs into Astra's branches, and each was merged into the Astra branch it targeted. Merge order followed recommendations 1–4; merge-commit CI below is the integration push run at each merge commit.

| Findings | Fix PR and commit | Merged into Astra's branch | Integrated | Integration push run |
|---|---|---|---|---|
| -01 to -04 (#42) | #51 `b3e29a669d0d7e7852dee339bb52990ec0716333` | `7b5f0f0` | #42 at `1c8a0d6c3bd4cdbde07da36fb9186eb65bc180da` | 37826999034, success |
| -09 to -13 (#44) | #49 `81b3949b29694a2eb757981b3e3ab9982e52fba5` | `27ff140` | #44 at `bd7bcc7064f78ef23b0a9cf7899c7c02e39b82bc` | 37828021628, success |
| -05 to -08 (#43) | #48 `4b01b25e676893bebe24013def28adebf860003e` | `3d020d6` | #43 at `994c767f3d01b4b1bd87d88a9452dd50631b384d` | 37828940200, success |
| -14 to -19 (#45) | #50 `765f4e7bd832131201b328c5eaa4e31e5f2df2a5` | `f0eac0b` | #45 at `865e1a1e14d4d857d83f99d184cae2b67cb31e66` (exact-head `0384d1a`: push 37830266874 and PR 37830272919, success) | 37831138250, success |

Astra confirmed both judgement calls in DECISION-LOG ("Astra confirmation of the two review judgement calls"): -03 keeps the 8 MiB cap and shows an oversized result as NOT SAVED; -06 keeps an object mapped under two overlapping collections as ownership unknown. The gates below are unchanged by these merges.

## Gates that remain open

None of these is closed by this review:
- Human and live acceptance of scoped checks, the naming audit and the reports against a tenant, including the Graph query options in -11 and large-tenant timing.
- Native Windows interactive-console evidence for the portable CLI (-14).
- Reports UI and CLI, naming authoring and the desktop audit, and the Exchange mailbox and quota adapter. These are not claimed by the PRs.
- Verified entitlement classification, milestone candidate records, and the separately approved 1.1.0 promotion and publication.

The source remains unpublished 1.1.0-preview.19. No version bump or release approval has occurred.
