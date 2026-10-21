# Security context and external check results

This work follows INT-138. The current source slice provides strict local external-result admission and offline CLI exports. Incident collection, native import/preview and reference mappings are still being implemented; this is not complete J delivery.

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
