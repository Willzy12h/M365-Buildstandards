# Release preparation and servicing

Current distribution is an unsigned internal preview with synthetic validation. A green build does not certify live service behaviour, effective protection or production support. Final publication and tenant acceptance remain separately approved.

On 6 October 2026 the user explicitly requested publication of the verified Preview.18 work. The manual publisher (originally a `publish_preview18` input in the build workflow; separated afterwards so a dispatch cannot leave skipped required checks, and gated by the `release` environment) promotes the pinned successful run's original ZIP after checking its exact source, clean .NET 10 package metadata, extracted-package evidence and independently recorded SHA-256. It uploads a draft first, verifies server-side asset digests and the source tag, then publishes an unsigned **prerelease**, with the Claude review prompt and supplementary native UI/catalogue output. It does not merge PRs, rebuild the application, claim A–G complete or authorise live acceptance. This specific publication approval does not establish a general signing, production-support or automatic-release policy. Use the published `RELEASE-RECORD.json` for binary source identity; the publisher/review-document commit is separate.

The package contains `VERSION.json` (exact source commit and source-tree cleanliness at build start, SDK/runtime, default standard and packaged documents), `DEPENDENCIES.json` derived from the published `.deps.json`, including self-contained runtime packs, supplied NuGet licence/notice files in `licenses/`, historical catalogues and their integrity manifest, `SHA256SUMS.txt` and an adjacent ZIP checksum. Supplied runtime notices are included; redistribution obligations need release-owner review. A dirty local build is diagnostic only and must not be promoted as an exact clean-commit artifact. An inventory is not legal approval or a software signature.


## Publishing a release

Since 7 October 2026 one publisher serves every version: `publish-release.yml` (manual, `release` environment) runs `build/Publish-ValidatedRelease.py --version <version>`. It publishes only what a reviewed pin file, `build/releases/<version>.json`, names: the exact source commit and branch, the successful `build.yml` push run, the tested ZIP's SHA-256, the extracted file count, the default standard, release notes (`build/releases/<version>.md`), the recorded approval, the distribution statement and pending gates. Pins are strictly validated (unknown or missing members, malformed identities and paths outside the repository are refused) in CI's `secrets` job, with negative tests in `build/Test-PublishValidatedRelease.py`.

To publish: after the candidate's push run on `integration` or `main` passes, add its pin and notes through a reviewed pull request, using the run ID and the SHA-256 from that run's `portable-verification.json`. Once merged and approved, dispatch **Publish validated release** from `integration` or `main` (other branches are refused) with the version. A version containing `-preview.` publishes as a prerelease; any other version publishes as the latest release. The generated notes always end with the SHA-256 to compare, the source, the standard, the run, and the allow-by-policy route below. `build/releases/1.1.0-preview.18.json` records the published Preview.18 for reference; the publisher will not replace a published release.

## Unsigned internal distribution and application control

The approved channel is unsigned and internal (William, 7 October 2026). Windows therefore shows no verified publisher. Engineers compare the ZIP's SHA-256 with the fingerprint published separately through the organisation's trusted channel, as well as with the release page, before extracting it. Where WDAC, AppLocker, Smart App Control, ASR or SmartScreen blocks the tool, the security owner allows it under the organisation's policy, normally with a file-hash rule for that exact build (a path rule only for a location that standard users cannot write to). The product does not bypass application control, change execution policy or start through another script host to avoid a block. Organisation-managed code signing (for example Azure Trusted Signing) remains the preferred later route and would replace hash rules with a publisher rule.

## Promotion record to complete before distribution

| Field | Required evidence |
|---|---|
| Product/version | Exact version and preview/accepted support status. |
| Source/PRs | Exact reviewed commit, merged contract decisions and authorised integration/main merge sequence. |
| CI | Exact run/check IDs and observed strict build, engine/application tests, native UI, parser and fresh portable results. |
| Artifact | Original complete portable ZIP, byte size and SHA-256 from a trusted CI/approval channel; extraction check. |
| Standard | Release, catalogue digest/manifest identity and per-control capability/access matrix. |
| Acceptance | Independent findings and synthetic journeys; separately approved service/device effectiveness and supported recovery for every advertised live capability. |
| Known limits | Unsupported/manual controls, unrun checks, ambiguous-write rules and outstanding exceptions. |
| Upgrade/rollback | Prior approved complete package, intact evidence backup/handoff, reader compatibility and [continuity procedure](WORKSPACE-CONTINUITY.md). Binary downgrade is not tenant reversal. |
| Trust/distribution | Organisation-managed signing, or explicit approval of controlled unsigned/hash-pinned internal distribution. No application-control bypass. |
| Approval | Named product, release, security/client approvers, date, approved scope/channel, support owner and next review. |

Promote the exact tested artifact. Do not rebuild after approval while reusing the old checksum or test record. Any rebuild or dependency/catalogue change needs new provenance and checks. Keep the preceding approved package and latest evidence securely available. Hashes distributed inside the same untrusted ZIP do not establish publisher trust. Compare against the independently approved release record before launch.

## Servicing

The release owner reviews Microsoft runtime/MSAL/module servicing and vendor EOL dates monthly, with urgent security fixes triaged sooner. The standard owner reviews time-sensitive default values and licences at least quarterly; stale-default warnings are not an automatic catalogue rewrite. Pin build SDK/dependencies, generate the resolved inventory, test on Windows and publish changes through reviewed PRs. Engineers receive an approved complete package; this product does not install modules or self-update automatically.

Signing credentials must remain in an approved organisation-managed facility. No credential extraction or private-key copying is part of this source work. If signing is unavailable, the product owner explicitly chooses the internal unsigned distribution policy and records its constraints. An approved channel, trusted digest reference, support owner and withdrawal procedure are required before publication.

Use [controlled acceptance](CONTROLLED-ACCEPTANCE.md), [the start guide](OPERATOR-START.md) and [the operating model](INTERNAL-OPERATING-MODEL.md) for the recorded new-client, legacy-backfill, repeat-run and second-engineer journeys. Candidate creation alone does not complete either journey.
