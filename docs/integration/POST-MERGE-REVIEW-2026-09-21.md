# Post-merge review of PR #9 — 21 September 2026

Reviewed merge: `1021a238b54e5b758525b4d623c4edaa72ea4483` on integration (PR #9, `astra/review-fixes`, safety findings F1–F5).
This was a review and test pass, not live acceptance. No tenant authentication or Graph, Exchange, Intune, application,
consent or device operation was performed, read-only or otherwise. Only synthetic fixtures were used; no client evidence,
token or saved connection data was read. Neither preserved source repository was changed.

## What the merge delivers, as verified

F1–F5 were each traced from the finding to the code and to a test that fails without it.

| Finding | Where it now lives | What holds it |
| --- | --- | --- |
| F1 — material Conditional Access additions accepted at activation | `Core/Safety/ConditionalAccessMaterialState.cs`; `ReviewedChangeService.AssertVerifiedCaBaseline`, gated at both preview and execute | Extra targeting, application exclusions, device filters, session controls and unknown fields each block activation; a triple digest binds approved payload, verified observation and current state |
| F2 — enrolment and Autopilot assignment contracts | `ReviewedChangeSafety.AssignmentType` | Autopilot generic group assignment and removal, including empty removal, are rejected before transport |
| F3 — beta recovery re-verification | `WriteVerificationService`, now passing `definition.ApiVersion` | Beta EDR restoration verifies through beta reads; v1.0 EDR is still rejected |
| F4 — assignment records treated as deployment proof | `Engine/Assessment/ApplicationDeploymentAssessment.cs` | Uninstall, available-only, wrong or group targets, mixed intents, filters and missing scope cannot establish compliance |
| F5 — array caveats compared as equality | Count operators on `partialClients` and `exemptsMany` in 2026.09.11 | Tests load the shipped catalogue and cover one/two clients and five/six platforms |

The read-only digest relaxation (`StandardDigestCompatibility.MatchesForReadOnlyVerification`) is confined to two
read-only call sites, and `GraphRouteAllowList` still blocks child-path writes on creation-only paths. Neither widens a
write surface.

## Findings from this review, and what was done

| # | Severity | Finding | Disposition |
| --- | --- | --- | --- |
| 1 | Medium | The headless runner reimplemented record loading. It invented a minimal profile when the installation held none, passed no deviations, and returned an empty mapping set where `EvidenceStore` refuses a tenant mismatch. All three change assessment *status*, not only wording: `AssessmentEngine` selects a toolkit-managed candidate from the mappings and falls back to equivalence when there is none, and profile parameters resolve a standard's template values. | **Fixed.** The runner now reads profiles, mappings and deviations through `EvidenceStore` and refuses a snapshot whose tenant has no client record here. Two conformance tests added. |
| 2 | Low | `ApplicationDeploymentAssessment`'s positive branch is unreachable with the shipped catalogue: all eight APP-WIN controls declare an exclusion prerequisite, and the exclusion check returns before the target inspection. Correct behaviour, but it reads as live logic. | **Documented,** in [automation coverage](../AUTOMATION-COVERAGE.md). Code unchanged. |
| 3 | Low | `build/Build-Portable.ps1` copied the whole of `docs/` into the client package, including preserved source history, agent coordination, review briefs and decision logs. | **Fixed.** The package stages a named list of ten operator documents and fails the build if one is missing, so a rename cannot silently drop a document and a new internal document cannot silently ship. |
| 4 | Low | Operator casing in 2026.09.11 is inconsistent: ID-003 uses `Equals`, `CountAtLeast`, `CountAtMost`, and ID-002 mixes `AtMost` with `atLeast`. Everything else is camelCase. | **Deferred deliberately.** See below. |
| 5 | Informational | `bdit` is built by the solution but not staged into the portable package. | **Settled: it does not ship.** See below. |

The NUL byte previously reported in `EquivalenceEvaluator.cs` is confirmed absent at this merge.

## Deliberate deferrals

**Operator casing stays as published.** `ToolkitJson` registers `JsonStringEnumConverter`, which matches enum names
case-insensitively and throws on a name it does not know. Both spellings therefore load identically and an invented
operator still fails loudly, so the inconsistency is cosmetic. Against that, `standards/2026.09.11.json` is published
evidence: its bytes are digested, plans and accepted runs reference those digests, and rewriting it to tidy five strings
would invalidate historical verification for no behavioural gain. The next catalogue release should use camelCase
throughout — the five occurrences to correct when authoring it are ID-002 `signals[4]` (`AtMost`) and ID-003
`signals[0..2]` plus `caveats[0]` (`Equals`, `CountAtLeast`, `CountAtMost`, `CountAtLeast`). Do not edit .11 in place.

**`bdit` is a build and CI tool, not a package component.** The portable ZIP ships one executable, and its release note
asks the client to allow that one path or hash in application control. A second executable widens that exception, and
everything `bdit` does — assess a stored snapshot, write the client document — the application already does with a
window. Revisit this only when W4 gives it a job an engineer cannot do from the desktop.

## Limits of this pass

- The authoring sandbox cannot run .NET; every executed-test result cited here comes from CI on Windows, not from a
  local run. Nothing was validated against a tenant, and none of this is tenant acceptance.
- The review read code, catalogue data and CI output. It did not inspect client evidence, and it makes no claim about
  Graph behaviour beyond what the synthetic transport tests assert.
- F1–F5 were verified as implemented and tested. That is not the same as verifying that the safety properties they
  protect are complete.
