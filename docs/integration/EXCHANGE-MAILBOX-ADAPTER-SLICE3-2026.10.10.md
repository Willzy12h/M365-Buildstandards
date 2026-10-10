# INT-088 slice 3: the shipped read-only mailbox adapter (Claude, 10 October 2026)

This is the third of the four source PRs that INT-088 ([READ-ONLY-RUNNER-CONTRACT-2026.10.08.md](READ-ONLY-RUNNER-CONTRACT-2026.10.08.md)) asks for: **the first shipped adapter**, run only through the owned runner from slice 2 ([EXCHANGE-REPORT-RUNNER-SLICE2-2026.10.10.md](EXCHANGE-REPORT-RUNNER-SLICE2-2026.10.10.md)).

Run is still not offered anywhere. The desktop Scripts page and `bdit scripts` keep showing only the 29 Copy-only items; the CLI lists the new item with "toolkit run only; no Copy form, and Run is not available in this build". Offering Run is slice 4, behind the experimental opt-in from #73. No permission, role, sign-in, consent or tenant access is added, and nothing has been run against a tenant.

## What it adds

| File | Content |
|---|---|
| `scripts/exchange-online/Get-MailboxSizeAndQuota.ps1` | The adapter body. It reads only through `Use-BditRead`: `Get-EXOMailbox` once, then `Get-EXOMailboxStatistics` per mailbox, and `-Archive` only where an archive exists |
| `scripts/exchange-online/Get-MailboxSizeAndQuota.json` | Schema 2 manifest `exo.mailbox-size-quota`: report `exo-mailbox-inventory`, the slice 2 runner pin, 10,000 rows, 1,800 seconds, View-Only Recipients or View-Only Organization Management |
| `scripts/registry.json` | Registry schema 2, with `manifestSchemaVersion` on every entry; 30 pinned items, one of them runnable |
| `build/Update-ScriptRegistry.py` | Writes registry schema 2 |
| `build/Test-ScriptLibrary*.ps1` | Skip the schema 2 item, which cannot run outside the runner; the 29 Copy items are checked as before |
| App and CLI | The Copy page and its search list schema 1 items only; the CLI labels the schema 2 item |

## How values are recorded

- **Identity.** A row is keyed by the Exchange mailbox GUID. A mailbox returned without one is not listed, and the section becomes Partial and says so. An Entra object ID that is not a GUID is left empty and makes the row Partial.
- **Each value group has its own read state.** Size, quotas and archive are Collected, Failed or NotAttempted on their own. A value that was not returned, or could not be read, is never written as zero or blank; the row becomes Partial and its error names each missing value.
- **Values are recorded exactly as Exchange returned them.** `Unlimited` stays `Unlimited`; byte parsing and eligibility are left to the #58 mailbox capacity evaluator.
- **Archive.** Only when Include archive is ticked. An archive exists only when Exchange returns a non-empty `ArchiveGuid`; a missing `ArchiveGuid` makes the archive state Failed, not "no archive".
- **Cap.** It asks for 10,001 mailboxes. More than 10,000 lists the first 10,000 and makes the section Partial.
- **Every section limitation is kept.** The cap, mailboxes without a GUID and unread row values each add their own sentence to the section error; one never replaces another.

## Validation

Synthetic stand-in module only (`tests/.../Fixtures/ReadRunner`). The new regressions cover the exact reads and their parameters, archive reads only where one exists, each unreadable value leaving its row Partial (quotas, archive presence, object ID, size, archive size), a missing GUID, a missing type, and the absence of a Copy form. The schema 2 admission tests now assert that the shipped library has exactly one runnable item.

## Review corrections

- **AST-20261010-20 (Astra, P2).** `Get-EXOMailbox` returns only its Minimum property set plus the properties asked for, and `ExchangeGuid` is not in Minimum, so a real tenant would have listed no mailboxes. The adapter now asks for `ExchangeGuid`. The stand-in module now returns only Minimum plus the requested properties whenever `-Properties` is passed; with that change all nine adapter cases failed on `c3d96f1` and pass now. The older synthetic slice 2 body asks for no properties and keeps the full shape.
- **AST-20261010-21 (Astra, P3).** A mailbox without a GUID overwrote the cap's section reason, so a truncated list no longer said so. Each limitation is now kept. `A_section_over_the_cap_with_a_mailbox_missing_its_guid_keeps_both_limitations` runs 10,001 mailboxes with one missing GUID and checks both reasons and 9,999 rows.

## Not verified

The adapter has not been run against a real tenant. The property names, the `ArchiveGuid` signal for archive presence and the run time for large tenants are from Microsoft's documentation, not observation. Live acceptance stays a human gate.
