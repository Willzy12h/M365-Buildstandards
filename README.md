# BuildStandard

A Windows application for engineers to capture tenant configuration, compare it with a versioned build standard, and prepare controlled deployment candidates.

Claude's C#/.NET implementation is the primary baseline. Asta remains a reference for selected features and safeguards. See the [source register](docs/integration/SOURCE-REPOSITORIES.md), [baseline review](docs/integration/BASELINE-REVIEW.md), [comparison](docs/integration/COMPARISON-MATRIX.md) and [testing evidence](docs/integration/TESTING-EVIDENCE.md).

**1.1.0-preview.19 — owner-accepted source for promotion to `main`; unsigned and unpublished.** The completed .NET 10 native WPF command centre combines tenant readiness, configuration capture, assessment, controlled plans, jobs, reports and registered scripts. The contained WebView2 reader shows the selected Engineer Build Standard catalogue reference; authentication, approval and execution remain native. Use [the engineer start guide](docs/OPERATOR-START.md), [automation coverage](docs/AUTOMATION-COVERAGE.md) and [current source/package evidence](https://github.com/Willzy12h/M365-Buildstandards/issues/83). Automated checks use synthetic data. Human accessibility/H1, workstation policy and live service effectiveness remain unverified; source acceptance does not certify them.

Historical standard 2026.09.30 and published catalogue bytes are preserved. See [manual document exports](docs/ENGINEER-DOCUMENTS.md) and [release and servicing](docs/RELEASE-AND-SERVICING.md) for document and distribution boundaries.

To repair an existing preview installation, enter its exact assessment/deployment client IDs in **Application setup**, preview and approve the displayed changes, approve each app's permission list, then validate and continue. This updates registration redirects and branding through the reviewed workflow.

## Testing this build

A green build publishes a ready-to-run Windows package and a set of offline screenshots of every page. See [testing this build](docs/TESTING-THIS-BUILD.md) for how to download, verify and run an authorised package, what to review without connecting a tenant, and the separate approval needed for live checks. If application control blocks it, use the security-owner approval route in that guide; do not bypass the control.

## Workflow and scope

Connect read-only → capture → inspect/export → compare → select changes → check dependencies → review exact plan → deploy authorised candidates → verify/export.

Start on **Home**, choose **Connect / change access**, and connect read-only. Open **Overview and licences** for subscriptions and user assignments. Subscription counts load after connection. Choose **Load user assignments**, select a subscription and search by name, sign-in address or object ID. Build a plan to view the supported user-scope checks. See [licensing](docs/LICENSING.md) for count definitions and unknown results.

To recover a recorded policy change, use **Undo and recovery → Load change register**, select the original creation or latest update and preview the action. The tool captures fresh evidence, displays current settings and consequences, and requires explicit approval and the tenant ID. Supported actions delete toolkit-created policies, restore recorded inactive updates, or disable unexpectedly active Conditional Access policies. See [recovery](docs/RECOVERY.md) for the supported scope and limitations.

Standard 2026.09.30 groups controls into Entra, Intune, Exchange and Purview. Assessment and Plan filter by area. [Automation coverage](docs/AUTOMATION-COVERAGE.md) lists every control, client input, manual fallback and retirement. Exchange/Purview use an integrated delegated read-only capture with a supported preinstalled PowerShell module, strict evidence validation, separate Graph/Exchange contexts, accepted-domain selection and explicit DNS checks (manual export/import remains available); selected write proposals are inert review files. The Build Standard and Manual implementation and verification guide export locally in HTML/Markdown without a connection. CI publishes those four generated files alongside the portable package and synthetic UI evidence. Application setup compares existing registrations with the documented requirements and previews any differences. Review consent only where required; a source upgrade does not itself grant new access or require recreating both applications.

If a policy write was accepted but its readback failed, select it in **Undo and recovery → Re-verify**. This performs reads and records fresh verification without repeating the write. Completed 1.0.0 records require explicit historical acknowledgement and matching ownership/settings. Unknown modern write outcomes remain blocked. Stop cancels deployment reads; an in-flight policy write retains its configured timeout (100 seconds by default). Readback and after-capture each have a 60-second budget, with incomplete evidence clearly reported.

Authentication is delegated, with separate assessment/deployment registrations. The [application setup wizard](docs/APPLICATION-SETUP.md) previews, creates or explicitly repairs both registrations and enterprise applications, guides administrator consent and validates configuration, grants and engineer assignment. Windows sign-in uses a Microsoft pop-up, with browser fallback. Setup can assign the approved engineer, uses a registered consent callback and continues directly to connection. It uses a separate privileged session; app-only authentication is not implemented. Connect once without saving a profile, or opt in to remembering the client.

Resolve exclusion accounts by sign-in address, object ID or display name. Select the exact result, purpose and reason, then apply it to the tenant's plan inputs. The planner shows names and IDs, retains dedicated emergency-access and delegated-creator safeguards, and invalidates prior plans after changes. An admin-style name never automatically grants an exclusion. One-time connections still retain local audit evidence.

Conditional Access candidates are disabled with stored targeting and exclusions. Intune candidates are unassigned. Use **Policy automation** for separately approved activation/targeting and removal of assignments; creation itself never assigns. CA creation preserves the exact reviewed targeting and exclusions; disabled does not mean unassigned. Creator exclusions persist until deliberately reviewed.

The Graph client blocks assessment writes. The executor independently validates durable complete snapshots, plan/input integrity, licensing, creation references, ownership and drift. Plans are single-use after a run begins. Guarded PATCH updates are limited to supported owned inactive policies. Directory group membership and named locations remain creation-only; the provisioning owner on the recorded empty PRE-011 group is a separate constrained action. Ambiguous writes need manual reconciliation. Snapshots are evidence, not automatic rollback.

## Build and package

Requires .NET 10 SDK on the build machine:

```powershell
dotnet build BuildStandard.sln -c Release -warnaserror
dotnet test tests/BuildStandard.Tests -c Release
dotnet test tests/BuildStandard.App.Tests -c Release
./build/Build-Portable.ps1
```

BUILD-ME-FIRST.cmd provides the existing user-local SDK bootstrap. Packaging produces a self-contained win-x64 ZIP; engineers need no Node, manual .NET installation or routine local administrator rights. WPF requires Windows. The optional catalogue reader additionally requires Microsoft Edge WebView2 Runtime; native working pages remain available without it. Core/Graph/Engine target net10.0, with Windows-specific MSAL cache protection.

Extract a reviewed package to a writable folder and use Start.cmd. Shipped custom client IDs are blank. Registration, consent and live tenant testing require separately authorised scope.

## Development

[Architecture](docs/ARCHITECTURE.md) · [decisions](docs/integration/DECISION-LOG.md) · [next task](docs/integration/HANDOVER.md) · [live validation](docs/LIVE-VALIDATION.md)

Use the canonical `main` source after the authorised promotion and separate owned branches with reviewed PRs. Read [the current goal](docs/GOAL.md) and check open PRs before starting. Frozen integration, phase history, source repositories and other contributors' branches remain preserved.

The [preserved source README](docs/CLAUDE-BASELINE-README.md), source review and handover documents contain historical claims. Use the current baseline review where they conflict.

Never commit credentials, tokens, keys, saved connections or real tenant evidence. Root reports/ is for generated output; C# Reports/ source must remain tracked.
