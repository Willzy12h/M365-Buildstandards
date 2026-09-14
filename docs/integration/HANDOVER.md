# Handover and priorities

## Current work

Preview.4 adds WAM sign-in, exact registered admin-consent callback, explicit existing-app repair, custom icon/GitHub metadata, approved engineer assignment and direct setup-to-connect handoff. Standard 2026.09.4 adds the supplied BitLocker candidate and optional long paths (14 recipes); other policy defaults require user input. See [application setup](../APPLICATION-SETUP.md) and [device automation](../DEVICE-AUTOMATION.md). Previous source namespaces and standard 2026.09.3 remain for compatibility and evidence. All new Microsoft interactions still require authorised live validation.


Preview.3 addresses Claude's review of `6712d8e`: explicit not-sent write classification, read-only re-verification, acknowledged historical evidence reconciliation, cancellable deployment reads and bounded follow-up capture. See [review dispositions](CLAUDE-REVIEW-RESPONSE.md). Original records remain unchanged; unknown modern requests remain blocked.

[PR #2](https://github.com/Willzy12h/M365-Buildstandards/pull/2), branch `astra/engineer-workflow`, targets integration and depends on [PR #1](https://github.com/Willzy12h/M365-Buildstandards/pull/1). Until #1 is merged, #2 includes its imported source. Both source repositories and Claude's coordination branch remain untouched. Merge through review.

The user authorised source development of app setup, account/exclusion automation, usability, selective recovery and licence visibility, and explicitly approved PR publication. No live tenant, consent or registration actions were performed in this development session.

## Implemented in the preview

- Complete durable before-evidence validation at the execution boundary, all plan/input bindings, private execution copies, licence and candidate-reference readiness.
- Truthful terminal states, separate write acceptance/readback, no write retry, single-use plans and unresolved-write protection.
- Forced silent token renewal after a GET 401; second rejection fails. Writes are never replayed.
- Missing properties remain different from JSON null in assessment and verification.
- Isolated delegated app-setup wizard: permission preview, explicit approval, two registrations/SPs, browser consent and actual grant/configuration/direct-assignment validation.
- One-time connections, optional saved profiles, account lookup and exclusions with purpose/reason/object-ID evidence. No name-based admin exemptions.
- Coherent WPF navigation and identity/access header, plan/exclusion review and result semantics; cooperative stop/shutdown; background report exports.
- Corrected report claims; historical run integrity preserved for records without acceptance metadata.
- Selective recovery: durable change register, exact returned IDs and before/after values, confirmed-creation deletion, latest inactive-update restoration and explicit CA disablement with drift review. Recovery/deployment share a tenant write lease. See [recovery](../RECOVERY.md).
- Main-page subscription counts, searchable assigned users and conservative direct-user policy scope checks. Unknown memberships and entitlement remain explicit review items. See [licensing](../LICENSING.md).

See [testing evidence](TESTING-EVIDENCE.md) for actual checks. Source and synthetic tests do not establish live acceptance.

## Remaining acceptance work

| Priority | State | Required verification / next work |
|---|---|---|
| P1 | Implemented but live-unverified | Authorised tenant: bootstrap sign-in, app creation, consent propagation, direct engineer assignment, separate assessment/deployment sign-in |
| P1 | Implemented but live-unverified | Capture pagination/details, actual permissions/licensing, all 12 candidate recipes, readback and supported inactive PATCH cases |
| P1 | Connected-session-unverified | Sign-in/capture/setup/policy cancellation and shutdown, network failure and recovery |
| P1 | Implemented; synthetic tests only | In an authorised disposable tenant, create a disabled/unassigned candidate, remove it and confirm absence; test restoration and reviewed CA disablement with full evidence |
| P1 | Manual recovery for unknown acceptance | Accepted writes now have read-only re-verification. Truly ambiguous requests still require separately reviewed reconciliation; no automatic rollback/adoption/delete-and-retry |
| P2 | Limited validation | Group-based assignment, PIM/custom-role effectiveness and Intune RBAC are not inferred from direct role/assignment reads |
| P2 | Acceptance pending | Human keyboard, screen-reader, high-DPI and representative large-tenant usability testing; offline rendering is narrower evidence |
| P2 | Manual-only scope | 33 controls have no creation recipe. Add each only with reviewed settings, supported API behaviour, safeguards and tests |
| Later | Deferred | App-only identity, automatic policy activation/assignment, cloud interface and extra workloads |

The next concrete acceptance task is **application setup → consent → engineer assignment → read-only capture → licence/user checks**, followed by candidate creation and selective recovery in an explicitly authorised disposable scope. The user plans to test the expanded preview. Repository approval does not authorise tenant operations.

Additional creation recipes remain follow-on work after these safety checks. Current manual controls often need client choices, provider dependencies or API validation; do not invent minimum OS versions, Defender risk thresholds, optional long-path policy settings or device targeting to claim broader automation. Keep the current 12 recipes explicit.

Model preference: advise the user when a different model suits the next task. Astra with High reasoning is recommended for recovery, authentication, deployment safety and architecture; reserve faster models such as Spark for bounded low-risk presentation changes. This is guidance, not a claim that a model setting was changed.

## Product decisions

CA candidates remain disabled with stored targeting and exclusions. Stored targeting is separate from enforcement; do not clear it to interpret the historical word "unassigned". Creator and chosen exclusions persist if a policy is later enabled. Intune candidates remain unassigned. Activation requires a separate reviewed workflow and tenant scope.

One-time connection means the profile is not saved; local audit evidence is retained. Disconnecting setup removes temporary tokens but does not revoke Entra consent. Business Premium remains the normal SME baseline; the planner uses captured service-plan evidence and blocks unavailable or unknown requirements.

## Status vocabulary

Implemented and verified names the actual check. Implemented but unverified identifies missing verification. Planned is agreed work; Candidate is a proposal; Deprecated is superseded behaviour; Known issue is an observed defect. Passing tests do not verify sign-in, consent, live Graph or recovery.
