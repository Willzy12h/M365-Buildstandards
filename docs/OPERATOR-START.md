# Engineer start guide

The tool opens on **Home**. Choose a task or its suggested next step; every shortcut opens an existing page without signing in, collecting data or changing the tenant. The pinned header keeps the current client, account, access and evidence source visible. **Current client and evidence details** expands Home's full status text; connection/evidence details remain copyable in the header. **Overview and licences** retains the subscription and assigned-user tables. Historical or incomplete evidence and scoped checks do not establish readiness or job completion.

This is an internal preview. Use the exact reviewed package and its trusted checksum reference. Available recipes are not live effectiveness acceptance. The release owner records approved scope using [controlled acceptance](CONTROLLED-ACCEPTANCE.md). A new client build and legacy backfill follow the same evidence and approval boundaries.

## Native command centre and working context

Use the five groups **Workspace**, **Build standard**, **Reports**, **Scripts** and **Settings**. The page selector opens existing pages; navigation does not sign in or approve an action. Home’s recommendation and the header’s **Next step** use the same current-state guidance. Home refreshes that recommendation when clicked, so a session or plan that expired while the window was idle leads back to the appropriate review step. Navigation does not renew access or approve changes. **Discover the tenant**, **Assess saved evidence**, **Backfill a legacy build** and **Review a change plan** open their existing workflows. Repeat review and engineer handover remain available below them.

The header keeps the working client, signed-in identity and permission meaning visible. **Connection and evidence details** exposes exact IDs/scopes and **Copy details**. Assessment operations remain read-only even when returned access is broader; broader or unclassified capabilities are highlighted. A mode label is not an authorisation grant.

**Pin capture** opens an original saved reference, with a side pane on a wide window and a drawer on a compact window. It is not selected evidence for a new client and has no run/approve action. Changing client, account or source collapses sensitive details and shows a mismatch warning. **Copy original reference** retains its original provenance; **Close reference** does not delete evidence.

Assessment counts separate compliance, review-needed results, matched-but-not-enforced settings, unread settings, recorded deviations and client exclusions. Open the review breakdown and collection limitations; no compliance score is calculated. A filtered selected finding remains explicitly labelled hidden. New source/context clears its old detail.

**Last built plan** describes the recorded proposal separately from current ticked controls. Different selection, expired evidence or changed identity requires rebuilding; runtime guards still check permissions, mappings, deviations, evidence and exact approval. Prerequisite guidance says what the tool prepares and what the engineer must do. An empty group, disabled CA candidate or unassigned Intune candidate is not functional completion.

Recorded outcomes distinguish request acceptance, configuration readback, saving and protection state. An intended payload is not readback. Unknown readback remains unverified; earlier accepted/uncertain rows must not be replayed because a later row stopped. Saving is verified against the durable local record when available. Functional user/device/sign-in checks remain separate.

William has accepted this source and authorised promotion to `main`; the exact source, checks and package fingerprint are recorded in the promotion PR and issue #83. This is owner acceptance, not a new independent review. The package remains unpublished. H1 accessibility, workstation-policy and live acceptance are unverified; passing automated checks does not close them.


## Your first read-only assessment

Nothing in these steps changes the client's tenant.

1. **Download** the application ZIP from the release you were told to use ([testing this build](TESTING-THIS-BUILD.md) shows where).
2. **Check** its SHA-256 against the one you were given: `Get-FileHash .\BuildStandard-*.zip -Algorithm SHA256`.
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

For offline automation on a development/CI installation, `buildstandard check --snapshot <file> --area Entra` (or `--control <id>`, optionally with `--exchange-snapshot <file>` as for `report`) emits a separate historical scoped-check JSON wrapper to stdout, using the installation's saved client and verified standard. It never connects or writes installation evidence. The portable ZIP includes the same offline commands through `buildstandard.cmd`; see **Offline command line in the portable ZIP** below.

## Handoff checklist

Record tenant/job intention, responsible engineer/reviewer, package and standard/digest, current capture and assessment IDs, applicable controls, retained external protection, approved exceptions and expiry, manual/prerequisite owners, exact plan/run references, uncertain writes, remaining activation/effectiveness checks and the next review date. Include evidence locations through the secure custodian process. Do not transfer caches or assume the next engineer inherits authentication or approval. Use [workspace continuity](WORKSPACE-CONTINUITY.md) and [the operating model](INTERNAL-OPERATING-MODEL.md).


## Offline command line in the portable ZIP

