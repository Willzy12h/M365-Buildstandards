# BDIT Microsoft 365 Tenant Toolkit

A Windows application for Blue Diamond IT to capture tenant configuration, compare it with a versioned build standard, and prepare controlled deployment candidates.

Claude's C#/.NET implementation is the primary baseline. Asta remains a reference for selected features and safeguards. See the [source register](docs/integration/SOURCE-REPOSITORIES.md), [baseline review](docs/integration/BASELINE-REVIEW.md), [comparison](docs/integration/COMPARISON-MATRIX.md) and [testing evidence](docs/integration/TESTING-EVIDENCE.md).

**Development review candidate: live tenant deployment is unverified.** The [handover](docs/integration/HANDOVER.md) records safety gaps, including incomplete-snapshot enforcement, execution terminal states, token renewal and missing-versus-null comparison. Passing builds do not resolve these.

## Workflow and scope

Connect read-only → capture → inspect/export → compare → select changes → check dependencies → review exact plan → deploy authorised candidates → verify/export.

Standard 2026.09.3 contains **45 controls, 12 creation recipes and 13 collection definitions**. The remaining controls have no creation recipe. This is not full automation of the standard.

Authentication is delegated, with separate assessment/deployment registrations. App-only and integrated app provisioning are not implemented. Connections currently use saved profiles; a one-time workflow is a candidate port.

Conditional Access candidates are disabled with stored targeting and exclusions. Intune candidates are unassigned. The historical CA wording "disabled and unassigned" remains unresolved; this import does not change targeting. Creator exclusions persist until deliberately reviewed.

The Graph client blocks assessment writes. The implementation includes guarded PATCH updates to owned inactive objects; it is not create-only. Ambiguous writes need manual reconciliation. Snapshots are evidence, not automatic rollback.

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
