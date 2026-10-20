# Operational context reports

This in-progress expansion separates service-health observations and partner-relationship context from tenant configuration and deployment evidence. The current G branch implements service-health engine capture and offline visibility. Its connected native journey and partner reporting remain incomplete; no live Microsoft behaviour or named safety acceptance is claimed.

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

The native Read/Stop/access and original-recovery journey is still being implemented. Do not infer a runnable connected command or partner report from this offline command. Optional scopes are not granted by source development, and no agent tenant action is authorised. H1, physical accessibility/workstation policy, named runtime acceptance and live delegated-role/API/licence behaviour remain separate.
