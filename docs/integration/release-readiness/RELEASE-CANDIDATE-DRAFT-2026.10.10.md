# 1.1.0 candidate preparation record — draft, not approved

This draft describes the tested baseline and the remaining source gates. It is **not** a publisher pin, a version bump or an approval to distribute. Populate the final candidate fields only after the required source is independently reviewed, merged and tested at its actual integration commit. Existing [release/servicing rules](../../RELEASE-AND-SERVICING.md) and [controlled acceptance](../../CONTROLLED-ACCEPTANCE.md) apply.

## Verified baseline (not the final candidate)

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
| Promotion draft | [#77](https://github.com/Willzy12h/M365-Buildstandards/pull/77); placeholder, no promotion authorised |

Current branch artifacts retain their own source identities. They cannot replace the integration artifact or certify a later candidate. During this pass the environment refused the Azure artifact-download destination; CI's original artifact, source and extracted-package/hash annotations remain available through the corresponding GitHub run. Local possession or an independent rehash of that download is not claimed.

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
