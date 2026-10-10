# Offline registered report export — PR #62

`bdit report-evidence --input "saved report.json" --tenant <expected-tenant-guid> --format html` renders the already captured registered Graph report through the shared strict reader and exporters. It never signs in, collects, refreshes, assesses against another standard or runs a script. The expected tenant is explicit for an offline file; no live session is selected. No saved client record or standard is required. Output goes to the existing installation reports directory, or the directory selected with `--root`.

Formats: HTML, JSON, CSV (a ZIP with provenance and section sheets), and Excel (`xlsx`). Exact source interval, identity, registered routes, read states, limits and integrity remain in the document. Friendly names and exact IDs use the existing report renderer. HTML escaping and CSV formula protection use the existing exporters. JSON preserves the strict registered evidence, including null/unknown values; it is not a configuration capture or deployment before-evidence.

The command accepts only `--input`, `--tenant`, `--format`, `--root`. Unsupported formats/options, other tenants, altered digests, unknown/duplicate fields, wrong kind/resource/row schema, invalid UTF-8 and input over 32 MiB refuse before export. Reading is bounded even if a file grows. It writes no profile, evidence, cache, session or historical record; the input is unchanged. The source is not independently authenticated by its integrity digest.

Exit zero means an export was written, not that the original reads succeeded. Console and document explicitly retain Failed, Partial, NotAttempted and Cancelled states. A genuinely completed empty collection says checked successfully; an inaccessible/partial empty result says it was not an empty successful check. This remains historical/offline visibility, even when the record describes a live source at its original time.

Validation: before command implementation the 21 actual CLI-process regressions all failed because the command was unknown (usage exit 64), with no false passing replacement tests. After implementation, those tests plus a strict synthetic package-fixture check passed (22 executed). An initial Excel assertion used ZIP entry Name instead of FullName; correcting the path assertion preserved verification of the actual workbook entry. Strict solution and full suite results bind the PR's exact pushed head.

Fresh portable checks additionally execute the same self-contained application in CLI mode against a strict synthetic Partial report, verify the actual HTML and exact printed path, refuse another tenant without an extra export, and retain the existing no-desktop/no-authentication checks. This Windows check is not executable on Linux and must pass on the exact source before review readiness.

Registered connected Reports/navigation remains separate source work. Exchange report schemas and script execution are proposed in #61, not enabled by this command. No new access, consent, version, live action or publication; Microsoft/human acceptance remains open.

## Claude review response — 9 October 2026

CLA-20261008-78: refusal fixtures now isolate the actual safeguards. Oversize input is otherwise valid JSON with whitespace beyond 32 MiB. Invalid UTF-8 occurs within a JSON string, whose digest is sealed for the replacement character a permissive decoder would produce. Wrong-kind evidence is re-digested so a digest mismatch cannot mask the kind guard. Tests require the specific size/UTF-8/kind refusal reasons.

CLA-79: offline file exports show an explicit supplied-file warning in HTML and CSV/Excel provenance; account identity is labelled recorded, not verified by the export. JSON preserves the original evidence unchanged and cannot itself authenticate its claims. Export success never proves source, identity, live collection or read success.

CLA-80: this route rejects duplicate options case-insensitively, including repeated tenant/input. Other command parsers retain their existing behaviour to avoid an unrelated CLI contract change. No new authentication, cache, collection, write or evidence-store path is introduced.

Negative-control verification: temporarily remove the CLI/schema size guards, strict UTF-8 decoder and kind guard together in the local checkout. The seven refusal process cases then yield exactly three failures (kind, UTF-8, oversize: incorrect success/exit 0) and four passes. Restore the exact backed-up source bytes before final validation. The mutation is not committed. A fixture assertion independently proves the permissively decoded UTF-8 input has a valid digest/schema, so unrelated integrity failure cannot make that case pass.

After restoring guards, all 24 actual CLI process cases passed locally. Windows exact-head and extracted portable verification remain required.
