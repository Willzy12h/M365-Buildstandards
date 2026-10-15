# What stands between today and M365 BuildStandard 1.1.0 (non-preview)

Read from `integration` at `2536216` on 7 October 2026. No code was changed.

## Where it stands

- Source is `1.1.0-preview.19`, unpublished. The only release is the `v1.1.0-preview.18` prerelease (6 October). No open PRs or issues.
- The definition of "finished" is already agreed in `docs/integration/PRODUCT-COMPLETION-PLAN.md` (work packages A–G, approved 6 October). Most of the source work in A–F has landed, including the job, observation, disposition, cutover and upgrade-impact work merged today (#22–#32).
- **Nothing has ever run against a live tenant.** Every result so far is synthetic tests, CI on Windows and offline UI renders. `docs/LIVE-VALIDATION.md` and `docs/CONTROLLED-ACCEPTANCE.md` are prepared but unexecuted. That is the main gap between "preview" and "product".

## Priority 1: release blockers

| # | Item | Who | Notes |
|---|---|---|---|
| 1 | **Live acceptance in a disposable tenant** (Stage A read-only: setup, consent, sign-in, capture, assess; Stage B: create disabled CA-001 and unassigned CFG-WIN-011, recover and remove them; then a separately approved pilot) | **William** (tenant, admin, two emergency accounts, a licensed pilot user, an enrolled test device, and approval for each consequential step) | Claude can prepare the run sheet and evidence forms from CONTROLLED-ACCEPTANCE, and review sanitised results. Claude cannot sign in to a tenant. Tenant exports stay out of the repo. |
| 2 | **Decide the supported scope from what passed** | William decides; Claude implements | Anything not live-accepted must be labelled experimental or manual *and gated in the app*, not just in a document (plan item A). Proposed default: Windows 11 x64, SME/Business Premium baseline, client-owned assessment and deployment registrations. |
| 3 | **Independent review of PR #22–#32** | William runs Astra/Codex | They merged without the other agent's review. Claude reviewing its own work does not count as independent under AGENTS.md. |
| 4 | **Name the owners** (product, standard, release, security/access, evidence custodian, support) and set evidence storage/retention and the exception/deferral closure policy | **William** | `docs/INTERNAL-OPERATING-MODEL.md` lists the roles as proposals only. One person can hold several. Until a deferral policy exists, a job with any deferral can never show as complete (DECISION-LOG). |
| 5 | **Signing and distribution decision**: organisation-managed Authenticode signing, or an explicitly approved unsigned, hash-pinned internal channel | **William** | Claude can wire signing into CI once a signing facility exists. Claude never handles the key. |
| 6 | **Human usability and accessibility checks**: Narrator, keyboard-only, physical 125%/150% scaling, and a second engineer who did not build it running the new-tenant and legacy journeys unaided | **William and a second engineer** | `docs/TESTING-THIS-BUILD.md` is the 15-minute checklist. Automated harness checks already pass, but the plan needs human checks as well. |
| 7 | **Repository and release settings**: branch protection on `main`, required reviewers on the `release` environment, immutable releases | **William** (repo settings) | Until reviewers are set, the `release` gate is documentation only (comment in `publish-preview18.yml`). |
| 8 | **A reusable publisher**: `publish-preview18.yml` is hard-pinned to run 37515288607 and to Preview.18 | **Claude** | Parameterise it by version and run ID, keeping the current checks (exact source, original ZIP, independent SHA-256, draft then publish). |
| 9 | **Promote `integration` to `main`** at the Preview.19 release candidate, through a merge-commit PR, then tag the commit CI built | Claude prepares; **William approves** | This is the plan you already approved. Items 3, 7 and 8 come first. |

## Priority 2: needed for GA, mostly Claude's to do

| # | Item | Who |
|---|---|---|
| 10 | Publish Preview.19 as a prerelease so engineers can use it, and collect their feedback | Claude prepares; William approves the publish |
| 11 | Usability fixes from that engineer feedback (HANDOVER's next item after review) | Claude, once feedback exists |
| 12 | Bring the trackers up to date. Feedback register rows R01–R05, R07, R10, R11, R15 and R16 still say "claimed in #19" although most of that work has merged. WORK-CLAIMS still lists #20 as Open, and the top of COMPLETION-REGISTER still calls #20 open. | Claude |
| 13 | Finish R10, the current-guidance clean-up (labels, confirmation wording, one start guide), and check what remains of R07 (actionable module/role/app preflight) | Claude |
| 14 | Fill in the release record for 1.1.0 (the promotion record table in `docs/RELEASE-AND-SERVICING.md`: source, CI runs, artifact hash, standard digest, capability matrix, known limits, approvers), bump the version to `1.1.0` and write the CHANGELOG entry | Claude drafts; William and named approvers sign |
| 15 | Servicing cadence: a monthly review of the runtime, MSAL and modules, and a quarterly review of standard defaults and licences | William names the owner; Claude can set up a recurring reminder |

## Explicitly out of scope for 1.1.0 (already deferred in the plan)

R13 standard-authoring compiler, R14 portfolio roll-up and import adapters, Exchange/Purview writes, SPF-bypass activation, app-only operation, a hosted portal, a central database, automatic rollback and an auto-updater. Each needs its own decision later.

## Suggested order

1. William: settings (7), owners and policies (4), and kick off the Astra/Codex review (3). In parallel, Claude does 8, 12 and 13.
2. Preview.19 release candidate: promote to `main` (9), publish the prerelease (10).
3. William: live Stage A on a disposable tenant, then Stage B (1). Claude turns the results into the support matrix and runtime gating (2).
4. Second engineer and accessibility checks (6), with fixes (11).
5. Signing decision (5), release record (14), then publish 1.1.0 on approval.

**The critical path runs through William.** Claude can keep the code side moving, but the release can't be called final without a live tenant run, named owners and a distribution decision.
