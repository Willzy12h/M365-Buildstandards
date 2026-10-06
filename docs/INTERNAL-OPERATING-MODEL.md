# Internal operating model and incidents

These are proposed business assignments for the product owner to name and approve. They do not imply named personnel or approved retention periods already exist.

| Responsibility | Decisions and duties |
|---|---|
| Product owner | Approved internal scope, supported journeys, exception/deferral closure rules, engineering priorities. |
| Standard owner | Immutable published catalogues, reviewed defaults and licence assumptions, requirement changes and lineage review. |
| Release owner | Exact package/source/check provenance, distribution/signing decision, accepted capability matrix, servicing and withdrawal. |
| Security/access owner | Approved bootstrap and daily permission model, engineer onboarding/offboarding, emergency access, RBAC and consent review. |
| Evidence custodian | Secure local storage, access/retention/sharing, backup hashes, complete handoff and incident preservation. |
| Client engineer | Correct identity and inputs, fresh captures, selective previews, approvals, manual/prerequisite outcomes and functional verification. |
| Independent reviewer | Evidence/ownership/compatibility review, negative tests and recorded closure of findings. |
| Client approver | Business impact, pilot population, exceptions, activation/replacement/retirement and residual risks. |

## Access lifecycle

Daily assessment and deployment use separate applications and requested permissions. Application bootstrap uses a distinct administrator connection and approval; do not retain bootstrap privileges as daily access. The exported **capability-and-access-matrix.md** derives diagnostic, read, write and bootstrap scopes from the loaded catalogue and implementation definitions. It avoids a competing manually maintained permission list. Declared permissions still need correct consent, assignment, role and successful access checks. Exchange/Purview uses separate delegated service RBAC; Graph consent does not provide it.

Onboard an engineer with approved workstation, secure workspace access, least privilege and the accepted runbook. Review each target tenant and account; GDAP/client boundaries remain explicit. Offboarding removes organisational access and approved assignments through the access owner, ends sessions and hands intact evidence to the custodian. Review consent, emergency exclusions, app ownership and engineer access at the organisation's chosen cadence. Do not delete application registrations automatically at project closure.

## Incident decision tree

1. **Incomplete read or unsupported service/module:** stop the affected plan. Retain collection status and source evidence. Check supported version, scope/consent/RBAC and the actual failing request; missing data remains unknown. Retry reads only after the cause is understood. See [Exchange/Purview](EXCHANGE-PURVIEW.md).
2. **Uncertain or interrupted write:** stop, preserve the entire workspace and journal, and identify the exact tenant/operation. Do not replay, clear records or restore an older backup. Use [unresolved-write reconciliation](UNRESOLVED-WRITES.md); a new capture alone cannot prove an ambiguous create failed.
3. **Accepted request with failed/unknown readback:** treat write acceptance and configuration verification separately. Re-verify through the supported evidence path; do not convert a transport success into a functional pass.
4. **Corrupt or unsupported evidence:** preserve originals, make a verified separate copy and escalate to the custodian/release owner. Do not edit digests, strip unknown fields, delete failing files or overwrite the active data tree.
5. **Bad protection or unsupported recovery:** preserve existing protection where possible, use emergency access and escalate to the client/security owner. Preview only supported ownership-bound containment/recovery. Broad targeting is not a pilot, and arbitrary deletion/automatic rollback is not supported. See [recovery](RECOVERY.md).
6. **Product defect:** collect the previewed support metadata and exact version/page/error classification. Review any additional private evidence before sharing. The release owner identifies affected source/packages and can withdraw the approved distribution; tenant remediation needs its own approval.

The handoff names the next owner, remaining actions and review date. Unresolved, failed and unverified outcomes stay visible. Storage policy, retention, signing and final support commitments require human business approval before general internal release.
