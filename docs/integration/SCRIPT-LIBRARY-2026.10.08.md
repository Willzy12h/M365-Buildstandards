# Scripts & Reports library — first slice (INT-080, proposed)

William asked on 2026-10-08 for an organised library of cloud and on-premises scripts that engineers can run against the connected tenant or copy, with the tenant highlighted, a confirmation before every run and required fields checked. The full design and the 403-item catalogue are in the shared design document linked from PR #46. William assigned the build to Claude while Astra's response was pending, so this PR is held for Astra's review before merge.

## What this slice does

| Part | Where | Behaviour |
|---|---|---|
| Library files | `scripts/<category>/*.json`, `*.ps1` | One manifest per script. Scripts are plain ASCII so Windows PowerShell 5.1 reads them identically. |
| Registry | `scripts/registry.json` | Pins each manifest and script SHA-256. `build/Update-ScriptRegistry.py` re-pins after a reviewed change; CI runs it with `--check`. |
| Catalogue | `Engine/Scripts/ScriptCatalogue.cs` | Loads only registered, contained, pinned files from the engine assembly. Any problem refuses the whole library. Read-only bodies may only use read verbs (Get, Search, Select, Where, ForEach, Sort, Group, Measure, Write, ConvertTo, ConvertFrom, Test, Format) plus `Set-StrictMode`, and no dynamic code. |
| Inputs | `Engine/Scripts/ScriptInputs.cs` | Typed binding. Blank optional fields are left out; defaults are visible in the manifest; several values split on new lines, commas or semicolons; control and hidden formatting characters are refused; rules check "at least one of" and date ranges. |
| Copy | `Engine/Scripts/ScriptCopy.cs` | Header with purpose, needs and script hash; `#requires` for PowerShell and the module; Exchange Online sign-in; refusal of an ambiguous connection, another tenant or, when an account was confirmed, another signed-in account (case-insensitive, checked before any value or body is read); every value as a single-quoted literal (typographic quotes doubled too); the body unchanged; CSV of the declared columns; disconnect in `finally`. No credentials, installation or policy change. |
| CLI | `bdit scripts`, `bdit script --id <id> [--copy]` | Offline. Lists items, shows a form's fields and rules, and prints or saves (never overwriting) the Copy script. |
| Desktop page | `App/ViewModels/ScriptsViewModel.cs`, `Views/ScriptsView.xaml`, `Views/ScriptCopyDialog.xaml` | **Scripts & Reports**: the library grouped by area, search, the tenant banner, the generated form with live checks and a command preview, and Copy or Save as .ps1 after a confirmation. No Run. |

## Additive manifest fields proposed by INT-080

| Field | Meaning |
|---|---|
| `area`, `keywords` | Grouping and search terms for the library page. |
| `rules` | `atLeastOne` names two or more optional fields; `dateRange` names a start and end date with `maximumDays` and optional `maximumAgeDays`. |
| `parameters[].label`, `format`, `maxLength`, `maxItems`, `minimum`, `maximum`, `allowed`, `default` | Form label and typed checks. `array: true` takes several values. |

Supported parameter types: string, boolean (tick box), integer, date, GUID and enum. Arrays are limited to string and enum.

## First items

All read-only, all Exchange Online, all unverified: mailbox inventory, quota and 100 GB audit, mailbox permissions (Full Access, Send As, Send on Behalf as tick boxes), mailboxes a user can access, calendar permissions, inbox rules, mail forwarding, shared mailboxes, message trace (up to 90 days in 10-day queries, at least one of sender, recipient, subject or message ID) and unified audit search.

## Desktop page

**Scripts & Reports** sits under Evidence and reference in the navigation. Nothing on it signs in, reads the tenant or runs a script.

