# Claude review response — 14 September 2026

Review input: user-supplied independent findings for PR #2 at `6712d8e`. Claims were traced against the current implementation before changes. The review's opening build-status sentence conflicts with its later statement that no local suite could run; only its explicit CI evidence is treated as prior verification.

| Finding | Disposition in preview.3 | Verification / limit |
|---|---|---|
| D1 pre-request failures lock controls | Fixed with WriteNotSentException at the transport boundary; executor/recovery retain Accepted on follow-up failure | Transport tests prove no HTTP send on token failure; engine tests allow fresh plans but never replay the original |
| S1 accepted recovery has no exit | Added read-only re-verification for deployment and recovery, separate evidence and mapping finalisation | Tests cover delayed CA/Intune absence, restoration, containment, cancellation, corruption and unchanged original records; live propagation untested |
| S3 historical 1.0.0 blocks | Chose acknowledged reconciliation, not automatic acceptance promotion | Exact latest ownership, payload digest, current settings and safe state required; synthetic legacy tests only, no client evidence inspected |
| D2 unbounded Stop/read retries | Stop cancels deployment reads; policy write keeps its own timeout; readback and after-capture each budgeted to 60 seconds | Hung-read and incomplete-capture tests; connected shutdown remains live-unverified |
| S2 admin-consent localhost port | Retained registration; added clear UI fallback and current Microsoft references | Endpoint-specific live behaviour remains unverified; test before relying on callback |
| S4 / D3 colour and access badges | Retained intentional amber for a write-capable token even in assessment mode; green reserved for verified outcomes | Assessment execution gate remains separate; no security change |
| S5 compliance arrays | Retained conservative equality and Review required outcome | Tenant-added defaults remain a live payload test; no relaxed comparison |
| S6 no target users | Retained explicit No target users status | It is not an entitlement pass; engineers must review targeting. No licence assignment added |
| Optional version / interruption notice | Setup sign-in sends current version; Overview identifies interrupted policy runs | Build/offline UI coverage; connected startup acceptance still required |
| Optional account substring search | Deferred | Existing sign-in address / exact ID / display-name prefix lookup remains; no broader directory search added |

The highest-priority next step is authorised disposable-tenant acceptance, beginning with setup/consent and read-only collection, then candidate creation, re-verification and recovery. No tenant mutation, consent or app registration was performed during this work. See [testing evidence](TESTING-EVIDENCE.md), [recovery](../RECOVERY.md) and [live checks](../LIVE-VALIDATION.md).
