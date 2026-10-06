# Workspace continuity, backup and handoff

Initial operating model: one secure local workspace, one engineer at a time, an identified evidence custodian and deliberate handoff. A network share is not a concurrent database. The portable root contains binaries, shipped catalogues/settings and private working data; `BDIT_TOOLKIT_ROOT` selects the whole root, not a separate data directory.

## Backup

Disconnect, end application setup and close every other tool copy using this workspace. In **Settings and diagnostics**, choose **Create sensitive evidence backup**. The archive is written to the workspace's `transfers/` folder, kept apart from shareable `reports/`. Backup is refused while another tool copy holds a tenant write lease (a deployment, recovery or other tenant write in progress). This is a private operational archive, unlike **Export support metadata**. It preserves exact existing JSON, JSONL and NDJSON bytes throughout `data/`, including profiles, mappings, deviations, manual checks, captures, plans, journals, recovery/verification/package records, imported catalogues and application setup evidence. Unknown fields and embedded digests are not rewritten.

DPAPI MSAL caches and policy-write lock files are excluded. Logs, reports, settings and binaries are outside this evidence archive: retain any required exported reports separately and keep the previous approved complete package. Unsupported file types, interrupted temporary records and reparse/symbolic links cause refusal. Do not delete the source to force a backup through; ask the custodian to inspect the condition. Limits are 20,000 evidence files, 32 MiB per file, 1 GiB including metadata and 4 MiB of checksum metadata. Larger archives need a separately reviewed transfer procedure.

An adjacent `.sha256` file identifies the ZIP. Send that digest to the receiving engineer through the approved handoff channel, separately from the archive. A digest supplied with the same untrusted archive is not independent proof. Secure the archive under the organisation's client-evidence storage/retention policy. No export uploads anything.

## Verify and restore separately

On **Settings and diagnostics → Restore a backup into a separate folder**, select the ZIP, a new destination folder (by default a new folder under `transfers/`) and paste the **trusted archive SHA-256** received through the handoff channel. A mismatch refuses the restore before anything is written. If no trusted digest exists, the engineer must tick the explicit acknowledgement, which is logged; internal checksums then detect accidental damage only. **Verify and restore separately** checks safe paths, duplicates, size bounds, exact checksum coverage and all file hashes before publishing the separate folder. Existing folders and the active data directory cannot be overwritten. **Verify restored folder** (or `bdit verify-restore --folder <dir>`) re-checks a restored folder at any time and refuses changed, missing or unlisted files.

Restored `data/` contains the original records. It is not loaded into the active workspace automatically. Historical captures remain historical; plans cannot be replayed; unresolved writes still block the corresponding new work. No sign-in or approval is restored. Keep the source and prior package until the custodian accepts the transfer.

## Upgrade into a new complete package

1. Retain the old approved package and a verified evidence backup. Confirm the new package's source/run, integrity, runtime, default catalogue and known limits. A rebuilt artifact requires fresh validation.
2. Extract the new complete package into a separate secure folder. Keep its shipped `standards/` and manifest intact; they include historical releases. Do not merge old binaries or replace the new manifest.
3. Open the new package, stay disconnected and use **Settings and diagnostics → Adopt into this empty workspace** on the verified restored folder. Adoption is refused unless the new package's `data/` holds no files. It re-verifies the restored folder, copies every record (profiles, mappings, deviations, captures, run journals, unresolved recovery and verification records) through staging, re-checks each file's SHA-256 in place, and restores no sign-in, approval or session. Restart the tool afterwards. Do not copy `data/` by hand: a partial copy can drop the journals that keep uncertain writes blocked.
4. Review old `config/toolkit.settings.json` separately. Transfer only intended non-secret business/app settings that the new settings model still accepts. Do not overwrite new defaults wholesale. Client profiles remain in `data/`; no authentication cache is transferable access.
5. Open the new package offline first. Check profile selection, historical reports/standard identity and unresolved registers. Compare original record bytes/digests; stop on corruption, unknown unsupported records or changed history. Schema 3/4/5 catalogues remain readable through existing loader rules; this procedure does not migrate unknown future schemas into valid records.
6. Sign in independently and gather fresh live evidence before a new approved plan. Retain old protection and resolve uncertain operations; upgrade is not remediation.

Binary downgrade and tenant recovery are different operations. Never restore an old workspace over newer write history. To use an older supported binary, preserve the latest complete records, check its reader compatibility and escalate unsupported schema versions. Never discard a journal to make an old version run. Use [recovery](RECOVERY.md) and [unresolved writes](UNRESOLVED-WRITES.md) for tenant incidents.

## Support metadata

**Export support metadata** contains only the exact generated preview: product/framework/architecture, constrained catalogue version/digest and control count, plus README and checksums. It does not read or include logs, local paths, profiles, tenant/account identifiers, captures, plans or caches. **Copy support metadata** copies the same preview. This intentionally small bundle cannot diagnose a tenant-specific failure alone. A human reviews any additional client evidence before separately sharing it.
