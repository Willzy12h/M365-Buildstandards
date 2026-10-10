# Testing this build

This guide follows the current integration source and immutable standard 2026.09.30. Use the exact version/source/check record supplied with your candidate; Preview.18 is a historical published package, not proof for changed integration bytes. Agent development and CI checks use synthetic fixtures only. William has reported his own tenant tests; new capabilities still need their recorded acceptance. Sections 1-3 require no tenant. Later sections describe future, explicitly authorised maintainer acceptance; they do not authorise this development task to connect, grant consent, query DNS or operate a device.

## 1. Get the build

Engineers use a **published release**, not a CI artifact. Open the repository's **Releases** page, choose the release you were told to use (for example `v1.1.0-preview.18`), and download the application ZIP. Its `RELEASE-RECORD.json` names the exact source commit, validated run, package SHA-256 and standard; compare the ZIP's SHA-256 with that record and with the digest given to you through the approved channel. You do not need Visual Studio or the .NET SDK.

Reviewers checking an unreleased change use the CI artifact instead. It is not a promoted package: use it only for authorised review/acceptance, and do not present it as an approved production release:

1. Open the repository's **Actions** tab and select the successful run for the exact PR head under review. After merge, use the reviewed `integration` run. Check the commit, not only the green badge on an older run.
2. Download the **portable-windows-review** artifact. It contains the application ZIP and its `.sha256` checksum.
3. Verify the original ZIP against the independently trusted fingerprint, then extract it normally. If organisation application control or Windows security blocks it, use your security owner's approval route for that exact application. Do not unblock, change execution policy, rename it or use another host to evade the block; there is no broad writable-path exception.
4. Run `Start.cmd`, or `app\BDIT.TenantToolkit.App.exe` directly.

To check the download, compare the `.sha256` file with:

```powershell
Get-FileHash .\M365-BuildStandard-Tool-*.zip -Algorithm SHA256
```

The same run also publishes **standard-definition-exports** containing catalogue-only default/settings documents and reusable JSON with integrity metadata. Use [the engineer start guide](OPERATOR-START.md) and [continuity guide](WORKSPACE-CONTINUITY.md) to exercise standard exports, support metadata and separate backup/restore.

The same run publishes **synthetic-ui-review** at three window sizes, with binding, command/refusal and keyboard records, and **engineer-standard-documents** with both generated documents in HTML and Markdown. These contain catalogue data, not tenant evidence. Chromium catalogue-export checks and representative print inspection are recorded in the release completion register. Human screen-reader experience remains a separate acceptance check.

## 2. What it will and will not do

- Nothing is enabled or assigned automatically. Conditional Access policies are created **disabled**, device policies are created **unassigned**, groups are created **empty**, and the office named location is created **untrusted**.
- Assessment operations are read-only and request read scopes; any write-capable token is explicitly highlighted. Deployment is a separate application with separate consent.
- Any live sign-in, read, consent or write requires separate human authorisation, including a disposable tenant. This release has no live Microsoft acceptance evidence.

## 3. Review the interface without a tenant

Launch the application and go straight to these two pages.

**Build Standard.** Load 2026.09.30, browse all 93 controls and expand **More export formats and engineer guides → Export engineer standards and manual guide**. Export both documents in both formats; every control should appear under its area with exact settings and complete manual sections. No client selection or connection is needed. Historical releases should refuse an incomplete manual guide with an explanation. The separate *Prepared for* export is the client-facing document. [Export instructions](ENGINEER-DOCUMENTS.md) explain the distinction; Chromium checks cover all 93 articles, internal anchors, catalogue identity and viewport overflow; both printed PDFs retain all control IDs, and representative pages have been visually inspected. This does not replace checking your chosen browser, printer or assistive technology.

**Configuration.** Open **Capture details, exports and saved captures → HTML inventory** to export the captured settings and objects, with collection status and original provenance. JSON, CSV and Excel remain alongside it. This inventory is separate from assessment and the intended build standard. An incomplete capture remains incomplete; exports do not grant deployment authority.

**Assessment and Plan.** Review the Entra, Intune, Exchange and Purview area filters. A new Plan area clears selection; Select visible selects only eligible rows displayed in that area. **Configuration > Exchange and Purview** exposes the domain, read-only script export, capture import and selected inert proposal. Do not execute the references or press DNS during an offline review; use only synthetic imported captures for local checks.

**Policy automation → Policy inputs and imports.** This is the generated input form. Each field shows what it accepts and its current status: supplied, needed, or which default applies. Type a deliberately wrong value, for example `abc` in a field asking for object identifiers, and confirm the problem appears beside that field rather than as a dialogue.

**Scripts & Reports.** Search for "trace", open *Message trace* and leave it empty: each required field and the "at least one of" rule are listed, and Copy stays off. Fill the dates and one recipient; the command preview shows only what you filled plus the defaults the form names. With no client selected the banner is grey and says to select one; with a client selected it shows the client's name, tenant ID and colour. **Copy script…** asks you to confirm the tenant and every value first. Do not run the copied script during an offline review.

### Checks only a person can make (about 15 minutes)

