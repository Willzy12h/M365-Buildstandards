# Engineer start guide

The tool opens on **Home**. Choose a task or its suggested next step; every shortcut opens an existing page without signing in, collecting data or changing the tenant. The pinned header keeps the current client, account, access and evidence source visible. **Current client and evidence details** expands Home's full status text; connection/evidence details remain copyable in the header. **Overview and licences** retains the subscription and assigned-user tables. Historical or incomplete evidence and scoped checks do not establish readiness or job completion.

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
9. Deployment needs a separately consented deployment application, current verified identity, complete durable before evidence, an unchanged exact preview and explicit approval. On **Connect**, review the application and requested permissions before requesting deployment access. Once connected, inspect the verified tenant/account and returned permissions, then enable **experimental changes** for that session. This opt-in starts off, is not saved and expires when the reviewed connection or its authority changes; it does not replace the final approval for each operation. Disabled deployment, automation and recovery actions explain the next step. Confirm the displayed tenant/account and selected changes; do not repeat a write after a timeout or uncertain result. New CA remains disabled, Intune remains unassigned and existing protection remains intact. Read [unresolved-write handling](UNRESOLVED-WRITES.md) before any recovery.
10. Review write acceptance and configuration readback separately from user/device/sign-in effectiveness. Activation, assignments, pilots, recovery and retirement require their own scope/approval and accepted evidence. Recapture and reassess after approved work. Keep missing, manual, uncertain and failed results open.

## Copy a reviewed library script

Under **Scripts**, **Scripts & Reports** lists the reviewed, read-only library by area; search matches names, descriptions and keywords. Select a client on **Connect** first: the banner at the top shows the client's name, tenant ID and colour, and a copied script signs in to that tenant only. Fill in the form; blank optional fields are left out, several values go one per line or separated by commas or semicolons, and each problem is shown next to its field. When the form is complete, **Copy script…** or **Save as .ps1…** shows a confirmation restating the tenant, the item, "Read only. Makes no changes." and every value; tick it and confirm. Nothing runs from this page. Review the script before running it in your own PowerShell session; it refuses a connection to any other tenant and, when an account is shown in the confirmation, any other signed-in account. Rows that could not be read say Unknown rather than False, and a person or group Exchange named only by name is Unresolved: confirm who it is before acting. A warning starting `BDIT:PARTIAL` means a limit was reached and the list is not complete. No item has been tested in a tenant yet.

## Export the standard definition without a tenant

On **Build Standard**, select a manifest-verified release and choose **Export full standard set**. The visible result gives the local ZIP path. It includes printable defaults/settings HTML, document-ready Markdown, exact original catalogue JSON with its compatible integrity manifest, the generated capability/access matrix and complete manual references where available. Individual HTML, Markdown and JSON buttons are under **More export formats and engineer guides → Defaults and settings — HTML, reusable JSON and document source**. Browser **Print → Save as PDF** produces a shareable document.

The export distinguishes fixed settings, reviewable defaults and unresolved client inputs, and keeps candidate state separate from intended production state. JSON is a reusable catalogue definition with templates; it is not observed configuration, a deployment plan or authority to write. Review its provenance before importing into another tool or a reviewed catalogue release. **Load local candidate** is for separately saved policy-import candidates; this export does not install or overwrite a published standard. See [engineer documents](ENGINEER-DOCUMENTS.md).

## When write history blocks completion

An uncertain reviewed change or LAPS operation blocks completion of every job for that tenant. An unresolved write on a job's control also blocks it, including a historical run against an older standard. A new job, capture or plan does not clear that uncertainty. Read the blocker and [unresolved-write guidance](UNRESOLVED-WRITES.md); retain the original evidence and arrange the documented reconciliation or recovery review. Do not repeat the write to make the job appear complete.

## Check one area or requirement

On **Assessment**, open **Check an area or requirement**. Select an area or a named requirement and click **Check this area** or **Check this requirement**. A verified current connection reads only its required collections; it does not require a full capture first or change anything in the tenant. To review an existing capture instead, tick **Review stored evidence instead of reading live**. That review retains the original capture time and cannot establish a fresh sign-in.

Read the separate **Partial check result and evidence** field, including any unable-to-check reasons. **Copy partial result** includes the saved evidence location. Partial results never replace the full assessment or plan and cannot satisfy complete before-evidence for deployment. Missing read permissions, failed reads and unsupported Exchange/Purview evidence remain unknown; this button does not perform separate Exchange authentication or turn Graph access into Exchange access.

For offline automation on a development/CI installation, `bdit check --snapshot <file> --area Entra` (or `--control <id>`, optionally with `--exchange-snapshot <file>` as for `report`) emits a separate historical scoped-check JSON wrapper to stdout, using the installation's saved client and verified standard. It never connects or writes installation evidence. The portable ZIP includes the same offline commands through `bdit.cmd`; see **Offline command line in the portable ZIP** below.

## Handoff checklist

Record tenant/job intention, responsible engineer/reviewer, package and standard/digest, current capture and assessment IDs, applicable controls, retained external protection, approved exceptions and expiry, manual/prerequisite owners, exact plan/run references, uncertain writes, remaining activation/effectiveness checks and the next review date. Include evidence locations through the secure custodian process. Do not transfer caches or assume the next engineer inherits authentication or approval. Use [workspace continuity](WORKSPACE-CONTINUITY.md) and [the operating model](INTERNAL-OPERATING-MODEL.md).


## Offline command line in the portable ZIP

