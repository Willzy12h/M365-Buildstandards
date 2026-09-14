# Live Microsoft 365 validation required

Unit tests exercise safety logic with a scripted Graph client. They cannot prove that Microsoft Graph accepts every payload or that permissions behave as documented. Complete the following in an explicitly authorised BDIT test tenant before the toolkit is used against a client tenant. Record outcomes on the Manual checks page of the test tenant profile. Approve a disposable test scope separately; repository development does not authorise these operations.

## Environment

1. A test tenant with Business Premium (Entra ID P1, Intune Plan 1) and at least two test users, one Windows device enrolled in Intune, one iOS device if available.
2. Use the application setup wizard to create **BDIT Tenant Assessment** and **BDIT Tenant Deployment** in the authorised test tenant, or validate existing IDs. Each is a single-tenant public client with redirect URI `http://localhost`; consent and engineer assignment are separate. See [application setup](APPLICATION-SETUP.md).
3. Two emergency access accounts, one named location (office), one MAM-only group.

## Authentication and connection

| Check | Expected |
|---|---|
| Connect read-only with the assessment registration | Browser sign-in completes; header shows tenant name, primary domain, "verified", operator resolved and verified |
| Connect with the wrong tenant ID in the profile | Connection refused with a tenant mismatch message; no snapshot possible |
| Connect with an account lacking read roles | Access check lists the failing collections with the expected scope |
| Enable deployment access | Second sign-in with the deployment registration; mode badge turns amber; write scopes observed |
| Disconnect, then reconnect | A fresh Microsoft sign-in is required (cache removed) |
| Token expiry during a long capture | Silent renewal works; if Microsoft requires interaction the operation fails with a clear reconnect message |
| Admin-consent browser return on temporary localhost port | Record whether callback succeeds. On AADSTS50011/failed redirect, stop waiting and Validate setup; inspect actual grants before any further creation |

## Collection

| Check | Expected |
|---|---|
| Read tenant configuration | Every collection *Collected*; beta collections flagged in limitations; users and groups resolve names in reports |
| Revoke `DeviceManagementApps.Read.All` and capture again | Apps and app protection *Not collected*; dependent controls *Unable to assess*, never *Missing* |
| Tenant with more than one page of users | Pagination completes; count matches the portal |

## Licence overview

| Check | Expected |
|---|---|
| Connect and inspect Overview and licences | Actual SKU/status, enabled, consumed, available, warning and suspended counts agree with the captured Graph response; missing values stay unknown |
| Load user assignments, select each subscription and search | Names, UPNs and exact IDs match the selected SKU; search narrows the list; count mismatches are explained |
| Build a direct-user CA plan with one deliberately unlicensed disposable test user | Assignment gap appears; excluded direct users are omitted from the scope count |
| Group/role/guest or device scope | Explicit scope-unknown result; no entitlement inferred from tenant seats |
| Failed licence/user read or tenant switch | Incomplete status; no previous tenant's user list or false zero/pass |

## Assessment

| Check | Expected |
|---|---|
| Existing MFA policy named differently | CA-001 reports *Compliant* (if enabled) or *Settings match, not enforced* with the policy name |
| Policy with the standard name but different settings | *Partial match* with property-level differences |
| Approved deviation for a missing control | *Compliant (approved deviation)*; the underlying finding appears in the notes |

## Deployment (each recipe once)

For each of CA-001, CA-003, CA-004, CA-005, CA-006, CA-007, CA-008, CA-009, CA-010, CMP-WIN-001, CMP-IOS-001 and CFG-WIN-003:

