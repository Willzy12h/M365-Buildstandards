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

## Current-head connect and cooperative-history corrections

CLA-20261010-97: strict startup history could refuse after a verified new connection was adopted, before access/licence reads. The old tenant notice was already cleared; the confirmed problem was an empty notice and unfinished checks, not retained prior-tenant notice. Isolate expected local history refusal, retain an explicit unknown/untrusted history notice and original bytes, then continue the existing read-only access/licence checks. Later read cancellation still cancels; no authentication, scope, experimental opt-in, exact approval or deployment-history guard is bypassed. Local diagnostics use fixed wording rather than raw exception data.

CLA-20261010-98: deterministic valid-first/damaged-last and reverse-order fixtures prove whole-history validation before any marking write. A narrow internal enumerator feeds the actual reader; production enumeration semantics are unchanged. The lazy-validation mutation fails the valid-first byte-preservation check.

CLA-20261010-99: startup marking now takes the existing cross-process tenant write lease across verification and all marking, so a cooperating active deployment/recovery cannot have its Running record reclassified by another host. A two-store real-lease test proves refusal and unchanged bytes while the lease is held, then honest marking and idempotence after release. Arbitrary external editors ignoring that lease remain outside this guarantee; the precise lost-update interleaving was not reproduced.

The delegated exact-source repair passed 70 Linux engine/executor/completion checks and strict App compilation; two controlled negative tests failed as intended. Six actual post-authentication App cases compile, using an actual GraphClient with synthetic HTTP. Windows execution/native/package and Claude independent review of this new source remain required. Preserve #81's authorisation-invalidated subscription when bringing this helper forward; no subscriptions, permission-preview budgets or cache behaviour change here.

## CLA-20261010-110: distinguish unavailable history access

A competing tenant evidence-store lease prevents history inspection; it does not establish damaged history. Only the existing lease-acquisition exception receives a diagnostic marker, preserving its type/message and every write guard. The connection notice explains that exclusive access is unavailable, another operation may be active, and the engineer should wait then retry. Other integrity refusals retain the untrusted-history/reconciliation guidance.

Validation: 28 actual engine recovery/integrity tests passed. Both assessment and deployment App cases use a real competing lease, preserve original Running bytes and unresolved-write refusal, and keep GET-only access/licence checks; Windows-targeted App tests compile with zero warnings/errors. A portable extraction of the actual completion method passed held-lease/corrupt-history cases; the original method fails the new held-lease wording assertion. The extraction uses stub dependencies and is not native App execution. Root independently compiled the actual App test project successfully. Exact-head Windows execution and Claude's delta review remain required.
