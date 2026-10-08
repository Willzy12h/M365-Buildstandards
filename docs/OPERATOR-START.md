# Engineer start guide

This is an internal preview. Use the exact reviewed package and its trusted checksum reference. Available recipes are not live effectiveness acceptance. The release owner records approved scope using [controlled acceptance](CONTROLLED-ACCEPTANCE.md). A new client build and legacy backfill follow the same evidence and approval boundaries.

## Your first read-only assessment

Nothing in these steps changes the client's tenant.

1. **Download** the application ZIP from the release you were told to use ([testing this build](TESTING-THIS-BUILD.md) shows where).
2. **Check** its SHA-256 against the one you were given: `Get-FileHash .\M365-BuildStandard-Tool-*.zip -Algorithm SHA256`.
3. **Extract** the whole ZIP to a secure local folder and run `Start.cmd`.
4. **Connect** → **Quick Connect with Microsoft**, sign in with the client's account, check the organisation, then **Connect to this tenant read-only**.
5. **Configuration → Capture configuration (read-only)**. A collection that could not be read is unknown, not absent.
6. **Assessment** → review the findings and their evidence.
7. **Assessment → Export assessment reports → Engineer report (HTML)** to keep a copy.

The walkthrough below explains each step in full, including application setup, client inputs, planning and deployment.

## Hand a workspace to another engineer

1. Disconnect. On **Settings**, choose **Create evidence backup**.
2. Send the backup file and its **SHA-256 fingerprint** by two different routes, for example the file by the shared folder and the fingerprint by Teams message.
3. The receiver extracts a fresh copy of the tool and opens **Settings → Receive a workspace**: choose the file, paste or load the fingerprint, then **Adopt into this workspace**.

No sign-in, approval or plan travels with the evidence. See [workspace continuity](WORKSPACE-CONTINUITY.md).

## Full walkthrough

1. Extract the complete package into a secure local folder. Start `Start.cmd`. No SDK or administrator rights are required to run the application. Keep client evidence out of source repositories and shared unprotected folders.
2. On **Connect**, choose **Quick Connect** and sign in with the client's own account. Inspect the verified organisation, domain and account, then confirm the connection. The same discovery authentication is retained where the selected assessment application supports it; a different application, changed identity, expired access or Conditional Access may still require Microsoft sign-in. Partner/GDAP is a distinct path and needs an explicitly selected client tenant.
3. Assessment requests read permissions and makes no tenant changes. Inspect the permission banner: access containing write scopes is highlighted even while assessment operations remain read-only. Copyable connection details are under **Connection details**. A hidden or blank object setting is not a pass: use collection status and assessment evidence to distinguish explicit null/empty, missing property and incomplete collection.
4. If application readiness needs attention, choose **Application setup → Quick setup · check existing applications**. It uses the verified tenant. Review proposed changes and exact existing application IDs before approval. Setup finds/checks the configured or discovered applications and compares configuration, grants and assignment requirements. Ambiguous matches need explicit selection; a name alone proves nothing. Registration and consent are consequential and separately approved. Administrator bootstrap access is distinct from daily assessment/deployment access. Microsoft's browser consent page can be required even when account authentication uses WAM. **Setup complete** still needs consent/access verification; see [application setup](APPLICATION-SETUP.md).
5. On **Overview and licences**, check available licences. Supply client policy inputs and emergency exclusions where requested. Defaulted policy values are reviewable suggestions; tenant/account/group/office identities have no invented defaults.
6. On **Configuration**, capture current configuration. Inspect each collection status. Failed, truncated or partially read data is unknown and cannot establish absence or authorise writes. Exchange/Purview is a separate read connection with service RBAC and a supported module; relevant accepted domains come from captured evidence where available. See [the integrated and offline collection choices](EXCHANGE-PURVIEW.md). It does not supply a missing Graph pre-change snapshot.
7. On **Assessment**, inspect property evidence and unknown/manual results. On a legacy client, retain existing protection while reviewing equivalent or stricter configuration. Similar names do not grant ownership. Record approved exceptions on **Deviations**. On **Jobs and completion**, open or select a job, select a requirement and record a **Check result**, **Decision about existing protection** or **Replacement stage**. Cite a stored assessment or the saved capture where appropriate. These records keep their history, standard and client-input bindings; they do not perform manual checks or change automated findings. An approved stage alone never completes replacement or authorises activation/retirement. Use **Copy requirement details** for the local handoff record.
8. On **Plan changes**, resolve prerequisite rows before selecting changes. The row explains whether the tool can create a candidate or an engineer/client/vendor must act. Greyed rows are ineligible because of missing inputs, incomplete evidence, licences, an exception, an existing object or no supported recipe; inspect the reason instead of trying to bypass it. A recipe may create a disabled/unassigned candidate while activation dependencies remain outstanding. **Policy automation → Check readiness** explains the corresponding workflow steps.
9. Deployment needs a separately consented deployment application, current verified identity, complete durable before evidence, an unchanged exact preview and explicit approval. Confirm the displayed tenant/account and selected changes; do not repeat a write after a timeout or uncertain result. New CA remains disabled, Intune remains unassigned and existing protection remains intact. Read [unresolved-write handling](UNRESOLVED-WRITES.md) before any recovery.
10. Review write acceptance and configuration readback separately from user/device/sign-in effectiveness. Activation, assignments, pilots, recovery and retirement require their own scope/approval and accepted evidence. Recapture and reassess after approved work. Keep missing, manual, uncertain and failed results open.

