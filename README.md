# M365 BuildStandard Tool

A Windows application for engineers to capture tenant configuration, compare it with a versioned build standard, and prepare controlled deployment candidates.

Claude's C#/.NET implementation is the primary baseline. Asta remains a reference for selected features and safeguards. See the [source register](docs/integration/SOURCE-REPOSITORIES.md), [baseline review](docs/integration/BASELINE-REVIEW.md), [comparison](docs/integration/COMPARISON-MATRIX.md) and [testing evidence](docs/integration/TESTING-EVIDENCE.md).

**1.1.0-preview.7 — implementation preview, not tenant-accepted.** Standard 2026.09.7 has 45 controls: 37 candidate recipes, 3 controls assessed from captured evidence and changed only through reviewed tenant actions (authentication methods, MDM scope, tenant compliance default), and 5 that remain manual by nature (emergency accounts, administrator access, Apple and Google ownership, Autopatch). Policy automation now includes input/import UI, saved tenant-local candidate standards, Entra LAPS, reviewed tenant settings and activation/assignment, Autopatch device enrolment, Win32 package publishing and readiness checks. Tests were not run for this increment at the user's request. See [automation coverage](docs/AUTOMATION-COVERAGE.md), [handover](docs/integration/HANDOVER.md) and [testing evidence](docs/integration/TESTING-EVIDENCE.md).

To repair an existing preview installation, enter its exact assessment/deployment client IDs in **Application setup**, preview and approve the displayed changes, approve each app's permission list, then validate and continue. This updates registration redirects and branding through the reviewed workflow.

## Workflow and scope

Connect read-only → capture → inspect/export → compare → select changes → check dependencies → review exact plan → deploy authorised candidates → verify/export.

Start on **Overview and licences**, choose **Connect / change access**, and connect read-only. Subscription counts load after connection. Choose **Load user assignments**, select a subscription and search by name, sign-in address or object ID. Build a plan to view the supported user-scope checks. See [licensing](docs/LICENSING.md) for count definitions and unknown results.

To recover a recorded policy change, use **Undo and recovery → Load change register**, select the original creation or latest update and preview the action. The tool captures fresh evidence, displays current settings and consequences, and requires explicit approval and the tenant ID. Supported actions delete toolkit-created policies, restore recorded inactive updates, or disable unexpectedly active Conditional Access policies. See [recovery](docs/RECOVERY.md) for the supported scope and limitations.

Standard 2026.09.7 contains **45 controls, 37 creation recipes and 22 collection definitions**. 2026.09.5 added LAPS, Defender Antivirus, firewall and EDR candidates; 2026.09.6 added the Settings Catalogue, app, app-protection, enrolment, Autopilot and endpoint-protection candidates; 2026.09.7 adds evidence-based assessment of the authentication-method, MDM-scope and tenant-compliance controls. See [policy automation APIs](docs/POLICY-AUTOMATION-CODE.md) for settings, supported imports and the Entra LAPS prerequisite, and [device automation](docs/DEVICE-AUTOMATION.md) for the preserved BitLocker/long-path recipes. Eight controls have no creation recipe: three are changed only through reviewed tenant actions and are assessed automatically; five (ID-001, ID-003, ENR-005, ENR-006, UPD-001) need an engineer or an external vendor portal and are reported for review. See [automation coverage](docs/AUTOMATION-COVERAGE.md).

If a policy write was accepted but its readback failed, select it in **Undo and recovery → Re-verify**. This performs reads and records fresh verification without repeating the write. Completed 1.0.0 records require explicit historical acknowledgement and matching ownership/settings. Unknown modern write outcomes remain blocked. Stop cancels deployment reads; an in-flight policy write retains its configured timeout (100 seconds by default). Readback and after-capture each have a 60-second budget, with incomplete evidence clearly reported.

Authentication is delegated, with separate assessment/deployment registrations. The [application setup wizard](docs/APPLICATION-SETUP.md) previews, creates or explicitly repairs both registrations and enterprise applications, guides administrator consent and validates configuration, grants and engineer assignment. Windows sign-in uses a Microsoft pop-up, with browser fallback. Setup can assign the approved engineer, uses a registered consent callback and continues directly to connection. It uses a separate privileged session; app-only authentication is not implemented. Connect once without saving a profile, or opt in to remembering the client.

Resolve exclusion accounts by sign-in address, object ID or display name. Select the exact result, purpose and reason, then apply it to the tenant's plan inputs. The planner shows names and IDs, retains dedicated emergency-access and delegated-creator safeguards, and invalidates prior plans after changes. An admin-style name never automatically grants an exclusion. One-time connections still retain local audit evidence.

Conditional Access candidates are disabled with stored targeting and exclusions. Intune candidates are unassigned. Use **Policy automation** for separately approved activation/targeting and removal of assignments; creation itself never assigns. The historical CA wording "disabled and unassigned" remains unresolved; this import does not change targeting. Creator exclusions persist until deliberately reviewed.

The Graph client blocks assessment writes. The executor independently validates durable complete snapshots, plan/input integrity, licensing, creation references, ownership and drift. Plans are single-use after a run begins. Guarded PATCH updates are limited to owned inactive objects. Ambiguous writes need manual reconciliation. Snapshots are evidence, not automatic rollback.

## Build and package

Requires .NET 8 SDK on the build machine:

```powershell
dotnet build BDIT.TenantToolkit.sln -c Release
dotnet test tests/BDIT.TenantToolkit.Tests -c Release
./build/Build-Portable.ps1
```

BUILD-ME-FIRST.cmd provides the existing user-local SDK bootstrap. Packaging produces a self-contained win-x64 ZIP; engineers need no Node, runtime installation or routine local administrator rights. WPF requires Windows. Core/Graph/Engine target net8.0, with Windows-specific MSAL cache protection.

Extract a reviewed package to a writable folder and use Start.cmd. Shipped custom client IDs are blank. Registration, consent and live tenant testing require separately authorised scope.

## Development

[Architecture](docs/ARCHITECTURE.md) · [decisions](docs/integration/DECISION-LOG.md) · [next task](docs/integration/HANDOVER.md) · [live validation](docs/LIVE-VALIDATION.md)

Use separate feature branches and reviewed PRs targeting integration; keep main releasable. Check open PRs before starting. Source repositories and other contributors' branches remain preserved.

The [preserved source README](docs/CLAUDE-BASELINE-README.md), source review and handover documents contain historical claims. Use the current baseline review where they conflict.

Never commit credentials, tokens, keys, saved connections or real tenant evidence. Root reports/ is for generated output; C# Reports/ source must remain tracked.
