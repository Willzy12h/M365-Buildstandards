# Proposed INT-099 — three copy-only Graph report scripts

Decision-only claim from freshly fetched integration 724d9e0. No implementation, new access, module installation, tenant query, authentication, version or publication. The master scope asks for licence/device/MFA library seeds; actual registry inspection shows 29 Exchange Copy entries and one runnable mailbox entry, while the three native Graph Reports already exist. Preserve those collectors and do not rebuild them.

Astra owns implementation under William’s sole-agent direction. This shared resource/Copy-wrapper contract must be independently reviewed and merged before dependent source; source development cannot grant access or approve live use.

## Proposal

Add graph.users-licences, graph.intune-devices and graph.mfa-registration as schema-1, read-only, **Copy/Save only** entries. Their forms use existing typed inputs and approval preview; PS 7 only, with each required preinstalled Microsoft.Graph module named explicitly. No Run button and no schema-2 Graph admission. Existing 29 manual Exchange entries, their bytes/pins and the registered Exchange runner stay unchanged.

The fixed Graph Copy wrapper uses an **already connected delegated Graph PowerShell session**. It never calls Connect-MgGraph, requests consent, installs modules, acquires a token, copies a cache or embeds a credential. If no suitable session exists it refuses before report reads and tells the engineer to use an independently authorised session or the native Tenant reports page. A new PowerShell process lacking that session also refuses; Copy/Save is not a claim of a runnable standalone session.

Open questions about new scopes, registrations or sign-in are not resolved by this proposal. The missing library functionality remains explicit until reviewed implementation, not a reason to invent authentication.
