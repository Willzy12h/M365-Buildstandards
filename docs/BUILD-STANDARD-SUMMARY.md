# Build Standard 2026.09.12 - summary

The loaded catalogue is the source of truth: 96 controls across Entra, Intune, Exchange and Purview, with 61 creation recipes. Open **Build Standard > Export engineer standards and manual guide** for the complete settings and manual procedures in HTML or Markdown. [Export instructions](ENGINEER-DOCUMENTS.md) and [coverage by control](AUTOMATION-COVERAGE.md) describe the implemented scope. Older published catalogue files are unchanged.

## Required outcomes

- Identity: passkeys including Authenticator, phishing-resistant administrator MFA, system-preferred MFA, Authenticator registration campaign, user self-consent off and admin consent workflow with named reviewers. SSPR stays off and is checked only.
- Offices: individually named public-IP locations, created untrusted. CA creation remains disabled, with two emergency accounts and the current operator excluded.
- Devices: everyday Entra join with automatic MDM enrolment; Autopilot device preparation with a separately reviewed provisioning owner on an empty group. Classic registration/ESP are deferred. Reset this PC is untouched.
- Windows: ESET antivirus/EDR; no managed firewall; BitLocker recovery rotation; Hello minimum eight with lowercase required and digits allowed; long paths and web sign-in. Native supported settings implement UK time zone, Storage Sense, eligible-edition consumer features, Windows settings backup, mains sleep and conditional removal of consumer Copilot only. Chrome SSO, UK setup, file extensions, Files On-Demand, a user-changeable starting taskbar and fast startup have explicit manual procedures where the native contract is unconfirmed. Microsoft Store is unrestricted by this standard; Microsoft 365 Copilot stays.
- Compliance: mark non-compliant after five days on every policy, with no email notifications. Autopatch is intended for all eligible devices, with manual prerequisite/licence and conflict review; category enrolment alone does not prove rollout.
- Mobile: eight Microsoft apps on each platform, created unassigned; Android requires Managed Google Play. No ABM, VPP or supervised-only requirement.
- Exchange/Purview: narrow own-domain SPF-pass spam bypass; Standard preset for all recipients with manually maintained impersonation users; blocked external forwarding, organisation SMTP AUTH off, external tags and auditing on; DKIM with actual DNS targets; DMARC and audit retention reported. Exchange execution uses the reviewed manual fallback, not an automatic write transport. The bypass remains a disabled audit candidate pending SPF trust validation.

## Candidate state and production state

| Control type | Toolkit candidate or reviewed workflow | Production acceptance |
| --- | --- | --- |
| Conditional Access | Disabled; exact targeting and exclusions retained | Separate activation after recovery-access and policy-impact review |
| Intune policy/app | Unassigned; packages remain separately reviewed | Pilot, licence/edition, assignment and actual device/app checks |
| Prerequisite group/location | Empty group / untrusted public-IP location | Deliberate membership and scope review; no adoption by name |
| Tenant-wide identity settings | Separate typed-confirmed, journalled action preserving unrelated settings | Exact read-back plus manual scope and functional checks |
| Exchange/Purview | Strict read-only import and inert selected proposal | Engineer-owned change process with before/after evidence; no automatic retry |

Retired in .12: SEC-WIN-001, CMP-WIN-002, SEC-WIN-003. Deferred pending recommendation: ENR-003, ENR-004, SEC-WIN-002. Branding is an optional discussion, not a control. Remote Help, Quick Assist, Wi-Fi profiles, Universal Print, DLP, retention writes and Outlook automatic account configuration are not implemented.

New delegated scopes and renewed consent for both applications are documented in [application setup](APPLICATION-SETUP.md). Licence counts and policy read-back do not prove entitlement or effective behaviour. [Testing this build](TESTING-THIS-BUILD.md) separates offline evidence from the maintainer's future authorised acceptance; no live validation was performed for this release.
