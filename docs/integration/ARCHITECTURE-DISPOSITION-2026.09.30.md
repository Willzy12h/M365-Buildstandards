# Architecture disposition — Preview.15

The existing engine/desktop/headless boundaries are retained. UI changes project existing evidence into a reusable ResultEvidencePanel; they add no authentication, transport, writes or schema. .NET 10, explicit read cancellation and exact package provenance/startup checks address current release correctness. The architecture roadmap remains the specification for later work; its historical counts are not current validation.

| Workstream | Actual state / release disposition |
| --- | --- |
| W0, W2 | Completed in PR #15; existing equivalence/parameter tests retained |
| W3 | Completed headless consumer; read-only conformance and desktop parity retained. Catalogue exports reuse the engine, without a UI dependency |
| W1, release compiler | Deferred to a separate claim. Historical flat files remain immutable and new release digest/retirement/payload parity are tested. Byte-identical decomposition of .11 and release-source versioning need dedicated acceptance; changing the publication mechanism is unnecessary for this bounded release |
| W4, scheduled multi-tenant reporting | Deferred product scope. No unattended authentication/scheduler or new tenant access introduced |
| W5, extension contract | Existing closed enums, allow-list/guard and conformance tests retained. No new write kind. Adding a native/mobile/profile transport requires a separate reviewed contract with negative tests; no data-driven bypass |
| W6, release engineering | Preview.15 has pinned SDK/bootstrap, strict Windows tests, versioned immutable successor, source commit/runtime/standard metadata, ZIP/stage hashes, fresh-extraction and actual packaged first launch checks. Schema loader refusals and source package limitations remain explicit; this is an unsigned preview |
| W7, Graph drift watch | Deferred: public documentation is researched for this release, but a scheduled issue-writing watcher is a separate feature. No automatic catalogue rewriting |
| W8, configuration-as-code imports | Existing guarded import retained. Additional third-party export adapters need dedicated foreign-ID/template fixtures and tenant metadata acceptance |
| W9a, responsibility splits | Only the shared result-evidence presentation is extracted here. Workspace/assessment orchestration is preserved to minimise changes to safety paths; split larger seams in a separate claim with behavioural parity |
| W9b, async convention | Deferred library-wide change. Current cancellation boundaries are fixed and negatively tested. A consistent ConfigureAwait/analyser migration needs explicit UI-independent callback and dispatcher tests, beyond release presentation work |
| W9c, digest caching | Deferred until profiling proves benefit. Mutable evidence/ownership and stored integrity must not become stale through speculative caching |
| W9d/e | Completed export-format and parser/input parity work retained |
| W9f | Regression coverage extended for cancellation, historical digests, successor payload parity, row inspection and evidence presentation. Current counts belong in COMPLETION-REGISTER |

Improved WPF remains the default. The interface comparison records the representative HTML workflow, observed browser checks and unresolved hosting/runtime/bridge work. Engine guards continue to decide whether any action is allowed. Planned structural work does not establish operational acceptance.
