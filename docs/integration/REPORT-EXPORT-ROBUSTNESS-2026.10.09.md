# Report export robustness — 9 October 2026

PR #68 validates existing registered-report exporters end to end, independently of CLI #62, scale #65, transport #67 and Claude's script library. It branches from integration `92cb07c` and changes no production source or report contract.

Eight synthetic tests cover HTML, JSON, CSV ZIP and XLSX:

- A Partial historical report retains its explicit read error and original sealed bytes after export. Unknown last-sync data stays unknown.
- Accented text, combining characters, a supplementary Unicode character, commas, quotes and multiline text survive the applicable output format. CSV fields are parsed, not counted by line splitting.
- A long value remains complete in JSON/HTML/CSV. XLSX follows the existing Excel cell limit, emits its explicit truncation marker and produces parseable XML without formula elements.
- HTML encodes company/row text, and CSV guards formula-looking cell text. XLSX stores it as literal inline strings.
- A second export creates a different file and leaves the earlier file unchanged.
- Modifying a sealed report refuses all four formats before output, leaving an existing sentinel file byte-for-byte intact.

These extend lower-level writer and strict-reader tests; they do not substitute for physical Excel/browser accessibility or human/live acceptance. No Microsoft service, authentication, private report or script execution is involved.

```text
dotnet test tests/BDIT.TenantToolkit.Tests -c Release -p:EnableWindowsTargeting=true -warnaserror --filter FullyQualifiedName~ReportExportRobustnessTests
```

Exact local and Windows results are recorded on the PR before readiness. Claude reviews independently, then the branch refreshes from integration and checks rerun before a merge commit. Its actual merge CI is recorded separately. New application reports require their own registered schema/access decision; the proposed offline doctor command follows existing CLI/readiness claims. The script library remains with Claude.
