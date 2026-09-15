# Automation coverage — 2026.09.6

Use **Policy automation** for inputs, imports, prerequisites, assignments, packages and readiness. Create policy/app candidates through the existing **Plan → Deploy** workflow. Candidates stay disabled/unassigned until a separate activation is explicitly approved.

**45 controls: 37 candidate recipes, 4 reviewed tenant-action workflows, 4 readiness/engineer workflows.** This is implemented source, not verified tenant behaviour or certification. Some recipes require client-approved inputs before a plan can create them.

## Required client inputs

- Android minimum supported OS; ESP blocking app IDs already published in this tenant (Office is excluded from ESP).
- Reviewed OneDrive and Edge Settings Catalogue instances or policy JSON exports including their separate settings collection. This release does not guess Microsoft setting identifiers, browser preferences, or foreign tenant IDs. The engine resolves definition IDs/choices before creation. Import requires explicit GUID replacements, including GUIDs embedded in string values.
- Store IDs for Chrome, Adobe Reader, OneDrive and Teams. Only Intune-compatible Microsoft Store identifiers are accepted; a community winget ID is not sufficient. If a product is unavailable through that source, import a supported Win32 metadata export for that control and publish its client-approved .intunewin package.
- RMM and third-party endpoint agent .intunewin packages, installation/uninstall commands and detection rules. The code publishes supplied packages; it does not generate vendor installers, embed tenant tokens, create vendor subscriptions, or prove console check-in.
- Exact group/device IDs for activation, assignment, TAP onboarding and Autopatch. The preview resolves group names and rejects invalid/overlapping targets. Group membership and licensing are ongoing responsibilities. Autopatch preview requires deployment access because Microsoft exposes its reads through a write-capable permission.

## Coverage by control

| Control | Requirement | Implementation path | Inputs / remaining review |
| --- | --- | --- | --- |
| ID-001 | Emergency access accounts | Readiness check plus engineer/external step | Review scope and prerequisites |
| ID-002 | Authentication methods and Temporary Access Pass | Reviewed authentication-method actions | Review scope and prerequisites |
| ID-003 | Administrator access | Readiness check plus engineer/external step | Review scope and prerequisites |
| CA-001 | Require MFA | Candidate recipe | emergencyAccountIds, officeLocationId |
| CA-003 | Block legacy authentication | Candidate recipe | emergencyAccountIds |
| CA-004 | Block unsupported platforms | Candidate recipe | Review scope and prerequisites |
| CA-005 | Require compliant desktop devices | Candidate recipe | officeLocationId |
| CA-006 | Require compliant mobile devices | Candidate recipe | officeLocationId, mamGroupId |
| CA-007 | Require mobile app protection | Candidate recipe | Review scope and prerequisites |
| CA-008 | Corporate mobile compliance | Candidate recipe | emergencyAccountIds, officeLocationId, mamGroupId |
| CA-009 | Secure security-info registration | Candidate recipe | emergencyAccountIds, officeLocationId |
| CA-010 | Secure device registration | Candidate recipe | officeLocationId |
| ENR-001 | Automatic MDM enrolment | Reviewed MDM enrolment scope change | Review scope and prerequisites |
| ENR-002 | Enrolment restrictions | Candidate recipe | Review scope and prerequisites |
| ENR-003 | Windows Autopilot profile | Candidate recipe | Review scope and prerequisites |
| ENR-004 | Enrolment Status Page | Candidate recipe | espBlockingAppIds |
| ENR-005 | Apple MDM ownership and certificate | Readiness check plus engineer/external step | Review scope and prerequisites |
| ENR-006 | Managed Google Play connection | Readiness check plus engineer/external step | Review scope and prerequisites |
| CMP-001 | Devices with no compliance policy | Reviewed tenant compliance setting | Review scope and prerequisites |
| CMP-WIN-001 | Windows core compliance | Candidate recipe | Review scope and prerequisites |
| CMP-WIN-002 | Defender compliance supplement | Candidate recipe | Review scope and prerequisites |
| CMP-IOS-001 | iOS and iPadOS compliance | Candidate recipe | Review scope and prerequisites |
| CMP-AND-001 | Android personal work profile compliance | Candidate recipe | androidMinimumVersion |
| CMP-AND-002 | Android corporate compliance | Candidate recipe | androidMinimumVersion |
| CFG-WIN-001 | BitLocker and recovery escrow | Candidate recipe | Review scope and prerequisites |
| CFG-WIN-002 | Windows LAPS | Candidate recipe | Review scope and prerequisites |
| CFG-WIN-003 | Windows Hello for Business | Candidate recipe | Review scope and prerequisites |
| CFG-WIN-004 | Windows core restrictions | Candidate recipe | Review scope and prerequisites |
| CFG-WIN-005 | OneDrive sign-in and Known Folder Move | Candidate recipe | oneDriveSettings |
| CFG-WIN-006 | Microsoft Edge configuration | Candidate recipe | edgeSettings |
| CFG-WIN-007 | Long paths (optional) | Candidate recipe | Review scope and prerequisites |
| SEC-WIN-001 | Antivirus configuration | Candidate recipe | Review scope and prerequisites |
| SEC-WIN-002 | Microsoft Defender EDR onboarding | Candidate recipe | Review scope and prerequisites |
| SEC-WIN-003 | Firewall protection | Candidate recipe | Review scope and prerequisites |
| MAM-IOS-001 | iOS app protection | Candidate recipe | Review scope and prerequisites |
| MAM-AND-001 | Android app protection | Candidate recipe | Review scope and prerequisites |
| APP-WIN-001 | Microsoft 365 Apps | Candidate recipe | Review scope and prerequisites |
| APP-WIN-002 | Company Portal | Candidate recipe | Review scope and prerequisites |
| APP-WIN-003 | Google Chrome | Candidate recipe | chromeStoreId |
| APP-WIN-004 | Adobe Acrobat Reader | Candidate recipe | adobeReaderStoreId |
| APP-WIN-005 | OneDrive application | Candidate recipe | oneDriveStoreId |
| APP-WIN-006 | Microsoft Teams | Candidate recipe | teamsStoreId |
| APP-WIN-007 | Remote management agent | Candidate recipe | rmmFileName, rmmInstallCommand, rmmUninstallCommand, rmmDetectionRules |
| APP-WIN-008 | Endpoint protection agent | Candidate recipe | endpointAgentFileName, endpointAgentInstallCommand, endpointAgentUninstallCommand, endpointAgentDetectionRules |
| UPD-001 | Windows Autopatch | Reviewed Autopatch category enrolment/removal | Review scope and prerequisites |