- **List.** Items are grouped by area and sorted by name. Search uses `ScriptCatalogue.Search`: every word must appear in the name, ID, area, description or keywords. Each item shows its description, a "Read only" badge, the modules and roles it needs, and "Live status: not yet tested in a tenant". A library that fails its pins is refused whole and the page says so.
- **Tenant banner.** Always at the top: the selected client's name, tenant ID and colour band, with "Online scripts — copied scripts sign in to this tenant only". The target is the client selected on Connect (the profile), never merely whatever session is open. With no client selected the banner is neutral, says to select one, and Copy is off.
- **Form.** Generated from the manifest: a text box for string, GUID, integer and date (validated as YYYY-MM-DD, not a date picker, so the value is unambiguous in every regional format), a tick box for boolean, a pick list for an enum (tick boxes when several may be chosen), and a several-line box for array fields with "one per line, comma or semicolon". Labels, help, required markers and the default that applies are shown. Every change is bound with `ScriptInputs.Bind`; each problem appears beside its field and in a list above the buttons, and Copy and Save stay off until the binding is valid. A tick box always passes true or false, so unticking a box that defaults to ticked leaves it out. The command preview shows the bound arguments exactly as the copied script writes them (`ScriptCopy.Literal`).
- **Confirmation.** **Copy script…** and **Save as .ps1…** first show a dialog restating the tenant name and ID on its colour band, the item, "Read only. Makes no changes." and every value, with an unticked confirmation box; the confirm button is not the default button. The review carries a fingerprint of the tenant, item, pinned hashes and arguments: if any of them changes before generation, nothing is produced. The script is `ScriptCopy.Generate` with the selected client's tenant ID and name, and the connected account only when the session is for that tenant and the account is a valid sign-in name. Save writes UTF-8 with a byte order mark to a new `.ps1` file and refuses an existing name. Generation and checks live in `ScriptsViewModel`, not in code-behind.
- **Not here yet.** Run, by design, is absent rather than shown disabled. It needs the owned PowerShell session in the next slice.

## Pending decision: client colours

William has not yet chosen how a client's colour is set (chosen per client, imported, or derived). Until he does, `Core/Safety/TenantColour.cs` derives a stable colour from the tenant ID alone: SHA-256 of the canonical GUID picks one of eight dark colours, each carrying white text at 4.5:1 or more and standing at 3:1 or more against the page (tested). Red and amber are excluded because they already mean danger and warning. Nothing is stored and the client profile schema is unchanged. The colour is only a recognition cue beside the tenant's name and ID. `TenantColours.For` is the single place it is chosen, so a profile colour can replace it later without touching the pages; that change needs its own decision because it alters the profile schema.

## Review corrections (Astra, 8 October 2026)

Astra's independent review of PR #46 (AST-20261008-05 to -09) was fixed by Claude in `27c40b0`. Each fix has a synthetic case in `build/Test-ScriptLibraryStubs.ps1` that failed against the reviewed head `3d993c9` and passes now.

| ID | Correction |
|---|---|
| AST-20261008-05 | The quota audit never drops a mailbox it cannot measure. An Unlimited or unreadable quota, or an unreadable size, is listed with `Status` Unknown and a `Reason`, with default inputs as well as IncludeAll, and the run warns `BDIT:UNKNOWN`. Measured mailboxes are `AtOrAboveThreshold` or `BelowThreshold`. |
| AST-20261008-06 | Every true/false column in the ten scripts is written as True, False or Unknown; a value Exchange did not return is no longer cast to False. Mailbox inventory and shared mailbox rows say what was not returned in `Notes`. An inbox rule whose delete or mark-as-read action is Unknown stays in an "only risky rules" list, and an entry whose inheritance is Unknown is never filtered out as inherited. |
| AST-20261008-07 | The Copy script compares the signed-in account with the account the engineer confirmed, case-insensitively, after the tenant check and before any value or body is read. A mismatch is refused with the two accounts named; nothing is read or written and the session is disconnected. With no confirmed account, the account chosen at the Microsoft prompt is used. The desktop Copy action still makes no tenant call. |
| AST-20261008-08 | "Mailboxes a user can access" matches the user's exact identities (sign-in name, primary SMTP address, distinguished name, object IDs), never a display name. An entry naming the user only by name is `Unresolved`, a deny entry `Denied`, and an entry that does not say whether it allows or denies `Unknown`; only an allow entry on an exact identity is `Granted`. It lists direct entries, not effective access: group and inherited rights are not resolved to the user. |
| AST-20261008-09 | Unified audit search reads one more page when the row limit falls exactly at the end of a page; if that page holds records the result is marked `BDIT:PARTIAL`. Send As entries are requested one past the 5,000 limit, and a full result is marked partial. |

## Second Exchange Online pack

Eleven more read-only Exchange Online items, on branch `claude/scripts-exo-pack2-2026-10-08`. Each follows the rules from Astra's review of #46: a value Exchange did not return is Unknown and never cast to False; an identity is never joined on a display name or Name; a bounded or truncated read warns `BDIT:PARTIAL`; anything that could not be measured warns `BDIT:UNKNOWN`; scripts are ASCII, Windows PowerShell 5.1 compatible and use only Get- reads; nothing installs a module or changes execution policy. Every manifest lists its roles and limitations and is live-unverified. The roles follow Microsoft's documentation and have not been confirmed in a tenant.

