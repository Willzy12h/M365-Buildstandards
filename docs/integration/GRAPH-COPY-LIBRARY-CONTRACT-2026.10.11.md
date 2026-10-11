# Proposed INT-099 — three copy-only Graph report scripts

Decision-only claim from freshly fetched integration 724d9e0. No implementation, new access, module installation, tenant query, authentication, version or publication. The master scope asks for licence/device/MFA library seeds; actual registry inspection shows 29 Exchange Copy entries and one runnable mailbox entry, while the three native Graph Reports already exist. Preserve those collectors and do not rebuild them.

Astra owns implementation under William’s sole-agent direction. This shared resource/Copy-wrapper contract must be independently reviewed and merged before dependent source; source development cannot grant access or approve live use.

## Proposal

Add graph.users-licences, graph.intune-devices and graph.mfa-registration as schema-1, read-only, **Copy/Save only** entries. Their forms use existing typed inputs and approval preview; PS 7 only, with each required preinstalled Microsoft.Graph module named explicitly. No Run button and no schema-2 Graph admission. Existing 29 manual Exchange entries, their bytes/pins and the registered Exchange runner stay unchanged.

The fixed Graph Copy wrapper uses an **already connected delegated Graph PowerShell session**. It never calls Connect-MgGraph, requests consent, installs modules, acquires a token, copies a cache or embeds a credential. If no suitable session exists it refuses before report reads and tells the engineer to use an independently authorised session or the native Tenant reports page. A new PowerShell process lacking that session also refuses; Copy/Save is not a claim of a runnable standalone session.

Open questions about new scopes, registrations or sign-in are not resolved by this proposal. The missing library functionality remains explicit until reviewed implementation, not a reason to invent authentication.

## Exact identity and access boundary

The preview binds expected tenant GUID, account object GUID, confirmed UPN and assessment client GUID, plus script/body/manifest pin, runtime, purpose, required scopes and typed values. Desktop may supply these from the current verified connection; CLI Copy must require explicit expected identifiers and label them intended claims, not a verified session. Do not silently choose an account or registration. New expected-ID CLI arguments and the typed Copy context are part of this reviewed contract; legacy Exchange Copy signatures/behaviour remain compatible.

At manual execution, the wrapper first requires exactly one suitable Get-MgContext result with delegated authentication, matching tenant/client/UPN and existing required scopes. Missing or ambiguous facts refuse, without report reads. A fixed GET to /me selecting id and userPrincipalName verifies the actual account object/UPN before any report read. Recheck context before and after each page and /me before accepting final output; a change refuses output rather than rebinding. Never read an access token, invoke interactive fallback, switch registration or turn an identity-query failure into empty results. SDK-managed silent renewal is not a promise of a universal zero-prompt flow.

The SDK session must already have the reviewed access. Required scope sets mirror the existing native registry: users/licences uses Organization.Read.All and User.Read.All; managed devices uses DeviceManagementManagedDevices.Read.All; MFA registration uses AuditLog.Read.All. The last remains proposed access where not already authorised; source does not grant it. Missing scope/role/module stays explicit and requires separately authorised setup. No Directory.Read.All, write scope, role assignment or application permission is added. Document affected existing assessment registration/purpose in APPLICATION-SETUP; the toolkit's broker/cache/Quick Connect remain unchanged.

## Narrow manifest and helper admission

Support microsoftGraph only for the three compiled schema-1 IDs above, mode readOnly, declared PS 7 runtime and the fixed Graph Copy wrapper. Unknown resource/ID combinations refuse. Schema-2 ScriptCatalogue/ScriptReadRequest/ReadRunner and Exchange-only Run validation continue rejecting Graph; no generic Run adapter or process execution is introduced. Existing Exchange registry/body/manifest bytes remain unchanged; adding pinned entries creates new registry bytes without modifying historical packaged releases.

Use a separately pinned fixed wrapper with Microsoft.Graph.Authentication preinstalled through the organisation's approved process. The candidate minimum is SDK 2.0.0; implementation must verify required API/parameter availability against current documentation and synthetic module tests before declaring that minimum supported. Module installation, execution-policy changes and application-control workarounds are absent. SDK/runtime roles and live behaviour remain unverified until accepted; official SDK guidance recommends PS 7. Windows PS 5.1 is not claimed for these new items.

Only the wrapper may issue Invoke-MgGraphRequest, with Method GET and compiled route/projection definitions. Do not add Invoke to the library body verb allowlist. Bodies use a fixed Get-BditGraphPage helper whose collection key is restricted to the selected compiled registration; there is no engineer-supplied URL or command. Routes: /me for identity; /subscribedSkus, /users and /users/{canonical user GUID}/licenseDetails for licensing; /deviceManagement/managedDevices for devices; /reports/authenticationMethods/userRegistrationDetails for MFA registration. No beta, unrelated entity route, POST, PATCH or DELETE.

