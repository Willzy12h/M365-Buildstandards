# M365 BuildStandard: connection and usability release goal

## Goal

Deliver and validate a more understandable, less repetitive engineer workflow in PR #19, from Microsoft sign-in through application setup, read-only capture and readiness for reviewed deployment. Continue the .NET 10 WPF application as Preview.16. Keep standard 2026.09.30 and all published catalogue bytes unchanged.

Short ChatGPT desktop goal:

> Finish the M365 BuildStandard usability update in PR #19, following docs/integration/QOL-GOAL-2026.10.01.md.

This supplements [the release goal](RELEASE-GOAL-2026.09.30.md), which still governs the standard, evidence and release boundaries. It is source-development and offline-validation authorisation, not permission for the agent to sign into a real tenant, grant consent or make tenant changes.

## 1. Connect without unnecessary data entry

- Add **Quick Connect with Microsoft** for the client's own work or school account. Use WAM by default, with an explicit system-browser option. Discover and show the organisation name, tenant ID, primary domain and account. Require a deliberate confirmation before adopting a read-only connection.
- Do not require the engineer to type the client label, tenant ID or application ID for the existing shared assessment fallback. Respect installations that disable that fallback. Dedicated tenant applications still require their correct IDs; setup fills them after creation.
- Keep discovery separate from assessment and deployment. Restrict discovery to organisation/account reads; reject mismatched tenant or operator identity. A missing identity is not a successful connection. Show previously consented write scopes truthfully while keeping the assessment transport read-only.
- A one-time connection must not inherit another client's exceptions, targeting, application IDs or saved-profile identity. Saving remains optional. Cancelled discovery or confirmation must not replace the current connection or save a profile.
- Add a separate **Partner / GDAP** route. Select the customer explicitly through its saved connection or tenant ID; show Microsoft's account chooser for the partner identity in that tenant. Do not assume the partner's home tenant is the customer, invent access, enumerate customers without a supported scope, or claim live GDAP compatibility from synthetic tests.

**Acceptance:** tests cover tenant/operator mismatch, confirmation, cancellation, fallback disabled, customer selection and profile isolation. Normal tenant-pinned authentication and all deployment boundaries remain enforced.

## 2. Reduce avoidable authentication prompts

- Page navigation must not authenticate. Check access, capture and ordinary reads use the current session and silent token renewal.
- Repeated connection actions for the same verified tenant, mode, application and profile reuse the session where possible. Explicit reconnects try a matching cached account before opening an interactive prompt.
- Keep account selection deliberate when requested. Changing tenant, application or permission mode must preserve identity checks and obtain the correct token. Setup, assessment, deployment and administrator consent remain distinct security contexts.
- Explain prompts that Microsoft still requires for consent, MFA, Conditional Access or expired/revoked access. Do not promise a single prompt across different applications, bypass MFA, persist new secrets or silently escalate permissions.

**Acceptance:** tests reject session reuse after identity, mode, application or profile changes. Source review confirms navigation is local. Live WAM/MFA prompt counts remain a separate authorised acceptance check.

## 3. Make application creation and consent obvious

- Present a clear sequence: **administrator sign-in → review and approve creation/configuration → browser consent → verify access → connect**. A preview must visibly say that no applications have been created yet.
- Put **Approve and create/configure applications** directly with the reviewed tenant and app summary. Collapse lengthy permission/payload details by default, keep them accessible, and bring the approval section into view after preview. Do not bury the required action below long tables.
- Remove repeated tenant-ID typing from **application setup** once the setup session has verified the tenant. Require an explicit approval of the displayed tenant and plan, bound to the current verified operator and a fresh eligible plan. Changing identity or setup inputs invalidates approval. This does not remove typed confirmation for policy deployment or other consequential workflows.
- Explain that normal desktop authentication uses WAM, while tenant-wide administrator consent uses Microsoft's browser approval endpoint. Name the application being approved, explain **Accept → return to the tool**, and read actual grants back instead of treating browser success as proof.
- Show both application names, their **Application (client) IDs**, and verified enterprise-application object IDs when available. Distinguish configured/proposed names from observed creation results and unverified values. Make the identities and next steps copyable.
- Tell the engineer precisely what remains: repair configuration, approve assessment/deployment permissions, assign the intended user, recheck propagation, or connect. Explain where to find each enterprise app in Entra using its application ID, that object IDs differ, and that these are desktop sign-in applications rather than websites launched from My Apps.
- Preserve existing IDs when returning to setup for the same tenant. Never reuse them for another tenant or automatically create duplicates when an app may already exist.

