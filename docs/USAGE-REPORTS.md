# Usage reports and licence review

This experimental reporting work is in progress. Its initial source handles the four fixed Microsoft365 active-user, email, Teams and OneDrive activity CSV schemas and a separate original-data usage pack. The credential-free download transport and engine capture service are implemented with synthetic tests. Native source selection, deliberate optional-access connection/setup and original-result recovery are implemented; current Windows/native/package verification and named independent acceptance remain separate gates. It never removes or purchases licences and never changes reporting privacy.

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

## Native engineer workflow

1. In Connect, deliberately choose **Include optional usage-report read access** (default off), then Connect/reconnect. Review the tenant/account, registration name/app ID and Reports.Read.All request. Existing verified matching access can be reused; Microsoft may require consent, CA or MFA at this explicit connection action. A missing scope never launches authentication from Reports. If registration configuration needs attention, carry the choice into Application setup and review its exact permission preview. Turning the choice off does not revoke existing permissions or consent.
2. Open Reports → **Usage review** → **Read usage**. Select one to four sources and D7/D30/D90/D180. Review access/privacy/bounds and acknowledge the exact selection. Read is experimental/read-only; Stop retains the separate source states and any earlier complete CSVs. A failed, cancelled or unattempted source never becomes a successful empty report.
3. **Usage results** shows provenance plus a table per admitted source. Search changes the view only. HTML/CSV ZIP/Excel and original JSON exports retain complete source states and rows. Reported identities remain unverified text; privacy stays Unknown and there are no automatic directory/licence joins, licence-removal proposals or inferred savings.
4. Resolve **Original report** before another read or ordinary close: Save the exact original to its own client's managed evidence, Export original JSON, or deliberately confirm Discard. Changing client clears current rows/acknowledgement but preserves this pending original's identity. No recovery option reruns a source. A forced termination can still lose an in-memory original.

Managed saves use the existing immutable atomic record writer in a separate `usage-review-evidence` folder. They never overwrite, become deployment before-evidence or rebind to the current client. The managed limit is32MiB so the existing workspace backup can include every saved byte; ordinary40MiB usage import/export remains supported. A valid pack over32MiB must be separately exported (not included in managed backup), with no truncation or raised transfer limit. Use Open saved/supplied JSON to inspect original records for the selected client; integrity is not proof of Microsoft source authenticity. Failed save/export retains the pending original for another deliberate local action.

Source implementation and synthetic validation do not establish delegated-role/licence availability, live Microsoft behaviour, accessibility/H1 or named independent safety acceptance. No new consent or tenant operation is performed during development.