## Export the standard definition without a tenant

On **Build Standard**, select a manifest-verified release and choose **Export full standard set**. The visible result gives the local ZIP path. It includes printable defaults/settings HTML, document-ready Markdown, exact original catalogue JSON with its compatible integrity manifest, the generated capability/access matrix and complete manual references where available. Individual HTML, Markdown and JSON buttons are under **More export formats and engineer guides → Defaults and settings — HTML, reusable JSON and document source**. Browser **Print → Save as PDF** produces a shareable document.

The export distinguishes fixed settings, reviewable defaults and unresolved client inputs, and keeps candidate state separate from intended production state. JSON is a reusable catalogue definition with templates; it is not observed configuration, a deployment plan or authority to write. Review its provenance before importing into another tool or a reviewed catalogue release. **Load local candidate** is for separately saved policy-import candidates; this export does not install or overwrite a published standard. See [engineer documents](ENGINEER-DOCUMENTS.md).

## When write history blocks completion

An uncertain reviewed change or LAPS operation blocks completion of every job for that tenant. An unresolved write on a job's control also blocks it, including a historical run against an older standard. A new job, capture or plan does not clear that uncertainty. Read the blocker and [unresolved-write guidance](UNRESOLVED-WRITES.md); retain the original evidence and arrange the documented reconciliation or recovery review. Do not repeat the write to make the job appear complete.

## Check one area or requirement

On **Assessment**, open **Check an area or requirement**. Select an area or a named requirement and click **Check this area** or **Check this requirement**. A verified current connection reads only its required collections; it does not require a full capture first or change anything in the tenant. To review an existing capture instead, tick **Review stored evidence instead of reading live**. That review retains the original capture time and cannot establish a fresh sign-in.

Read the separate **Partial check result and evidence** field, including any unable-to-check reasons. **Copy partial result** includes the saved evidence location. Partial results never replace the full assessment or plan and cannot satisfy complete before-evidence for deployment. Missing read permissions, failed reads and unsupported Exchange/Purview evidence remain unknown; this button does not perform separate Exchange authentication or turn Graph access into Exchange access.

For offline automation on a development/CI installation, `bdit check --snapshot <file> --area Entra` (or `--control <id>`) emits a separate historical scoped-check JSON wrapper to stdout, using the installation's saved client and verified standard. It never connects or writes installation evidence. The current portable package still excludes the CLI until INT-074 hosting and native package checks are implemented.

## Handoff checklist

Record tenant/job intention, responsible engineer/reviewer, package and standard/digest, current capture and assessment IDs, applicable controls, retained external protection, approved exceptions and expiry, manual/prerequisite owners, exact plan/run references, uncertain writes, remaining activation/effectiveness checks and the next review date. Include evidence locations through the secure custodian process. Do not transfer caches or assume the next engineer inherits authentication or approval. Use [workspace continuity](WORKSPACE-CONTINUITY.md) and [the operating model](INTERNAL-OPERATING-MODEL.md).
