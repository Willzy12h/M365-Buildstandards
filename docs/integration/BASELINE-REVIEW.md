# Baseline review — 14 September 2026

## Scope and outcome

Targeted source review of the three repositories, current branch/PR state, safety tests, remote CI outcomes, local Windows compilation and reporting recovery. This is not an exhaustive security audit or live tenant acceptance test. Exact revisions are in [SOURCE-REPOSITORIES.md](SOURCE-REPOSITORIES.md).

Retain Claude's Core, Graph, Engine and WPF separation. Standards and branding remain data/configuration. No Asta runtime is imported. The first implementation repairs the reporting/build baseline; deployment behaviour is preserved and the gaps below remain open.

## Previous findings rechecked

| Finding | Current evidence / status |
|---|---|
| Reporting source missing / Windows CI broken | Confirmed on both remote Claude refs; reproduced 13 missing-type errors locally. Six existing local report files recovered in this PR |
| Broad reports ignore rule suspected | Confirmed that reports/ matches the C# Reports/ path with case-insensitive Git matching; master uses root-only /reports/ |
| 401 retry can reuse cached token | Confirmed in GraphClient.SendReadAsync and MsalAuthenticator.GetAccessTokenAsync. No force-refresh contract; known issue |
| Unexpected exception leaves InProgress item | Confirmed in DeploymentExecutor: outer catch marks run failed, finally only converts Pending to NotRun; known issue |
| Documentation contradicts updates | Confirmed PATCH paths for owned inactive objects with live state/drift checks. Source prose saying objects are never updated is inaccurate |
| 45 controls / 12 recipes | Confirmed in standard 2026.09.3; 13 collection definitions. Candidate recipes are not proof of live support |
| Delegated-only authentication | Confirmed MSAL public-client sign-in. App-only and integrated provisioning deferred |
| Live deployment not independently validated | Still unverified. No tenant connection, write, registration or consent performed |

Recovery exposed another compilation defect: ConfigurationViewModel.CopySummaryCommand referenced nonexistent Summary; it now uses the existing SnapshotText. The old report test expected six sheets. Recovered code retains those six and adds equivalence observations and caveats. The updated test checks all eight table names, plus a new test of unmet signals and caveat contents. No safety expectation was removed.

Independent review also found the master's inherited evidence/ ignore rule hid the imported EvidenceStore.cs on Windows. Runtime evidence/connections/exports/snapshots rules are now root-anchored, and the complete selected source inventory was checked against Git's index before publication.

## Additional findings

- **Complete evidence:** collection records incomplete reads and planner blocks affected collection rows, but DeploymentPlanner.Validate never requires overall Snapshot.Complete. UI acknowledgement only records an ID. The requirement for complete durable pre-change evidence is not fully enforced.
- **Dependencies:** BuildRow produces warnings, without resolving a dependency graph or proving prerequisites. Distinguish requirements for candidate creation from requirements for later activation.
- **Execution boundary:** the workspace validates the plan; the executor checks Graph mode/tenant but does not independently enforce full plan validation. A future caller could bypass the workspace.
- **Missing versus null:** CanonicalJson.IsSubset ignores the boolean from TryGetPropertyValue. An absent member can therefore match an expected JSON null. Comparison and verification need targeted tests and a shared fix.
- **UI lifecycle:** async commands reject re-entry; collection awaits I/O. Assessment/export run synchronously. The workspace does not wire collection/sign-in cancellation. Deployment close/stop waits at action boundaries. App.OnExit blocks on async shutdown and needs connected-session deadlock/token-cleanup tests.
- **Report wording:** client-summary prose asserts no tenant changes occurred, although assessment can follow deployment. Review before client-facing use. Markdown escapes table delimiters but is not a complete untrusted-content rendering guarantee. XLSX stores inline strings, truncates long cells with a notice, and is not a replacement for full JSON evidence.

## Microsoft documentation checked

Graph models state separately from conditions: [conditionalAccessPolicy](https://learn.microsoft.com/en-us/graph/api/resources/conditionalaccesspolicy?view=graph-rest-1.0). Retaining targeting on a disabled policy differs from empty targeting. This import changes neither.

MSAL offers [WithForceRefresh](https://learn.microsoft.com/en-us/dotnet/api/microsoft.identity.client.acquiretokensilentparameterbuilder.withforcerefresh?view=msal-dotnet-latest). A provider contract and bounded GET retry need testing; never add automatic write replay.

Business Premium is the normal SME baseline, including [Entra ID P1](https://learn.microsoft.com/en-us/entra/fundamentals/licensing) and [Intune Plan 1](https://learn.microsoft.com/en-us/microsoft-365/admin/security-and-compliance/m365bp-devices-enrollment?tabs=Windows10-11&view=o365-worldwide). Actual tenant licences and feature-specific requirements still need checking. This review does not certify every Graph recipe.

## Recovery and release meaning

Snapshots, mappings and journals support manual reconciliation, not automatic rollback. Write acceptance, configuration readback and functional sign-in/device testing are separate outcomes. Build/package success is engineering evidence only; live deployment remains outside this task's authorisation.