| Check | Expected |
|---|---|
| Graph accepts the create payload | HTTP 201, object ID returned, readback *Pass* |
| Conditional Access object in the portal | State **Off**; emergency accounts and the operator in excluded users; expected include targeting present |
| Intune object in the portal | No assignments |
| Run the same plan again | Rejected as already used; no Graph write |
| Capture again and build a fresh plan after verified creation | Owned matching candidates produce *NoChange*; no duplicate objects |
| Fresh plan after an unresolved write | Affected control remains blocked pending manual reconciliation; no replay |
| Edit the created object in the portal, then plan again | Row is *Drift*; the toolkit refuses to update it |
| In a separately approved disposable scope, enable a created Conditional Access policy, then plan again | Normal deployment remains blocked/manual; only the separate reviewed recovery action may disable it |
| Create an unrelated policy with the standard name, then plan | Row is *Conflict*; nothing is adopted or created |

Known payload risks to verify explicitly: `scheduledActionsForRule` on compliance policy creation (v1.0), `osMinimumVersion` formats, OMA-URI custom policy acceptance for CFG-WIN-003, `deviceFilter` rule syntax for CA-008, `includeUserActions` for CA-009 and CA-010.

## Selective recovery

Start with one disposable candidate and confirm its exact returned ID before testing removal. Keep an independent administrator session and agreed emergency access available. Do not deliberately activate a broadly targeted policy to test containment.

| Check | Expected |
|---|---|
| Create a CA candidate and an unassigned Intune candidate, then load the change register | Exact IDs, creation acceptance and readback status agree with local runs and Graph |
| Preview removal of the original creation | Fresh complete snapshot, current object/assignments, exact ID and permanent-deletion consequence shown |
| Approve and type the correct tenant ID | One DELETE; accepted outcome retained; direct 404 plus collection absence verified; mapping removed only after verification |
| Restore the latest supported inactive update | Only recorded changed settings return to their before-values; object stays disabled/unassigned; prior mapping restored |
| Change the object after preview or select a different tenant/operator | Recovery blocked before any write; require a fresh preview |
| Separately approved CA activation with only disposable targets | Disable preview highlights drift; explicit drift acknowledgement required; only state changes, current targeting/exclusions remain stored |
| Assigned Intune object or missing creation/ownership evidence | Recovery blocked; separate administrative procedure required |
| Interrupted/uncertain recovery request | No retry; intent/outcome retained; affected control blocked pending reconciliation |
| Recovery accepted but readback unavailable | Accepted and unverified displayed separately; preserve evidence and independently verify outcome |
| After delayed deletion, choose Re-verify recovery in assessment mode | Reads only: direct 404 plus complete collection absence, separate verification evidence, mapping finalised; original recovery record unchanged and no second DELETE |
| Accepted deployment readback fails, then Re-verify deployment | Exact ID, latest ownership, matching settings and disabled/unassigned state verified; no repeated POST/PATCH |
| Re-verify an Unknown modern write | Refused even if a search returns nothing; original block retained |

Record actual device/sign-in effects separately from Graph configuration verification. See [recovery](RECOVERY.md) for supported routes and limitations.

## Failure handling

| Check | Expected |
|---|---|
| Disconnect the network during a write | Run stops with *Review required*; acceptance and configuration remain truthful. A returned ID/mapping may already be recorded; unknown acceptance is not failure proof. After capture is attempted and affected controls remain blocked pending reconciliation |
| Close or Stop during a hung preflight/readback/after-capture | Reads cancel; incomplete after evidence retained. An in-flight policy request keeps its configured timeout (default 100 s). Record actual elapsed time; readback and after-capture each have a 60 s budget when not cancelled |
| Force token renewal failure before a policy request in an approved disposable test | NotAttempted / NotRun and Review required, no HTTP mutation, fresh reviewed plan allowed; original plan remains single-use |
| Start after an interrupted policy run | Overview calls attention to interrupted evidence; unknown outcomes remain blocked |
| 429 throttling during capture | Reads retry honouring `Retry-After`; log shows the wait |

## Reports

| Check | Expected |
|---|---|
| Engineer HTML and client summary | Names, not GUIDs, for groups, users and locations; client summary contains no raw JSON |
| Excel export opened in Excel | Every cell is text; a display name beginning with `=` does not execute |