## Consequences and recovery

- CA activation changes only state, preserving stored targeting and exclusions. It requires two emergency accounts and the current operator retained in user exclusions. Report-only and disabled containment are separate choices.
- Intune assignment writes replace the empty candidate assignment list with explicitly selected groups. Removal clears all current assignments from an owned object. Removing assignments does not guarantee settings reverse on devices. Device/user group exclusion semantics must be reviewed.
- MDM All clears Microsoft's selected-group targeting. Tenant secure-by-default compliance can affect existing CA immediately. SMS/voice disablement can deny users their only method. TAP is limited to an approved group and one-use passes of at most one hour; no pass is issued by this workflow.
- Toolkit-owned candidate deletion supports the added policy/app collections. Settings Catalogue child-settings updates remain blocked; delete/recreate an unassigned candidate after review. Publishing a package into an app with already committed content is also blocked; upgrades need a separate procedure.
- Package publishing journals each Graph request and records app/version/file IDs. It never saves encryption keys or signed storage URLs. An unknown package write blocks another upload/activation; deleting the recorded unassigned app can remove its content after review. An accepted final publication can be re-verified without repeating writes.
- Autopatch enrolment establishes category authority for selected devices. It does not create ring groups, approve an OS release, guarantee licence assignment, or undo installed updates. Enrolment removal is restricted to a toolkit-recorded enrolment.
- Apple certificate and Google Play first-time ownership/consent still require their external workflows. Emergency/admin identity checks do not prove credential custody, effective/group/PIM permissions or working recovery. The UI explicitly reports those limits.

## Microsoft documentation checked during implementation

- [MDM scope update](https://learn.microsoft.com/en-us/graph/api/mobiledevicemanagementpolicies-update?view=graph-rest-beta) — All removes selected groups; delegated Mobility Management scope.
- [TAP method configuration](https://learn.microsoft.com/en-us/graph/api/resources/temporaryaccesspassauthenticationmethodconfiguration?view=graph-rest-1.0) and [tenant compliance settings](https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-devicemanagementsettings?view=graph-rest-1.0).
- [Settings Catalogue definitions](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfigv2-devicemanagementconfigurationsettingdefinition-list?view=graph-rest-beta), [ESP](https://learn.microsoft.com/en-us/graph/api/resources/intune-onboarding-windows10enrollmentcompletionpageconfiguration?view=graph-rest-beta), [Office suite app](https://learn.microsoft.com/en-us/graph/api/resources/intune-apps-officesuiteapp?view=graph-rest-beta), [Store app](https://learn.microsoft.com/en-us/graph/api/resources/intune-apps-wingetapp?view=graph-rest-beta).
- [Win32 app creation](https://learn.microsoft.com/en-us/graph/api/intune-apps-win32lobapp-create?view=graph-rest-1.0) and [content-file lifecycle](https://learn.microsoft.com/en-us/graph/api/resources/intune-apps-mobileappcontentfile?view=graph-rest-1.0).
- [Windows compliance supplement](https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-windows10compliancepolicy?view=graph-rest-beta) — Defender-specific properties use beta; `signatureOutOfDate=true` requires current signatures. [iOS app protection](https://learn.microsoft.com/en-us/graph/api/resources/intune-mam-iosmanagedappprotection?view=graph-rest-beta) declares the Microsoft-app selection group.
- [Autopatch enrolment](https://learn.microsoft.com/en-us/graph/api/windowsupdates-updatableasset-enrollassets?view=graph-rest-beta), [removal](https://learn.microsoft.com/en-us/graph/api/windowsupdates-updatableasset-unenrollassets?view=graph-rest-beta), [licensing prerequisites](https://learn.microsoft.com/en-us/windows/deployment/windows-autopatch/prepare/windows-autopatch-prerequisites).

Beta APIs remain explicitly identified. Compilation and documentation checks do not prove these payloads or workflows are accepted by a tenant.