The build is checked automatically on every change: every page is drawn at three window sizes, every button and
field is checked for a name a screen reader can announce, every table column for readable width, all text for
contrast, every page is walked with the Tab key, every local command is pressed, and the final tenant confirmation
is exercised. What that cannot tell you is how it feels on a real screen, with a real keyboard and a real screen
reader. No tenant is needed for any of this.

1. **Window and scaling.** In *Settings → Display*, set scaling to 150%. Start the application. It should open within
   the screen, with the bottom of the page visible. Make the window as small as it will go: every page should scroll
   to its content rather than hide it. Return scaling to your usual value and look again.
2. **Keyboard only.** Put the mouse aside. From the navigation, press Tab and Shift+Tab through *Connect*,
   *Plan changes* and *Build Standard*. You should always be able to see where focus is (a dashed blue outline), reach
   every button and field, and move within a table with the arrow keys. Press Enter or Space on a navigation item to
   open that page.
3. **Screen reader.** Start Narrator (Windows+Ctrl+Enter). Tab through *Connect* and *Plan changes*. Each field should
   be announced by what it is for - "Tenant ID", "Controls to include in the plan" - never just "edit" or "data grid".
   Stop Narrator with Windows+Ctrl+Enter.
4. **Readability.** On a laptop screen in daylight, check that the edges of input fields are easy to see and that
   status colours in the tables (green, amber, red, blue) are distinguishable. The status is always written as well,
   so colour is never the only signal.
5. **Final confirmation.** This needs a connected deployment session, so leave it for section 5: the *Deploy* button
   must stay disabled until you explicitly approve the displayed verified tenant and exact listed changes; pressing Enter must never deploy. Changing the tenant/operator/client or plan invalidates approval.

Note anything that does not behave as described, with the page and window size, under *What is worth your feedback*.

## 4. Connect a tenant read-only

1. On **Connect**, choose **Quick Connect with Microsoft**, sign in with the client account, inspect the verified tenant/account and choose **Connect to this tenant read-only**. If application readiness needs attention, use **Set up or validate applications**; the verified tenant is carried into setup. Select **Quick setup · check existing applications** or **Administrator sign-in / preview**, review the two applications and their permissions, tick the reviewed approval and select **Approve and create/configure applications**. If an application needs new or changed grants, review and approve its Microsoft browser consent page separately. Existing matching applications/grants are validated and reused; previewing creates nothing. See [application setup](APPLICATION-SETUP.md) for what happens at each stage and what to do if one stops.
2. After any separately approved setup/consent, choose **Connect read-only now**. A different dedicated app or changed/expired identity may require its own sign-in.
3. **Overview and licences** loads subscription counts. **Capture** reads the tenant configuration.
4. **Assessment** compares the capture with the standard. Export the engineer report in any format.

Application setup above creates/repairs registrations and requests consent, so that stage is consequential. The subsequent assessment connection and capture perform reads only. No part of this sequence was performed during development.

## 5. Try a deployment in a disposable tenant

Validate both existing registrations and actual grants against the loaded standard using [application setup](APPLICATION-SETUP.md). Review and separately approve genuinely missing or changed permissions; matching existing grants do not require blanket re-consent. Read the generated before/manual/after instructions before an authorised acceptance test.

Do it in this order, because everything else depends on the first step:

1. **Prerequisites first.** Plan only selected empty prerequisite groups and untrusted office instances. Each office needs a stable key, name and public CIDRs; owner assignment on PRE-011 is a separate reviewed action.
2. Record the created group and location identifiers in the client profile inputs.
3. Plan the controls you want. Read each plan row: it states the exact object to be created, the safe state it will be created in, and any warning, including where a shipped default was used instead of a value you supplied.
4. Execute. Each write is recorded with before and after evidence.
5. Activation and assignment are separate reviewed actions on **Policy automation → Tenant changes and targeting**. Candidate creation does not establish production enforcement, deployment or installation.
6. **Undo and recovery** can delete or restore what the toolkit created, from its own change register.

## 6. What is worth your feedback

- Does the input form ask for things in language that makes sense to an engineer who has not read the code?
- Are the plan row warnings clear enough to act on, particularly the one about a shipped default?
- Does the client document read the way you would want to send it, especially the "Without it" wording?
- Anything that needs an identifier pasted in where it could have offered you a list instead.

## 7. Known limits in this preview

- No live tenant acceptance was performed. An unexpected Microsoft response may require a code correction or a future standard release; never edit a published standard file.
- OneDrive and Edge configuration, the four Microsoft Store applications, and the two agent packages cannot be created until you supply their inputs.
- The existing Autopatch device-category API uses a write-capable scope even for reads. The release separately reports captured update conflicts; processor configuration, entitlement and all-device coverage remain manual prerequisites.
- Group membership is never written. The toolkit creates groups empty and reports who is in them.
- Exchange/Purview use [read-only capture import and inert manual proposals](EXCHANGE-PURVIEW.md). Module retries prevent automatic write execution; SPF trust/alignment must be established before activating the disabled bypass candidate. DMARC and audit retention remain report-only.
- Native-setting gaps, licence/edition conditions, app installation, authentication effects and device behaviour are explicitly unverified. Inspect the generated after-checks. A green build proves the recorded offline checks, not Microsoft service acceptance.
