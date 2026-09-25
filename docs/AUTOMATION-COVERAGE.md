# Automation coverage - 2026.09.12

**96 controls: 25 Entra, 61 Intune, eight Exchange and two Purview. There are 61 creation recipes.** The other 35 controls use reviewed tenant actions, evidence-backed observations or explicit manual procedures. Creation counts do not describe production deployment or certification. No service or device behaviour was tested against a tenant for this release.

Create selected candidates through **Plan changes > Deploy**. Assessment and Plan have an area filter; changing the Plan area clears the selection. **Policy automation** holds separate reviewed tenant settings, assignments and package actions. Configuration has the [Exchange/Purview capture and proposal workflow](EXCHANGE-PURVIEW.md). The [two engineer documents](ENGINEER-DOCUMENTS.md) contain every exact setting and manual check, generated directly from the loaded catalogue.

## Required client inputs

- Offices: one stable unique key, mandatory name and one or more public CIDRs per location. Invalid/private/reserved ranges and overlaps with those ranges are refused; each PRE-008 instance is independently selected and untrusted. The singular office location used by a CA exclusion must still be deliberately resolved.
- Two distinct emergency identities, current operator and approved exclusion groups for CA. Groups are created empty; an ID alone does not prove correct membership. Admin consent reviewers are mandatory, explicitly resolved enabled users with no default.
- OneDrive/Edge native settings instances, client-reviewed Store identifiers, all iOS store URLs, all Android URLs/package IDs, and Managed Google Play readiness. No guessed native definition IDs, foreign tenant IDs or ABM/VPP/supervised-only dependency.
- RMM and ESET client packages, commands and detection rules. The standard supplies no installer or credentials. Metadata, publication and actual installation/health are separate checks.
- Exchange domain: mandatory when the domain-bound control is selected; it must be a valid entered DNS name and match the captured accepted domain before its proposal. Impersonation users-to-protect are entered by the engineer in the preset policy.
- Licence/edition, supported builds and reviewed operating-system minimums. Dated defaults warn; they do not establish current entitlement or device acceptance. Only inputs needed by the chosen workflow should be completed; a required empty value is refused with an explanation.

## Coverage by control

