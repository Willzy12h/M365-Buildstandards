# Interrupted deployment evidence — Astra review, 10 October 2026

## AST-20261010-07 (P2): startup recovery re-sealed untrusted evidence

Identifier correction: this finding was initially called AST-20261010-04 on #79. That ID already belongs to the preserved #77 promotion-description finding in #76’s review strategy. AST-20261010-07 is the canonical ID for this startup defect; earlier comments and validation commits remain historical evidence.

Confirmed at integration `5ef5470e1d6d28653261d88f40e854ebe9d09d02`, `src/BDIT.TenantToolkit.Engine/Evidence/EvidenceStore.cs:345–367`. `MarkInterruptedRuns` used the display-only `LoadRuns` loader, then saved each Running record with a fresh digest. The loader neither refused a bad digest nor surfaced malformed/empty/wrong-tenant records to its caller.

Reproduction: save a synthetic Running deployment with a Create result In progress and Unknown write acceptance. Edit only the saved result status to Pending, retaining the original digest. Completion correctly refuses it initially. Call `MarkInterruptedRuns`: the original implementation changes Pending to Not run / Not attempted and re-seals the edited record. The subsequent unresolved-write/completion check incorrectly succeeds. No attacker-computed digest, tenant request or actual write is needed for the reproduction. This demonstrates failed handling of an unsealed local edit; the evidence digest is not a signature.

PR #79 uses one strict run-history reader for startup recovery and the existing write/completion guards. It validates all records before startup modifies any, preserving refused files byte for byte. It does not clear ambiguity, reconcile outcomes or permit old-plan replay. Valid interrupted evidence keeps its established meaning. The display loader remains available for historical inspection.

## Offline validation

- `InterruptedRunIntegrityTests`: five refusal cases fail on the original production source; the verified-history positive control passes. The focused original-source reproduction also fails at the post-startup unresolved-write assertion: no exception is thrown.
- The six new cases plus executor, workflow, completion, disposition and cutover regressions: 138 passed locally on Linux/.NET 10. Valid interruption remains idempotent, preserves the unknown dispatched write and refuses replay of the original plan.
- Original and restored test results are retained outside the repository in the task validation directory. Fixtures contain synthetic IDs only.
- Exact-head Windows CI, independent review and an integration merge are still pending. No native, human or live acceptance is claimed by these local tests; no version change or publication is included.

## Operational consequence

An engineer encountering refused history must preserve the local files and reconcile the evidence through the existing support/recovery process. Editing or deleting the evidence to dismiss the error is not a supported recovery route. Startup does not repair untrusted evidence or authorise a tenant write.
