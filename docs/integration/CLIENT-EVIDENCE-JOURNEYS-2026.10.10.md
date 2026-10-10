# Client-bound editors and offline evidence — PR #81

This app-only source slice extends own #80/#73 through the existing tenant, manifest-verified standard and ReviewedClientScope contracts. Historical bytes, persisted schemas, authentication/cache architecture, engine deployment/recovery and Claude’s Scripts/runner remain unchanged.

## Engineer behaviour

Connect now offers **Review saved evidence offline** for an explicitly selected saved client. It resolves the exact persisted validated profile, refuses active connection/setup or a busy workspace, releases pending discovery and clears old transient evidence/approval. Unsaved form edits do not choose the offline target. Open its stored capture on Configuration, then review local assessment, reports, jobs and the change register. Nothing authenticates, consents or makes tenant requests.

The header keeps original capture source/time/completeness separate from connection access. Saved profile identity, reference domain and exact IDs remain copyable. Historical/scoped evidence never authorises writes. Assessment explains an empty filtered view and retains the original Exchange-only capture time.

Manual checks and exceptions reset unfinished fields on changed tenant, standard digest or material client inputs. Harmless labels and ordinary same-context refresh preserve a draft. Foreign/stale rows and direct save/delete actions refuse before persistence or a deletion modal. Local register mutations also refuse while busy.

Recovery retains durable same-tenant rows offline while clearing preview/approval on disconnect or replaced/materially changed authority. Current-row/attempt membership and verified tenant/operator/application remain prerequisites. Each disabled action states its next step; read-only re-verification and separately reviewed recovery remain distinct. A tenant change during local register loading refuses the result.

AST-20261010-15 additionally closes an existing active-profile tenant-edit gap: the real SaveProfileCommand now clears all prior tenant transient capture/assessment/access/plan/run state when the tenant ID changes. Durable history is retained. The header defensively refuses mismatched capture attribution.

## Validation identity and limits

The original unchanged product at 55ed373 executed five failing defect cases and two passing preservation controls in Windows push 38077676842 / PR 38077679357 (172 App passes, 5 failures, zero skips). The earlier missing-namespace run was a build failure, not regression proof.

The repair defines 31 journey cases: seven original, eight extended and sixteen offline cases. Actual saved-client/save-profile commands and persisted files cover cold start, adoption, target selection, cancellation/release, context drift and refusal. Reconstructed Workspace/Shell is an in-process restart boundary, not a new operating-system process. Reflection-injected recovery/connection fixtures prove state-reset semantics only.

Strict Linux .NET 10 App.Tests and UiReview cross-builds passed zero warnings/errors on the bounded writer patch 89cc983c1236229e86afc2c976429cac76a20c99304c7a2ff72a853980b72ba5. Actual Windows execution, the test-only HWND-bound integration, native three-size journeys and fresh package checks remain required on the committed final head. The native journey uses actual selection/buttons for offline activation, saved capture opening and local register loading, plus persistent header, filter guidance and editor binding assertions.

Claude independent review remains separate from the delegated bounded review. Human newcomer/Narrator/physical-scaling/second-engineer and live Microsoft gates stay open. William selected guided visual concept A with persistent visibility and an optional separate details window; that detached window is a synthetic design proposal, not an implemented product capability.
