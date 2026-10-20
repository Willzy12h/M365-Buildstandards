# Local candidate standards

Candidate authoring is offline. It does not connect to a tenant, install a catalogue, change historical standard files or publish a release. A candidate is not deployment approval or a verified successor lineage.

The current source provides a shared draft/validation/output engine, a native offline editor and the CLI commands below. The supported policy-import journey is still being implemented. Native validation results must match the candidate source; source implementation alone is not Windows acceptance.

## Native offline editor

Open **Catalogue reference → Create or edit a local candidate standard — offline**. Choose **Create from loaded standard**, select a control and choose **Show settings**. The editor shows the published baseline value separately from the proposed value. Text settings accept plain text; other settings use their existing JSON value type, with whole arrays edited together. Unsupported routes, fields, safety changes and incompatible values are refused by the shared engine.

Choose **Apply to draft**, then **Validate candidate**. **Save draft** writes a new local draft; **Export candidate and comparison** creates the four-file set described below. Neither action loads the candidate into the active tenant session. To undo an applied value, set it back to the displayed baseline value and apply it. **Reset input** discards only un-applied typing.

Keep working, save, or explicitly **Discard unsaved draft** before replacing its baseline or closing the application. Navigating away keeps the draft in memory; closing with unsaved edits returns to the expanded editor. A client/session/catalogue context change clears comparison and validation but preserves the draft: save it, then deliberately reopen that file. Existing files are never deleted by Discard.

## Create a draft

Use the exact filename of a catalogue already registered in the installation's verified standards manifest:

```text
bdit candidate --action create --catalogue 2026.10.15.json --title "Proposed internal standard" --root "C:\Tools\BuildStandard"
```

The JSON response names a new local draft under `reports`. Its identity includes the exact baseline file SHA-256, release and schema. Moving identical verified catalogue bytes to another registered filename is allowed; replacing them with a newer same-name or same-release catalogue is not. Missing baseline bytes block validation/export.

The editor's draft model supports existing control recipe settings and reviewable parameter defaults. It does not add controls, collections, permissions, execution routes, licence rules or new template syntax. Foreign references need explicit supported mappings. Conditional Access candidates stay disabled and preserve their exact targeting/exclusions; Intune candidates remain unassigned. macOS remains unsupported and blocked. Edited defaults lose their inherited vendor-review date; editing is not a new vendor review.

Do not hand-edit a saved draft's digest to bypass validation. Policy import is not yet exposed in this editor. Saving an unresolved draft will not make it exportable.

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

Outputs are created without overwriting earlier files and are read back before success is reported. Ctrl+C requests cancellation. If any file has been created, the error identifies the possibly incomplete paths for local review. The tool does not delete by pathname after releasing a file handle, because another process could have replaced that file. No completed export is claimed after cancellation or failure.

A changed draft, mismatched validation result, unavailable baseline, unsupported shape or unresolved reference refuses export. The CLI never creates client profiles, tenant evidence, approvals or execution plans. Publication, independent review and human/live acceptance retain their separate processes.