| Area | Control | Requirement | Implementation / remaining check |
| --- | --- | --- | --- |
| Entra | PRE-009 | GRP - Policy Exclusions Users | Empty group candidate. |
| Entra | PRE-010 | GRP - Policy Exclusions Devices | Empty group candidate. |
| Entra | PRE-004 | GRP - MAM Only Users | Empty group candidate. |
| Entra | PRE-005 | GRP - Pilot Devices | Empty group candidate. |
| Entra | PRE-008 | Office locations (one candidate per named office) | One untrusted candidate per selected office. |
| Entra | ID-001 | Emergency access accounts | Engineer-controlled emergency identities, custody and recovery tests. |
| Entra | ID-002 | Authentication methods and Temporary Access Pass | Captured assessment plus separate reviewed tenant change; complete scope/behaviour checks by hand. |
| Entra | ID-003 | Administrator access | Captured direct roles plus manual PIM, account-purpose and licence review. |
| Entra | CA-001 | Require MFA | Disabled CA candidate; separate activation. |
| Entra | CA-003 | Block legacy authentication | Disabled CA candidate; separate activation. |
| Entra | CA-004 | Block unsupported platforms | Disabled CA candidate; separate activation. |
| Entra | CA-005 | Require compliant desktop devices | Disabled CA candidate; separate activation. |
| Entra | CA-006 | Require compliant mobile devices | Disabled CA candidate; separate activation. |
| Entra | CA-007 | Require mobile app protection | Disabled CA candidate; separate activation. |
| Entra | CA-008 | Corporate mobile compliance | Disabled CA candidate; separate activation. |
| Entra | CA-009 | Secure security-info registration | Disabled CA candidate; separate activation. |
| Entra | CA-010 | Secure device registration | Disabled CA candidate; separate activation. |
| Intune | ENR-001 | Automatic MDM enrolment | Captured assessment plus separate reviewed tenant change; complete scope/behaviour checks by hand. |
| Intune | ENR-002 | Enrolment restrictions | Unassigned candidate; separate assignment and device/app verification. |
| Intune | ENR-003 | Windows Autopilot profile | Deferred classic profile; retain reference pending retirement decision. |
| Intune | ENR-004 | Enrolment Status Page | Deferred classic ESP; retain reference pending retirement decision. |
| Intune | ENR-005 | Apple MDM ownership and certificate | Client-owned Apple push certificate and renewal. |
| Intune | ENR-006 | Managed Google Play connection | Manual Play connection/consent; projected metadata read, no connection token. |
| Intune | CMP-001 | Devices with no compliance policy | Captured assessment plus separate reviewed tenant change; complete scope/behaviour checks by hand. |
| Intune | CMP-WIN-001 | Windows core compliance | Unassigned candidate; separate assignment and device/app verification. |
| Intune | CMP-IOS-001 | iOS and iPadOS compliance | Unassigned candidate; separate assignment and device/app verification. |
| Intune | CMP-AND-001 | Android personal work profile compliance | Unassigned candidate; separate assignment and device/app verification. |
| Intune | CMP-AND-002 | Android corporate compliance | Unassigned candidate; separate assignment and device/app verification. |
| Intune | CFG-WIN-001 | BitLocker and recovery escrow | Unassigned candidate; separate assignment and device/app verification. |
| Intune | CFG-WIN-002 | Windows LAPS | Unassigned candidate; separate assignment and device/app verification. |
| Intune | CFG-WIN-003 | Windows Hello for Business | Unassigned candidate; separate assignment and device/app verification. |
| Intune | CFG-WIN-004 | Windows core restrictions | Unassigned candidate; separate assignment and device/app verification. |
| Intune | CFG-WIN-005 | OneDrive sign-in and Known Folder Move | Unassigned candidate; separate assignment and device/app verification. Required native settings supplied/reviewed by engineer. |
| Intune | CFG-WIN-006 | Microsoft Edge configuration | Unassigned candidate; separate assignment and device/app verification. Required native settings supplied/reviewed by engineer. |
| Intune | CFG-WIN-007 | Long paths | Unassigned candidate; separate assignment and device/app verification. |
| Intune | SEC-WIN-002 | Microsoft Defender EDR onboarding | Deferred Defender onboarding; ESET selected, retirement proposed. |
| Intune | MAM-IOS-001 | iOS app protection | Unassigned candidate; separate assignment and device/app verification. |
| Intune | MAM-AND-001 | Android app protection | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-WIN-001 | Microsoft 365 Apps | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-WIN-002 | Company Portal | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-WIN-003 | Google Chrome | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-WIN-004 | Adobe Acrobat Reader | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-WIN-005 | OneDrive application | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-WIN-006 | Microsoft Teams | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-WIN-007 | Remote management agent | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-WIN-008 | ESET endpoint protection (client package) | Unassigned candidate; separate assignment and device/app verification. |
| Intune | UPD-001 | Windows Autopatch | Read captured update conflicts, manually confirm prerequisites and all-device coverage; separate reviewed device-category enrolment is not full Autopatch rollout. |
| Entra | PRE-011 | GRP - Autopilot Device Preparation | Empty group candidate. Existing Intune Provisioning Client owner set by a separate reviewed, exact-ID action. |
| Entra | ID-004 | Passkeys including Microsoft Authenticator | Captured assessment plus separate reviewed tenant change; complete scope/behaviour checks by hand. |
| Entra | ID-005 | System-preferred MFA | Captured assessment plus separate reviewed tenant change; complete scope/behaviour checks by hand. |
| Entra | ID-006 | Authenticator registration campaign | Captured assessment plus separate reviewed tenant change; complete scope/behaviour checks by hand. |
| Entra | ID-007 | User application consent off | Captured assessment plus separate reviewed tenant change; complete scope/behaviour checks by hand. |
| Entra | ID-008 | Admin consent workflow | Captured assessment plus separate reviewed tenant change; complete scope/behaviour checks by hand. |
| Entra | ID-009 | Self-service password reset off | Check SSPR is off by hand; never change it. |
| Entra | CA-011 | Require phishing-resistant MFA for administrators | Disabled CA candidate; separate activation. |
| Intune | ENR-007 | Windows Autopilot device preparation | Device-preparation portal workflow; native creation contract not confirmed. |
| Intune | CFG-WIN-008 | Windows web sign-in | Unassigned candidate; separate assignment and device/app verification. |
| Intune | CFG-WIN-009 | Chrome single sign-on | Native CloudAPAuthEnabled definition ID unconfirmed; manual, no uploaded ADMX. |
| Intune | CFG-WIN-010 | UK region and keyboard | UK setup region/keyboard by hand; Welcome/new-user settings untouched. |
| Intune | CFG-WIN-011 | UK time zone | Unassigned candidate; separate assignment and device/app verification. |
| Intune | CFG-WIN-012 | Show file extensions | Explorer file extensions by hand; no confirmed native catalogue ID. |
| Intune | CFG-WIN-013 | OneDrive Files On-Demand | OneDrive Files On-Demand by hand; native catalogue ID unconfirmed. |
| Intune | CFG-WIN-014 | Storage Sense | Unassigned candidate; separate assignment and device/app verification. |
| Intune | CFG-WIN-015 | Hide consumer features | Unassigned candidate; separate assignment and device/app verification. |
| Intune | CFG-WIN-016 | Taskbar starting layout | Five starting pins by hand; no enforced taskbar template. |
| Intune | CFG-WIN-017 | Microsoft Store available | Review effective Store access; no broad unblock/removal write. |
| Intune | CFG-WIN-018 | Windows settings backup and restore (ESR successor) | Unassigned candidate; separate assignment and device/app verification. |
| Intune | CFG-WIN-019 | Fast startup off | Power Options by hand; Hiberboot disabled does not force off. |
| Intune | CFG-WIN-020 | No sleep on mains | Unassigned candidate; separate assignment and device/app verification. |
| Intune | CFG-WIN-021 | Remove consumer Copilot app | Unassigned candidate; separate assignment and device/app verification. Consumer app only; documented conditions and conflicting Pro applicability require manual verification. |
| Intune | APP-IOS-001 | Outlook for iOS/iPadOS | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-IOS-002 | Teams for iOS/iPadOS | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-IOS-003 | Microsoft Authenticator for iOS/iPadOS | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-IOS-004 | OneDrive for iOS/iPadOS | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-IOS-005 | Edge for iOS/iPadOS | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-IOS-006 | Word for iOS/iPadOS | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-IOS-007 | Excel for iOS/iPadOS | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-IOS-008 | Company Portal for iOS/iPadOS | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-AND-001 | Outlook for Android | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-AND-002 | Teams for Android | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-AND-003 | Microsoft Authenticator for Android | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-AND-004 | OneDrive for Android | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-AND-005 | Edge for Android | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-AND-006 | Word for Android | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-AND-007 | Excel for Android | Unassigned candidate; separate assignment and device/app verification. |
| Intune | APP-AND-008 | Company Portal for Android | Unassigned candidate; separate assignment and device/app verification. |
| Exchange | EX-001 | Own-domain SPF spam bypass | Imported read-only observations; selected inert manual proposal, no toolkit write execution. Disabled/audit only until trusted SPF/From boundary is established. |
| Exchange | EX-002 | Standard preset security policy | Imported read-only observations; selected inert manual proposal, no toolkit write execution. |
| Exchange | EX-003 | Block external automatic forwarding | Imported read-only observations; selected inert manual proposal, no toolkit write execution. |
| Exchange | EX-004 | Disable organisation SMTP AUTH | Imported read-only observations; selected inert manual proposal, no toolkit write execution. |
| Exchange | EX-005 | External sender tagging in Outlook | Imported read-only observations; selected inert manual proposal, no toolkit write execution. |
| Exchange | EX-006 | Mailbox auditing enabled | Imported read-only observations; selected inert manual proposal, no toolkit write execution. |
| Exchange | EX-007 | DKIM signing and published selectors | Imported read-only observations; selected inert manual proposal, no toolkit write execution. Show actual CNAME targets; both fresh answers required before an enable proposal. |
| Exchange | EX-008 | DMARC record report | Report-only observations and manual verification; no write. |
| Purview | PUR-001 | Unified audit log enabled | Imported read-only observations; selected inert manual proposal, no toolkit write execution. |
| Purview | PUR-002 | Audit retention report | Report-only observations and manual verification; no write. |

