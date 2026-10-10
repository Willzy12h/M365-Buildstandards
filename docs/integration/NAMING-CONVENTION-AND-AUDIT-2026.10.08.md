# Naming convention and read-only audit

Status: source implementation in PR #43; report/desktop integration and Microsoft live acceptance remain separate. This defines internal engineer naming rules under William's agreed `<TYPE> - <description>` scheme. It does not assert that Microsoft enforces these prefixes or restricted characters.

| Object family | Type | Example |
|---|---|---|
| Groups | GRP | GRP - Pilot devices |
| Named locations | LOC | LOC - London office |
| Conditional Access | CA | CA - Require MFA |
| Enrolment restrictions (`deviceEnrollmentConfigurations`) | ENR | ENR - Reviewed enrolment restrictions |
| Compliance policies | CMP | CMP - Core compliance - Windows |
| Device configuration and endpoint protection profiles (`deviceConfigurations`); settings catalogue policies | CFG | CFG - Endpoint protection - Windows |
| Mobile application protection | MAM | MAM - Managed applications - iOS |
| Intune applications (`apps`, and the beta `applications` read) | APP | APP - Company application |

Type is uppercase, the separator is exactly ` - ` and the description is non-empty. Description casing should be readable sentence case; proper names retain their spelling. No leading/trailing/repeated spaces or control characters. The internal allowed set is letters, digits, spaces and `- _ ( ) . , & / '`. Characters are read as Unicode scalars after NFC normalisation, so decomposed accents and supplementary-plane letters count as letters; a combining mark is accepted only when attached to a letter, and unpaired surrogates are refused. This is an internal readability policy, not a general list of Microsoft's allowed characters. Newly authored platform-specific control names use ` - Windows`, ` - iOS`, ` - Android` or ` - macOS`, as established by the control's WIN/IOS/AND/MAC identity. Raw tenant names cannot prove platform where a returned type/field is absent; this first audit does not infer it from a name.

## Microsoft constraints and uncertainty

Inspected official resource references on 8 October 2026:

