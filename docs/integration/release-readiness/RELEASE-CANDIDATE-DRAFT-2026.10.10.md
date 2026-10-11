# 1.1.0 candidate preparation record — draft, not approved

This draft describes the tested baseline and the remaining source gates. It is **not** a publisher pin, a version bump or an approval to distribute. Populate the final candidate fields only after the required source is independently reviewed, merged and tested at its actual integration commit. Existing [release/servicing rules](../../RELEASE-AND-SERVICING.md) and [controlled acceptance](../../CONTROLLED-ACCEPTANCE.md) apply.

## Historical verified baseline (not the final candidate)

| Field | Observed value |
|---|---|
| Integration | `5ef5470e1d6d28653261d88f40e854ebe9d09d02` |
| Source version | `1.1.0-preview.19`, unpublished |
| Actual integration CI | [38057154787](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/38057154787), passed; 1,716 engine/CLI and 157 App tests, native/PS5.1/fresh portable checks |
| Original integration ZIP SHA-256 | `cf4a15b9c570007b8f5dc52c8ac3e57675451f0af9221b6329ff154912bf16ca` |
| Default standard | `2026.09.30` |
| Catalogue file SHA-256 | `124a033454385b262960db2a6f61392ee3e64ad8abb1a6f0d8276db29e21302f` |
| Standard manifest file SHA-256 | `1531eaa86f707bb4506983f8a190b0d4af1d5879d3463b3f32a0c27473da68b3` |
| Features / known gaps | [Current completion matrix](../COMPLETION-REGISTER.md), [per-control coverage](../../AUTOMATION-COVERAGE.md), [focused review packet](XHIGH-REVIEW-2026.10.10.md) |
| Earlier promotion placeholder | [#77](https://github.com/Willzy12h/M365-Buildstandards/pull/77), closed/deferred to avoid duplicate builds; reopen/recreate only for the exact candidate |

Current branch artifacts retain their own source identities. They cannot replace the integration artifact or certify a later candidate. During this pass the environment refused the Azure artifact-download destination; CI's original artifact, source and extracted-package/hash annotations remain available through the corresponding GitHub run. Local possession or an independent rehash of that download is not claimed.

## Current preparation — 11 October 2026

Integration remains **724d9e05cfd486c4af8fed733fac8f31ecccbfd3**, unpublished Preview.19, standard 2026.09.30. Actual integration push **38134379280** failed before any step: annotations identify recent account payments/spending limit and direct William to Billing & plans. Last verified integration/package remains 0c963e2/push38094369170, original ZIP SHA-256 `479fc88bc54a9f46741939754aeba8af7472e71a755047b839f0b87cb869ba00`; it certifies only those historical bytes.

#94 head **487585cadc55a599bd5abdc409a19dde819c0dac**, navigation source **35a8ae1b7daf96e5974af28e78e204ecb96aaf9c**, is the current composed review stack. Strict App.Tests/UiReview crossbuilds pass zero warnings/errors; the new Windows cases are unexecuted. Earlier 6d4f1bc full Linux 2,026 pass/30 fixed-host discovery failures and available 1,972 pass remain pinned to unchanged Engine/CLI/scripts. They are not a complete Windows suite or a candidate package. [Current matrix](../COMPLETION-REGISTER.md) and [source/proof](https://github.com/Willzy12h/M365-Buildstandards/blob/487585cadc55a599bd5abdc409a19dde819c0dac/docs/integration/release-readiness/MAILBOX-JOURNEY-VALIDATION-2026.10.11.json) separate source implementation from validation.

### Remaining engineering and gates

| Item | Concrete completion condition | Current blocker |
|---|---|---|
| Application owner outcomes | Review/merge #95; implement required structured outcomes and historical inspection, then the published failing regression and adjacent controls pass | Reviewed shared contract and exact checks |
| Required executable Exchange reports | Review/merge #96, implement Full Access adapter, then register calendar/trace/audit with exact typed evidence and bounded coverage; reuse Run UI/store | Required registration/identity decisions precede dependent source; only inventory currently runnable |
| Library coverage | Existing licence/device/MFA native Reports remain; the library currently contains Exchange entries, not those Graph script seeds. Record and settle the registered non-Exchange path before adding scripts; no new consent or generic executor | Current resource/Copy/Run contracts are Exchange-only; do not silently call the master requirement complete |
| Engineer journeys | Execute 21-page three-size/native command/keyboard cases and new collection-to-capacity paths; repair concrete failures | Windows execution and human newcomer/Narrator/scaling gates |
| Branch integration | Merge independently cleared exact heads sequentially and verify each actual integration push | Actions start restriction; new contracts/source still need independent review |
| Exact candidate | Fill authorised version, source, actual push, original ZIP/hash, extracted doctor/startup/context-menu/CLI results and reviewer fields | No final source/package exists; #77 remains deferred |

No final-source ZIP or independent rehash is claimed. No live/module consent, permission change, deployment, main promotion, tag or publication occurred. William’s current instruction retains all implementation with Astra; reviews and blank approvals remain gates. Other registered adapters and the non-Exchange library path remain authorised source requirements, not silently deferred extras.

## Final candidate fields — fill after source integration

| Field | Value |
|---|---|
| Authorised next-preview version | Pending milestone integration; the goal authorises preparation, not publication |
| Exact source and merged PRs | Pending |
| Actual integration push run and check IDs | Pending |
| Original ZIP location, byte size and SHA-256 | Pending |
| Native/package/CLI verification | Pending on that candidate's changed bytes |
| Independent review disposition | Pending |
| Live-unverified/manual/experimental limitations | Carry all applicable unrun gates and visible runtime restrictions from the capability matrix |
| Upgrade/rollback | Preserve the preceding approved complete package and protected evidence backup; binary rollback is not tenant reversal |
| Distribution | Unsigned internal only; compare an independently published trusted fingerprint. Security-owner hash/publisher/path policy as applicable; no writable-path exception or control bypass |
| Publication pin | Not created; the publisher requires an explicitly approved exact-source record |

## Sanitised acceptance form

Record private evidence locally; return no tenant/user/object IDs, credentials or tokens in chat or committed files. Use the numbered [live run sheet](../../LIVE-ACCEPTANCE-RUNSHEET.md) and preserve its staged approvals.

```text
Candidate version:
Source commit:
Original ZIP SHA-256:
Standard release and digest:
Run-sheet check number:
Expected result:
Observed result (sanitised):
Outcome: Not run / Pass / Fail / Blocked
Evidence reference (local, private):
Remaining limitation or follow-up:
```

The empty fields below are deliberately not human approvals:

```text
Independent code reviewer:
Product/release approver:
Security/distribution approver:
Support and standard owners:
Stage A read-only live approval/date:
Stage B exact-change approval/date:
Applicable pilot/recovery approval/date:
Interactive bdit.cmd acceptance/date:
Newcomer/second-engineer acceptance/date:
Narrator/keyboard/physical-scaling acceptance/date:
Promotion/publication approval/date:
Final 1.1.0 acceptance/date:
```
