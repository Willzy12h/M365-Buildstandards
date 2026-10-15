# Policy automation APIs — preview.6

Use **Policy automation** in the WPF app. See [coverage](AUTOMATION-COVERAGE.md) for exact boundaries and client inputs.

- `PolicyImporter.Import`: supported Graph policy JSON into a new candidate catalogue; no tenant writes. Strict recipe properties, bounded JSON, duplicate-key rejection, assignment/metadata stripping and explicit foreign-GUID replacements. `DevicePolicyImporter` remains available for the original restricted four-control adapter.
- `ReviewedChangeService.PreviewAsync / ExecuteAsync / ReverifyAsync`: tenant settings, activation/containment, group assignments and Windows update management. Approval binds tenant/operator/client/profile/standard/snapshot/mappings. Exact live before-state is re-read; plans expire and are single-use. Unknown acceptance blocks further reviewed writes. Read-only re-verification never repeats a write.
- `EntraLapsService`: existing full-PUT enablement preserving device-registration settings, now integrated in the UI.
- `ApplicationPackageService`: preview a supplied `.intunewin` package, publish it to a confirmed unassigned Win32 app, re-verify final publication. `IntuneWinPackage` reads the archive in place; no extraction or execution. Graph version/file/commit/publication stages are independently journaled. Signed upload URLs and encryption material are memory-only. Azure public-cloud storage only; 8 GiB package cap; no automatic retry or upgrade of existing committed content.
- `ServiceReadinessService`: emergency-account metadata, direct Global Administrator principal count, Apple certificate expiry/owner and Managed Google Play binding/sync. Does not certify effective privilege, credential custody or external ownership.
- `CandidateReferenceValidator`: checks ESP app publication and resolves Settings Catalogue definitions/choices before candidate writes.

Local evidence: tenant `reviewed-changes/`, `packages/`, `entra-laps/`, `readiness/`, `catalogues/` directories. Imported catalogues retain a digest sidecar and can be reloaded for the selected tenant while disconnected. All remain under ignored `data/`.

No live tenant validation, GUI acceptance or tests were performed for this increment. Windows compilation is separate evidence. External ownership, vendor console check-in and actual device effects cannot be inferred from an accepted Graph response.