- [Group](https://learn.microsoft.com/en-us/graph/api/resources/group?view=graph-rest-1.0): displayName is required and cannot be cleared; maximum 256 characters. The validator conservatively counts UTF-16 units of the name as returned, before normalisation, so a decomposed name can be reported over the limit when its composed form would fit.
- [Conditional Access policy](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesspolicy?view=graph-rest-1.0) and [named location](https://learn.microsoft.com/en-us/graph/api/resources/namedlocation?view=graph-rest-1.0): define displayName, without a numeric name limit in the inspected page.
- [Compliance](https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-devicecompliancepolicy?view=graph-rest-1.0), [device configuration](https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-deviceconfiguration?view=graph-rest-1.0) (cited for both `configuration` and `endpointProtection`, which read `/deviceManagement/deviceConfigurations`), [settings catalogue](https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfigv2-devicemanagementconfigurationpolicy?view=graph-rest-beta), [managed application policy](https://learn.microsoft.com/en-us/graph/api/resources/intune-mam-managedapppolicy?view=graph-rest-1.0) and [mobile application](https://learn.microsoft.com/en-us/graph/api/resources/intune-apps-mobileapp?view=graph-rest-1.0) (cited for `apps` and `applications`): name/displayName properties; no verified numeric limit from these inspected references.

An unknown maximum remains null, with an explicit service-limit limitation. Conforming means conforming to the internal naming scheme, not proven API acceptance. Do not reuse group limits for policy types or claim unlimited names. Beta resources remain experimental under the existing collection contract.

## Authoring versus historical data

`NamingConvention.CheckAuthored` / `RequireAuthored` validate an explicitly resolved new control name. Unknown families refuse the required authoring check until a rule is reviewed. These APIs do not alter StandardsLoader or globally reject published catalogues. They are not an automatically integrated catalogue-authoring screen; that surface does not exist today.

Historical names such as `M365 - CA-001 - Require MFA` may fail the new policy. Report that result; preserve the published catalogue's original bytes and digest. A correction requires a new standard release, lineage and normal review. A tenant audit never proposes or performs an automatic rename, even for a toolkit object. Naming does not establish settings compliance, ownership or safe deployment authority.

## Evidence and ownership

`NamingAudit.Review` is an engine projection over a supplied capture, mappings and runs. It performs no Graph/Exchange/DNS calls or writes. Exact collection/object IDs accompany all returned names. Missing IDs, duplicate captured IDs, missing/non-string names and unknown object families cannot become a clean conforming result. Failed, missing and partial collection states remain explicit; a successful empty collection says checked successfully with no objects returned.

Names alone never identify toolkit management. A matching exact collection/object mapping without adequate proof is **Toolkit ownership unable to confirm**. Corroboration requires an intact capture, one mapping, one intact matching run/result, accepted recorded Create, matching payload digest and complete readback digest/subset, and write time no later than the capture. Old mappings without this proof, later mappings, ambiguous mapping histories or an update-only referenced run remain unconfirmed.

Some collections read the same Graph objects: `configuration` and `endpointProtection`; `compliance` and `extendedCompliance`; `apps` and `applications`; and `appProtection` with `iosProtection` and `androidProtection` (subtypes returned by the managedAppPolicies read). Each rule names its Graph object family; a test checks those families against every published catalogue's collection routes. When an object has no mapping under the collection being reviewed but its exact ID is mapped under an overlapping collection, the row is **Toolkit ownership unable to confirm** with a reason naming that collection, not **No toolkit mapping for this object**. Management is still decided only under the mapping's own collection, and an object ID mapped under two overlapping collections is ambiguous and stays unconfirmed in both. **No toolkit mapping for this object** describes the record, not ownership by another product or permission to rename it. **Toolkit-managed** is bounded to recorded creation and captured identity; it is not a claim of current live state or effectiveness. A digest detects changes, not authenticity.

## Verification and remaining work

Synthetic tests cover internal naming, documented group limits, platform-specific authoring, name-only ownership refusal, accepted creation proof, modified/unknown/later/duplicate evidence, duplicate object IDs, empty/failed/missing reads, input immutability and cross-tenant refusal. Windows and package checks are recorded for the exact source, independently of Microsoft acceptance. Reports/UI exposure, richer object-type/platform limits and automatic authoring-pipeline integration remain open; no broad product feedback gate closes from this engine implementation alone.

## Local candidate imports — PR #57

Both `DevicePolicyImporter.Import` and `PolicyImporter.Import` require the existing authored-name rule for the selected control before building a new local candidate. Use the collection prefix, a meaningful description and the control's platform suffix; for example `CFG - Reviewed LAPS - Windows` for CFG-WIN-002. Leading/trailing spaces are rejected rather than silently repaired. The naming check does not certify configuration correctness, service acceptance or object ownership. Import shape, foreign-reference, assignment removal and transport checks still apply.

Only the supplied name of the changed candidate control is checked. Other controls, the baseline object and historical catalogue files retain their original bytes/meaning; no live object is renamed. This is validation of the existing bounded import workflows, not a general authoring compiler.

Failing-first evidence: 22 dedicated cases on `14e62275` with test-only commit `00a61eb`: 14 invalid-name refusals failed because both importers accepted the names; eight valid-candidate/historical-preservation controls passed. With the two entry-point checks, all 22 dedicated cases and 25 existing importer cases pass locally (47 executed, zero skipped). Existing importer fixtures now use conforming names, including negative shape tests, so those tests continue exercising their original safeguards. Windows and independent review remain pending until recorded.

Final local validation before push: 1,292 engine/CLI tests executed and passed, zero skipped; strict solution cross-build passed with zero warnings/errors. An interrupted first broad run is not counted as passing; the completed replacement run supplies this evidence.

### Enrolment authoring correction — 10 October 2026, PR #57

Merged INT-091 (#71) adds the reviewed ENR prefix for `enrolment` and `/deviceManagement/deviceEnrollmentConfigurations`. The beta [platform restrictions reference](https://learn.microsoft.com/en-us/graph/api/resources/intune-onboarding-deviceenrollmentplatformrestrictionsconfiguration?view=graph-rest-beta) establishes the existing family, without a verified numeric name limit. The maximum stays unknown; ENR-002 has no inferred platform suffix. Both import entry points retain their existing supported-control, payload and assignment safeguards. General import of current 2026.09.30 ENR-002 is covered directly; the device-only importer retains its narrower control set.

Current-catalogue regressions fail before the rule is added, then allow a conforming local candidate while preserving the baseline and every other control. Wrong prefix/casing/whitespace refuse, and the existing route test checks this family against all published catalogues. Historical standards are unchanged. Source/synthetic conformance does not establish beta API acceptance or permit a live rename.
