# Handover and priorities

## Current work

[PR #2](https://github.com/Willzy12h/M365-Buildstandards/pull/2), branch `astra/engineer-workflow`, targets integration and depends on [PR #1](https://github.com/Willzy12h/M365-Buildstandards/pull/1). Until #1 is merged, #2 includes its imported source. Both source repositories and Claude's coordination branch remain untouched. Merge through review.

The user authorised source development of app setup, account/exclusion automation and usability improvements. No live tenant, consent or registration actions were performed in this development session.

## Implemented in the preview

- Complete durable before-evidence validation at the execution boundary, all plan/input bindings, private execution copies, licence and candidate-reference readiness.
- Truthful terminal states, separate write acceptance/readback, no write retry, single-use plans and unresolved-write protection.
- Forced silent token renewal after a GET 401; second rejection fails. Writes are never replayed.
- Missing properties remain different from JSON null in assessment and verification.
- Isolated delegated app-setup wizard: permission preview, explicit approval, two registrations/SPs, browser consent and actual grant/configuration/direct-assignment validation.
- One-time connections, optional saved profiles, account lookup and exclusions with purpose/reason/object-ID evidence. No name-based admin exemptions.
- Coherent WPF navigation and identity/access header, plan/exclusion review and result semantics; cooperative stop/shutdown; background report exports.
- Corrected report claims; historical run integrity preserved for records without acceptance metadata.

See [testing evidence](TESTING-EVIDENCE.md) for actual checks. Source and synthetic tests do not establish live acceptance.

## Remaining acceptance work

| Priority | State | Required verification / next work |
|---|---|---|
| P1 | Implemented but live-unverified | Authorised tenant: bootstrap sign-in, app creation, consent propagation, direct engineer assignment, separate assessment/deployment sign-in |
| P1 | Implemented but live-unverified | Capture pagination/details, actual permissions/licensing, all 12 candidate recipes, readback and supported inactive PATCH cases |
| P1 | Connected-session-unverified | Sign-in/capture/setup/policy cancellation and shutdown, network failure and recovery |
| P1 | Manual recovery | Ambiguous writes require manual reconciliation. No automatic rollback/adoption/delete-and-retry; retain evidence. A supported reconciliation/resume UI remains future work |
| P2 | Limited validation | Group-based assignment, PIM/custom-role effectiveness and Intune RBAC are not inferred from direct role/assignment reads |
| P2 | Acceptance pending | Human keyboard, screen-reader, high-DPI and representative large-tenant usability testing; offline rendering is narrower evidence |
| P2 | Manual-only scope | 33 controls have no creation recipe. Add each only with reviewed settings, supported API behaviour, safeguards and tests |
| Later | Deferred | App-only identity, automatic policy activation/assignment, cloud interface and extra workloads |

The next concrete acceptance task is **application setup → consent → engineer assignment → read-only capture** in an explicitly authorised tenant, before candidate deployment. The user plans to test the expanded preview. Repository approval does not authorise tenant operations.

## Product decisions

CA candidates remain disabled with stored targeting and exclusions. Stored targeting is separate from enforcement; do not clear it to interpret the historical word "unassigned". Creator and chosen exclusions persist if a policy is later enabled. Intune candidates remain unassigned. Activation requires a separate reviewed workflow and tenant scope.

One-time connection means the profile is not saved; local audit evidence is retained. Disconnecting setup removes temporary tokens but does not revoke Entra consent. Business Premium remains the normal SME baseline; the planner uses captured service-plan evidence and blocks unavailable or unknown requirements.

## Status vocabulary

Implemented and verified names the actual check. Implemented but unverified identifies missing verification. Planned is agreed work; Candidate is a proposal; Deprecated is superseded behaviour; Known issue is an observed defect. Passing tests do not verify sign-in, consent, live Graph or recovery.
