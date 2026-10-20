# Local candidate standards

Candidate authoring is offline. It does not connect to a tenant, install a catalogue, change historical standard files or publish a release. A candidate is not deployment approval or a verified successor lineage.

The current source provides a shared draft/validation/output engine and these CLI commands. The native editor and supported policy-import journey are still being implemented; this page does not claim they are available.

## Create a draft

Use the exact filename of a catalogue already registered in the installation's verified standards manifest:

```text
bdit candidate --action create --catalogue 2026.10.15.json --title "Proposed internal standard" --root "C:\Tools\BuildStandard"
```

The JSON response names a new local draft under `reports`. Its identity includes the exact baseline file SHA-256, release and schema. Moving identical verified catalogue bytes to another registered filename is allowed; replacing them with a newer same-name or same-release catalogue is not. Missing baseline bytes block validation/export.

The editor's draft model supports existing control recipe settings and reviewable parameter defaults. It does not add controls, collections, permissions, execution routes, licence rules or new template syntax. Foreign references need explicit supported mappings. Conditional Access candidates stay disabled and preserve their exact targeting/exclusions; Intune candidates remain unassigned. macOS remains unsupported and blocked. Edited defaults lose their inherited vendor-review date; editing is not a new vendor review.

Do not hand-edit a saved draft's digest to bypass validation. The native editing/import surface is not yet available in this source milestone. Saving an unresolved draft will not make it exportable.

## Validate, then explicitly export

```text
bdit candidate --action validate --input "C:\Tools\BuildStandard\reports\standard-draft-....json" --root "C:\Tools\BuildStandard"
```

Read the returned `draftDigest` and the proposed changes. Export requires that exact digest and repeats shared-engine validation against the current exact baseline:

```text
bdit candidate --action export --input "C:\Tools\BuildStandard\reports\standard-draft-....json" --reviewed-digest <draftDigest> --root "C:\Tools\BuildStandard"
```

The JSON response names four fresh local files:

- Candidate catalogue JSON, with `Candidate` status and content-derived identity.
- The draft and its baseline/import provenance.
- A readable HTML configuration comparison with separate assignment source observations.
- SHA-256 checksums of those three files.

The comparison HTML uses local inline styling, with no scripts or external assets. It is a separate saved document, not another embedded-browser command surface. Unknown assignment observations are not an empty assignment inventory, and import digests do not authenticate a source.

Outputs are created without overwriting earlier files and are read back before success is reported. Ctrl+C requests cancellation; the operation removes only its newly created incomplete files. If cleanup cannot complete, the error identifies the remaining files. No completed export is claimed after cancellation or failure.

A changed draft, mismatched validation result, unavailable baseline, unsupported shape or unresolved reference refuses export. The CLI never creates client profiles, tenant evidence, approvals or execution plans. Publication, independent review and human/live acceptance retain their separate processes.
