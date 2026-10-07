# Continuation prompt — review PR #21, merge the open PRs, then improve usability

**Prepared 7 October 2026 by Claude for the next agent (ChatGPT/Codex, working as Astra).** Paste the block below as the task. William has delegated commits and merges to the agents; the delegation and its conditions are recorded in [HANDOVER](HANDOVER.md#owner-delegation-of-commits-and-merges--7-october-2026). Nothing else in this prompt widens what the agent may do.

```text
You are continuing work on Willzy12h/M365-Buildstandards (BDIT M365 Build Standard tool:
.NET 10 WPF, Windows x64, portable; CLI "bdit"). You are the Astra/Codex agent: astra/*
branches are yours, claude/* branches are Claude's. Read AGENTS.md,
docs/integration/AGENT-COORDINATION.md and docs/integration/HANDOVER.md first and follow
them. HANDOVER records William's 7 October delegation: agents commit and merge; William
advises and approves but does not want to commit or merge by hand. Use UK English.
Ignore claude/M365-BuildStandard-Project-Context.md (the user excluded it).

CURRENT STATE (re-fetch and verify; do not trust these references blindly)
- integration @ 5cadd3a; main @ 81b18f5.
- PR #20 (Astra, ready): astra/product-contracts-2026-10-06 @ 674b1ba -> integration.
  Docs only: INT-049..051 workflow/evidence/lineage contracts.
- PR #19 (Astra, draft): astra/release-2026-09-12 @ 2668d00 -> integration.
  Preview.18 was published as an unsigned prerelease from source 10808de.
- PR #21 (Claude, draft): claude/m365-buildstandards-preview-review-mjr04x ->
  astra/release-2026-09-12 (stacked on #19). Review docs/integration/
  CLAUDE-PREVIEW18-REVIEW.md (CLA-20261006-01..17) plus fixes. Code head 4202f02 green on
  Windows CI run 37542836818: 918 Engine + 115 App tests, WPF harness 42 layouts /
  79 commands / 0 binding issues / 0 tenant calls, PowerShell 5.1 packaging, fresh
  297-file package. Later commits are docs only.
- PR #21 changes: modified snapshots refused at assessment, and every report states
  integrity (Intact/Modified/NotRecorded); stored evidence judged as of its capture
  (Graph/Exchange/DNS, capped at now); the capability matrix shows the real read route
  and licence; the build verifies the committed standards manifest; backups go to
  transfers/ with a .sha256 sidecar; restore/adopt check a trusted out-of-band SHA-256
  or a logged acknowledgement; `bdit verify-restore --folder`; "Verify and adopt into
  this empty workspace" with quarantine; unknown-write blockers survive; publishing has
  its own workflow (environment: release), SHA-pinned actions; timestamped exports.

TASK 1 — REVIEW PR #21 (read-only)
Review the #21 diff against #19 head: WorkspaceBackup (trusted digest, single-stream
hash and extract, adoption staging/quarantine, no recursive delete, lease probe), the
AssessmentEngine evidence-time rule, SnapshotIntegrity in every report format,
verify-restore exit codes, and the Settings transfer view model. Findings:
GPT-20261007-NN. A finding that weakens a safeguard blocks the #21 merge. Other findings
are follow-ups, fixed on your own branch after the merges, never pushed to claude/*.

TASK 2 — MERGES (authorised by William, 7 October 2026)
Merge these, in this order, each with a merge commit (repository convention; no squash,
rebase or force-push):
  1. PR #21 into astra/release-2026-09-12.
  2. PR #20 into integration.
  3. PR #19 into integration (it then carries #21).
Before each merge, all must hold, otherwise stop that merge and report:
  - The head SHA is the one you reviewed; re-fetch it immediately before merging.
  - Every required check on that exact head has passed. Read the result, don't assume.
  - No open review thread or Task 1 finding says a safeguard was weakened, unless it has
    been answered or fixed.
  - The PR is mergeable. Mark drafts ready for review first.
Known conflict (Claude trial-merged on 7 October): after #20 is merged, #19 conflicts
only in docs/integration/DECISION-LOG.md, at the top. On astra/release-2026-09-12,
merge origin/integration and keep BOTH sections: #20's "Proposed product workflow
contracts — 6 October 2026" first, then #19's "Approved Preview.17 continuation — PR #19,
1 October 2026". WORK-CLAIMS.md merges cleanly. Push, wait for CI to pass on the new
head, then merge #19. Resolve any other conflict by keeping the stricter behaviour and
re-running checks.
After merging:
  - Confirm CI on the integration merge commit is green. If red, fix it on a new
    astra/* branch through a PR; don't revert others' work without asking.
  - Mark #19, #20 and #21 Merged in WORK-CLAIMS.md. Add a line to DECISION-LOG.md that
    INT-049..051 are settled by the #20 merge, leaving the proposal text unchanged.
  - Add dated notes to COMPLETION-REGISTER.md and HANDOVER.md with the merge SHAs and
    the verifying CI run.
  - Keep the merged branches.
NOT authorised without William's explicit approval each time: integration -> main,
tags, releases, running the publish workflow, branch protection or repository settings.
Never approve a PR on William's behalf.

TASK 3 — USABILITY AND EASE OF USE (main goal, after the merges)
New branch from merged integration (e.g. astra/usability-2026-10-07) with a draft PR as
your claim. The safety model is sound; the tool is harder to use than it needs to be.
Make the common journeys obvious to a newcomer without weakening any safeguard. Verify
each item against current source first:
U1  Settings > Backup and transfer is a wall of controls. Replace it with a guided flow:
    "Hand this workspace to another engineer" (backup, show the digest with Copy, explain
    to send it separately) and "Receive a workspace" (archive -> digest -> verify ->
    restore separately or adopt). Disable steps until ready and say why.
    (App/Views/SettingsView.xaml, App/ViewModels/SettingsViewModel.cs)
U2  No hand-typed 64-character hashes: "Load digest from .sha256 file" and paste-tolerant
    input ("hash  filename"). A sidecar travelling with the archive is not trusted; label
    the source and pre-fill only from a file the user deliberately picks.
U3  Workspace.ExportAsync shows "Writing report" for restore/verify/adopt too
    (App/Services/Workspace.cs). Use task-specific titles and progress text.
U4  Results say what happened, where the files are ("Open folder") and the next step;
    errors say what to do, not just what failed.
U5  Quick Connect (App/Views/ConnectView.xaml): while confirming, the header can show an
    existing deployment session ("WRITES POSSIBLE" beside "Mode: read-only assessment";
    render quick-connect-confirmation-1480x940). Make identity, tenant and read-only
    status unambiguous.
U6  Plan review at 1180x640 shows about two lines of detail (render
    plan-review-1180x640). Give it room or let the summary collapse; never hide safety
    prerequisites.
U7  docs/OPERATOR-START.md is ten dense paragraphs. Lead with a one-line-per-step "first
    read-only assessment" path (download -> check SHA-256 -> extract -> Start.cmd ->
    Quick Connect -> capture -> assessment -> export); link the safety detail without
    deleting it. Keep TESTING-THIS-BUILD and the in-app Overview consistent.
U8  Consistent vocabulary across app, CLI and reports; UK English.
U9  Accessibility: AutomationProperties.Name, tab order and keyboard access on new
    controls; keep the harness passing. Human Narrator/keyboard/scaling acceptance stays
    listed as unperformed.
U10 With #20 merged: INT-051 read-only lineage/impact first, giving clear messages when a
    reused PRE control ID meets an older release's ownership record (CLA-07;
    ReusedControlIdFixtureTests must keep passing). Then INT-049, then INT-050, each in
    its own PR.
Also:
- Record CLA-20261006-01..17 in PRODUCT-FEEDBACK-REGISTER.md (paste-ready entries in
  section 6 of CLAUDE-PREVIEW18-REVIEW.md) with implementation status; add a DECISION-LOG
  note for AssessmentResult.SnapshotIntegrity (additive optional field).
- Before any new package: version 1.1.0-preview.19 in Directory.Build.props, add the
  missing Preview.18 CHANGELOG entry, and turn "Unreleased" into Preview.19. Build and
  test the package in CI; do not publish.
- Keep listed, owned by others: CLA-10 support-bundle opt-ins (privacy owner), CLA-17
  decision row, release-environment reviewers and immutable releases (owner), Node 20
  Actions deprecation.
Your own later PRs into integration follow the standing delegation in HANDOVER: merge
them yourself once the conditions there hold, including Claude's independent review.

HOW TO WORK
- Claim in WORK-CLAIMS.md and with a draft PR. Never push to claude/* branches or the
  source repositories.
- UI changes: view-model/engine tests where logic changed, the command registered in the
  UiReview harness, renders at all supported sizes. App tests and the harness run only on
  Windows CI: read the logs; don't claim them from Linux.
- dotnet build BDIT.TenantToolkit.sln -c Release -warnaserror;
  dotnet test tests/BDIT.TenantToolkit.Tests -c Release; Windows CI for the rest.
- Remove dead code and stale comments you touch.
- Hand back: merges done (PR, merge SHA, verifying CI run), IDs addressed,
  branch/base/head, what changed and why, exact checks and results, what is open, next
  bounded action.

HARD LIMITS
- Merges only under the HANDOVER delegation conditions. No approvals, main promotion,
  tags, releases or publication without William's explicit approval.
- No live sign-in, consent, tenant or device operation, module installation or external
  upload.
- Synthetic fixtures only; never commit tenant exports, credentials, tokens or secrets.
- Preserve every safeguard (read-only assessment, immutable reviewed plans, execution
  guards, no ambiguous write replay, unknown-write blockers, trusted digest or logged
  acknowledgement, no restored authority). Don't edit published standards or weaken,
  skip or delete tests to get a merge through.
```
