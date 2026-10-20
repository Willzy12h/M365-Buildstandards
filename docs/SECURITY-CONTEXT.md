# Security context and external check results

This work follows INT-138. The current source slice provides strict local external-result admission only. Incident collection, native import/preview, exports and reference mappings are still being implemented; this parser is not an end-to-end report feature.

The neutral interchange is one UTF-8 JSON object with `kind: external-check-results`, `schemaVersion: 1`, `toolName`, `toolVersion`, `sourceRunId`, claimed `tenantId`, explicit UTC `performedAt`, `sourceState`, `rows` and `integrityDigest`. Source state is Complete, Partial, Failed, Cancelled or Unknown: these are source claims, not BuildStandard verification. Each row has a unique `testId`, raw `outcome`, plain-text `title` and optional plain-text `reason`/`reference`. Known outcomes are Passed, Failed, Skipped, Error and Unknown; other values remain raw and are displayed as Unclassified.

The digest is SHA-256 over the repository's canonical JSON representation with `integrityDigest` set to the empty string, expressed as 64 lowercase hexadecimal characters. Validation also preserves the SHA-256 of the exact original bytes, including a supplied UTF-8 BOM. Neither digest proves authenticity or that the external tool ran. Original bytes remain separate from any display projection.

Admission requires the selected client's tenant GUID to match the source claim. Bounds are 8 MiB, JSON depth 16, 5,000 rows, 200 characters for identities/version fields and 4,096 for explanatory text. Duplicate keys or test IDs, unknown structural fields, malformed or unsupported formats, missing required values and digest mismatches are refused. Files and parent directories cannot be links. No code, archive, remote content or referenced file is opened by the parser.

This is not a direct Maester/Pester/vendor JSON importer. Deliberate conversion to the neutral format is required; conversion cannot turn skipped, failed or partial execution into a successful benchmark. External claims cannot be used as native compliance, deployment before-evidence, exact approval, recovery verification or job-completion authority. Historical standards are unchanged.

Automated synthetic parser tests do not establish Windows/native behaviour, live Microsoft access or independent safety acceptance. No new consent or tenant call is made in this source slice.
