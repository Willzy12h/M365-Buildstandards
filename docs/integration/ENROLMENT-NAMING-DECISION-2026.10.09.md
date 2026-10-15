# Enrolment naming decision — PR #71

Proposed; independent review and merge are required before dependent source changes.

CLA-20261008-34 found that PR #57 refuses every ENR-002 import from standard 2026.09.30 because `enrolment` has no reviewed naming rule. Do not bypass the check or rewrite the published catalogue.

Add internal prefix `ENR` under the agreed `<TYPE> - <description>` scheme, for the existing `enrolment` collection and `/deviceManagement/deviceEnrollmentConfigurations` Graph family. Example: `ENR - Reviewed enrolment restrictions`. Existing character, casing and whitespace rules apply. ENR-002 has no platform identity suffix; do not infer one from its restrictions.

Use the catalogue's existing [beta deviceEnrollmentPlatformRestrictionsConfiguration reference](https://learn.microsoft.com/en-us/graph/api/resources/intune-onboarding-deviceenrollmentplatformrestrictionsconfiguration?view=graph-rest-beta). No numeric name limit has been verified: retain a null maximum and explicit service-limit limitation. Internal conformance is not API acceptance. Beta and deployment safeguards remain unchanged.

This adds one reviewed naming rule. Schemas, permissions, ownership proof, historical names, collection routes and tenant objects remain unchanged. Subsequent #57 implementation must cover successful current ENR-002 import, wrong prefix/whitespace refusal, immutable baseline/other controls and naming-route consistency across historical catalogues. Record a failing-first regression against the current implementation. Unknown families continue to refuse authoring until reviewed.

Source, synthetic tests and Microsoft live acceptance remain separate. No live calls or renames are authorised by this decision.