## Retired and deferred controls

SEC-WIN-001 (Defender antivirus), CMP-WIN-002 (Defender compliance supplement) and SEC-WIN-003 (managed firewall) are removed from .12 only. ESET is APP-WIN-008; no firewall policy is introduced. ENR-003/004 and SEC-WIN-002 remain deferred references, not recipes. Recommend retiring them from default scope after the maintainer's decision; retain CFG-WIN-004 SmartScreen as separate shell protection pending confirmation.

Historical renumbering remains important: in .9/.10 PRE-001/002 named the user/device exclusion groups (now PRE-009/010), PRE-003 named MAM Only Users (now PRE-004), PRE-004 named Pilot Devices (now PRE-005), and PRE-005 named the office location (now PRE-008 instances). Read release and control name when reconciling older evidence; never transfer ownership by a reused ID or name.

## Boundaries and verification

Assessment remains read-only. Every automated write requires complete durable before evidence, preview/selection, matching typed tenant, exact identity, unchanged reviewed inputs, durable intent and individual results. CA retains the operator and emergency exclusions. New Intune policies/apps have no assignments, groups have no members, and locations are untrusted. An uncertain request is not retried. Failed or malformed reads are unknown.

PRE-011 ownership is limited to the recorded, still-empty group and the existing Microsoft provisioning service principal; the toolkit does not create a principal or add group members. Imported Exchange evidence cannot satisfy a deployment snapshot. Exchange proposal exports are commented review files that refuse execution. Retention policy and DLP writes are excluded.

App assignment records do not prove installation or effective exclusions. Policy presence does not establish Windows edition support, licence effect, all-device Autopatch coverage, working sign-in, or third-party protection. Follow the generated after-checks and record anything untested as unverified. Microsoft sources and permission/licence notes are in each control and its generated documents; [application setup](APPLICATION-SETUP.md) records the three added delegated scopes and renewed consent for both apps.
