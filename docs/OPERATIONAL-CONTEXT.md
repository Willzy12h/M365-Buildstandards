# Operational context reports

This in-progress expansion separates service-health observations and partner-relationship context from tenant configuration and deployment evidence. The current G branch implements service-health engine capture, native visibility/original recovery and offline exports. The health milestone287c5bc passed source-matched Windows/native/package checks (PR156/run39485176532); later partner backend, export and native additions require their own current-source Windows verification. No live Microsoft behaviour or named safety acceptance is claimed.

## Native service-health workflow — implemented, live-unverified

1. In **Connect**, expand **Optional read access** only if this connection needs service-health reporting. Select the default-off service-health choice, review the exact scope/registration and reconnect deliberately through the existing flow. Missing consent/roles are not repaired from Reports. The summary keeps selected purposes visible when collapsed.
2. Open **Reports → Tenant reports → Operational context → Read context**. Review the source tenant/account/app in the pinned header, the two fixed reads and the limitations. Tick acknowledgement, then **Read service-health context**. Navigation or selecting a choice makes no request.
3. **Stop context read** requests cancellation; earlier admitted rows can remain in the resulting partial/stopped record. Inspect source read states, not just the number of table rows. A failed source is not an empty success.
4. In **Context results**, choose the source/provenance table. Search changes the display only. HTML, original JSON, CSV ZIP and Excel retain the entire report and source states. Unknown values are never healthy, false or zero.
5. In **Original context**, save the exact original to managed evidence or export original JSON. Written bytes must pass readback before pending recovery clears. A failed save retains the original. Explicit discard deletes no managed record and performs no new read.

Changing context clears the current display/acknowledgement; pending originals keep their source tenant. Ordinary close waits for owned capture/serialisation and refuses unresolved originals. Opening a supplied file blocks overlapping reads and refuses a late changed-client result. Force-closing the process can lose an in-memory pending original.

These remain experimental paths awaiting named acceptance. The exact health milestone has offline Windows/native/package evidence; human accessibility/workstation policy and live roles/API behaviour remain unverified. The partner access choice is separate and is not needed for service health. Its backend, offline exports and native selection are implemented in later source, with source-matched Windows verification pending.

## Native partner workflow — implemented, validation pending

Select **Partner relationships** in the same Read context page. Deliberately request the separate optional `DelegatedAdminRelationship.Read.All` purpose through Connect first; the Reports page never authenticates or grants it. Declare the current verified connection as the partner source and enter the distinct exact customer tenant ID. This is a reporting filter, not a customer connection or proof of delegated access. Acknowledge that exact selection before **Read partner relationships**.

The bounded parent inventory filters by returned customer identity. Only uniquely returned, supported parents from a completed scan admit child assignment reads, at most 50 inventories. Other customers are excluded from persisted rows. Missing identities, unsupported types, inaccessible children and limits remain explicit. Direct role IDs are advertised assignments, not effective rights. Raw relationship status and expiry at the frozen as-of time are separate; `active` does not override an observed past end time. Failed child reads have unknown counts, never an invented zero.

Changing report, customer or declaration invalidates acknowledgement and current display. A late original still belongs to the original partner source and selected customer; it can be recovered without rerunning. The same original save/export, readback, cancellation and complete-export controls apply. The native choice is experimental pending current Windows/native/package checks and named acceptance.

## Open a saved operational report offline

Use the original source tenant GUID recorded in the report, even when reviewing a different client:

```text
bdit operational-report-evidence --input report.json --tenant <original-source-tenant-guid> --format html
```

Supported formats are `html`, `json`, `csv` (a ZIP of all tables) and `xlsx`. Optional `--root` selects the existing workspace root. No URL, authentication or live-read option is accepted. Original JSON exports retain the supplied bytes; other formats show the entire admitted evidence, regardless of later native display filters. A successful export does not turn a failed read into a successful check or authenticate a supplied file's source claims.

Review **Operational provenance** before the tables: source tenant/account/application, original access intent/mode, frozen as-of and collection interval, source routes, read states, limitations and digest. Empty tables mean no returned objects only where the corresponding read completed. Failed, stopped or incomplete sources remain explicit; missing or future service values are unknown, never healthy, false or zero. Advisories are not proof of this client's availability, impact or incident cause. Overview and issue states remain separate.

## Evidence and limits