Open Command Prompt or PowerShell in the extracted toolkit folder and run `buildstandard.cmd --help` (PowerShell: `.\buildstandard.cmd --help`). The launcher uses the same packaged application; no SDK or additional executable is needed. It preserves the current working directory, so quote input paths containing spaces. For example: `buildstandard.cmd inventory --snapshot "C:\Engineer work\configuration.json" --format html`. `--root` optionally selects an existing toolkit root. Reporting commands consume stored evidence; doctor checks local portable prerequisites. Offline commands never connect, request consent or deploy.

Run `buildstandard.cmd doctor` for readable local package/catalogue/settings, writable-folder and optional PowerShell-host findings. Use `--format json` for a separate diagnostic JSON result and `--root "C:\Engineer work\toolkit"` to select another existing portable folder. Doctor uses temporary owned probes; it offers no repair, installation, sign-in or script execution. Local Ready is not publisher trust, tenant readiness or write approval. Exit 0 means required local checks pass, 1 means required Fail/Unknown, 2 refuses invocation/root input, 3 is a sanitised unexpected failure and 130 is cancellation. Ctrl+C requests cancellation and probe cleanup; incomplete required checks remain Unknown. Native interactive Windows Ctrl+C remains an acceptance step.

Help and report paths go to standard output; refusals/errors go to standard error with a non-zero exit code. Redirection is supported (`buildstandard.cmd --help > help.txt 2> errors.txt`). A blocked application stays blocked: follow your security owner's application-control approval route, using the independently trusted fingerprint. Do not switch hosts or bypass controls. Source implementation and fresh Windows package verification do not establish live Microsoft or human accessibility acceptance.

## Captured configuration and the reviewed script library

On **Configuration**, the short capture summary identifies live or stored/offline evidence, completeness, time and standard. Open **Capture details, exports and saved captures** for the exact capture ID/actor/provenance, **Copy capture summary**, the full **HTML inventory**, JSON/CSV/Excel and saved captures. HTML lists the observed configuration and collection limitations; assessment reports explain matches/gaps, and build-standard exports describe intended defaults. These are different documents. Nothing is collected anew by exporting.

Under **Scripts**, open **Scripts & Reports** and select a library item, read its purpose and tenant banner, fill in the typed form and open **Requirements and limitations · review before copying**. **Copy script…** or **Save as .ps1…** presents the target/account/values for unticked review. The page runs nothing, installs nothing and makes no tenant call. The copied script is for a separately authorised manual run with the declared runtime/module/RBAC. Its tenant/account checks refuse a different reviewed context. Exchange changes remain copy-only; unknown or partial output is not a successful check.

The portable **bdit.cmd** uses the same executable for offline commands, including `buildstandard.cmd scripts` and `buildstandard.cmd script --id <id> --copy`. It needs no .NET SDK. Follow command help for required inputs; offline evidence never becomes fresh deployment evidence. An interactive Windows console run remains an acceptance step even though CI verifies redirected output and exit codes.


## Tenant reports and naming audit

Navigation groups the existing tools under **Tenant**, **Build standard**, **Reports**, **Scripts** and **Settings**. Numbered build steps and their approval/evidence boundaries are unchanged.

On **Tenant reports**, choose **Read a report** and select users/licences, Intune devices, MFA registration, sign-ins, directory audit, application credential expiry or observed application consent. For logs, supply the UTC range. Expiry uses a whole-number horizon of 1–365 days (default 30); consent uses no date or horizon. Credential reports expose metadata only, never secret values. Missing delegated-grant access remains NotAttempted without requesting Directory.Read.All. Open **Required permissions, limits and current access**, read the experimental acknowledgement and deliberately choose **Read selected report**. Missing authorised access stays NotAttempted; use Connect or Application setup separately if it needs review. Opening this page never signs in or consents. Registration does not prove MFA enforcement or successful use, and report data is not complete deployment before-evidence.

Use **Results** to inspect section/read status, original time, unknown fields and exact row details. **Provenance and limits** retains source limitations. Search/read-state filters change the table only: **HTML report**, **JSON evidence**, **CSV ZIP** and **Excel XLSX** export the full original record. A failed, cancelled or partial read is distinct from a successful zero-row read. Read **NOT SAVED** reasons before relying on local retention.

Use **Saved evidence → Refresh saved reports → Open saved report**, or **Choose JSON → Open supplied JSON**, for offline review of the selected tenant’s registered Graph schema 1 or application schema 2 report evidence. Damaged records explain why they cannot open; an incomplete listing does not imply there are no other files. Supplied-file claims keep their original times and provenance and never establish a new sign-in. Opening a report does not overwrite a configuration capture or authorise writes.

