# Unresolved tenant writes

Confirmed and owned policy changes can now be reviewed in [Undo and recovery](RECOVERY.md). This is distinct from uncertain requests: missing IDs or unknown acceptance cannot be converted into ownership by searching for a matching name. Recovery itself also persists intent and blocks affected controls after an uncertain or incompletely verified operation.

Before sending a tenant write, the executor durably records its payload digest and an Unknown write-acceptance outcome. A crash between recording intent and receiving a response therefore cannot present the write as definitely unattempted.

Once a run exists, its plan cannot be replayed. A fresh plan also cannot write a control whose earlier run records Unknown acceptance, or Accepted without a valid object ID. A new capture returning no object does not clear the block: Microsoft Graph can be eventually consistent. Other controls can still be reviewed separately. Malformed or modified prior run evidence blocks deployment until it is reconciled.

Preserve the original run, journal, before/after captures and mappings. Investigate the request using its recorded time, tenant, operator, payload digest and any returned object ID, and confirm the outcome in Microsoft 365. Never adopt an object because its name matches, delete evidence to unlock the control, or infer failure from an empty search result.

Confirmed failures before any HTTP write is sent are recorded NotAttempted, with verification NotRun. A fresh reviewed plan is allowed; the original plan remains single-use. Exceptions after transport begins are never assumed to mean nothing happened.

Accepted writes with an exact ID and failed configuration checks can use the read-only **Re-verify** workflow in [Undo and recovery](RECOVERY.md). It records a separate integrity-checked observation, preserves the original records and finalises only supported local ownership changes. Completed 1.0.0 records with absent acceptance metadata require an explicit historical acknowledgement plus fresh matching ownership/settings before future supported writes can resume. This is a narrow migration path, not a general override.

Modern Unknown acceptance, missing IDs, corrupt evidence and insufficient historical ownership remain blocked. Re-verification cannot prove that an uncertain request was never sent, adopt an object by name, or unlock it based on an empty search. Such cases still need a separately reviewed reconciliation procedure; never edit or delete evidence to force execution. Snapshots support investigation and manual recovery, not automatic rollback.

## Original review lifetime at candidate dispatch

Candidate-plan execution retains the original plan and complete before-snapshot timestamps/age limits. Preflight or token acquisition does not renew them. The engine checks current evidence/input/connection binding again after preflight, before this row's intent, and passes the same immutable `ReviewedWriteAdmission` to the required `IGraphClient.WriteReviewedAsync` contract. Graph rechecks it after token acquisition/request preparation, immediately before the initial `HttpClient.SendAsync`, with no intervening awaited operation. The existing `age > limit` rule is retained: equality is admitted, a greater age refuses. A stop already requested at this boundary refuses too.

A proven pre-send refusal is `WriteNotSentException`, recorded per row as NotAttempted/NotRun verification. Earlier Accepted rows and their object IDs/evidence remain Accepted; never summarise a partly accepted run as "nothing sent". Once transport is invoked, cancellation or an exception may be an uncertain write and cannot become a pre-send claim. No new retry, plan refresh, reapproval or replay is introduced. Process interruption can still leave conservatively unresolved intent requiring reconciliation.

This is a last cancellable software boundary, not Microsoft-side atomicity or a monotonic distributed deadline. It applies to candidate-plan writes through this executor. Other separately reviewed recovery, setup, LAPS, assignment and package operations retain their own contracts; they do not automatically gain this plan's deadline. The callback is trusted engine logic; no web content or copied script can supply approval or execution authority.