The strict `operationalContextReport/1` envelope is separate from configuration snapshots, approvals and deployment before-evidence. It admits at most16MiB encoded evidence and5,000 total rows. The service-health source has a shared100-request/five-minute budget, bounded streams, exact registered public Graph paths and one silent401 renewal; it has no interactive retry, generic URL or write operation. Original context changes refuse current admission. Earlier admitted rows survive a later failed/stopped/limited read with truthful source states.

Managed originals use `operational-context-evidence` under their source tenant and are included in ordinary workspace backup without increasing its32MiB bound. Saves refuse overwrite, links, wrong tenant/record IDs and altered content. Export/save readback must match the original before pending recovery can be cleared. Integrity detects modification; it does not establish Microsoft authenticity.

Native Read/Stop/access and original-recovery are implemented in source, with Windows/native/package verification pending. The CLI supports strict saved service-health and partner evidence only; it has no connected collection command. Optional scopes are not granted by source development, and no agent tenant action is authorised. H1, physical accessibility/workstation policy, named runtime acceptance and live delegated-role/API/licence behaviour remain separate.

## Collaboration metadata — E implementation checkpoint

The registered `collaboration-inventory` original records a bounded site search, exact selected sites/groups and independently chosen existing-library, group-owner and Team-member reads. Its engine, original JSON, offline HTML/CSV/Excel exports and native read controls are implemented in source on #157; current composed Windows/native/package checks and named acceptance remain pending. This is experimental and live-unverified.

Use the same offline command for a supplied original:

```text
bdit operational-report-evidence --input collaboration.json --tenant <original-source-tenant-id> --format html
```

Use `json` to preserve exact original bytes, `csv` for all tables in a ZIP, or `xlsx` for Excel. The source tenant is the connection that captured the record, not a hostname or guessed resource owner. Imported source claims remain historical/unverified; an integrity digest does not authenticate Microsoft or the author.

Each selected source has its own status/reason and returned count. A known count of0 means that source completed and returned no rows; an unknown count means it did not complete. Group-owner coverage remains Partial even when the returned list is empty: Microsoft Graph v1.0 omits service-principal owners and has unsupported group populations. An unestablished Team is not an empty Team. Direct roles do not establish effective permissions, and another recorded user tenant does not itself establish a guest role.

Site search is not a complete estate. Library rows cover existing libraries of the selected sites only. Full site permissions require unapproved Sites.FullControl.All and are blocked. User/me OneDrive discovery can provision storage and is blocked. Content, sharing links, inheritance and tenant-wide OneDrive ownership are not checked. These limitations are retained in every export.

Keep exact original JSON before replacing a result. Collaboration uses the same source-bound immutable operational evidence and unchanged workspace-backup limit; it never becomes configuration before-evidence, a deployment approval or a completion claim.

### Native collaboration journey

1. In Connect or application setup, deliberately select optional site metadata and/or direct Team membership access, then use the existing preview/reconnect flow. Both default off. Sites.Read.All permits content access, although the registered inventory reads metadata only; selecting a checkbox never grants consent.
2. Open **Reports → Tenant reports → Operational context → Sites, groups and Teams**. For discovery, enter a bounded site search such as `*`, leaving exact IDs and child options empty. Review the displayed access/limits, acknowledge, then **Read selected collaboration metadata**. Nothing runs from navigation.
3. Save or export original JSON through **Original context**. In **Context results**, copy exact returned site IDs from `site-search`; their commas are part of SharePoint identity. Return to **Read context**, clear the search if it is no longer needed and paste exact site IDs one per line. Group/Team inputs accept exact canonical GUIDs one per line. Select only the child sections needed, up to50 combined parents.
4. Changed input clears acknowledgement and the current view; a changed client also clears the collaboration input. Review the new exact selection and acknowledge again before reading. Missing access and invalid selections explain why Read is unavailable. Stop preserves already admitted rows; failed or unsupported children are shown independently rather than as empty lists.
5. Review each source and its original count/coverage in **Context results**, then Save/Export the original before replacing it. HTML/CSV/Excel include all source tables regardless of the display filter. Normal close waits for capture/original recovery; forced termination can lose an unsaved in-memory original.

Selection currently uses exact IDs copied from results or existing tenant records; it does not guess sites or group identities from display names. Human accessibility, workstation policy and live Microsoft behaviour remain unverified.
