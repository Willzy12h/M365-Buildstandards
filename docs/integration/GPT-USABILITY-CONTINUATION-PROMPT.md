# Continuation prompt — review PR #21, then improve usability and ease of use

**Prepared 7 October 2026 by Claude for the next agent (ChatGPT/Codex/Astra).** Paste the block below as the task. It is a handoff, not execution authority beyond what it states.

```text
You are continuing work on Willzy12h/M365-Buildstandards (BDIT M365 Build Standard tool:
.NET 10 WPF, Windows x64, portable; CLI "bdit"). Read AGENTS.md and
docs/integration/AGENT-COORDINATION.md first and follow them. Use UK English.
Ignore claude/M365-BuildStandard-Project-Context.md (the user excluded it).

CURRENT STATE (re-fetch and verify; do not trust these references blindly)
- PR #19 (Astra, draft): astra/release-2026-09-12 @ 2668d00 -> integration @ 5cadd3a.
  Preview.18 was published as an unsigned prerelease from source 10808de.
- PR #20 (Astra, open, not merged): INT-049..051 workflow/evidence/lineage contracts @ 674b1ba.
  Nothing that produces or consumes INT-049..051 may be built until a human merges it.
- PR #21 (Claude, draft): claude/m365-buildstandards-preview-review-mjr04x, stacked on PR #19.
  It contains the independent review docs/integration/CLAUDE-PREVIEW18-REVIEW.md
  (findings CLA-20261006-01..17) and their implementation. Code head 4202f02 is green on
  Windows CI run 37542836818: 918 Engine + 115 App tests, native WPF harness 42 layouts /
  79 commands / 0 binding issues / 0 tenant calls, PowerShell 5.1 packaging, fresh
  297-file portable package start/stop/Copy. The latest commits only change docs.
- What PR #21 changed (details in the CHANGELOG "Unreleased" entry and the review report's
  "Implementation status"):
  * Evidence integrity is enforced at assessment: a modified primary snapshot is refused;
    reports carry Intact / Modified / NotRecorded (SnapshotIntegrity).
  * Stored evidence is judged as of its capture (Graph, Exchange and DNS times, capped at
    now), so a re-assessment days later gives the same findings.
  * Capability matrix shows the real read route per control (enum-based) and licence.
  * The build verifies the committed standards manifest instead of regenerating it
    (build/Test-StandardsManifest.ps1); the package ZIP uses '/' entry names.
  * Backup/restore/handoff: backups go to <root>/transfers/ with a .sha256 sidecar;
    restore and adoption check a trusted archive SHA-256 received out of band (or an
    explicit, logged "no trusted digest" acknowledgement); one read-locked stream is both
    hashed and extracted; "Verify restored folder" and `bdit verify-restore --folder`;
    "Verify and adopt into this empty workspace" moves verified evidence into an empty
    data/ folder, quarantining on failure. Unknown-write blockers survive; no approval or
    execution authority is restored.
  * Publishing moved to its own workflow (publish-preview18.yml, environment: release);
    all actions are pinned by SHA. Definition exports are timestamped with a sidecar.
  * Labels: "candidate recipes (inert)"; Conditional Access wording only on CA controls.

TASK 1 — REVIEW PR #21 (read-only first)
Independently review the PR #21 diff against PR #19 head. Check concrete paths and negative
cases, not test counts. Focus on: WorkspaceBackup (trusted digest, single-stream hash and
extract, adoption staging/quarantine, no recursive delete, write-lease probe), the
AssessmentEngine evidence-time rule, SnapshotIntegrity propagation to every report format,
the CLI verify-restore exit codes, and the Settings transfer view model. Record findings
with stable IDs (e.g. GPT-20261007-NN) in your own branch/PR or send them to the
integrator. Fix only confirmed defects that are small and local; propose anything larger.

TASK 2 — USABILITY AND EASE OF USE (main goal)
The safety model is sound; the tool is now harder to use than it needs to be. Make the
common journeys obvious for an engineer who has never seen the tool, without weakening any
safeguard. Prioritised backlog (verify each against current source before changing it):

U1  Settings > Backup and transfer is a wall of controls (backup file, trusted digest
    box, acknowledgement checkbox, three verify/restore/adopt buttons). Replace with a
    guided, step-by-step flow: "Hand this workspace to another engineer" (create backup,
    show the digest with a Copy button, explain to send it separately) and "Receive a
    workspace" (pick archive -> digest -> verify -> choose restore-separately or adopt).
    Disable steps until their prerequisites are met and say why.
    Source: src/BDIT.TenantToolkit.App/Views/SettingsView.xaml,
    ViewModels/SettingsViewModel.cs.
U2  Do not make people type a 64-character hash. Offer "Load digest from .sha256 file"
    and paste-tolerant input (trim, accept "hash  filename" lines). Keep the rule that a
    sidecar travelling WITH the archive is not trusted: label the source clearly and only
    pre-fill from a file the user picks deliberately.
U3  Workspace.ExportAsync always shows the busy title "Writing report", including for
    restore, verify and adoption (src/BDIT.TenantToolkit.App/Services/Workspace.cs).
    Pass a task-specific title and progress text.
U4  Result messages: after backup, restore, verify or adopt, show what happened, where the
    files are (with an "Open folder" action) and the next step, in plain language. Errors
    should say what to do, not just what failed (IntegrityException/ConfigurationException
    text surfaced in LastResult).
U5  Quick Connect (Views/ConnectView.xaml): during the confirmation the header can still
    show an existing deployment session, so "WRITES POSSIBLE" sits beside "Mode: read-only
    assessment" (render quick-connect-confirmation-1480x940). Make it unambiguous which
    identity and tenant the new connection uses and that it is read-only.
U6  Plan review at the smallest supported size (1180x640): the detail pane shows about two
    lines (render plan-review-1180x640). Give the detail more room or let the summary
    collapse. Do not hide safety prerequisites (an earlier iteration did; see
    COMPLETION-REGISTER).
U7  First run: docs/OPERATOR-START.md is ten long, dense paragraphs. Lead with a short
    "first read-only assessment" path (download release -> check SHA-256 -> extract ->
    Start.cmd -> Quick Connect -> capture -> assessment -> export), one line per step,
    and move the safety detail into linked sections without deleting it.
    docs/TESTING-THIS-BUILD.md is already release-first; keep the two consistent and make
    the in-app Overview point to the same first steps.
U8  Consistent vocabulary across app, CLI and reports (e.g. "evidence", "capture",
    "candidate recipe (inert)", "trusted digest"). Keep UK English.
U9  Accessibility: every new control needs AutomationProperties.Name, logical tab
    order and keyboard access; keep the harness passing. Human Narrator, keyboard-only and
    physical scaling acceptance is still unperformed and must stay listed as such.
U10 After PR #20 is merged by a human (verify on GitHub): clearer messages when a reused
    PRE control ID meets an ownership record from an older release (INT-051, CLA-07;
    fixture tests/BDIT.TenantToolkit.Tests/ReusedControlIdFixtureTests.cs already holds
    the safety outcome).

Also outstanding, not usability-led: CLA-10 support-bundle opt-ins (needs the privacy
owner's field list), CLA-17 decision row, version bump to the next preview and the
missing Preview.18 CHANGELOG entry (release owner), recording CLA-01..17 in
PRODUCT-FEEDBACK-REGISTER.md and a DECISION-LOG note for SnapshotIntegrity, the Node 20
Actions deprecation, and repository settings (release-environment reviewers, immutable
releases) which only the owner can change.

HOW TO WORK
- Claim work in docs/integration/WORK-CLAIMS.md on your own branch. Never push to
  astra/* or claude/* branches or to the source repositories. Stack on PR #21 or PR #19 as
  coordination requires and say which.
- Every UI change needs: a view-model or engine test where logic changed; the command
  registered in the UiReview harness; renders at the supported sizes. App tests and the
  native harness only run on Windows CI - read the logs, do not claim them from Linux.
- Run: dotnet build BDIT.TenantToolkit.sln -c Release -warnaserror;
  dotnet test tests/BDIT.TenantToolkit.Tests -c Release; Windows CI for App tests/harness.
- Remove dead code and stale comments you touch; keep comments accurate.
- Hand back: IDs addressed, branch/base/head, what changed and why, exact checks run and
  their results, what is still open, next bounded action.

HARD LIMITS
- No merges, approvals or publication. No live sign-in, consent, tenant or device
  operation, module installation or external upload.
- Synthetic fixtures only. Never commit tenant exports, credentials, tokens, secrets or
  saved connection data.
- Preserve every safeguard: read-only assessment separate from writes, immutable reviewed
  plans, execution guards, no ambiguous write replay, unknown-write blockers, trusted
  digest or logged acknowledgement for transfers, no restored approval/authority. Do not
  edit published standards or weaken negative tests to make something easier.
```
