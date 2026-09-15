# Follow-up source review — preview.6

The pasted review was checked against `astra/engineer-workflow` starting at `713535cd6f7738624801221317169de12aa4753f`. Findings were treated as claims to verify against the complete source.

| Finding | Disposition |
| --- | --- |
| Shutdown discards an exception from the current deployment task | Fixed: observe even an already-completed task, log the failure, retain a scrubbed completion summary and show it during close. This addresses the finalisation failure; individual write outcomes retain their existing durable records. |
| Closing handler has property access outside its exception boundary | Hardened: the entire handler is guarded, concurrent closes remain cancelled, and the queued final close is awaited so failures are observed. |
| Paging limits or cycle protection may be missing | Already implemented: page/item budgets, visited links and same-route next-link validation. Tightened item-budget enforcement before each append and reject non-object collection entries instead of silently omitting them. |
| Missing LINQ import | Not a defect: the shared build properties enable implicit usings. |
| ProfileValidator not imported | Not a defect: it resides in Core.Models, already imported. |
| Only takes non-generic IEnumerable | Not present in the source: it already accepts IEnumerable<GraphRoute>. Added an explicit null guard. Non-generic IEnumerable does not mean IEnumerable<object>. |
| ScopesFor accepts null at runtime | Added ArgumentNullException.ThrowIfNull for a clear caller error. |
| Editable application IDs | Retained intentional configuration. Client ID, tenant, operator and relevant input digests remain bound to previews and execution. Local configuration edits are not authorisation to grant consent or change tenants. |
| Stop may interrupt verification | Retained truthful accepted/unverified outcomes and read-only re-verification. Cancelling a read cannot prove that an accepted tenant write was reversed. |

Validation: Release solution compilation and source/diff review only. The user requested no repeated automated tests. No GUI shutdown exercise, network fault injection or live tenant writes were performed for this revision.
