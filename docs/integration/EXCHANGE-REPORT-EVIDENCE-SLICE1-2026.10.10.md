# INT-088 slice 1 — strict Exchange report evidence (Claude, 10 October 2026)

This is the first of the four source PRs that INT-088 ([READ-ONLY-RUNNER-CONTRACT-2026.10.08.md](READ-ONLY-RUNNER-CONTRACT-2026.10.08.md), merged in #61) asks for: **strict models, reader and store**. It adds no runner, no collector, no adapter, no UI, no CLI command, no permission and no tenant access. Nothing in the product can create one of these records yet, except a test.

## What it adds

| File | Content |
|---|---|
| `Core/Models/ExchangeReportEvidence.cs` | `exchangeReportEvidence` schema 1, its typed parameters and the first registered row type, `MailboxReportRow` |
| `Core/Reporting/ExchangeReportRegistry.cs` | Package-owned registrations. Adapter-to-resource mapping is exactly `exchangeOnline` → `ExchangeOnline` and `purview` → `Purview`; anything else refuses (CLA-20261008-76). One registration: `exo-mailbox-inventory` |
| `Engine/Reports/ExchangeReportEvidenceSchema.cs` | The strict, bounded reader and the sealing step |
| `Engine/Evidence/ExchangeReportEvidenceStore.cs` | Create-once storage under `tenants/<tenant>/exchange-report-evidence/` |

## What the reader refuses

- **Anything outside the registration.** An unknown report, a resource that is not the adapter's exact resource, an adapter version other than `registered-exchange-report/1`, source commands that differ in any way (order, casing, an extra command), parameters that differ in name, type, order or count, and sections that differ in ID or order. The registry comes from the package. A record cannot supply its own.
- **Any parameter value that is not in its single normalised form.** Booleans are `true` or `false`; integers are canonical decimals; dates are `yyyy-MM-dd`; text is bounded and has no control characters. Equal inputs therefore always record identical values.
- **Loose JSON.** Unknown fields, at the top level and in rows, are refused. So are missing required fields, duplicate property names, more than 32 MiB, and a changed record (the integrity digest is checked).
- **Incomplete provenance.** This covers:
  - Id, run ID and tenant must be canonical GUIDs.
  - The tenant must be the expected one.
  - Script, manifest and runner-template digests must be lower-case SHA-256.
  - Toolkit, module and runtime versions must be present and bounded.
  - Times must be UTC, with the end not before the start.
- **A live run without its two accounts.** A `live` record needs the initiating account's object ID and the UPN the module observed. A `historical` record may lack them, but any claim it does make must be well-formed. Imported output cannot become an authenticated live run.
- **A better state than the evidence supports.** This covers:
  - The record's status must equal `Overall` of its sections.
  - A Collected section can contain only Collected rows and has no error.
  - Failed and NotAttempted sections hold no rows.
  - Every unsuccessful row or section needs a reason.
  - Row identity is the exact canonical Exchange mailbox GUID, unique within the section.

## Mailbox value groups

Each group carries its own read state: primary size, the three quotas together, archive state, and archive size.

- A group that was read records every value.
- A group that was not read records none, so a failed read can never look like zero or like a known quota.
- `Unlimited` is kept as Exchange's own text.
- A recorded value must be readable: empty or whitespace-only type, size, quota, archive or address text is refused in any row (AST-20261010-01). Readable raw values, including zero and `Unlimited`, are kept exactly as returned; a value that could not be read is absent, with its group's read state and the row's reason.
- `HasArchive` is set only by a successful archive-state read, so absence of an archive is never inferred from a failed request.
- Archive size and archive quota apply only to a mailbox whose archive was read as present.
- A run with `IncludeArchive=false` records no archive state at all.
- A Collected mailbox row needs its type, size and quotas. In an archive run it also needs its archive state, and its archive size when the archive is present.
- `ExternalDirectoryObjectId` is optional and, when present, a canonical GUID. Shared, room and equipment mailboxes stay distinct mailbox rows with or without it.
- Recoverable Items are not part of this registration, and no OST or PST claim is made.

## Storage boundaries

- Records are tenant-partitioned and created atomically.
- An existing ID is never replaced.
- A file whose content names another record ID is refused on load.
- The folder sits apart from Graph report evidence (`report-evidence`), snapshots and the configuration Exchange capture. Neither report reader accepts the other kind.
- The support bundle does not include these files.
- Protected workspace backups include them like other client evidence under `data/`.

No report record can load as configuration before-evidence or authorise deployment. Nothing in deployment or assessment reads this folder.

## Proof

- `ExchangeReportEvidenceTests`: 31 synthetic cases. The full engine suite (1,406 tests) passes locally with warnings as errors.
- The tests were **mutation-checked** locally against 15 reader rules. Each rule was disabled in turn, and the intended test failed every time. The rules were:
  - each value-group direction;
  - archive existence; archive presence; archive values outside an archive run;
  - Collected-row completeness; archive-run completeness;
  - source commands; overall status; Boolean normalisation; live accounts;
  - a partial row in a Collected section; row-identity uniqueness; resource; row reason.
- The first Boolean-normalisation test passed for the wrong reason, because an archive rule refused the record first. It now runs without archives so that only the parameter rule can refuse it.

## Review corrections

- **AST-20261010-01 (Astra, P2).** Blank size, quota and mailbox-type values could be sealed as Collected. The reader now refuses any empty or whitespace-only mailbox value, and a Collected row needs a readable type. Thirteen negative cases (type, primary size, each quota, archive size, archive quota, address; empty and whitespace) and a partial-row case fail on `9cb3b05` and pass after the fix; a positive control keeps zero and `Unlimited` exactly as returned.

## Not done here, and still required

- **Slice 2:** the owned runner, with fake-process and module regressions.
- **Slice 3:** the registered Exchange adapters, which make the first real `exo-mailbox-inventory` projection.
- **Slice 4:** the Reports and Scripts UI.
- **Live acceptance:** none. Every live, role and module-output claim in the registration's limitation text remains unverified until William accepts it in a test tenant.