| ID | Name | Reads | Roles |
|---|---|---|---|
| `exo.group-members` | Group members and owners | `Get-EXORecipient`, `Get-DistributionGroupMember`, `Get-DistributionGroup`, `Get-UnifiedGroupLinks` | View-Only Recipients, View-Only Organization Management |
| `exo.transport-rules` | Mail flow rules | `Get-TransportRule` | View-Only Organization Management |
| `exo.connectors` | Mail connectors | `Get-InboundConnector`, `Get-OutboundConnector` | View-Only Organization Management |
| `exo.domains-dkim` | Accepted domains and DKIM | `Get-AcceptedDomain`, `Get-DkimSigningConfig` | View-Only Organization Management, Security Reader |
| `exo.mobile-devices` | Mobile devices per mailbox | `Get-EXOMailbox`, `Get-MobileDevice`, `Get-MobileDeviceStatistics` | View-Only Recipients, View-Only Organization Management |
| `exo.mailbox-audit` | Mailbox auditing settings | `Get-OrganizationConfig`, `Get-AdminAuditLogConfig`, `Get-EXOMailbox`, `Get-MailboxAuditBypassAssociation` | View-Only Organization Management, View-Only Audit Logs |
| `exo.mailbox-holds` | Holds and retention per mailbox | `Get-OrganizationConfig`, `Get-EXOMailbox` | View-Only Recipients, View-Only Organization Management |
| `exo.protection-policies` | Anti-spam, phishing and malware policies | `Get-HostedContentFilterPolicy`/`Rule`, `Get-HostedOutboundSpamFilterPolicy`/`Rule`, `Get-AntiPhishPolicy`/`Rule`, `Get-MalwareFilterPolicy`/`Rule` | Security Reader, View-Only Organization Management |
| `exo.resource-mailboxes` | Room and equipment booking | `Get-EXOMailbox`, `Get-CalendarProcessing` | View-Only Recipients, View-Only Organization Management |
| `exo.send-on-behalf` | Send on Behalf delegates | `Get-EXOMailbox`, `Get-EXORecipient` | View-Only Recipients, View-Only Organization Management |
| `exo.archive-mailboxes` | Archive mailbox status | `Get-OrganizationConfig`, `Get-EXOMailbox` | View-Only Recipients, View-Only Organization Management |

What each item decides, and what it leaves Unknown:

- **Identities.** Group members are listed with the address and object ID Exchange returned; an entry with neither is Unresolved. Distribution group owners and Send on Behalf delegates can come back as names, so each is looked up once and is Resolved only when Exchange finds exactly one recipient whose primary SMTP address, distinguished name, object ID or GUID is that value. A Name, alias or display name, several matches or none is Unresolved with no address, and the run warns. Dynamic distribution groups are NotExpanded. Resource delegates are shown as returned and not resolved.
- **Holds.** HoldStatus is MailboxHold only when a hold is set on the mailbox. An in-place hold entry starting with a minus sign is an exclusion from an organisation-wide policy, not a hold. Organisation-wide holds are shown on every row but not worked out per mailbox, and hold IDs are not resolved to policy names (that needs Security and Compliance PowerShell).
- **Protection.** A custom policy with no rule is NoRule. A preset (Standard or Strict) policy is Preset: its preset rule is not read, so who it applies to is not shown. A policy with no rule that Exchange does not mark as custom or preset is Unknown, never NoRule. Safe Links and Safe Attachments are not included.
- **Mail flow.** BypassesSpamFiltering is True only for the bypass spam filtering action (spam confidence level -1). Connector TLS returned empty is NotSet; Exchange's behaviour without a setting is not inferred. DKIM is read from Exchange only, with no DNS query: the selector CNAMEs are what Exchange expects, not proof they are published.
- **Mobile devices.** Each mailbox's devices are read up to the row limit plus one, so a longer list is marked partial; Exchange otherwise stops at a default size without saying so. A device whose last sync cannot be read, including when its statistics read fails, is Unknown with the reason and is always kept by the stale filter.
- **Archive.** Archive size is not read: it needs one statistics read per mailbox, which this item does not make, so the only bound is the mailbox limit (2,000 by default, 10,000 at most), marked partial when reached. HasArchive comes from the archive GUID; when ArchiveStatus disagrees, the row says so. The archive quota is a configured value, not an entitlement.
- **Auditing.** Finding NoneFound is not proof that events are recorded: with organisation auditing on by default, a mailbox's own AuditEnabled can read False and still be audited. Unified audit ingestion that cannot be read (for example without an audit role) is Unknown on every row.

