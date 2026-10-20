# Operational context reports

This in-progress expansion separates service-health observations and partner-relationship context from tenant configuration and deployment evidence. The current G branch implements service-health engine capture, native visibility/original recovery and offline exports. Source-matched Windows/native/package checks are pending; partner reporting remains incomplete. No live Microsoft behaviour or named safety acceptance is claimed.

## Native service-health workflow — implemented, validation pending

1. In **Connect**, expand **Optional read access** only if this connection needs service-health reporting. Select the default-off service-health choice, review the exact scope/registration and reconnect deliberately through the existing flow. Missing consent/roles are not repaired from Reports. The summary keeps selected purposes visible when collapsed.
2. Open **Reports → Tenant reports → Operational context → Read context**. Review the source tenant/account/app in the pinned header, the two fixed reads and the limitations. Tick acknowledgement, then **Read service-health context**. Navigation or selecting a choice makes no request.
3. **Stop context read** requests cancellation; earlier admitted rows can remain in the resulting partial/stopped record. Inspect source read states, not just the number of table rows. A failed source is not an empty success.
4. In **Context results**, choose the source/provenance table. Search changes the display only. HTML, original JSON, CSV ZIP and Excel retain the entire report and source states. Unknown values are never healthy, false or zero.
5. In **Original context**, save the exact original to managed evidence or export original JSON. Written bytes must pass readback before pending recovery clears. A failed save retains the original. Explicit discard deletes no managed record and performs no new read.

Changing context clears the current display/acknowledgement; pending originals keep their source tenant. Ordinary close waits for owned capture/serialisation and refuses unresolved originals. Opening a supplied file blocks overlapping reads and refuses a late changed-client result. Force-closing the process can lose an in-memory pending original.

These are experimental source paths awaiting current Windows/native/package and named acceptance. Human accessibility/workstation policy and live roles/API behaviour are not verified. The partner access choice is separate and is not needed for service health; its collector is not yet implemented.

## Open a saved service-health report offline

Use the original source tenant GUID recorded in the report, even when reviewing a different client:

```text
bdit operational-report-evidence --input report.json --tenant <original-source-tenant-guid> --format html
```

Supported formats are `html`, `json`, `csv` (a ZIP of all tables) and `xlsx`. Optional `--root` selects the existing workspace root. No URL, authentication or live-read option is accepted. Original JSON exports retain the supplied bytes; other formats show the entire admitted evidence, regardless of later native display filters. A successful export does not turn a failed read into a successful check or authenticate a supplied file's source claims.

Review **Operational provenance** before the tables: source tenant/account/application, original access intent/mode, frozen as-of and collection interval, source routes, read states, limitations and digest. Empty tables mean no returned objects only where the corresponding read completed. Failed, stopped or incomplete sources remain explicit; missing or future service values are unknown, never healthy, false or zero. Advisories are not proof of this client's availability, impact or incident cause. Overview and issue states remain separate.

## Evidence and limits

The strict `operationalContextReport/1` envelope is separate from configuration snapshots, approvals and deployment before-evidence. It admits at most16MiB encoded evidence and5,000 total rows. The service-health source has a shared100-request/five-minute budget, bounded streams, exact registered public Graph paths and one silent401 renewal; it has no interactive retry, generic URL or write operation. Original context changes refuse current admission. Earlier admitted rows survive a later failed/stopped/limited read with truthful source states.

Managed originals use `operational-context-evidence` under their source tenant and are included in ordinary workspace backup without increasing its32MiB bound. Saves refuse overwrite, links, wrong tenant/record IDs and altered content. Export/save readback must match the original before pending recovery can be cleared. Integrity detects modification; it does not establish Microsoft authenticity.

Native Read/Stop/access and original-recovery are implemented in source, with Windows/native/package verification pending. The CLI remains offline-only; do not infer a connected CLI command or partner report from it. Optional scopes are not granted by source development, and no agent tenant action is authorised. H1, physical accessibility/workstation policy, named runtime acceptance and live delegated-role/API/licence behaviour remain separate.
