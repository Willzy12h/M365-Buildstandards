# M365 BuildStandard Tool

The Blue Diamond IT Build Standard, applied to a Microsoft 365 tenant by an engineer at a desktop. It reads a
tenant's configuration, reports it against the standard, and — only when you explicitly approve each change —
creates the missing policies as inert candidates for you to review before anything takes effect.

**This is a preview build and is not signed.** It has not been accepted against a live tenant. Read
`docs/LIVE-VALIDATION.md` before you point it at anything that matters.

## Verify this package before you run it

`SHA256SUMS.txt` lists every file. The ZIP's own `.sha256` sidecar covers the archive. Check both before first use;
they are what tells you the package reached you unmodified.

```powershell
Get-FileHash .\M365-BuildStandard-Tool-<version>-win-x64.zip -Algorithm SHA256
```

Windows marks downloaded archives as blocked. Unblock the ZIP before extracting, or the application will not start:

```powershell
Unblock-File .\M365-BuildStandard-Tool-<version>-win-x64.zip
```

## Run it

Extract the whole folder and run `Start.cmd`, or `app\BDIT.TenantToolkit.App.exe` directly. Nothing needs
installing — the .NET runtime is included. If application control is enforced on the machine, allow
`app\BDIT.TenantToolkit.App.exe` by path or hash; this package contains exactly one executable.

`Start-Diagnostics.cmd` runs the same application with diagnostic logging. `Open-Evidence.cmd` and
`Open-Reports.cmd` open the folders where captures and reports are written.

## What is in the box

| Folder | Holds |
| --- | --- |
| `app\` | The application and its runtime. |
| `standards\` | Every published Build Standard release, as data. The application reads these; do not edit them. |
| `config\` | Settings templates. Client identifiers are blank until you complete application setup. |
| `docs\` | The documents below. |
| `data\`, `logs\`, `reports\` | Empty. Captures, logs and reports are written here as you work. |

`data\` will hold real tenant evidence once you connect. Keep it secure and never commit it anywhere.

## Where to start

1. **`docs\TESTING-THIS-BUILD.md`** — what you can review with no tenant at all, and the order to follow when you
   do connect one. Start here.
2. **`docs\APPLICATION-SETUP.md`** — creating or validating the two application registrations, and administrator
   consent.
3. **`docs\LIVE-VALIDATION.md`** — the checks to complete in an authorised test tenant before any client tenant.
4. **`docs\AUTOMATION-COVERAGE.md`** — every control, what is automated, and what still needs an engineer.

Then, as you need them: `BUILD-STANDARD-SUMMARY.md` for what the standard requires, `POLICY-AUTOMATION-CODE.md` and
`DEVICE-AUTOMATION.md` for the settings each recipe writes, `EQUIVALENCE-SIGNALS.md` for how the report decides an
existing policy is equivalent, `LICENSING.md` for the licence counts, `RECOVERY.md` for undoing a recorded change,
and `UNRESOLVED-WRITES.md` when a write's outcome is uncertain.

## What it will not do

Assessment holds no write permission at the token level. Conditional Access policies are created disabled, Intune
policies unassigned, groups empty and named locations untrusted — activating or assigning any of them is a separate
decision you make explicitly. The toolkit never adopts an existing object because its name matches, never modifies
an object it did not create and record, and never retries a write whose outcome it could not confirm. A read that
failed is reported as unknown, never as absent.

## Development record

The source, its full history and the internal review records are at
<https://github.com/Willzy12h/M365-Buildstandards>. `CHANGELOG.md` in this package lists what changed in each
release.