Open Command Prompt or PowerShell in the extracted toolkit folder and run `bdit.cmd --help` (PowerShell: `.\bdit.cmd --help`). The launcher uses the same packaged application; no SDK or additional executable is needed. It preserves the current working directory, so quote input paths containing spaces. For example: `bdit.cmd inventory --snapshot "C:\Engineer work\configuration.json" --format html`. `--root` optionally selects an existing toolkit root. Reporting commands consume stored evidence; doctor checks local portable prerequisites. Offline commands never connect, request consent or deploy.

Run `bdit.cmd doctor` for readable local package/catalogue/settings, writable-folder and optional PowerShell-host findings. Use `--format json` for a separate diagnostic JSON result and `--root "C:\Engineer work\toolkit"` to select another existing portable folder. Doctor uses temporary owned probes; it offers no repair, installation, sign-in or script execution. Local Ready is not publisher trust, tenant readiness or write approval. Exit 0 means required local checks pass, 1 means required Fail/Unknown, 2 refuses invocation/root input, 3 is a sanitised unexpected failure and 130 is cancellation. Ctrl+C requests cancellation and probe cleanup; incomplete required checks remain Unknown. Native interactive Windows Ctrl+C remains an acceptance step.

Help and report paths go to standard output; refusals/errors go to standard error with a non-zero exit code. Redirection is supported (`bdit.cmd --help > help.txt 2> errors.txt`). A blocked application stays blocked: follow your security owner's application-control approval route, using the independently trusted fingerprint. Do not switch hosts or bypass controls. Source implementation and fresh Windows package verification do not establish live Microsoft or human accessibility acceptance.

## Captured configuration and the reviewed script library

On **Configuration**, the short capture summary identifies live or stored/offline evidence, completeness, time and standard. Open **Capture details, exports and saved captures** for the exact capture ID/actor/provenance, **Copy capture summary**, the full **HTML inventory**, JSON/CSV/Excel and saved captures. HTML lists the observed configuration and collection limitations; assessment reports explain matches/gaps, and build-standard exports describe intended defaults. These are different documents. Nothing is collected anew by exporting.

Under **Scripts**, open **Scripts & Reports** and select a library item, read its purpose and tenant banner, fill in the typed form and open **Requirements and limitations · review before copying**. **Copy script…** or **Save as .ps1…** presents the target/account/values for unticked review. The page runs nothing, installs nothing and makes no tenant call. The copied script is for a separately authorised manual run with the declared runtime/module/RBAC. Its tenant/account checks refuse a different reviewed context. Exchange changes remain copy-only; unknown or partial output is not a successful check.

The portable **bdit.cmd** uses the same executable for offline commands, including `bdit.cmd scripts` and `bdit.cmd script --id <id> --copy`. It needs no .NET SDK. Follow command help for required inputs; offline evidence never becomes fresh deployment evidence. An interactive Windows console run remains an acceptance step even though CI verifies redirected output and exit codes.


## Tenant reports and naming audit

Navigation groups the existing tools under **Tenant**, **Build standard**, **Reports**, **Scripts** and **Settings**. Numbered build steps and their approval/evidence boundaries are unchanged.

On **Tenant reports**, choose **Read a report** and select users/licences, Intune devices, MFA registration, sign-ins or directory audit. For logs, supply the UTC range. Open **Required permissions, limits and current access**, read the experimental acknowledgement and deliberately choose **Read selected report**. Missing authorised access stays NotAttempted; use Connect or Application setup separately if it needs review. Opening this page never signs in or consents. Registration does not prove MFA enforcement or successful use, and report data is not complete deployment before-evidence.

Use **Results** to inspect section/read status, original time, unknown fields and exact row details. **Provenance and limits** retains source limitations. Search/read-state filters change the table only: **HTML report**, **JSON evidence**, **CSV ZIP** and **Excel XLSX** export the full original record. A failed, cancelled or partial read is distinct from a successful zero-row read. Read **NOT SAVED** reasons before relying on local retention.

Use **Saved evidence → Refresh saved reports → Open saved report**, or **Choose JSON → Open supplied JSON**, for offline review of the selected tenant’s schema 1 Graph report evidence. Damaged records explain why they cannot open; an incomplete listing does not imply there are no other files. Supplied-file claims keep their original times and provenance and never establish a new sign-in. Opening a report does not overwrite a configuration capture or authorise writes.

On **Naming and ownership**, choose **Review selected capture** after opening or deliberately collecting configuration in **Configuration**. Review names, collection completeness, exact IDs and ownership reasons. A matching name cannot establish toolkit ownership; overlapping collections and missing corroboration remain unknown. The audit makes no tenant request and never renames an object.


## Review a saved client offline

On **Connect**, select a saved client and choose **Review saved evidence offline**. Disconnect tenant and application-setup sessions first. This opens the persisted saved client, rather than unsaved form edits, without authentication or tenant requests. On **Configuration**, select its stored capture and open it; the evidence banner keeps its original time and completeness visible on other pages. Connection/access and evidence source are separate: historical evidence does not verify current access or authorise deployment.

Use **Undo and recovery → Load change register** to inspect local history offline. Re-verification requires a fresh verified read connection; recovery preview/execution requires the separate deployment context and exact approval. Disabled-action guidance states the next step. Switching clients, standards or material client inputs clears unfinished manual-check/exception drafts. Ordinary refreshes and label-only changes preserve a draft for the same context. Restart or workspace adoption requires explicit saved-client selection again; authentication and approval are never transferred.