If an evidence path is refused, use a fully downloaded copy in an ordinary local folder outside OneDrive or other sync/link locations, then choose that JSON again. Evidence readers refuse linked/reparse paths and never relax that guard for a cloud placeholder. A file-access refusal does not mean an empty report; preserve the original and investigate storage access. Actual Windows Files On-Demand behaviour still needs human acceptance.

On **Naming and ownership**, choose **Review selected capture** after opening or deliberately collecting configuration in **Configuration**. Review names, collection completeness, exact IDs and ownership reasons. A matching name cannot establish toolkit ownership; overlapping collections and missing corroboration remain unknown. The audit makes no tenant request and never renames an object.


## Review a saved client offline

On **Connect**, select a saved client and choose **Review saved evidence offline**. Disconnect tenant and application-setup sessions first. This opens the persisted saved client, rather than unsaved form edits, without authentication or tenant requests. On **Configuration**, select its stored capture and open it; the evidence banner keeps its original time and completeness visible on other pages. Connection/access and evidence source are separate: historical evidence does not verify current access or authorise deployment.

Use **Undo and recovery → Load change register** to inspect local history offline. Re-verification requires a fresh verified read connection; recovery preview/execution requires the separate deployment context and exact approval. Disabled-action guidance states the next step. Switching clients, standards or material client inputs clears unfinished manual-check/exception drafts. Ordinary refreshes and label-only changes preserve a draft for the same context. Restart or workspace adoption requires explicit saved-client selection again; authentication and approval are never transferred.

The offline command `buildstandard.cmd report-evidence --input "report.json" --tenant <expected-tenant-id> --format html` uses the same strict registered schema dispatcher for existing Graph and application reports. JSON/CSV/Excel preserve original sections, nested entries, source times and limitations. It never collects new data or grants access.

### Check this portable folder offline

In Settings and diagnostics, expand Offline prerequisite checks and choose Run offline checks. Read each finding and next step; Required Unknown or Fail means local readiness is unconfirmed. Cancel stops only this check, and Copy local result copies the completed findings. These local checks do not sign in, run scripts, repair the computer or certify deployment authority. The equivalent command is `buildstandard doctor`.

## Run a registered read-only Exchange report

Under **OPERATE → Run reports**, select **Mailbox size, quotas and archive**, **Mailbox protocol settings**, **Mailbox forwarding settings** or **Mailbox hold settings**, and an approved installed PowerShell runtime. Connect to the selected client first, review **Verified connection and prerequisites**, then deliberately enable **experimental read-only runs for this connection**. This starts off and is separate from deployment access; no consent or module installation is granted here.

Fill in the typed values and choose **Review run**. Read **Exact review**, tick its separate approval and choose **Run**. Changing the client, account, runtime or values requires a new review. Exchange sign-in is separate from Graph and may require Microsoft CA/MFA/consent. Page navigation does not sign in. If application control blocks execution, ask the security owner; do not change policy or switch hosts to evade the block.

Read **Outcome**, its original source/read states and the virtualised read-only row table. Columns can be sorted and scrolled horizontally; select/copy cells using the normal Windows grid controls. A collected null forwarding target is shown as **Not set (observed)**; a missing/failed value stays **Unknown / not returned**, and observed False stays False. Cancel waits for this owned run to stop; a cancelled or failed read is not an empty result. Choose an export format (HTML, JSON, CSV ZIP or Excel XLSX) and **Export report** for the complete original report, provenance and read states; no new collection occurs. Save/export the original evidence first if the recovery panel is active. The last successful export path is selectable for copying. **Copy outcome** and **Copy evidence JSON** are deliberate local actions after completion. If saving fails, the **Original report needs saving** panel retains verified bytes and their original tenant/account separately from the current client. Inspect folder access, then choose **Save to original client**, **Export original JSON** to a new file, or explicitly confirm **Discard original report**. Disconnecting or changing report/client inputs clears the current result view but does not destroy this pending original. Another run and normal window close are blocked until it is resolved. Export checks the written bytes and never overwrites a file; it does not admit evidence to the current client. None of these actions reruns the report. Pending recovery is in memory: a process crash or forced termination can still lose it. The separate **Scripts & Reports** page continues Copy/Save only, including all Exchange change proposals. Run is experimental/live-unverified until accepted.

Choose **Open mailbox capacity** in Outcome to continue to quota and eligibility review. It opens the historical review page without collecting, selecting a source or changing Run approval. You still need a separately collected users/licences report and must explicitly choose both records.

