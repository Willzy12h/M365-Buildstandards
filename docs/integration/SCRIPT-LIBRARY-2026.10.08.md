# Scripts & Reports library — first slice (INT-080, proposed)

William asked on 2026-10-08 for an organised library of cloud and on-premises scripts that engineers can run against the connected tenant or copy, with the tenant highlighted, a confirmation before every run and required fields checked. The full design and the 403-item catalogue are in the shared design document linked from PR #46. William assigned the build to Claude while Astra's response was pending, so this PR is held for Astra's review before merge.

## What this slice does

| Part | Where | Behaviour |
|---|---|---|
| Library files | `scripts/<category>/*.json`, `*.ps1` | One manifest per script. Scripts are plain ASCII so Windows PowerShell 5.1 reads them identically. |
| Registry | `scripts/registry.json` | Pins each manifest and script SHA-256. `build/Update-ScriptRegistry.py` re-pins after a reviewed change; CI runs it with `--check`. |
| Catalogue | `Engine/Scripts/ScriptCatalogue.cs` | Loads only registered, contained, pinned files from the engine assembly. Any problem refuses the whole library. Read-only bodies may only use read verbs (Get, Search, Select, Where, ForEach, Sort, Group, Measure, Write, ConvertTo, ConvertFrom, Test, Format) plus `Set-StrictMode`, and no dynamic code. |
| Inputs | `Engine/Scripts/ScriptInputs.cs` | Typed binding. Blank optional fields are left out; defaults are visible in the manifest; several values split on new lines, commas or semicolons; control and hidden formatting characters are refused; rules check "at least one of" and date ranges. |
| Copy | `Engine/Scripts/ScriptCopy.cs` | Header with purpose, needs and script hash; `#requires` for PowerShell and the module; Exchange Online sign-in; refusal of an ambiguous connection or another tenant; every value as a single-quoted literal (typographic quotes doubled too); the body unchanged; CSV of the declared columns; disconnect in `finally`. No credentials, installation or policy change. |
| CLI | `bdit scripts`, `bdit script --id <id> [--copy]` | Offline. Lists items, shows a form's fields and rules, and prints or saves (never overwriting) the Copy script. |

## Additive manifest fields proposed by INT-080

| Field | Meaning |
|---|---|
| `area`, `keywords` | Grouping and search terms for the library page. |
| `rules` | `atLeastOne` names two or more optional fields; `dateRange` names a start and end date with `maximumDays` and optional `maximumAgeDays`. |
| `parameters[].label`, `format`, `maxLength`, `maxItems`, `minimum`, `maximum`, `allowed`, `default` | Form label and typed checks. `array: true` takes several values. |

Supported parameter types: string, boolean (tick box), integer, date, GUID and enum. Arrays are limited to string and enum.

## First items

All read-only, all Exchange Online, all unverified: mailbox inventory, quota and 100 GB audit, mailbox permissions (Full Access, Send As, Send on Behalf as tick boxes), mailboxes a user can access, calendar permissions, inbox rules, mail forwarding, shared mailboxes, message trace (up to 90 days in 10-day queries, at least one of sender, recipient, subject or message ID) and unified audit search.

## Evidence

- Source and synthetic: engine tests cover strict loading, pins, injected writes, typed inputs and Copy literals with hostile values.
- `build/Test-ScriptLibrary.ps1` parses every body under PowerShell 5.1 with an allow-list and checks generated Copy scripts hold only constant literals and an unchanged body.
- `build/Test-ScriptLibraryStubs.ps1` runs every Copy script against a synthetic stand-in module: rows match the manifest columns, the session disconnects, and a different tenant is refused with nothing written.
- Live: none. Each item stays "not yet tested in a tenant" until William runs it.

## Next slices

1. Desktop Scripts & Reports page with the tenant banner, form, confirmation and Copy.
2. Run: an owned PowerShell process per library session with one Exchange sign-in, tenant check before each run, bounded output and report evidence (needs INT-071's store from PR #44).
3. More packs from the catalogue, then change items behind preview, typed confirmation and undo, which needs its own decision because INT-072 keeps changes copy-only.
