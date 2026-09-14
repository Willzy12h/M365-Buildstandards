# Policy automation APIs

This code-focused increment is ready for integration into the engineer interface. Standard **2026.09.5** contains **45 controls, 18 candidate creation recipes and 15 collections**. The four additions are Windows LAPS, Defender Antivirus, Windows Firewall and Defender EDR. The other 27 controls remain manual. The supplied BitLocker and long-path settings are unchanged.

## Use the new recipes

Load `2026.09.5.json` with `StandardsLoader`, capture and save complete evidence, then use the existing `DeploymentPlanner` and `DeploymentExecutor` with the chosen control IDs. This retains tenant/operator binding, licence checks, exact-ID ownership, preflight, unassigned creation, before/after evidence and selective recovery. Candidates never activate themselves or assign devices. Assigned policies remain protected from automatic deletion.

| Control | Candidate settings / prerequisite |
|---|---|
| CFG-WIN-002 | LAPS: Entra backup; 20-character complex passwords; 7-day age; reset password and sign out after 24 hours following authentication. Built-in administrator identified by SID; no account creation or enablement. Requires positive Entra LAPS evidence. |
| SEC-WIN-001 | Defender Antivirus: real-time, behaviour, cloud, downloaded-file, script and archive scanning; high cloud blocking; safe sample submission; PUA blocking; 8-hour signature checks. Third-party AV and tamper protection require separate review. |
| SEC-WIN-003 | Enable Domain, Private and Public firewalls; block inbound and allow outbound by default. Explicit allow rules remain effective. No invented ports or exclusions. |
| SEC-WIN-002 | Beta Defender EDR configuration: automatic onboarding data from the target tenant, sample sharing and expedited telemetry. Requires Intune plus provisioned MDE_SMB or WINDEFATP. Endpoint P1 alone does not pass the EDR gate. |

EDR creation relies on an already configured target-tenant Defender integration. Connector activation, user/device entitlement, onboarding-blob generation, sensor health and portal behaviour are **not proven by candidate creation**. No onboarding/offboarding blob from another tenant is accepted. Removing the unassigned candidate does not offboard a device or undo endpoint changes.

These are reviewable SME defaults informed by Microsoft's settings, Cyber Essentials control themes and NIST guidance, not a certification or a full implementation of either framework. Cloud/EDR sample sharing has privacy consequences; pilot business applications and remote support before assignment.

## Import supported exports

`DevicePolicyImporter.Import(baseline, controlId, json, candidateName)` returns a `DevicePolicyImport` containing a new in-memory `StandardCatalogue`, source digest and removed-property list. It never saves a file or calls Graph. Review the result, save it only in an appropriate local standards/evidence area, regenerate the local manifest if persisting it, and capture a **new** snapshot for that imported release before planning.

The adapter accepts one Graph `windows10CustomConfiguration` export for the LAPS/AV/firewall recipe, or `windowsDefenderAdvancedThreatProtectionConfiguration` for automatic EDR onboarding. All currently supported settings must be present exactly once, with matching OMA URI and type. Values may be customised and still require engineering review. Unsupported properties, missing settings, duplicate keys/URIs, templates, wrong types and input over 256 KiB are refused. Settings Catalogue `configurationPolicies` and arbitrary XML/PowerShell exports need separate adapters; they are not silently converted.

Source object IDs, assignments, group assignments, role scope tags, timestamps and descriptions are removed and reported. The new candidate uses the supplied name and generic description; no ownership is adopted and no target group is reused. Scope-tag removal means the new policy uses the target tenant's default scope; review RBAC before creation. Raw import JSON is never logged or saved by this API. Real exports and local imported catalogues must stay out of Git.

## Entra LAPS prerequisite

`EntraLapsService` provides:

1. `PreviewAsync(graph, session, standard, snapshot, ct)`: read the live tenant switch, require complete durable evidence, compare the current registration settings with the snapshot and save a bound preview. Assessment access can preview.
2. `ExecuteAsync(graph, session, standard, planId, approvedDigest, ct)`: after the engineer reviews the exact preview and its tenant-wide consequence, pass that preview's digest. Requires deployment access, the same tenant/operator/client/standard and evidence no older than 20 minutes. Already enabled is a verified no-op.
3. `ReverifyAsync(graph, session, runId, ct)`: read-only verification of an accepted request. Saves a separate source-linked verification record without rewriting the original run or resending a write.

Enabling uses the documented **PUT** on `/policies/deviceRegistrationPolicy`, preserving quota, MFA, registration, join and local-administrator membership settings. Missing or unsupported registration evidence fails closed. The transport has a second drift check and generic POST/PATCH operations on this route are refused. Graph does not document an atomic conditional update here: an external administrator changing the policy between the final read and PUT remains a race. Coordinate this tenant-wide operation; the toolkit lease covers only processes using the same evidence root.

Permissions: `Policy.Read.DeviceConfiguration` for the added read collection and `Policy.ReadWrite.DeviceConfiguration` for enablement. Microsoft requires a supported delegated role; Cloud Device Administrator is the least privileged role documented for the update. Existing tool applications need reviewed permission repair/consent and a new token. This code never grants directory roles.

Evidence is stored under `data/tenants/<tenant>/entra-laps/`: immutable previews, durable intent/result records and append-only verification records. A confirmed not-sent failure permits a fresh preview; every preview remains single-use. Unknown request acceptance stays blocked for reconciliation. Accepted-but-unverified results have the read-only re-verification path. Reads have a 60-second phase budget and respect cancellation; an in-flight PUT uses the Graph client's bounded write timeout and is never retried. A interrupted intent remains Unknown even if the process could not finish its terminal record.

The switch affects existing LAPS-configured devices across the tenant. There is no automatic disable/rollback of this tenant-wide setting. Saved before-values support a separately reviewed recovery procedure. This operation creates neither local accounts nor device assignments and never reads passwords.

## Integration and validation boundary

The ordinary candidate recipes use the existing UI/engine path. **Import selection and the LAPS prerequisite service have no new UI in this increment**, as requested. The next UI pass should show removed import fields, all proposed settings, prerequisite state, exact tenant-wide consequences, preview approval, per-write acceptance and re-verification. It must invalidate plans when imports or prerequisites change.

Synthetic tests exercise the real Graph HTTP adapter for LAPS and the engine's durable deployment/recovery flow for all four candidates. Live Graph acceptance, Defender integration, device LAPS password escrow/retrieval, encryption, device application and interactive approval remain unverified. No real tenant changes were made.

Sources checked 14 September 2026: [LAPS CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/laps-csp), [device registration GET](https://learn.microsoft.com/en-us/graph/api/deviceregistrationpolicy-get?view=graph-rest-1.0), [device registration PUT](https://learn.microsoft.com/en-us/graph/api/deviceregistrationpolicy-update?view=graph-rest-1.0), [Defender CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-defender), [Firewall CSP](https://learn.microsoft.com/en-us/windows/client-management/mdm/firewall-csp), [EDR Graph schema](https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-windowsdefenderadvancedthreatprotectionconfiguration?view=graph-rest-beta), [Microsoft service-plan identifiers](https://learn.microsoft.com/en-us/entra/identity/users/licensing-service-plan-reference), [Cyber Essentials requirements](https://www.ncsc.gov.uk/cyberessentials/resources), [NIST SP 800-53](https://csrc.nist.gov/pubs/sp/800/53/r5/upd1/final).
