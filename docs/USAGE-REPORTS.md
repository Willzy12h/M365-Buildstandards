# Usage reports and licence review

This experimental reporting work is in progress. Its initial source handles the four fixed Microsoft365 active-user, email, Teams and OneDrive activity CSV schemas and a separate original-data usage pack. The credential-free download transport and engine capture service are implemented with synthetic tests. Their native collection, deliberate optional-access setup and recovery journey are not yet complete. It never removes or purchases licences and never changes reporting privacy.

## Offline pack exports

Use the expected client tenant GUID explicitly:

```text
bdit usage-report-evidence --input usage-pack.json --tenant <guid> --format html --root <portable-folder>
```

Supported formats are HTML, JSON, CSV (one ZIP of separate tables) and Excel. The command makes no sign-in or service request. Duplicate/unsupported options, another tenant, invalid data or an unsupported format are refused. JSON export retains the supplied original bytes, including original CSV data/digests; export success does not prove those source claims are authentic. Formatted tables escape HTML and protect spreadsheet cells; the immutable original remains unchanged.

## What the results mean

Each source retains its selected period, read status, refresh/activity dates as reported, original CSV digest and privacy provenance. A failed or cancelled source has no successful empty CSV substituted. A successful CSV with only valid headings is an empty source result. Missing counts stay **Not reported — unknown**, not0. A returned0 remains0. A blank activity date means **No date reported — activity unknown**; an older activity date can legitimately predate the selected period.

The four CSV schemas do not report the tenant privacy setting. Consequently this first revision keeps privacy **Unknown**, even if a User Id looks like a GUID or a principal name looks like an email address. These are report identity text, not verified directory identities. No joins by UPN/display name, verified inactivity conclusion, licence-removal recommendation or savings estimate are offered. Active-user CSV does not return a Report Period column; the requested period alone is not proof of the returned interval. Compare sources and refresh dates separately.

CSV inputs are strict UTF-8, at most10MiB,20,000rows and256unique columns per source. Extra future column names are diagnostic; their arbitrary values are excluded from projection but original CSV bytes remain in the pack. Malformed quotes/widths/dates/counts/booleans are refused. The encoded original-data pack is at most40MiB; oversized input is not silently clipped or called saved. Integrity digests detect change; they do not authenticate source, tenant, account or live-mode claims.

The planned connected route reuses only the already-approved optional Reports.Read.All purpose, with separate credential-free bounded downloads. Effective reporting roles/workload licences and actual Microsoft responses remain live-unverified. ReportSettings.Read.All is not included and the tool does not infer or change privacy. Reports never become complete deployment before-evidence, approval or job completion. Independent Graph/context acceptance, Windows/native/package proof and human/live gates remain outstanding for the finished workflow.
