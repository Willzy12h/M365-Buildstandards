# Independent review fixes — preview.13

## Follow-up F1–F5 safety review

The confirmed findings against `1d47c814...` are addressed by this PR's follow-up. See [finding-by-finding dispositions, validation and acceptance proposal](SAFETY-REVIEW-2026-09-21.md). This supersedes the earlier generic Autopilot/group-assignment guidance below: **Autopilot group assignment and removal are unsupported**, not a supported containment path. CA activation requires a verified material-policy baseline; assignment presence alone is not application compliance.

Review base: integration `d4e1e9a9bfed620c06103a54a28a0cf0a574a5fb`. Implementation: `astra/review-fixes`, [PR #9](https://github.com/Willzy12h/M365-Buildstandards/pull/9). No tenant operations are authorised by this work.

## Engineer guidance

Load standard **2026.09.11** with preview.13 or later. Open **Prerequisites and manual steps** on Build Standard, Plan or Policy automation. Each entry states the required action, whether the toolkit assists, and links to Microsoft documentation. It is guidance, not a claim that a tenant check passed.

- **LAPS:** use the existing reviewed Entra enablement action, then re-verify. Confirm supported joined devices, local administrator account choice, password backup and retrieval permissions before assigning the device policy.
- **Autopilot:** the toolkit creates an unassigned deployment profile; it does not register devices or prove licensing, network access or OEM/hardware-hash registration. Review automatic MDM enrolment separately using its existing workflow.
- **Windows Hello:** PIN-reset service/client consent remains a manual administrator step. Check device/TPM readiness and pilot provisioning; the policy alone does not complete rollout.
- **Activation and assignment:** confirm client-specific values and update any candidate whose stored settings differ. For resources whose related settings cannot be updated safely, use supported reviewed recovery to remove the unassigned candidate and create a fresh one; otherwise complete a separately reviewed manual change. Then create a fresh preview with exact targets/exclusions and approve it. Built-in All users/All devices are available only for supported Intune policy/application resources. Enrolment and app-protection policies require explicit groups. Autopilot generic group assignment and removal are blocked pending a dedicated reviewed workflow.
- **Directory prerequisites:** group and named-location creation is supported; updates, membership management and deletion remain manual. A named-location change can affect an already enabled CA policy, so the generic inactive-candidate update path must never change it.

Microsoft sources checked during implementation: [Autopilot requirements](https://learn.microsoft.com/en-us/autopilot/requirements), [Entra LAPS](https://learn.microsoft.com/en-us/windows-server/identity/laps/laps-scenarios-azure-active-directory), [PIN reset](https://learn.microsoft.com/en-us/windows/security/identity-protection/hello-for-business/pin-reset), [All users target](https://learn.microsoft.com/en-us/graph/api/resources/intune-shared-alllicensedusersassignmenttarget?view=graph-rest-1.0), [All devices target](https://learn.microsoft.com/en-us/graph/api/resources/intune-shared-alldevicesassignmenttarget?view=graph-rest-1.0). Live Graph/device acceptance remains open.

## Disposition

| Finding | Implemented resolution | Regression coverage |
|---|---|---|
| Reused prerequisite IDs could exempt a managed population | .11 reserves PRE-009/010 for exclusions, restores .8 meanings for PRE-004/005/008, and checks recorded exclusion purpose before injecting an ID | ExclusionGroupCoverageTests, ReviewFixSafetyTests |
| Existing groups/named locations could be PATCHed by generic deployment | Creation-only route, planner and executor gates | ReviewFixSafetyTests |
| Older unchanged catalogues no longer matched evidence digests | Narrow old-model projection for read-only verification of known .3–.8 releases; no write approval relaxation or evidence rewrite | HistoricalDigestTests |
| Group/location material settings were ignored | Compare security properties, CIDRs and trust; unrelated groups are not equivalent by shape; populated-group intent still needs review | AssessmentReviewRegressionTests |
| Administrator membership used numeric operators on arrays | Explicit array-count operators; record direct membership, not permanent/PIM effectiveness | AssessmentReviewRegressionTests |
| Failed manual-control reads could look assessable | Unknown/failed reads stay UnableToAssess before deviation handling | AssessmentReviewRegressionTests |
| Defaults were inconsistent and could reach assignment unconfirmed | Share effective defaults with assessment; never claim compliant from an assumption; require saved, matching candidate values for activation/assignment | AssessmentReviewRegressionTests and confirmed-input regressions |
| Form refresh/save and array round trips lost data | Refresh on catalogue/profile changes, preserve unknown saved keys, render/parse arrays by declared type | PolicyInputParserTests and offline UI harness |
| Advertised assignment targets were unsupported | Exact reviewed built-in targets on supported routes; group-only guidance elsewhere; CA-008 accurately describes its MAM group scope | ReviewFixSafetyTests and offline UI harness |
| TAP checked default rather than maximum lifetime | .11 checks the maximum, retaining one-hour limit | .11 catalogue and assessment tests |
| Report branding/claims and silent missing fixtures | Neutral client report, proposed-scope wording, no raw structured defaults; missing fixtures fail tests | BuildStandardDocumentTests, WindowsHelloRecipeTests |

## Upgrade and evidence

Published .3–.10 catalogue bytes remain unchanged. Schema 4 protects .11's new count/prerequisite semantics from older binaries; this build also reads schema 3. Plans must be regenerated after upgrading.

Do not move mappings between control IDs by editing evidence. .9/.10 used prerequisite IDs for different purposes; their existing objects require explicit historical review. The new release does not adopt them by name, change group membership, delete objects or rewrite historical evidence. A same-name conflict remains a review item. Recovery for directory groups/named locations is not implemented and is not implied by snapshots.

The legacy digest projection accepts only the original recorded digest for unchanged known releases and only for accepted-write read-only verification. Unknown writes and tampered standards remain blocked. Legacy plans preserve an absent `usesDefaultInputs` property when re-serialised.

## Validation and next handoff

See [testing evidence](TESTING-EVIDENCE.md) and the PR checks for exact results. Local build/tests, offline WPF rendering, packaging and live tenant acceptance are separate evidence. Independent review should concentrate on ownership-purpose checks, creation-only routes, legacy digest compatibility, default confirmation and built-in assignment scope.

Next product work requires separately authorised disposable-tenant acceptance of setup, capture, one disabled CA and unassigned Intune candidate, reviewed assignment and recovery. Source approval does not authorise it. Confirm Autopilot/LAPS/Hello prerequisites with representative devices before expanding automation.
