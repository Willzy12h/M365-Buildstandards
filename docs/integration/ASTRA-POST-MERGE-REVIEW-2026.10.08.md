# Astra post-merge workflow review — 8 October 2026

Baseline: integration `8f0285a48bdd719914037b0ab2d9eeb6e7f6cc2b`, Preview.19 source, after PRs #34/35. Fixes: PR #36. This is source and synthetic review, with separately identified Windows evidence. No tenant authentication, consent, Graph/Exchange/DNS query or endpoint operation was performed.

## Confirmed findings

| Finding | Baseline location | Failing regression / correction |
|---|---|---|
| AST-20261008-01: unresolved tenant writes were absent from completion | `src/BDIT.TenantToolkit.Engine/Workflow/JobCompletion.cs:63` | `Accepted_passes_do_not_complete_a_job_with_an_unresolved_tenant_write` failed before correction. Reuse strict evidence guards without changing plan replay ordering; block the job and its completion rows until write history is reconciled |
| AST-20261008-02: divergent semantic identities could create parallel histories for one requirement | `src/BDIT.TenantToolkit.Engine/Workflow/JobWorkflow.cs:262` and completion selection above | `A_second_semantic_identity_cannot_hide_a_failed_outcome_for_the_same_requirement` failed. Reject a second identity; already persisted divergent groups remain intact, project review-needed and show an outstanding completion row with no arbitrarily selected authoritative outcome |
| AST-20261008-03: later cutover stages stopped checking predecessor evidence | `src/BDIT.TenantToolkit.Engine/Workflow/JobProjection.cs:127` | `Later_stages_cannot_hide_missing_candidate_evidence` failed when a candidate run was deleted after closure. Recheck every attached stage's original pinned reference, including deleted or modified runs |

The defects affect record/projection truthfulness. No executor safeguard bypass has been reproduced. Recording or reading a job still grants no tenant-write authority. Historical evidence/catalogue bytes are unchanged; no persisted schema is replaced or migrated.

## Reviewed contract surfaces

| Surface / merged PRs | Inspected implementation and evidence | Conclusion in this bounded review |
|---|---|---|
| Lineage and client scope, #22/#24, INT-051/056/057/059 | `ReleaseLineage`, `LineageReview`, `ReviewedClientScope`; manifest pins, strict schema/endpoints, explicit reused-control relations; `ReleaseLineageTests`, `ReviewedClientScopeTests`, reused-ID tests | Existing explanatory lineage retained; no rebinding or catalogue edits. Material input changes remain bound; exclusion timestamps and set ordering retain their settled non-material treatment |
| Upgrade impact, #23, INT-058 | `UpgradeImpactAnalyser`, shared report formats and CLI; `UpgradeImpactTests` | Two catalogues assess the same stored capture/context; upgrade impact is distinguished from drift between captures. Missing reads/lineage remain explicit limitations |
| Job, observation and disposition history, #25/#27, INT-049/050/060/061 | Strict `WorkflowEvidence`, `WorkflowRecordRules`, `JobWorkflow` and projection; job/disposition/evidence-transfer tests | Original record bytes, subject/scope bindings, graph supersession and unattached/unreadable handling retained. Parallel-identity defect fixed as AST-02 |
| Cutover stages, #28/#31, INT-062/064 | `CutoverWorkflow`, `CutoverText`, Jobs form and `CutoverWorkflowTests` | Stage sequencing, real pilot capture, prerequisites/criteria, fresh closure capture and separate recorded approvals retained; no activation/assignment/retirement operation is introduced. Historical stage-evidence defect fixed as AST-03 |
| Completion and desktop/CLI parity, #30, INT-063 | `JobCompletion`, `Workspace.ProjectJob`, CLI `WorkflowContext`/`job`/`jobs`, Jobs tests and headless tests | Both callers use the shared projection/completion engine. CLI validates supplied capture integrity/tenant and names absence of a current capture. Unknown writes and conflicting histories now block completion truthfully |
| Stored assessment citations, #32, INT-065 | `JobWorkflow.AssessmentReference`, `AssessmentEvidence`, `SubjectReview.EvidenceIntact`; assessment-reference/headless/Jobs tests | Stored assessment/capture/catalogue/finding/digest bindings retained. A contradictory assessment cannot justify a Pass; legacy assessments without intact-capture proof require reassessment |
| Workflow pins, #29 and superseding #34 | Every `uses` in `build.yml` and `publish-release.yml` is a full commit SHA. The protected reusable publisher remains Claude's implementation | No publisher replacement, tag-based action or publishing dispatch added. Owner repository protection/live-publication gates remain outstanding |
| Status accuracy, #26/#33/#35 | Actual GitHub PR metadata confirms #32 merged as `7608150`, #33 as `2536216`, #34 as `c04a7ba`, #35 as baseline above | Only Astra's stale #19/#20 claims corrected (actual merged PRs). Claude's entries/findings and historical handover records preserved |

PR metadata and changed-file patches were retrieved for #21–#35 into local review outputs; current implementation and tests, rather than historical README claims, determine the conclusions. Relevant #21 foundations include primary snapshot integrity, shared assessment context, stored-evidence timing and protected transfer behaviour. The current reference-source inventory is [HISTORICAL-TOOLKIT-REVIEW](HISTORICAL-TOOLKIT-REVIEW-2026.10.08.md).

## Validation and gates

- Baseline regressions were observed failing, including the additional completion-row truthfulness assertions; failing compilation is not counted as regression proof.
- Final local engine/CLI suite: 1,060 passed, zero failed/skipped (`post-merge-completion-final.trx`). Earlier `4724b66` also passed 1,060 locally. No synthetic result establishes Microsoft live behaviour.
- Windows push run [37705351287](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/37705351287), at **4724b66**, passed 1,060 engine and 126 application tests; native harness 45 page/size records, 83 commands, zero binding issues and zero tenant calls; 299 extracted files verified with packaged offline startup/shutdown. This is prior-head evidence: the final completion-row/source changes require their own exact-head Windows run.
- Independent Claude review and exact-final-head checks are required before merge. Merge-commit CI must then be recorded. This document does not approve merging, publishing, live acceptance, newcomer/Narrator/physical-scaling acceptance or completion of the master programme.
