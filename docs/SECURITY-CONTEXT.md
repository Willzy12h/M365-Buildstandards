# Security context and external check results

This work follows INT-138. The current source provides bounded incident capture with a native experimental workflow, strict local external-result admission and offline CLI exports. Native external-result import/preview and reference interaction are still being implemented; this is not complete J delivery. All connected behaviour remains live-unverified.

The neutral interchange is one UTF-8 JSON object with `kind: external-check-results`, `schemaVersion: 1`, `toolName`, `toolVersion`, `sourceRunId`, claimed `tenantId`, explicit UTC `performedAt`, `sourceState`, `rows` and `integrityDigest`. Source state is Complete, Partial, Failed, Cancelled or Unknown: these are source claims, not BuildStandard verification. Each row has a unique `testId`, raw `outcome`, plain-text `title` and optional plain-text `reason`/`reference`. Known outcomes are Passed, Failed, Skipped, Error and Unknown; other values remain raw and are displayed as Unclassified.

The digest is SHA-256 over the repository's canonical JSON representation with `integrityDigest` set to the empty string, expressed as 64 lowercase hexadecimal characters. Validation also preserves the SHA-256 of the exact original bytes, including a supplied UTF-8 BOM. Neither digest proves authenticity or that the external tool ran. Original bytes remain separate from any display projection.

Admission requires the selected client's tenant GUID to match the source claim. Bounds are 8 MiB, JSON depth 16, 5,000 rows, 200 characters for identities/version fields and 4,096 for explanatory text. Duplicate keys or test IDs, unknown structural fields, malformed or unsupported formats, missing required values and digest mismatches are refused. Files and parent directories cannot be links. No code, archive, remote content or referenced file is opened by the parser.

This is not a direct Maester/Pester/vendor JSON importer. Deliberate conversion to the neutral format is required; conversion cannot turn skipped, failed or partial execution into a successful benchmark. External claims cannot be used as native compliance, deployment before-evidence, exact approval, recovery verification or job-completion authority. Historical standards are unchanged.

Automated synthetic parser tests do not establish Windows/native behaviour, live Microsoft access or independent safety acceptance. No new consent or tenant call is made in this source slice.

## Offline export

Use `bdit external-checks --input results.json --tenant <expected-tenant-guid> --format html` with the deliberately selected client identity. Formats are `html`, `json`, `csv` (a ZIP of tables) and `xlsx`; HTML is the default. `--root <local-toolkit-root>` selects an existing local toolkit context. This command does not authenticate or contact Microsoft, and it does not insert the imported claims into native assessment evidence.

JSON preserves exact supplied bytes; the other formats show original SHA-256, claimed tool/version/time/tenant, source completeness, raw outcomes and their display categories. References remain inert text. HTML escapes source text and disallows active content; spreadsheet writers preserve formula-injection defences. A returned empty optional reason/reference stays empty; missing or null text is labelled Not supplied.

Each export creates a fresh file and verifies its written bytes. A stopped or failed export that already created a file reports that possibly incomplete path for inspection; it never deletes a path another process could have replaced. Files are not automatically opened. A passed external row cannot make partial source execution complete, prove native compliance or permit deployment.

A valid synthetic example is supplied at [external-check-results.example.json](examples/external-check-results.example.json). It deliberately declares Partial even though one row claims Passed. Use the example tenant `11111111-1111-1111-1111-111111111111` only for this fixture. The sample is not tenant evidence. When preparing real converted results, preserve source states and unknowns, use the intended client's identifier and recalculate the canonical digest after any edit; never copy the example digest to different content.

The shared engine can create and resolve an engineer-entered link from an exact external test ID/original SHA-256 to a control in a manifest-verified standard release and byte digest. This is an additive reference, not a verdict translation. Changed/unavailable source or catalogue remains unresolved, without rebinding by name or to a newer standard. Native linking and durable reference export are not yet exposed in this source slice.

The incident transport foundation now admits only a frozen UTC created-date interval (start inclusive, end exclusive, at most 31 days, never beyond the captured clock). It uses the shared verified-account transport and the actually returned `SecurityIncident.Read.All` scope. Continuations must keep the exact resource and filter, page size is bounded to 100, and failed cursors cannot be replayed. There is one existing silent 401 renewal and no interactive retry. The shared collector retains only registered incident metadata, excludes alert bodies/comments/descriptions/identities, preserves prior rows after interruption and retains exact UTC precision in the original evidence. Unknown priority stays unknown; duplicate IDs and future enum values remain partial.

## Incident workflow (experimental; live-unverified)

In Connect or Application setup, optional security incident read access starts off. Select it only for this reporting purpose, then use the existing deliberate connection/setup flow. Selecting the checkbox does not consent, sign in or revoke any earlier grant. Returned `SecurityIncident.Read.All` and suitable security RBAC/workloads are required; an assigned Business Premium licence alone does not establish complete Defender coverage or P2 Identity Protection.

In Reports → Operational context, choose **Security incident context**. Enter explicit UTC start/end timestamps such as `2026-10-20T00:00:00Z`: start is inclusive, end exclusive, at most 31 days and no future end. Review the source, access and coverage notice and acknowledge before **Read incident context**. Changing the selection invalidates acknowledgement. Navigation never starts a read.

Save or export the original JSON before another read. Stop preserves earlier observations with incomplete status; changing client hides the former current result while retained originals keep their original source identity. HTML, CSV ZIP and Excel contain all tables regardless of display filters. Portal addresses remain plain text. Resolved is the source workflow state, not verified remediation; a completed empty query is not proof of no attacks. Offline JSON uses `bdit operational-report-evidence` with the existing explicit tenant and format options.

The additive `external-control-references` version1 document retains engineer-entered links to exact source and catalogue bytes. It refuses duplicate/conflicting mappings and does not resolve unavailable historical standards by name. It carries no verdict, ownership or execution authority. Its native save/open workflow remains unfinished.