The previous agent's work was saved unvalidated in `a247bca`. Reviewing it found, and this branch fixed, four places where it fell short of the rules above. Each has a synthetic case that fails against `a247bca` and passes now: owners and delegates were Resolved on a Name, alias or Identity; an exclusion entry counted as a hold; a preset policy with no rule was reported as applying to no one; and a mobile device read was unsized, so Exchange's default size could cut it short silently. Mail flow rules, connectors and protection policies now also warn `BDIT:UNKNOWN` whenever a row holds an Unknown value, as the other items do.

## Not implemented in this slice

- Everything here is manual and unverified live: each item is copied and run by an engineer, and none has been run in a tenant.
- No entitlement validation. A configured quota, including 100 GB, is not proof of a licence or archive entitlement.
- No integrated report execution: there is no Run, no owned PowerShell session and no report evidence store yet.
- No archive mailbox size reporting. Archive status shows the configured archive quota, which is not an entitlement.
- The library covers twenty-one Exchange Online items (the first ten and the second pack of eleven), not the full reporting scope in the design catalogue.
- **Timeouts.** `limits.timeoutSeconds` in each manifest is not enforced by the copied script, which runs until it finishes or the engineer stops it. Enforcing it is a limit for the future runner (next slice 2), not a property of the Copy script.

These stay open in the [feedback register](PRODUCT-FEEDBACK-REGISTER.md) and are not closed by the synthetic evidence below.

## Evidence

- Source and synthetic: engine tests cover strict loading, pins, injected writes, typed inputs and Copy literals with hostile values. For each second-pack item they check read-only and unverified status, documented roles and limitations, the absence of PowerShell 7-only operators and write or install commands, the `BDIT:UNKNOWN` and `BDIT:PARTIAL` warnings, default binding and an unchanged body in the Copy script; that owner and delegate lookups accept only exact identities; and each form's typed checks.
- `build/Test-ScriptLibrary.ps1` parses every body under PowerShell 5.1 with an allow-list and checks generated Copy scripts hold only constant literals and an unchanged body.
- `build/Test-ScriptLibraryStubs.ps1` runs every Copy script against a synthetic stand-in module: rows match the manifest columns, the session disconnects, and a different tenant or a different account in the same tenant is refused with nothing read (every stand-in read is logged) and nothing written. Further scenarios cover the review corrections above: unmeasurable quotas, true/false/not-returned values in six scripts, same-display-name and deny entries, an audit page ending exactly at the limit and a saturated Send As result. The second pack adds scenarios for each new item: exact, Name-only, display-name, ambiguous and missing identities; not-returned flags, settings, owner lists and delegate lists; dynamic groups; a recipient that is not a group or not a resource; hold exclusions; preset and unmarked policies; undated devices and a failed statistics read; and every row or member limit, each with its warning. The fake module answers the new cmdlets, and the wrong-tenant and wrong-account refusals with zero reads run for all twenty-one items.
- Desktop page: view-model tests in `tests/BDIT.TenantToolkit.App.Tests/ScriptsPageTests.cs` (search and grouping, required-field gating, blank optional fields, multi-select binding, the banner and its colour, confirmation before generation, a stale confirmation refused, the generated tenant and body, Save never overwriting). They target net10.0-windows and are compiled, not run, on Linux. Colour derivation and contrast are tested on Linux in `TenantColourTests`. The offline interface harness renders the page at all three sizes, presses Copy and Save with stand-in prompts, and lays out and exercises the confirmation dialog; it runs in Windows CI.
- Live: none. Each item stays "not yet tested in a tenant" until William runs it.

## Next slices

1. ~~Desktop Scripts & Reports page with the tenant banner, form, confirmation and Copy.~~ Done on this branch; see "Desktop page".
2. Run: an owned PowerShell process per library session with one Exchange sign-in, tenant check before each run, bounded output and report evidence (needs INT-071's store from PR #44).
3. More packs from the catalogue, then change items behind preview, typed confirmation and undo, which needs its own decision because INT-072 keeps changes copy-only.
