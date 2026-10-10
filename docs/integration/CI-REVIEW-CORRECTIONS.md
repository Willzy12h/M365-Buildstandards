# CI review corrections

## Recovery contract

CI run 34905926035 on 2ad370d reported 294 passed, 1 failed, 0 skipped. The failure was `DeviceRecipeTests.Beta_recovery_is_restricted_to_device_configuration_and_never_replayed`, at its assertion that beta `deviceCompliancePolicies` must be unsupported.

That assertion describes the preview.4 scope, before the user requested recovery for every supported created candidate and preview.6 added beta compliance and other Intune recipes. The corrected assertion permits **only** the eight explicitly named Intune candidate collection roots. Beta Conditional Access, users, groups, the tenant-management root and arbitrary child paths remain refused. The existing HTTP test still requires zero sends for beta CA and exactly one DELETE on an ambiguous device-configuration response. The test was renamed to describe those unchanged transport guarantees; a separate exhaustive allow-list theory expresses the expanded contract.

This is a documented contract correction, not a reason to remove ownership, approval, evidence, assignment or no-retry safeguards. Subsequent CI results must be recorded from the exact PR head, separately from local test results.

## Callback transport

The full local run also exposed a connection reset in `CorrelatedApprovalCompletesWithNoClaimOfVerifiedGrants`. Its successful-response assertion was retained. The callback now shuts down its send side after writing the complete response and allows a bounded 250 ms peer drain before disposal, avoiding an immediate close with unread peer bytes. Correlation, timeout, host/state/tenant validation and the requirement to validate actual grants are unchanged. Local filtering software remains an environmental factor; CI and local results are recorded separately.
