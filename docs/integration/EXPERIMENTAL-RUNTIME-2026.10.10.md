# Experimental desktop runtime — INT-082 / PR #73

Source implementation, live-unverified. Based on verified #55 merge `8e9c323` and refreshed with verified #56 merge `d6582df`. This does not close R07/R10/R11 human or live acceptance, nor authorise publication.

## Engineer behaviour

Read-only assessment, plan/operation previews, stored evidence and local exports retain their existing access. Tenant changes are off by default. On Connect, review the verified deployment tenant/account, application and returned scopes, then deliberately enable experimental changes for that session. Every operation still needs its existing exact review, complete before-evidence, tenant confirmation and engine checks. The header distinguishes write-capable access from an enabled experimental opt-in. Deploy, policy automation and recovery show the reason and next step even when their actions are disabled.

Opt-in is never saved, exported or copied into a support bundle. It binds the connection object, verified tenant/account/operator, deployment registration, Microsoft Graph resource/mode, exact returned scope set, catalogue integrity digest and canonical `ReviewedClientScope` digest. Changing these, disconnecting/reconnecting or reaching the original token expiry closes it. A renewed token does not extend the approval. An expired recorded session is refreshed on an explicit reconnect through the existing known-account silent-first path; it cannot remain trapped in the old reuse shortcut.

## Access requests and authentication

An unchanged verified session uses the existing reuse path. Known-account reconnect still tries the protected MSAL cache first. Read-only assessment/discovery shows its exact registration/resource/scopes only if explicit connection needs interaction; page changes, local plan review and silent renewal never open that dialog. A deliberate new deployment/bootstrap access request requires an unticked exact request preview before acquisition. Its five-minute approval can cover that request's interactive fallback without another approval popup.

Pre-sign-in approval authorises only the displayed access request; tenant/account are not treated as verified until returned identity checks succeed. Application setup has its own verified administrator context and exact plan approval. Standalone and chained consent steps show the actual target application's scopes, initiating setup operator and verification limitations. Changing setup/consent invalidates any coexisting deployment opt-in. Browser success is not verification of grants or the consenting actor.

Silent renewal has no interactive route. It verifies returned tenant, home account, operator ID and exact normalised scope set before replacing cached authorisation. Unexpected identity/access clears experimental authority and marks the desktop operator unverified; reconnect and review again. Only the existing provider and protected cache are used. No new access scope, permission grant, registration, cache, endpoint or broker policy is added. Assessment/deployment/bootstrap registrations and Microsoft Graph remain separate under APPLICATION-SETUP. Microsoft CA/MFA/consent can still require interaction; zero prompts is not promised.

## Boundaries and retained safeguards

The desktop guard covers candidate deployment, reviewed activation/assignment/retirement changes, Entra LAPS, package publication and recovery in both command availability and direct coordination entry points. Setup uses independent verified-context manual approval; consent and explicit scope acquisition use visible access previews. Guard closure cannot unsend an accepted write.

Complete before-evidence, single-use plans, exact operation approval, typed tenant confirmation, tenant/account pinning, per-tenant write leases, unresolved-write blocking, no replay and after-readback remain in the engine. CA candidates remain disabled with reviewed targeting/exclusions; Intune candidates remain unassigned. Exchange changes remain copy-only. Historical standards, evidence and cache-retention rules are unchanged. No live service, module, tenant, consent or DNS action was performed by the agent.

## Validation status

47 focused Linux synthetic tests pass across guard, acquisition and renewal policy seams, including existing connection prompt-count cases. Windows App boundary tests and native access-dialog renders at 960×760, 680×620 and 540×460 are added but require exact-head Windows CI. The new dialog starts unticked; merely ticking it does not authorise a request. Failed, cancelled or unapproved requests do not become successful sign-in.

The full restored engine/CLI suite passed 1,414 tests with no failures or skips. Mutation checks removed each safeguard temporarily: desktop refusal removal caused 16 failures, access-preview removal six and renewal-binding removal seven. The original source was restored before the full passing run. These are synthetic offline checks, with no Microsoft calls. Strict cross-build is recorded separately from native Windows proof. Newcomer, Narrator, physical scaling, Microsoft WAM/CA/MFA/consent and live deployment remain unperformed. Independent Claude source review is required before merge.
