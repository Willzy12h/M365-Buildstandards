# External check results

**Reports → Tenant reports → External results** opens local, neutral `external-check-results` version1 JSON. It does not run another tool, contact a tenant, import a native assessment or close a job. The current implementation is experimental and live-unverified; required independent acceptance remains separate.

1. Select the source client in Connect. Local review needs no sign-in. The claimed tenant GUID in the file must match that deliberately selected client.
2. Choose **Open neutral results JSON**. Inspect **Source and original**: claimed tool/version/run/tenant/time, declared completeness, each outcome count and original-byte SHA-256. Integrity proves consistency, not source authenticity or that checks ran.
3. Use **Claimed results** to search IDs/titles/outcomes/reasons. Future outcomes remain Unclassified. A partial or failed source remains partial or failed even if some rows claim Passed. An empty row list does not prove a successful check.
4. Export HTML, CSV or Excel for visibility. These include all rows regardless of the display search. **Original JSON** preserves the exact original bytes. Reference text and URLs are inert; nothing is automatically opened.
5. Export pending original JSON before opening another source or closing. Changing client hides the old result but retains its pending original under the original claimed tenant. **Export pending original** works without reconnecting to that old client. Explicit discard asks first. Forced termination can lose unsaved in-memory data.

## Exact references

In **Control references**, choose an exact source test ID and a control in the loaded manifest-verified catalogue, then **Add reference** and **Save references**. The file binds the original source SHA-256 to the exact standard release, byte digest and control ID. It is separate from the external result and changes no assessment verdict, ownership, plan approval or job completion.

**Open references JSON** requires the exact original source first. Save or explicitly discard unsaved references before replacing them. The table shows each target identity and whether it resolves. Missing historical catalogues and changed controls remain unresolved; a current name does not substitute. References retained after a client change keep their old source and can be saved without adding new links under the new client.

## Supported interchange and offline CLI

Use [the synthetic template](examples/external-check-results.example.json) and the INT-138 schema fields. Maximum8MiB, depth16 and5,000 uniquely identified test rows. The `integrityDigest` is the SHA-256 of canonical JSON with that field empty. The importer rejects unsupported versions, unknown structure, duplicate keys/IDs, invalid UTC/tenant values and mismatched digests.

This is not a direct Maester, Pester or arbitrary vendor JSON importer. Convert deliberately to the documented neutral envelope; no loose property matching, code execution or remote conversion is provided.

`bdit external-checks --input <file.json> --tenant <selected-tenant-guid> --format html|json|csv|xlsx` uses the same bounded admission and complete exports. It performs no authentication or tenant operation. JSON remains the supplied original; generated outputs do not gain native evidence authority.
