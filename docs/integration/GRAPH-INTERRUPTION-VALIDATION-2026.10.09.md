# Graph interruption validation — 9 October 2026

PR #67 is a separate offline robustness slice based on integration `92cb07c`. Existing tests already check ordinary 429/5xx retries, cancellation boundaries and write non-retry. This slice adds combinations through the actual `GraphClient`, using a synthetic HTTP handler and token provider; no external request is possible through that handler.

The five cases cover:

- Operator cancellation during the actual 30-second Retry-After delay, for 429 and 503. Only the first request/token acquisition occurs.
- A bounded report yields its first row, then encounters 429 on page two. Cancellation interrupts the real delay; the returned row remains intact, page one is not replayed and page two is not retried.
- 401 → 503 → 401 retains exactly one forced silent token renewal. The second 401 requires an explicit reconnect rather than another renewal loop.
- Operator cancellation after a candidate write has entered the transport yields AmbiguousWriteException, with exactly one POST and no retry. The synthetic handler waits on the actual request cancellation token; no tenant is changed.

Tests assert observable boundaries and counts. They do not sleep or require a tiny elapsed-time threshold. A ten-second outer timeout catches stuck cancellation while the configured retry delay is thirty seconds. This is a test ceiling, not a promised live response time. Production source, retry policy and uncertainty semantics remain unchanged. No fake authenticator can establish WAM, Conditional Access, consent or Microsoft behaviour.

Run with the repository's pinned .NET SDK:

```text
dotnet test tests/BDIT.TenantToolkit.Tests -c Release -p:EnableWindowsTargeting=true -warnaserror --filter FullyQualifiedName~GraphInterruptionValidationTests
```

Local and exact-head Windows results are recorded on the PR before readiness. Claude independent review is required before merge. Refresh from integration after Claude's source merges, rerun required exact-head checks, then use a merge commit and verify its actual CI. This is separate from #65 scale profiling, #63 executor scheduling and Claude's #66 script pack.

## Independent-review refinement — 10 October 2026

Claude noted that initial path-count assertions depended on synchronous token acquisition. The test provider now deliberately yields; the old assertions failed in three cases. The corrected tests await the observable back-off warning (after the read cancellation checks) or the actual synthetic write dispatch before stopping, with the same ten-second outer ceiling. All five focused cases pass. Removing cancellation from the real back-off still causes the three intended delay cases to fail; production source is restored afterwards. This avoids both a scheduling assumption and mistaking cancellation before the back-off for proof that the delay is cancellable. Exact-head Windows and full-suite results remain separate evidence recorded on the PR.
