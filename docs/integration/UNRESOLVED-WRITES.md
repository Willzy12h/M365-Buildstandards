# Unresolved tenant writes

Before sending a tenant write, the executor durably records its payload digest and an Unknown write-acceptance outcome. A crash between recording intent and receiving a response therefore cannot present the write as definitely unattempted.

Once a run exists, its plan cannot be replayed. A fresh plan also cannot write a control whose earlier run records Unknown acceptance, or Accepted without a valid object ID. A new capture returning no object does not clear the block: Microsoft Graph can be eventually consistent. Other controls can still be reviewed separately. Malformed or modified prior run evidence blocks deployment until it is reconciled.

Preserve the original run, journal, before/after captures and mappings. Investigate the request using its recorded time, tenant, operator, payload digest and any returned object ID, and confirm the outcome in Microsoft 365. Never adopt an object because its name matches, delete evidence to unlock the control, or infer failure from an empty search result.

This release does not implement a reconciliation acknowledgement or override. The affected control remains blocked in the toolkit; resolving it requires a separately reviewed recovery procedure and, if toolkit execution must resume, an explicit reconciliation workflow that retains the original evidence. Snapshots support investigation and manual recovery; they do not provide automatic rollback.
