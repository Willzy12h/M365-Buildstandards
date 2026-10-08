# Historical toolkit source located — 8 October 2026

The uploaded `BDIT-Portable-Tools-1.4.0-rc.1-Source-and-Tests.zip` contains a separate PowerShell Engineer Console. Archive SHA-256: `b8e0625913a1dc3400548d221b1913fcb9a6c23575419b58edfb201296858536`. This is reference source, not evidence that these features are already integrated into BuildStandard or that a newer local revision has been inspected.

Source root: `BDIT-Portable-Tools-Source-1.4.0-rc.1/BDIT-Engineer-Console/Modules/`. Modules include Exchange-OnPrem, Exchange-Online, Intune-Package-Builder, M365-Investigation, Mail-Investigation and Script-Runner. Only source was inspected; nothing was executed and no tenant data was required.

| Inspected capability | Evidence / reuse candidate | Current limitation |
|---|---|---|
| M365 user identity and licences | `Invoke-BDITM365Investigation.ps1`: user ID-based queries and `licenseDetails` | Existing reference implementation; integration and current Microsoft behaviour need separate validation |
| Intune device investigation | Same file: paginated `managedDevices`, bounded at 1,000 items | Partial result handling and source limits must remain explicit |
| Sign-in investigation | Same file: date-bounded `auditLogs/signIns`, capped at 5,000 | Requires current permission/licence/retention review; not an empty-success fallback |
| Exchange mailbox and permissions | `Invoke-BDITExchangeOnlineInvestigation.ps1`: `Get-Mailbox`, `Get-MailboxPermission`, `Get-InboxRule` | Read projection is a reference; not proof of mailbox quota or entitlement reporting |
| Message trace | Same file: `Get-MessageTraceV2`, capped at 5,000 | Current module/RBAC requirements and live acceptance remain unverified |
| Additional Exchange functions | Libraries contain mailbox statistics and calendar permissions, as well as write commands | Inspect each function before reuse; writes cannot bypass the existing no-integrated-Exchange-write decision |

Reuse bounded read/report patterns where suitable, through BuildStandard's shared engine and reviewed read-only adapters. Preserve existing broker/cache and authentication boundaries rather than importing a second implementation. Device diagnostics/repair, endpoint packaging and on-premises Exchange remain separate. No verified 100 GB eligibility implementation has been established by this inspection.

The linked “Technical Project Ideas” chat corroborates the separate console and archive history. Its instructions are historical reference material, not new authorisation. No access to the user's Windows filesystem or to newer local console source is claimed.