Returned next links are untrusted. Require HTTPS graph.microsoft.com, default port, no credentials/fragment, the exact original registered collection path and only reviewed query keys. Refuse a path/host/query change; do not forward credentials elsewhere. Keep original select projection and bounded top. Use current existing Graph route/pagination semantics as the reference rather than inventing a permissive URL checker. A rejected/missing malformed page, failed identity verification or inaccessible read never certifies a complete empty result.

## Bounded report semantics and local outputs

Typed inputs: MaxRows 1–5,000 default 100 for licensing and 500 for devices/MFA; MaxPages 1–100 default 20; licensing MaximumDetailReads 0–256 default 100. Cap page size at 100, total SDK GET calls at 512 and retained UTF-8 output at 8 MiB. Stop between reads on elapsed 300 seconds; use a documented supported per-request timeout verified in implementation. Copy-only execution cannot promise termination of the engineer-owned host or an in-flight SDK call. If the selected SDK cannot enforce that request bound, keep output explicitly manual/unverified and document the limitation; never advertise owned-process guarantees. No retry after an interaction-required, identity or malformed-output failure.

Retain one row per exact entity ID; duplicates/malformed IDs remain Partial with an explicit reason, never display-name merging. A fully completed successful zero-row inventory differs from failed/NotAttempted reads. Preserve unknown/null/unreturned fields; empty arrays and false/zero require an actually returned value. Caps, missing fields, pagination failure and missing per-user details retain Partial/NotAttempted states. Local output includes expected/observed context claims, request bounds, start/end times, collection/row status, reasons and source URLs; these are unsigned manual observations, not the toolkit's strict report evidence. No import into deployment or mailbox-capacity paired evidence is introduced.

Users/licences: one user identity per row; observed assigned SKU/service-plan IDs and provisioning status, with subscription names joined only by exact SKU GUID. Per-user licenseDetails reads have their own state and 256-call cap; a skipped detail read does not imply no licence or eligibility. Preserve raw assigned arrays distinctly from successful licenceDetails. No licence purchase/assignment or Business Premium capacity inference.

Devices: exact managed-device/Entra IDs where returned, ownership, device/enrolment/management type, OS, compliance and last sync. Missing type or compliance stays unknown; no guessed physical form factor.

MFA: exact user ID/name, observed reported methods, registration/capability fields and read status. Registration is not enforcement or successful use. Never emit phone/email method values, secrets, recovery codes or inferred physical devices. Reuse native report field/provenance definitions for semantic consistency.

Copy/Save produces safely represented typed values through the existing binder and reviewed fixed wrapper. No Invoke-Expression or untrusted command interpolation. CSV/JSON are deliberate secret-safe local outputs using existing standalone script output conventions, with status/provenance retained; no HTML/Excel or immutable-store compatibility is claimed for these manual files. Native Reports remains the recommended integrated path with existing HTML/CSV/JSON/Excel and strict saved evidence. Neither copied bytes nor output presence prove a successful check.

## Implementation and proof required after decision merge

Add the three pinned manifests/bodies, fixed Graph Copy wrapper, compiled resource/ID admission, explicit typed Copy context and CLI arguments, existing desktop form/preview dispatch and permission/module guidance. Keep collection logic outside WPF. No new Graph collector, broker/cache, general runner, SDK install, live session or write.

Tests: exact valid context; missing/ambiguous SDK session and missing scopes/modules; tenant/client/UPN mismatch with zero report reads; /me account-object mismatch and failed verification; before/after context change; copied parameter injection/URL/command attacks; repeated/cross-host next links; cap, cancellation, deadline and inaccessible reads; successful empty versus unknown/failed; per-user licence detail cap and exact joins; device/method missing fields; local output statuses/no secrets; CLI expected-ID validation; all existing Exchange Copy/Run refusals and historical pins unchanged. Use PS parser and synthetic Graph SDK stand-ins only, not a real tenant. Actual native/PS/runtime, SDK behaviour, RBAC/consent and human/live acceptance remain distinct gates.

## References inspected/source of truth

- Existing GraphReportRegistry registrations/scopes/provenance and current native collectors.
- Existing ScriptCatalogue resource guard and ScriptCopy's fixed Connect-ExchangeOnline wrapper establish why this is a shared boundary change, not three ordinary script files.
- https://learn.microsoft.com/en-us/powershell/microsoftgraph/installation?view=graph-powershell-1.0 — inspected 11 October: recommends PS 7; SDK/modules must be preinstalled by an authorised operator. Do not follow its execution-policy installation advice inside this product.
- Existing native registry official Microsoft API references for users/licences, managed devices and userRegistrationDetails. Check those exact current APIs before implementation.

This proposal implements nothing and grants no live action. A reviewed merge, actual exact-head checks, subsequent source tests and independent review are still needed.