## Review mailbox size, quotas and 100 GB eligibility

Under **OPERATE → Mailbox capacity**, select the client for historical review. In **Choose sources**, choose saved records or supplied JSON separately for the registered mailbox inventory and **users/licences** report, then **Review selected source pair**. You can collect mailbox evidence deliberately through **Run reports** and licensing through **Tenant reports**; this review page makes no tenant call or sign-in. Source times and limitations stay separate.

If either source is missing, **Open mailbox collection** opens Run reports and **Open users/licences collection** opens Tenant reports. Connect and review the intended report on that page before deliberately collecting it; these shortcuts do not sign in, enable experimental access or start a read. Return to Mailbox capacity, choose **Refresh saved source lists**, and select both original records. Navigation never guesses which source pair to use.

**Results** distinguishes observed 100 GB capacity, actual assigned-plan eligibility and unable-to-check cases. Select a mailbox for exact IDs and raw primary/archive values; Unlimited or failed reads are never converted to zero. A licence name alone does not prove configured quota. Use **Source provenance and limits** to inspect both source histories; an incomplete list is not evidence of no mailboxes. Historical eligibility may change and does not authorise a quota increase.

**Export HTML**, **Export JSON**, **Export CSV ZIP** and **Export Excel** export every review row and original source provenance, even after a display filter. Derived JSON includes the two original records and is clearly labelled as review, not importable deployment evidence. Changing source/client/context clears the old review and requires a new selection. No quota, archive or licence setting is changed.

## Running old and new portable copies

Use `buildstandard.cmd` for new offline commands; `bdit.cmd` is a compatible alias to the same host. New executables use `BUILDSTANDARD_ROOT` and accept the older `BDIT_TOOLKIT_ROOT`; conflicting root aliases refuse. While an old and new copy coexist, set **`BDIT_TOOLKIT_ROOT` to the same intended root for both**, or pass an explicit `--root` to each CLI command. An older executable does not understand `BUILDSTANDARD_ROOT`. The shared write lock protects one tenant in one resolved root; it does not coordinate separate roots. Do not duplicate a workspace to work around a refused lock or use a renamed executable as renewed approval. Loading settings/evidence performs no automatic path or cache migration.

For new installations the product title is BuildStandard. Organisation text in client reports is optional and blank by default; any previously saved organisation remains yours to review. An existing tenant application's observed name and ID remain unchanged. A new neutral setup suggestion does not find, rename or recreate an existing registration by display name.

### Engineer Build Standard HTML reference

The catalogue-only Engineer Build Standard HTML export requires a verified loaded release. It saves the exact generated UTF-8 document with its catalogue hash, generation time and separate presentation version; it does not contain current-client evidence or authorise changes. A source change before generation refuses the export; a later source change cannot silently replace an already generated reference. Save failure is reported as failure. The contained reader is a later phase task; this content/export change does not claim browser Runtime or human accessibility acceptance.

### Catalogue reference reader

In **Build standard → Catalogue reference**, select a control, then **View reference**. This displays the existing Engineer Build Standard document at that control; its release/document hashes are separate from the current client. It does not authenticate, assess, run PowerShell or approve changes. **Back to control** preserves the unchanged selection/filter; a changed client/account/catalogue hides the former reader. **Save HTML** writes the admitted document's exact bytes. Select/copy text, use native Find or zoom. A Microsoft link remains blocked until its destination is shown and you deliberately choose **Open Microsoft reference**.

The reader alone requires an organisation-managed Stable Evergreen WebView2 Runtime and a writable local profile folder. BuildStandard installs no Runtime and uses no alternate browser host. Missing/incompatible/blocked Runtime, cache failures or unreviewed Runtime/profile/debugger overrides leave native catalogue inspection and the existing export routes available. Ask the maintainer/security owner about permitted Runtime servicing or application-control approval; do not bypass a block. **Retry reader** is deliberate and presentation-only. Local reader caches are not evidence and do not transfer with engineer handover; an incomplete cleanup is recorded in the activity log. Human accessibility/scaling and managed-policy acceptance remain unrun H1 tasks.

### Saved assessment history in Jobs

The assessment selector lists only readable evidence matching the capture in view and the selected Build Standard. **Do not cite an assessment** records your own outcome without adding an assessment citation; it does not verify a requirement by itself. The guidance beneath the selector explains whether a saved capture is needed, no evidence matches, or other captures/releases were excluded.

An unreadable or wrong-client history file appears in the copyable **Job needs review** details. Healthy matching assessments remain available. Preserve the original file and inspect local evidence/diagnostics; the tool does not delete or repair it automatically. A selection or completed job does not authorise a tenant change.


