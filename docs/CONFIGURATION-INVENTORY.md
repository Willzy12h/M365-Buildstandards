# Captured configuration inventory

Use this to inspect all data returned in a configuration capture: collection status, objects, settings and returned assignments. It is separate from assessment findings, the intended build-standard definition, drift and upgrade impact. Exporting makes no Microsoft 365 calls or tenant changes.

Current source/build usage (the existing portable ZIP deliberately does not ship a CLI executable):

```powershell
dotnet .\src\BDIT.TenantToolkit.Cli\bin\Release\net10.0\bdit.dll inventory --snapshot "C:\approved-evidence\configuration.json" --root "."
```

Run this from a built source checkout with its development SDK. It is not an instruction for field engineers to install an SDK. Portable CLI exposure is a separate reviewed hosting/package contract; desktop Reports/navigation integration is also pending. The command prints the local report path. Open the HTML in a browser; use Print to keep a document copy. The capture's recorded tenant, primary domain, time, collector identity, permission mode, toolkit version and standard release stay visible. A different `--release` cannot relabel observed data. This command does not need a saved client profile because it exports observed data without assessing client inputs.

Supported `--format` values are `html` (default), `json`, `csv` and `xlsx`. JSON retains the capture structure; CSV/Excel retain the existing tabular export. The report destination is the installation's `reports` folder, or the workspace selected with `--root`. Keep this private client evidence in approved storage; do not upload it into source repositories or chat.

## How to read it

- **Collected**, with no incomplete details: the read completed. A successfully empty collection says no objects were returned.
- **Error / Not attempted / missing collection**: unable to check. An empty list cannot prove that the tenant has no objects.
- **Partially collected**: settings, assignments or relationships were not fully returned. Displayed values are available evidence; missing values remain unknown.
- **Recorded count differs**: the saved count and returned item list disagree. Review the capture rather than treating it as complete.
- **null**: Microsoft explicitly returned null. An omitted property was not returned. Neither means compliant.
- **Evidence integrity is unverified**: the digest is missing or invalid. The offline CLI refuses modified captures; a historical capture without a recorded digest remains visibly unverified. Digests detect modification and are not signatures.

Every returned object property is included, with friendly names first and exact IDs where returned. Nested arrays/settings/assignments retain their recorded values. No name-based ownership, compliance, enforcement or entitlement inference is made.

Exchange/Purview and stored DNS observations are separate sections with their original service/module/time provenance; no DNS lookup is performed. Supplemental data from another tenant is refused. Failed supplemental sections remain unable to check. They cannot complete a missing Graph before-change snapshot or authorise deployment.

The existing capture stores the primary domain but not the complete verified-domain list, and its standard user/device selections omit some fields needed for richer reports. Those omissions are stated; this inventory never guesses them. Additional discovery, mailbox capacity/eligibility and user/log reports are separate work. It is a full inventory of **captured data**, not proof that every Microsoft 365 setting was read.

Desktop Reports/navigation integration remains a separate implementation step; the shared exporter and offline CLI are available in this source change. Use [OPERATOR-START](OPERATOR-START.md) for assessment/deployment and [RELEASE-AND-SERVICING](RELEASE-AND-SERVICING.md) for trusted package handling.
