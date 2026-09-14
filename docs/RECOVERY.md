# Undo and recovery

1. Connect to the correct tenant with deployment access. Open **Undo and recovery** and load the durable change register.
2. Select the original creation to remove its object, or the latest supported update to restore its before-values. For an unexpectedly enabled toolkit-created Conditional Access policy, preview **Disable Conditional Access**.
3. Let the tool capture a complete fresh snapshot and read the current object, settings and assignments. Inspect the object ID, current settings and consequences. A preview expires after five minutes.
4. Approve the specific action, acknowledge any displayed drift, and type the exact tenant ID. Applying recovery makes a real tenant write. Approval of source code is not approval to run this workflow against a tenant.
5. Review **write acceptance** and **verification** separately. Keep all evidence, including incomplete or uncertain results. Do not replay the request.

## Supported operations

| Operation | Scope | Important boundary |
|---|---|---|
| Delete a recorded creation | Toolkit-created v1.0 Conditional Access, device configuration and device compliance policies | Requires a confirmed creation ID and matching ownership. Intune assignments must already be empty. A fresh 404 and collection read confirm absence before the mapping is removed. |
| Restore an update | Latest mapped update with complete recorded before-values | Restores only the fields changed by that update. Blocks later drift, missing before-values, reactivation and assignment changes. |
| Disable Conditional Access | Recorded toolkit-created CA policy, including unexpected activation or changed targeting | Explicitly review drift. Sends only `state: disabled`; retains current stored targeting and exclusions. Other policies may still affect sign-in. |

Existing unmanaged objects are never adopted by name. Missing ownership, unsupported types, incomplete captures or missing historical recovery values block automatic recovery. Older runs can be displayed, but only sufficiently evidenced operations are recoverable. Removing a policy permanently removes that object ID; creating another policy is a separate operation.

The complete-capture requirement can prevent toolkit recovery when unrelated tenant reads fail. Use an independently authorised Entra/Intune recovery procedure in that situation; the toolkit does not bypass its evidence gate. Assigned Intune policy cleanup, enterprise-app deletion, consent revocation and engineer-assignment removal remain separate reviewed administrative procedures. App setup already records application IDs, service-principal IDs and write outcomes in its own evidence; deleting the authentication applications could prevent the toolkit from continuing.

## Evidence and interruption

Policy runs retain exact object IDs, actor, payload, before/after objects, prior ownership and acceptance/verification. Recovery plans and runs are stored under `data/tenants/<tenantId>/recovery/`; original deployment runs, journals, snapshots and mappings remain in the same tenant evidence folder. Setup stores `app-setup-<runId>/before.json`, `result.json`, `journal.ndjson` and after evidence in its displayed evidence directory. Retain the whole data folder securely; never commit real tenant data to Git.

Recovery persists intent before sending the request and acceptance before readback. Confirmed failures before transport are NotAttempted; timeouts after sending, HTTP 408/5xx and transport failures are uncertain. No write is retried. Stop cancels reads while an in-flight policy request retains its configured timeout (100 seconds by default, including token acquisition). Recovery readback has a 60-second budget. Deployment readback and its after-capture each have a 60-second budget; cancelled or truncated capture remains incomplete. These are network-operation budgets, not a guarantee against an operating-system/storage hang. Accepted but unverified recovery, incomplete mapping updates and uncertain writes block further writes until the applicable verification path succeeds; see [unresolved writes](integration/UNRESOLVED-WRITES.md).

## Re-verify an accepted write

Connect to the same verified tenant; assessment access is sufficient. Load the change register, select a row, then choose **Re-verify deployment** or select its accepted recovery attempt and choose **Re-verify recovery**. This sends GET requests only, with a 60-second read budget. No original write is repeated and no old plan is reused.

Deployment verification requires the exact recorded ID, latest matching ownership/payload and disabled/unassigned state. Recovery verification checks the original reviewed standard and source evidence. Deletion requires both a direct object 404 and complete collection absence; only then is its local mapping removed. Restoration checks the recorded before-settings before restoring the prior mapping. CA containment verifies disabled state without changing targeting. A new owner or incompatible later mapping blocks finalisation.

Each attempt is stored under `data/tenants/<tenantId>/verification/`, with actor, source digest, observed configuration and mapping digests. Original deployment and recovery records are retained unchanged. The register shows later verification separately. A crash during local mapping finalisation can be checked again without another tenant write.

For completed toolkit **1.0.0** rows with no recorded acceptance and a valid ID, explicitly acknowledge historical reconciliation before re-verifying. The current mapping must belong to that run and its stored payload digest must match; unavailable evidence or later ownership requires separate review. Successful verification permits supported future work while preserving the original Unknown field. This does not infer acceptance for incomplete, explicitly Unknown or newer records. A creation superseded by a historical update may lack sufficient evidence for automatic recovery even after the latest update is reconciled.

A file lease serialises deployment and recovery processes sharing the same tenant evidence root. It cannot lock another evidence root, Entra administrators or external tools. Fresh reads detect changes before execution, but Graph does not provide an atomic transaction covering review, write and mapping updates. Coordinate recovery with other engineers.

Digests detect accidental evidence changes; they are not signatures or protection against an attacker who can rewrite the evidence. After evidence verifies observed configuration or absence, not reversal of previous sign-in, device or user effects. Recovery is selective and explicit, not automatic rollback.

## Verification and Microsoft references

Synthetic tests cover durable intent, exact IDs, deletion and absence, update restoration, unexpected activation, assignment/drift/tenant/approval gates, uncertain writes and shared-store exclusion. Real tenant recovery remains unverified; see [testing evidence](integration/TESTING-EVIDENCE.md).

The supported routes follow Microsoft's [Conditional Access deletion](https://learn.microsoft.com/en-us/graph/api/conditionalaccesspolicy-delete?view=graph-rest-1.0), [partial update](https://learn.microsoft.com/en-us/graph/api/conditionalaccesspolicy-update?view=graph-rest-1.0), [device configuration deletion](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfig-windows10generalconfiguration-delete?view=graph-rest-1.0) and [compliance deletion](https://learn.microsoft.com/en-us/graph/api/intune-deviceconfig-windows10compliancepolicy-delete?view=graph-rest-1.0) documentation, checked on 14 September 2026. Tenant permissions and Intune RBAC still require validation.