### Supported device scope

The 2026.10.15 successor standard requires compliant **Windows** devices in CA-005. macOS is unsupported: devices returned by Intune can be inspected in the inventory, but macOS deployment and macOS compliance requirements are blocked. There is no switch to enable them. This does not add a tenant policy denying macOS access. Other ordinary tenant-wide policies, such as MFA, retain their reviewed scope.

Historical standards remain readable and unchanged. Their Windows-and-macOS CA-005 recipe is now blocked from deployment; select the successor and review its upgrade impact. Existing live policies are never renamed or retargeted automatically. Preserve emergency/operator exclusions, disabled CA candidates and unassigned Intune candidates. Activation and assignment remain separately reviewed actions.
### Choosing client objects and offices

In Connect → Notes and policy parameters, **Find an existing group or named location** uses the current client's existing read access. Choose the purpose, enter a name or exact ID, then **Find objects**. Select the exact name/ID and **Use selected object**; this fills local inputs only. Save/apply them before collecting a new plan. Duplicate names remain separate choices. Failed or incomplete lookup is shown as unavailable, never as proof that the object is absent. Existing exact-ID entry remains available and is subject to normal readiness/reference checks.

In Policy automation → Policy inputs, add one office row per site. Enter its friendly name and public CIDR ranges, separated by commas. Correct the inline errors before saving. Its stable key appears in **Selected office stable key** and survives renaming; removing a row only changes local inputs until saved. Public-IP validation remains authoritative; no DNS lookup, named-location creation or assignment occurs here. Directory device lookup requiring extra access is not added by this update.


Policy inputs now show the fields used by the selected control. **Show all client inputs** reveals other saved fields and office rows without discarding any edits. Local policy imports have their own **Local imports** tab and require disconnecting. Application package upload and tenant activation/targeting remain separate tabs with their existing exact previews and approvals. Disabled actions explain the required connection, input or review step; the common progress display still reports the active operation.

### From a finding to a reviewed plan

Select a finding in the full current assessment and choose **Add to plan**. The tool opens Plan changes and ticks that exact eligible control, retaining other choices from the same source. Read the explanation beside a disabled button: historical/partial evidence, failed reads, manual controls and unowned partial matches cannot use this shortcut. Changing client, capture, standard, assessment or session clears stale selections.

This step does not build, approve or run a plan. Build the plan deliberately, resolve its readiness checks, review the exact proposal and obtain the normal approval before deployment. Candidates remain disabled/unassigned; no score or protection claim is inferred from adding a finding.

### Local exception review

Saving or removing an exception refreshes an assessment when either full configuration or supplemental Exchange evidence is open. Office-based exceptions select the individual office control, not its template. Original captures and their timestamps are unchanged. The existing Exchange assessment applies explicit Not applicable exceptions; an Approved deviation record does not currently replace its observed Exchange finding with Compliant with deviation. This update preserves that distinction and creates no deployment approval.


## Preview a Partner Center customer list offline

In the portable folder, run `.\buildstandard.cmd customers --csv "C:\Work\customers.csv" --root "C:\Tools\BuildStandard"` (the `bdit` alias also works). This prints JSON comparing the CSV with local saved clients. It does **not** save, overwrite, connect or verify access. Relationship/tags and domains remain unverified source text. The native bulk-import/save journey is not delivered by this command.

Use the Partner Center customer-list CSV with **Microsoft ID** and **Company name** headings; **Primary domain name** is optional. UTF-8 with optional BOM, quoted commas/newlines, at most 5 MiB and 5,000 data rows are supported. Invalid/conflicting rows stay visible; existing clients are skipped. Correct malformed quoting or conflicts in the source and preview again. An unreadable/blocked local profile store is an error, never an empty client list. The CLI bounds the saved-profile file at 8 MiB and 5,000 clients, checking the count before constructing profile objects. No Graph consent or Microsoft modules are needed.

### Supplied hypothetical CA evaluation reports (in-progress expansion)

`buildstandard ca-evaluation --input <original-json.zip> --tenant <expected-guid> --format html|json|csv|xlsx --root <portable-folder>` exports the explicitly supplied original report offline. JSON produces a ZIP containing original metadata, response bytes and a warning; it does not reformat or reseal them. Imports remain historical and unverified. HTML and tables distinguish unknown applicability from a returned false, and applicability from enforcement. This command never connects, evaluates, requests consent or deploys. Connected native capture is still being implemented and is not available merely because the exporter exists.
