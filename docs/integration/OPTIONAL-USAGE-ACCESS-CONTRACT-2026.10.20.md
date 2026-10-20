# Deliberate optional usage-report access — INT-133

Decision-only refinement of approved expansion F / INT-129. Base main `54789738c86c2217e7793620f9d566f3768d6be4`. William already approved optional Reports.Read.All source development and delegated routine recommendations within A–J; this introduces no new service permission or live authority. Contract review/checked merge must precede dependent optional-access source. Required named authentication/Graph/context/evidence/recovery reviews remain before implementation acceptance.

## Problem and smallest change

Current `TenantConnectionService.ScopesFor` and `ApplicationSetupService.RequiredScopes` derive requests from the standard plus diagnostic scopes. The four usage endpoints need the separately approved Reports.Read.All, which historical catalogues must not acquire by editing their bytes. A backend alone is not an engineer-usable connection journey.

Use one fixed typed **optional usage-report read access** choice, default off. Its only extra scope is Reports.Read.All for the existing public-cloud delegated Graph resource. No arbitrary scope list, application-only flow, report-settings permission, Directory.Read.All addition, new cache or second authentication implementation. This is not a general optional-permission framework for unimplemented packages.

## Connection and consent preview

- Reuse the existing native connection/access review, verified account/tenant, MSAL broker, known-account silent acquisition, protected cache, cancellation, expiry and sign-out contracts. No authentication from navigation or background refresh.
- The engineer deliberately selects usage-report access and chooses Connect/reconnect. Before acquisition, show the target tenant, registration name/app ID, Reports.Read.All purpose, role/licence caveats and that the request reads usage reports without licence/privacy changes. It is consent-capable authentication, not proof consent is granted.
- Freeze the selected access intent with the connect operation. Changes to client, account, registration, permission mode or access selection invalidate its preview/reuse decision. Reuse only a verified matching account/tenant/registration and an adequate actually returned scope set. A session without Reports.Read.All cannot satisfy this intent merely because tenant/mode match.
- If the existing session already has valid matching access, reuse it under the existing lifetime/expiry rules. Otherwise use existing silent-first acquisition during the explicit connection action; Microsoft may require interactive CA/MFA/consent. Never request extra access during a report or renew to a changed identity/scope set mid-read.
- The reporting action independently checks actual returned scopes, current client/account/resource and presentation generation before/after awaits. Missing access explains the deliberate connection/setup next step; it never launches setup, consent or sign-in itself.
- Preserve the existing optional fallback policy and warnings. The selection cannot enable a disabled fallback, suppress broader-token notices or alter assessment/deployment mode guards. Disconnect/sign-out retain current cache policy; no cache enters evidence, transfers or support.

## Application setup

Reuse the existing setup review/planner. A separate explicit optional-usage choice determines whether Reports.Read.All appears in the reviewed delegated permission configuration for the selected existing assessment/deployment registration. Show app names/IDs, original/required permissions, affected registration and consent impact. A changed choice invalidates the exact setup preview/approval.

Reuse an existing appropriate registration and permission when present; do not create duplicate apps. Selecting None requests ordinary baseline access; it does not automatically remove existing app permissions/consent. Preserve the current exact reviewed setup-write contract, operator/tenant binding, permission-ID resolution, ownership and no-replay rules. Developer source work performs no setup writes or consent. ReportSettings.Read.All remains excluded; CSV privacy stays Unknown and verified joined licence suggestions remain explicitly unavailable.

No persisted tenant-profile/evidence schema or historical standard changes are needed for this ephemeral access intent. If implementation finds a persisted-schema change necessary, review that separately before adding it.

## Usage evidence and existing transfer continuity

The accepted strict usage pack/import/export maximum stays40MiB. The existing managed workspace transfer maximum is32MiB per file and must not be raised globally or bypassed. For this slice, saving a usage pack into `data/tenants/<tenant>/usage-review-evidence/<id>.json` is additionally limited to32MiB, using the existing immutable atomic record writer, exact tenant/record binding, link refusal and no overwrite.

A valid pack over32MiB remains available as original, separately exported JSON outside managed data; it must not be silently clipped, split, re-labelled saved or partially inserted into the managed store. Preserve the exact original pending bytes, original tenant/account and clear Save/Export/Discard recovery. A save failure must not trigger another service read. Another usage run and ordinary close remain blocked until the original is explicitly resolved; a forced termination can still lose in-memory recovery. State the32MiB managed-save versus40MiB pack distinction in the UI/guide. Do not claim a separately exported file was included in a managed handoff.

Existing workspace backup still includes all managed JSON records under data without changing their bytes. The new folder is separate from snapshots, Graph/application/Exchange report schemas and deployment before-evidence. Supplied historical packs retain source claims and integrity limitations; their original bytes are never rebound to a live connection.

## Required evidence

Failing-first meaningful regressions for: None/selected scope requests; immutable historical catalogues; exact app/tenant/account/access preview and setup invalidation; existing-session reuse versus missing scope; silent-first/cancel/interaction-required outcomes without navigation sign-in; actual returned scope admission and assessment-write refusal; selection/context changes during capture/export; immutable save/no overwrite/link/wrong-tenant/record refusal;32MiB managed and40MiB import/export boundaries; backup/restore unchanged bytes; pending original retained across client change, failed save/export and deliberate resolution without rerun.

Use synthetic authenticators/HTTP/CSV/local files only. Source-matched Windows/App/native/portable checks remain required, plus named safety review. Human accessibility/H1/live role/licence/service/consent remain unrun unless separately authorised and actually recorded. No version bump, tag, publication, protection change, new backlog or generic script execution.