**Acceptance:** synthetic tests exercise every readiness state, changed/missing identity, missing approval, expired plan, existing-app handoff and cross-tenant clearing. Native UI evidence shows the approval action and IDs at the supported window sizes. Application setup still records complete before evidence and durable intent, validates the issued plan and performs no ambiguous write retry.

## 4. Make deployment setup recoverable

- Explain that **no deployment application configured** means no dedicated deployment client ID is recorded for the selected connection; it does not prove that the enterprise app is absent in Entra.
- Route the deployment page directly to setup for the connected client, carrying the appropriate known application IDs. Offer **Configure deployment application** when the ID is missing and **Enable deployment access** when configured.
- Explain the existing-app path: enter the correct deployment application ID, approve its permissions, confirm assignment/access, then continue to deployment. Never substitute the shared or dedicated assessment application.
- Keep a fresh capture, reviewed plan, evidence acknowledgement and final typed tenant confirmation necessary before policy writes.

**Acceptance:** a missing-ID action reaches the correct client's setup without attempting sign-in or a write; another client's edit buffer cannot change the target. Application existence, granted permissions and effective access remain separate statuses.

## 5. Explain read-only capture and fix the reported collection failures

- Clearly label tenant configuration and capture as **read-only**, including when the current session has write-capable permissions. Explain that capture reads Microsoft Graph and saves local evidence; it does not create, update, enable or assign objects.
- Replace blank object-state cells. **Not reported** means the response has no state field; **Unknown** means null, empty or unexpected data. Neither is a compliance verdict or evidence that an object is enabled/disabled. Keep targeting and collection completeness distinct.
- Correct authentication-method and passkey reads using documented parent responses. Preserve modern passkey profiles; missing or partial arrays must never become a successful empty collection or authorise legacy replacement.
- Read compliance scheduled-action rules and their action configurations explicitly instead of relying on the failing expanded relationship. Preserve pagination, route restrictions, API version, cancellation and completeness checks.
- Record the actual detail failure in the collection's existing error field and show it in the UI. Incomplete captures remain incomplete, dependent findings remain unable to assess, and deployment stays blocked by evidence requirements. Do not fix a dashboard by hiding errors or marking unknown settings compliant.
- Inspect supplied exports locally as diagnostic evidence only. Use independent synthetic fixtures in source/tests; commit no tenant captures, client identifiers, tokens or credentials. A corrected request still needs a new authorised capture to verify service behaviour.

**Acceptance:** HTTP fixtures assert the exact supported read requests; missing/malformed/truncated responses and detail failures remain unknown. Tests demonstrate that these guards fail when removed. All published standard hashes remain unchanged.

## 6. Repair and prevent the text-box context-menu failure

- Investigate the reported missing `Accessibility, Version=4.0.0.0` error. Verify the matching DLL and dependency-manifest entry in the actual self-contained package.
- Give an actionable early error for an incomplete/corrupt extraction instead of an unexplained right-click failure mid-session. Preserve the full portable runtime; do not add a requirement to obtain individual DLLs manually.
- Extend Windows validation to open the text-editing context menu and copy synthetic text in the actual freshly extracted application, as well as checking checksums and normal startup/shutdown.

**Acceptance:** the extracted package passes the context-menu interaction and records no missing-assembly/UI error. Distinguish an intact new package passing from confirmation that the user's older/local extraction was repaired.

## 7. Release evidence and definition of done

- Strict .NET 10 Release builds and relevant engine/application tests pass with no warnings, failures or skipped safeguards. Keep meaningful regression cases and document changes to earlier UX contracts.
- Run native Windows UI checks at 1480×940, 1180×760 and 1180×640. Include Quick Connect confirmation, visible setup approval and application identity/access guidance. Check bindings, reachability, clipping and commands without calling Microsoft services.
- Publish a new self-contained Windows x64 preview ZIP, ZIP/internal checksums, exact source revision, test results, native screenshots and fresh-extraction/context-menu evidence through PR #19. Record what was actually executed and any blocked visual inspection.
- Update application-setup instructions, changelog, decisions, comparison evidence and completion register. Keep the PR draft until its required review and outstanding acceptance are clear; do not merge or publish a production release automatically.
- Report live WAM, consent, GDAP, actual capture success, physical accessibility and tenant/device outcomes as pending unless separately performed with explicit authorisation. Passing synthetic checks is not live acceptance.

The outcome is a concrete, reviewable Preview.16 with the requested workflow improvements, clear explanations and evidence for the claimed fixes. Production rollout and the separate device-toolkit products are outside this goal.
