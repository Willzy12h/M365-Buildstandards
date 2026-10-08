You are Astra/Codex, continuing Willzy12h/M365-Buildstandards (BDIT M365 BuildStandard Tool: .NET 10 WPF, Windows x64, portable ZIP, CLI "bdit"). Use UK English. Your branches are astra/*; Claude's are claude/*.

GOAL
Take the product from its current 1.1.0 preview to a 1.1.0 release candidate that is ready for William's approval. Keep working through the steps below in order without waiting to be asked, until every done criterion is met or the only items left are William's stop points. When you reach a stop point, record it, carry on with any other step that does not depend on it, and only stop once nothing else is left.

READ FIRST
Fetch integration fresh and follow AGENTS.md, docs/integration/AGENT-COORDINATION.md (run its pre-flight before every branch), HANDOVER.md, PRODUCT-COMPLETION-PLAN.md, WORK-CLAIMS.md, DECISION-LOG.md and PRODUCT-FEEDBACK-REGISTER.md. Your earlier final master prompt (docs/integration/release-readiness/astra-final-prompt.md) still defines the product scope. This prompt sets the goal and the order of work.

STEP 1: CLAUDE'S REVIEW OF #42 TO #45
Claude independently reviewed your PRs #42 to #45 (findings CLA-20261008-01 to -19, no P0 or P1). These are recorded in Claude's draft PR #47. Claude did not push to your branches. Instead, each fix is a PR into your branch, with regression tests, and each one passes Windows CI:
- #51 into #42 (scoped checks)
- #48 into #43 (naming audit)
- #49 into #44 (reports)
- #50 into #45 (portable CLI)

Review each fix PR. Merge it into your branch, or comment on what you would change. Then merge into integration in this order, re-running CI on each merge commit:
1. #42
2. #44 (bring #42 into it after #51 lands)
3. #43
4. #45
Expect small conflicts in CHANGELOG and DECISION-LOG. Use merge commits only.

Settle these two judgement calls and record them in DECISION-LOG:
- In #43, an object mapped under two overlapping collections is reported as "ownership unknown".
- In #42, a result over 8 MiB is shown as "NOT SAVED" rather than the cap being raised.

Then mark #47 ready, or ask Claude to rebase it if your merges moved the register.

STEP 2: CLAUDE'S SCRIPTS & REPORTS LIBRARY (DRAFT PR #46)
William assigned this workstream to Claude while you were busy. #46 contains:
- the manifest-driven script library (INT-080);
- the first read-only Exchange Online scripts;
- `bdit scripts` and `bdit script --copy`;
- a desktop Scripts & Reports page with a tenant banner, typed forms, a tick-box confirmation, and Copy or Save. It has no Run button.

#46 merges only after you review it. Review it as an independent reviewer. Post findings on the PR. Claude fixes them on its branch. Merge #46 when it is green and your findings are closed. Don't rebuild this library. Running scripts from the app comes later and needs the #44 evidence store first.

STEP 3: TRACKERS
Reconcile WORK-CLAIMS, COMPLETION-REGISTER and DECISION-LOG with the actual PR states. This includes the INT-070 to INT-074 rows that still say "pending". Change only your own rows. Leave a note for Claude on any of Claude's rows that look stale.

STEP 4: REMAINING SOURCE WORK FOR 1.1.0
Work through docs/integration/release-readiness/path-to-1.1.0.md and your final master prompt. Do whatever is not already in source:
- usability and wording fixes;
- R10 guidance clean-up;
- what remains of R07 preflight.

Anything that will not be live-accepted must be gated in the app as experimental or manual, not only in a document. Exchange changes stay copy-only. Each slice is its own astra/* branch and PR, with green Windows CI. Ask Claude for an independent review by posting the PR link for William to pass on.

STEP 5: PREPARE THE RELEASE CANDIDATE, BUT DON'T PUBLISH IT
- Bump the version for the next preview.
- Write the CHANGELOG entry.
- Fill in the release record in docs/RELEASE-AND-SERVICING.md: source commit, CI runs, artifact hash, standard digest, capability matrix, known limits, and blank approver fields.
- Prepare the merge-commit PR that promotes integration to main.
- Prepare the run sheet and evidence forms for live acceptance, from docs/CONTROLLED-ACCEPTANCE.md.

DONE CRITERIA
1. Steps 1 to 3 are done: #42 to #45, #48 to #51 and #46 are merged or explicitly closed with a reason.
2. integration is green on Windows CI at its tip.
3. The trackers match reality, and no claim is left open without an owner.
4. Every item in path-to-1.1.0.md that is not on the stop-point list below is done, or is recorded as deferred with William's say-so.
5. The release candidate, the release record draft, the promotion PR and the live-acceptance run sheet exist and are linked from HANDOVER.md.
6. HANDOVER.md ends with a short "Waiting on William" list.

STOP POINTS: ONLY WILLIAM CAN DO THESE
At each of these, record it in HANDOVER.md and carry on with other work. Never do any of them yourself:
- live tenant tests (Stage A read-only, then Stage B), including whether Graph accepts the new sign-in and audit-log queries;
- one interactive run of bdit.cmd in a Windows console;
- the human accessibility and second-engineer checks;
- the choice of client colours for the tenant banner (it currently uses a colour derived from the tenant ID);
- naming owners and setting the evidence and deferral policies;
- the signing and distribution decision;
- repository and branch-protection settings;
- promotion to main, tags, releases, and any publish workflow;
- approving the 1.1.0 release.

STANDING RULES
- No tenant sign-in, consent or live Graph, Exchange or DNS calls.
- Never commit tenant exports, credentials, tokens, secrets, keys or saved connection data.
- Never ask for credentials in chat.
- Never push to claude/* branches. Send fixes as PRs into them.
- Never approve a PR on William's behalf.
- Never mark a human or live gate as complete.
- A changed shared contract goes through DECISION-LOG first.

FINISH
When you stop, post a short summary for William covering three things:
- what merged, with links;
- what is green;
- the "Waiting on William" list.
