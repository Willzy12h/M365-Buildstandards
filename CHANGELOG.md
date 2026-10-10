# 1.1.0-preview.19 — unreleased: Claude review corrections and usability

- Verify a successful throttled page-two retry preserves its nextLink, cancelled report collection retains explicit partial rows, and a renewed 401 fails the one-renewal count directly (PR #67 independent review corrections; production transport unchanged).

- Make interruption tests independent of synchronous authentication/dispatch while retaining real back-off cancellation regressions.

- Extend offline Graph interruption checks through real retry delays and second-page cancellation, preserving one silent renewal and treating cancellation after write dispatch as ambiguous without replay. Production transport behaviour is unchanged.

- Local candidate imports enforce the existing authored naming rules, including collection prefix and platform suffix. Restore current ENR-002 imports through the reviewed enrolment rule. Historical 2026.09.6–2026.09.11 ENR-003 Autopilot candidate imports now refuse because no reviewed Autopilot naming family is registered; stored historical standards and live names stay unchanged.

- Check registered report exports end to end for partial status, multilingual/multiline text, explicit Excel truncation, unknown values and earlier-file preservation; tampered records refuse every format before writing. Production export behaviour is unchanged.

- Add offline report scale validation and a repeatable measurement command: round-trip and HTML/CSV/Excel checks at 100, 1,000 and 5,000 rows, preserving unknowns and refusing over-cap evidence. Measurements do not change production limits or imply live Microsoft performance.

- Label offline report exports with supplied-file provenance, refuse duplicate export options, and strengthen malformed-input safeguard checks.

- Add offline `bdit report-evidence` HTML/JSON/CSV/Excel exports for validated registered Graph report evidence. Exact tenant, bounded UTF-8, schema and integrity checks refuse untrusted input; failed/partial/cancelled reads remain explicit. No sign-in, new collection, evidence mutation or script execution.

- Correct mailbox-capacity interpretation for legacy shared mailboxes, the actual reviewed Business Premium service-plan set and malformed byte displays; retain separate statistics/quotas read failures.

- Add pure read-only mailbox capacity evaluation using strict assigned-licence evidence: keep observed primary quotas, 100 GB configuration, commercial Plan 2 eligibility and archive limits separate. Exchange collection/report integration and live acceptance remain pending.

- Script library: a list entry made only of whitespace now makes the mailbox, protection or quarantine policy setting Unknown with a warning, instead of a known count.

- Show script requirements and limitations in the actual Copy/Save confirmation; retain stored-capture deployment ineligibility in the compact configuration summary.

- Keep a known emergency-account problem visible when another identity read is unknown, and explain in-progress Managed Google Play unbinding without proposing another bind.

- Add dedicated regression coverage for conflicting legacy disposition identities, unchanged refused-write history and truthful completion of preserved conflicting records; production workflow behaviour is unchanged.

- Add the owned read-only runner engine (INT-088 slice 2): schema 2 manifests and registry with explicit version dispatch; an engine-pinned PowerShell wrapper that checks the script's syntax tree before sign-in and reaches registered Exchange reads only through a private gate that rechecks the tenant, account and stop request before and after every read; a pinned, strictly typed request; and a strict result that must repeat the exact run, item, pins, tenant, account and parameters before it is sealed as `exchangeReportEvidence`. Output, diagnostics, result size and time are bounded; a stop ends the process tree after five seconds; a refused host, a missing module or a failed check keeps no rows. No shipped item becomes runnable and no UI, CLI, permission or tenant access is added; every regression uses a synthetic stand-in module. The runner refuses a child result with a null required member, collection or element as malformed output (AST-20261010-02), and the read gate refuses an expired or unconfirmed Exchange token before and after every read, keeping no rows when it expires mid-run (AST-20261010-03).

- Add strict `exchangeReportEvidence` schema 1 (INT-088 slice 1): models, a package-owned registry with the first `exo-mailbox-inventory` registration, a bounded reader and a create-once tenant-partitioned store. Anything outside the registration, any non-normalised parameter, any value from a group that was not read and any status better than its sections is refused; a failed read is never zero and `Unlimited` stays `Unlimited`. A blank mailbox type, size, quota or archive value is unreadable and is refused (AST-20261010-01). No runner, adapter, UI or tenant access is added, so nothing in the product creates these records yet.

- Verify the integrated Exchange capture refuses empty accounts before module lookup, authentication, collection or output creation.

- Integrated read-only Exchange/Purview capture verifies the returned confirmed account as well as tenant/resource before every collection; unpinned, unknown or mismatched accounts refuse rather than yielding empty-success evidence. Manual unpinned exports retain their historical meaning.


- Add a shared registered read-only report core for users/assigned licence details, Intune devices, MFA registration, sign-ins and directory audit, with separate strict immutable evidence and HTML/CSV/JSON/Excel exports. Preserve failed/partial/cancelled reads, exact identities and unknown values; reuse existing Graph authentication and authorised scopes. Reports UI/CLI and live acceptance remain pending; new AuditLog access is proposed only.
- Registered reports keep malformed, zero or duplicate identities and empty required licence text as explicit partial rows instead of discarding the report; read at most 5,000 rows per section (keeping returned rows when a later page fails, is cancelled or times out); query logs with the documented `ge`/`le` operators without `$select`; and refuse report access through a Graph client that cannot be restricted to report routes.

- Add dependency-only scoped collection and selected assessment with separately validated partial evidence. Assessment's partial-check tab checks an area or requirement using the current connection, or explicitly reviews stored evidence. Offline `bdit check --snapshot <file> --area <area>` / `--control <id>` emits the same historical partial wrapper. Original timing and integrity limitations remain explicit; scoped results cannot be imported as ordinary snapshots or used as complete before-evidence.
- Scoped checks match the full assessment for the selected controls: the same release-lineage review, separately imported Exchange/Purview evidence in stored-evidence checks (`bdit check --exchange-snapshot <file>`), and the source's own integrity state. A result too large to store is shown as not saved, with the reason, rather than lost (CLA-20261008-01 to -04).

- Add an engine-only read-only naming audit and explicit new-authoring validator under the TYPE - description convention. Exact IDs and creation/readback proof separate corroborated toolkit objects from unmapped/unconfirmed ownership. Empty, failed and missing reads stay distinct. Historical names are reported, never rewritten; Reports/UI and authoring-pipeline exposure remain separate.

- Ship the existing offline `bdit` CLI through the single portable application executable, with `bdit.cmd`, redirected output and actual exit codes. CLI dispatch precedes desktop workspace/authentication; no SDK, second executable, live connection or write route is added. Fresh Windows package tests exercise synthetic reports and truthful failures.
- Completion refuses unresolved write history using the existing evidence guards; reading a job never reconciles or authorises a write.
- Reject conflicting identities for one requirement instance when recording outcomes or decisions. Previously stored conflicting outcome/decision histories block completion and remain intact for review.
- Recheck the pinned evidence of predecessor cutover stages: a later closed stage cannot conceal a deleted or modified candidate run.
- Locate the historical Engineer Console source in the uploaded reference archive and distinguish its reporting functions from already integrated BuildStandard capabilities.
- Export the full captured configuration inventory as self-contained HTML, separate from assessment/standard/drift reports. All returned properties and assignments are included; failed/partial reads, missing domains/fields and separate Exchange/Purview provenance remain explicit.
- Add offline `bdit inventory --snapshot <file>` with HTML/JSON/CSV/Excel support and the existing integrity reader; no authentication, live reads or client profile is required. Desktop Reports integration remains separate.
- Prepare a numbered engineer/live acceptance run sheet with four practical journeys, explicit future-feature gates and a sanitised response template. No human/live gate is closed by this procedure.
- Start the Scripts & Reports library (INT-072, INT-080 proposed): ten pinned, read-only Exchange Online scripts (mailbox inventory, quota and 100 GB audit, mailbox permissions, a user's mailbox access, calendar permissions, inbox rules, mail forwarding, shared mailboxes, message trace up to 90 days, unified audit search). Forms are typed: blank optional fields are left out, several values can be entered, and rules such as "at least one of" and date limits are checked before anything is produced. `bdit scripts` lists items and `bdit script --id <id> --copy` prints a standalone Copy script that signs in to Exchange Online, refuses another tenant and carries every value as a literal. Nothing is run by the tool yet, and no item has been tested in a tenant.
- Desktop **Scripts & Reports** page (INT-080 slice 1): the library grouped by area with search, a read-only badge, what each item needs and its live status. Each item's form is generated from its manifest (text, dates, tick boxes, single and multi-select pick lists, several-value fields) and checked live by the engine, with each problem beside its field and a command preview of exactly what will be passed. A banner always shows the selected client's name, tenant ID and colour; with no client selected it is neutral and Copy is off. **Copy script…** and **Save as .ps1…** first show a confirmation restating the tenant, the item, "Read only. Makes no changes." and every value; a change after confirming is refused. Save writes a new UTF-8 file with a byte order mark and never replaces one. There is no Run yet. Tenant colours are derived from the tenant ID from a fixed accessible palette until William decides how client colours are set.
- Script library review corrections (AST-20261008-05 to -09): the quota audit lists mailboxes it cannot measure as Unknown with a reason; true/false columns show Unknown rather than False when Exchange returns nothing; a copied script refuses a different signed-in account as well as a different tenant before reading anything; a user's mailbox access matches exact identities and keeps deny and name-only entries as Denied and Unresolved; audit search and Send As mark a result partial when more records exist at the limit. Script timeouts are not enforced by copied scripts; that is left to the future runner.
- Second Exchange Online script pack: eleven more pinned, read-only items for group members and owners, mail flow rules, connectors, accepted domains and DKIM (no DNS query), mobile devices, mailbox auditing, holds and retention, anti-spam/phishing/malware policies, room and equipment booking, Send on Behalf delegates and archive status (no archive size reads). Owners and delegates are Resolved only on an exact address, distinguished name, object ID or GUID, otherwise Unresolved; hold exclusions are not counted as holds; preset policies are not reported as applying to no one; every bounded read warns when it is reached and every value that was not returned is Unknown. None has been tested in a tenant.
- Second pack review corrections (AST-20261008-11 to -14): protection policy settings and lists returned null are Unknown, not NotSet or 0; archive quotas, status and name not returned are Unknown with a reason, Unlimited is kept, and a mailbox with no archive has ArchiveName NotApplicable; a DKIM signing configuration without its domain makes unmatched domains Unknown, not False; distribution group owners are bounded before any lookup and a longer list is marked partial.
- Third Exchange Online script pack: eight more pinned, read-only, Copy-only items for client access protocols and SMTP AUTH per mailbox (with the organisation setting), remote domains, MRM retention policies and tags, OWA and mobile device mailbox policies, sharing policies and organisation relationships, journal rules, quarantine policies (end-user permissions decoded from the documented bits) and mail contacts and mail users. A mailbox SMTP AUTH setting returned empty follows the organisation; one not returned is Unknown. Retention tag links resolve only on an exact, unique tag Name, Identity or DistinguishedName, otherwise Unresolved, and a tag is NotLinked only when every link resolved. Every null or unreturned value is Unknown with `BDIT:UNKNOWN`, and every bounded read is marked `BDIT:PARTIAL`. Live-unverified.

Source changes on top of the published Preview.18 source (`10808de`). Not published; a package built from them needs its own provenance and Windows checks, and publication needs William's approval. IDs refer to `docs/integration/CLAUDE-PREVIEW18-REVIEW.md`.

- Explain the actual reason Microsoft interaction is required at explicit connect; cached access checks no longer claim a popup is already opening.
- Exercise existing known-account silent acquisition and prompt counts through synthetic delegates, with no second cache or mid-operation interactive retry. Ambiguous cached matches explicitly request account choice while retaining the confirmed hint.
- Document authentication paths, context/resource separation, cache/disconnect behaviour and source-versus-live prompt evidence in AUTHENTICATION-FLOW. No new permissions are requested.
- Lead with friendly requirement names, retain exact IDs in copyable details and show the next step for disabled job-recording actions.
- Explain that engineers perform and evidence prerequisite/effectiveness checks; replacement stage records do not activate, assign or retire protection.
- Bring the short start guide into line with the already implemented Jobs/completion screens. Human newcomer, Narrator and physical-scaling acceptance remain outstanding.
- Make the Jobs list fit its returned rows, preserving space for requirements on smaller windows; check two fully visible requirement rows in all three native harness sizes.

- Report evidence integrity. Assessments record whether the Graph snapshot still matches its recorded digest; modified evidence leads the limitations and is bannered in engineer and client reports ("Not for issue"), and the headless runner refuses it (CLA-20261006-01).
- Report the engine's real read route in the capability matrix (settings, equivalence evidence, observed evidence, Exchange/Purview capture, manual only), with the evidence read and licence per control. 20 evidence-assessed controls had been labelled Manual (CLA-20261006-02).
- Verify, rather than regenerate, the committed standards manifest in the portable build; pin 2026.09.30 by publication digest; write the package ZIP with '/' entry names (CLA-20261006-03, -14).
- Keep backups and restores in `transfers/`, apart from shareable reports. Restore checks a trusted archive SHA-256 or an explicit logged acknowledgement, hashing and extracting one read-locked stream; restored folders can be re-verified in the app or with `bdit verify-restore`. Adoption starts from the archive and its trusted digest, copies into an empty workspace, re-verifies every file in place, quarantines evidence that fails into `transfers/` and loads the adopted clients at once. Backup is refused while a tenant write lease is held (CLA-20261006-04, -05, -15).
- Judge stored Exchange/Purview evidence and DNS observations as of the evidence, so reopened history and headless reports reproduce their findings; live work keeps the wall-clock rule (CLA-20261006-06).
- Limit the operator-exclusion explanation to Conditional Access drift (CLA-20261006-08). Add a fixture for release .10 ownership records under .30 (CLA-20261006-07, no behaviour change pending INT-051).
- Separate the Preview.18 publisher into its own workflow gated by the `release` environment; pin workflow actions by commit (CLA-20261006-09).
- Release-first engineer guidance, "candidate recipes (inert)" wording, plan-time CA exclusions in the definition export, timestamped export names with per-file digests, and native renders of Settings, Deviations and Manual checks (CLA-20261006-11, -12, -13, -16).
- Support metadata gains four opt-in, previewed sections: Windows version and display scale, recent Graph errors with Microsoft request IDs, collection read status from the last capture, and timeouts. No section names a tenant, account or object; export writes exactly the preview (CLA-20261006-10).
- Settings walks through handing a workspace to another engineer (create backup, copy its SHA-256 fingerprint, send it separately) and receiving one (choose file, paste or load the fingerprint, adopt or restore). Pasted checksum lines are accepted; a fingerprint file beside the backup is flagged; disabled steps say what they need; results give the next step and an Open folder action.
- Progress titles say what is happening (backup, restore, verify, adopt) instead of "Writing report".
- Quick Connect's confirmation says when the header still describes a session already open, so a "writes possible" badge is not read as the new read-only connection.
- Plan review gives the change detail more room at 1180×640. Overview and the engineer start guide lead with a short first read-only assessment path.
- Release lineage into 2026.09.30 (INT-051 first slice): earlier-release ownership records whose control ID now means a different requirement are explained in the assessment, and records from releases without lineage are flagged for review. Nothing is moved or rebound (CLA-20261006-07).
- Reviewed client scope digest for INT-049/050 records: relabelling a client or editing notes no longer counts as a material change (CLA-20261006-17, INT-056).
- Review fixes for release lineage and the reviewed client scope (INT-059): resolving the same exclusion account again, or reordering account lists, no longer changes the reviewed scope; an unreadable lineage file or manifest is reported as a limitation instead of stopping the assessment; lineage skipped for a catalogue whose bytes differ is explained.
- Upgrade impact report (completes INT-051's first delivery): assess one stored capture under an earlier release and the current one, and see each requirement traced through the release lineage (same, renamed, changed, replaced, retired, added or unknown) with its status under both. On the Assessment page (**Compare with another release**) and as `bdit upgrade-impact`. It describes the standard, not the tenant, and changes no record.
- Job and observation records (INT-049, first implementation, engine only): open a tenant job and record versioned observations against it. Each one is bound to the standard, the reviewed client inputs, the capture and the exact objects observed. A revision supersedes the current record and never overwrites it. A read-only projection shows each requirement's current outcome and flags it for review when anything it relied on changes, its review date passes or its history forks. Old manual checks are shown as legacy attestations. Nothing here touches the tenant or grants authority to deploy.
- Legacy dispositions (INT-050, first slice, engine only): record what was decided about a legacy or missing requirement. The choices are retain external coverage, approved departure, add missing candidate, investigate, manual work or propose replacement. Each decision is bound like an observation and has a named owner and reason. Retaining an external policy names its exact objects and never creates a managed-object mapping or any right to change or delete them. An approved departure must cite an existing, in-date approved deviation for the same control and never creates one. Repeating an unchanged decision writes nothing. The projection flags a decision for review when its objects, deviation, standard, client inputs or review date change. Cutover cases are a later slice.
- Cutover cases (INT-050, second slice, engine only): record a move from old protection to its replacement as a case. Its stages run review, candidate created, pilot reviewed, effectiveness verified, retirement reviewed and closed, one step at a time. Each stage needs its own evidence:
  - the old objects as captured;
  - a saved run that wrote and read back each new object;
  - a separately approved pilot group present in a capture, with every prerequisite met;
  - every functional criterion actually passed;
  - an approved decision to retire or keep coexistence;
  - for closure, a fresh complete capture showing the old protection gone (or kept, for coexistence) and the new one present.

  Approval alone never closes a case. An unsupported scenario stops the case with an owner and escalation path, and a closed case is never reopened. Nothing is activated, assigned or retired by recording a stage. A disposition may now refer to a case for the same requirement in its job.
- Jobs and completion (INT-049/050 surfaces, first slice): a new **Jobs and completion** page opens a job for the selected client. It shows where every requirement stands (verified, approved departure, not applicable or outstanding, with the reasons) and records outcomes and legacy decisions; a second record for the same requirement revises the first. `bdit jobs` and `bdit job` show the same projection read-only. A job is complete only when every applicable requirement has an accepted Pass relying on stored evidence, or an approved departure, which is named as an exception and never as verification. A complete job grants no permission to change the tenant (INT-063).
- Cutover revisions from the Jobs page (INT-050 surfaces, second slice): start a cutover case at review for the selected requirement, then record each later stage. The form starts from the case's current state and offers the next stage. The engine's stage rules still decide what each stage needs, and recording a stage performs, approves or retires nothing (INT-064).
- Outcomes cite stored assessments (INT-049): an outcome recorded on the Jobs page can cite a stored assessment of the capture in view, under the same standard, as its evidence. The citation is pinned by digest, so a changed or deleted assessment sends the outcome back to review. A pass cannot cite an assessment that found the requirement unmet or could not assess it (INT-065).
- Release publishing: one manual publisher for every version (`publish-release.yml`), driven by a reviewed pin per release in `build/releases/`, keeping every Preview.18 provenance check. Dispatches only from `main` or `integration`. Release notes always carry the SHA-256 and the allow-by-policy route for unsigned internal distribution (INT-066).
- CI: GitHub Actions moved off Node 20 to checkout v5, setup-dotnet v5, cache v5, upload-artifact v6 and download-artifact v7, each pinned to a commit SHA, in the build and publisher workflows.
- Third Exchange Online pack review corrections (Astra, AST-20261009-01 to -04): retention tag links match raw names byte for byte and track linked tags individually; a list holding an unreadable entry is Unknown, never NoTags, NotSet or a shorter count, in the retention, sharing, mailbox, protection and quarantine policy items; a sharing entry with an empty domain or action part has Access Unknown. The quarantine item's limitations now note Microsoft's conflicting preset values (27/23 against 43/39) and that decoded bits are configured permissions, not effective actions.

# 1.1.0-preview.18 — 6 October 2026 (published prerelease, source 10808de)

- Add integrated catalogue-only standard definition exports: printable defaults/settings HTML, exact reusable JSON/manifest, document-ready Markdown, manual references and generated capability/access matrix. Preserve every published release and distinguish fixed values, reviewable defaults and unresolved client identities.
- Share existing assessment inputs between desktop and headless reporting; accept explicit separate Exchange/Purview evidence without changing write authority.
- Add previewed local support metadata and sensitive byte-preserving evidence backup, with bounded checksum verification and restore to a new separate folder. Preserve historical digests, unknown fields and unresolved-write blockers; exclude authentication caches.
- Derive packaged dependency/runtime inventory and supplied licence notices from resolved publish metadata; add current operator, continuity, incident and release guides.
- User-approved durable completion/backfill/version-aware contracts remain in separate decision PR #20 before dependent implementation. Preview status, live acceptance and publication gates remain explicit.

# 1.1.0-preview.17

- Retain Quick Connect authentication for one confirmed read-only connection, bound to tenant/operator/standard and five-minute expiry. Recheck identity without another interactive request; explain when a different dedicated app needs authentication.
- Carry the connected tenant into Quick setup, check configured/existing applications by exact ID, and show configuration, grants and assignment needs. Preserve Graph assessment/capture while using the separate privileged setup session.
- Replace policy GUID retyping with explicit approval of the verified tenant and exact change list. Preserve immutable plan validation, complete live evidence, durable intent and inactive/unassigned candidates.
- Make connection details easier to find/copy. Explain prerequisite ownership, disabled plan selection and readiness remedies with matching navigation labels.
- Integrate the embedded read-only Exchange/Purview capture in an owned Windows PowerShell process with module checks, WAM defaults, tenant verification, progress and cancellation. Discover accepted domains for domain-specific assessment; no manual domain entry for organisation-wide capture.
- Keep Exchange evidence independent of Graph captures/plans/acknowledgements. Support separate observation export, offline import, selected-domain DNS refresh and bounded strict evidence validation. No Exchange/Purview writes, module installation or policy bypass.
- Windows, portable-package and live-service acceptance are tracked in the completion register; passing synthetic tests is not proof of Microsoft service access.

# 1.1.0-preview.16 — connection and usability update

- Add client-account Quick Connect with organisation/account confirmation, optional browser fallback and a separate explicit-customer partner/GDAP sign-in route. Discovery creates no saved connection and cannot enable deployment.
- Reuse an unchanged verified connection and try matching cached credentials on explicit reconnect before requesting interaction. Page navigation does not sign in. Microsoft can still require MFA or consent for a different application or mode.
- Make application creation approval prominent, bring it into view after preview and remove repeated setup tenant-ID typing at the user’s request. Approval remains bound to the verified tenant/operator and fresh reviewed plan; policy deployment still requires its separate typed confirmation.
- Identify both applications and their client IDs, explain consent versus assignment versus effective access, and label administrator consent as a browser step distinct from WAM. Preserve known same-tenant setup IDs and route missing deployment configuration to the correct client’s setup.
- Label configuration capture as read-only and replace blank state cells with Not reported or Unknown. Retain individual detail-read errors for incomplete captures.
- Read authentication-method and passkey collections from their documented parent responses. Read Intune scheduled actions and configurations explicitly. Missing, malformed or partial data stays unknown, with no automatic write changes or alterations to published standards.
- Validate the Accessibility runtime dependency before use, fail an incomplete publish and extend extracted-package checks to text-box context-menu copying. A corrupt local extraction requires a fresh complete ZIP.
- Scope and acceptance: [QoL goal](https://github.com/Willzy12h/M365-Buildstandards/blob/astra/release-2026-09-12/docs/integration/QOL-GOAL-2026.10.01.md). Current validation is recorded in the completion register and PR checks; no live sign-in, consent or tenant write was performed by the agent.

# 1.1.0-preview.15 — 30 September 2026

- Move the application, tests and Windows release pipeline to .NET 10 LTS, pinned SDK 10.0.401 and ProtectedData 10.0.12. The portable release remains self-contained.
- Honour operator cancellation before read dispatch and after buffered responses. An accepted LAPS write retains unknown verification when stopped; no automatic write replay.
- Publish immutable standard 2026.09.30 (93 controls, 61 unchanged candidate recipes); retire classic Autopilot/ESP and Defender onboarding, preserve SmartScreen/ESET and all historical release bytes.
- Document native/manual settings, Outlook managed-device account setup and migrated passkey-profile procedures.
- Add control purpose/input/manual guidance, persistent next-step hints, and reusable before/requested/after result details on deployment and saved evidence pages.
- Add Windows CI checks for fresh ZIP extraction, exact staged bytes, blank settings, empty evidence and actual packaged offline startup/shutdown; normalise the extraction root consistently for Windows PowerShell path aliases.
- Retain WPF after a working standalone HTML comparison; 26 browser checks and representative catalogue print inspection pass. Keep the prototype out of the runtime.
- Add 10,000-object pagination and 10,000-finding filtering fixtures, and clear visible run details when switching clients.
- Continue PR #19 with a detailed release goal, one completion register and a bounded live-acceptance proposal. Exact-source CI and remaining acceptance gates are recorded separately.

# 1.1.0-preview.14

- New standard 2026.09.12; published releases are unchanged. PRE-008 expands to individually selected office locations using stable client keys, with public-only CIDR validation at profile, planner and transport boundaries. The old single PRE-008 ownership record is never adopted for a new instance.
- Adds administrator phishing-resistant MFA, reviewed identity settings, empty Autopilot device-preparation group and separate owner approval. Modern passkey profiles require manual configuration; the legacy reviewed method action refuses to overwrite them.
- Windows Hello explicitly requires a lowercase letter and allows digits, minimum eight characters; BitLocker rotates recovery passwords; compliance grace is 120 hours with no notification actions. ESET replaces the removed antivirus/compliance controls; firewall management is removed. Legacy Autopilot/ESP and Defender EDR remain visible manual references pending retirement decisions.
- Adds seven native Windows CSP candidates and sixteen unassigned mobile store-app candidates. Native settings without confirmed definition IDs or complete supported mechanisms have explicit manual guidance. Windows settings backup is the documented July 2026 successor to Entra-managed ESR.
- **Both applications need administrator consent again** after their configured permissions are updated. New delegated scopes: `Application.Read.All` to resolve the Microsoft provisioning service principal; deployment-only `Policy.ReadWrite.Authorization` for user consent and `Policy.ReadWrite.ConsentRequest` for the reviewer-bound workflow. See [application setup and permissions](docs/APPLICATION-SETUP.md).
- Fixes HTML collection-status reporting, numeric release ordering and keyboard access to nested automation tabs found in the fresh phase 0 review. All validation uses synthetic fixtures; no tenant or device behaviour is claimed as tested.
- **96 controls and 61 candidate recipes**, grouped by Entra, Intune, Exchange and Purview in Assessment and Plan. Selection stays within the displayed area; office instances are recorded separately. Retired from .12 only: SEC-WIN-001, CMP-WIN-002 and SEC-WIN-003.
- Exchange/Purview observations use an engineer-run delegated read-only PowerShell capture and a strict, tenant-bound import. DKIM shows the actual records; DNS checks are explicit and replaceable with fakes. Missing or malformed observations stay unknown. Imported captures cannot authorise deployment.
- Exchange write execution remains a documented manual fallback: Microsoft module retries do not satisfy the single-attempt uncertain-write rule. A selected, typed-confirmed export produces an inert commented proposal with before observations and after checks. DKIM enablement requires both CNAMEs; the SPF bypass remains a disabled audit candidate until its trusted SPF/From boundary is established. DMARC and audit retention are report-only; DLP and retention-policy writes are excluded.
- **The Build Standard** and **Manual implementation and verification guide** export from the Build Standard page as HTML and Markdown. Every control includes exact settings, scope, licence, required inputs, sources and complete manual sections. These catalogue-only exports contain no client profile or tenant evidence. CI also supplies the four generated files as `engineer-standard-documents`.
- Final audit correction: malformed new identity-policy states, passkey keys, registration targets and consent reviewers report unknown instead of implying a settings difference or stopping assessment. Regression tests were observed failing before the correction and passing afterwards.
- The portable package includes the engineer-document and Exchange/Purview instructions. Source, synthetic tests, rendered WPF workflows and package checks remain separate from unperformed Microsoft service/device acceptance. Generated HTML browser appearance remains unverified because the local-file preview was blocked by browser policy.

# 1.1.0-preview.13

- Standard 2026.09.11 brings the toolkit to 50 controls and 42 creation recipes. The directory prerequisites are created through the normal Plan and Deploy path, so the exclusion groups and the office named location are no longer a manual step before everything else. Groups are created empty and the named location untrusted; populating and trusting them remain separate decisions.
- **The prerequisites were renumbered.** The exclusion groups are now PRE-009 and PRE-010; MAM Only Users is PRE-004, Pilot Devices PRE-005 and the office named location PRE-008. Two of the old numbers now name a different control: a report written against 2026.09.9 or .10 says PRE-004 for pilot devices and PRE-005 for the office location, and both resolve to something else here. Read the control name, not only the ID, when reconciling an older report. [Automation coverage](docs/AUTOMATION-COVERAGE.md) carries the full mapping.
- Conditional Access activation now requires a verified baseline. Extra targeting, application exclusions, device filters, session controls or unknown fields on a policy block activation rather than being carried along, and the approved payload, the verified observation and the policy's current state must all agree. Emergency containment still disables a policy without requiring unchanged targeting.
- Generic Autopilot group assignment and removal are rejected before any request is sent. They are not a containment path. Enrolment assignment and removal use the resource-specific envelope Microsoft documents.
- Application deployment is no longer inferred from an assignment record. The eight Windows application controls report for review rather than compliant, because each names an exclusion group whose effective membership a name or object ID cannot establish. Confirm application deployment in Intune.
- Recovery re-verification understands beta collections, so an accepted Defender EDR restoration can be confirmed with reads alone. The v1.0 payload guard is unchanged.
- The Windows Hello policy configures the whole feature rather than only enabling it: an eight-character minimum PIN requiring a digit, no expiry, PIN recovery permitted, TPM 1.2 excluded, and biometric and FIDO2 sign-in allowed.
- Reviewable client inputs fall back to a dated default with a warning on the plan row instead of blocking the control. Identity inputs — account, group and location IDs — carry no default and still block, because a wrong identity is worse than a missing one.
- The package now ships eleven operator documents rather than the whole documentation folder, and carries its own README written for the engineer running it rather than for a contributor. The build fails if a packaged document is missing or if any link in one does not resolve inside the package.
- Every control an engineer can operate announces itself to a screen reader, and the offline interface harness fails the build if one does not.
- Tables no longer squeeze columns out of sight. At the minimum window size a table used to shrink every column towards 20px rather than scroll, so the Plan page's Select and Explanation columns - among others - were drawn too narrow to read while looking complete. A table that does not fit now scrolls, and each column keeps the width it was designed for.
- Input fields are visible. Every text box and drop-down was edged in the pale divider colour, about 1.3:1 against its background, well below the 3:1 accessibility minimum for a control's edge; they now use a darker edge.
- The window fits a laptop at 150% scaling. It could not be made shorter than 760 and opened at 940, on a 1080p screen that at 150% offers about 670 above the taskbar. It now opens within the screen and can be made as short as 600, and the six pages whose tables fill the window scroll instead of hiding their guidance when it is short.
- Table headers that wrap in a narrow column grow instead of losing their second line.
- Stopping a recovery no longer shows "Object reference not set to an instance of an object". The page says the recovery stopped before it returned a result and points to the change register, which is the record of whether anything was sent.
- The Plan page's selected-control count follows each tick, Select eligible and Clear selection.
- One captured object with an unexpected value can no longer stop the Configuration page, or appear to make a capture fail.
- Markdown reports escape HTML in tenant-supplied names, so a wiki or ticketing system shows an object named like a tag rather than rendering it.
- The Assessment result filter and the deviation kind use the same wording as the tables rather than internal names, for readers and screen readers alike. Compliant-with-deviation findings, rejected writes and the Deploy page's prerequisite steps are now coloured like the rest.
- The first-build script now points at the package folder it actually creates.
- The build fails on any compiler warning. The offline interface checks now also cover text and input contrast, clipping, keyboard reach on every page, every local command, the final tenant confirmation and a third window size; [the review record](https://github.com/Willzy12h/M365-Buildstandards/blob/integration/docs/integration/FULL-REVIEW-2026-09-24.md) has the detail and seven findings reported for decision; PR #18 settled them.
- **Application setup is one guided sequence.** Enter the tenant ID and sign in as an administrator; the page previews both applications and every permission. One approval tick and the tenant ID typed in full, then **Create apps and grant permissions** creates or repairs both applications, opens Microsoft's approval page for each in turn, reads the actual grants back and offers **Connect read-only now**. Microsoft's consent covers one application per page, so there are still two approvals, but the second opens without another click. Any stage that does not finish stops the sequence and says why: a partial or uncertain creation stops before consent is requested, and a declined or timed-out approval leaves the applications in place with the individual approval buttons to finish. Nothing is retried after an uncertain write, names still never establish ownership, and the separate tick before sign-in is gone: the toolkit writes nothing at sign-in, and the only thing accepted there is Microsoft's own prompt for the four temporary setup permissions, which asks for itself. The engineer running setup is assigned to both applications by default, so they can connect straight away; untick it for an account that will not use the tool.
- Object IDs read as names wherever the capture or the client profile knows them: policies, applications, named locations, directory roles (including the role template IDs Conditional Access uses), users as "Display name (sign-in name)" and the client's exclusion accounts. The ID is always shown beside the name; an unknown ID still reads "Unresolved". Display only: no decision reads a name.
- Tenant confirmations are one rule everywhere: the full tenant ID, ignoring surrounding spaces and letter case. LAPS, package execution and reviewed changes were case-sensitive where deploy, recovery and application setup were not.
- Another Build Standard release cannot be chosen while connected, as an imported or local candidate already could not, so the next connection asks for the routes and permissions that release needs.
- Package execution reads the tenant's app mappings once and refuses cleanly if the mapping or its collection is missing, rather than failing on a null.
- Policy automation sits directly after Deploy in the navigation. Duplicate exclusion accounts are detected whatever the case of the object ID.

# 1.1.0-preview.12

- Standard 2026.09.10 configures Windows Hello for Business end to end instead of only enabling it. The dedicated policy now sets a minimum eight-character PIN requiring at least one digit, permits but does not require letters and special characters, never expires the PIN, allows PIN recovery, excludes TPM 1.2, and permits biometric and FIDO2 security key sign-in. The tenant enrolment setting still stays Not configured.
- Composition rules follow the CSP convention where 1 requires a character class and 2 forbids it, so lowercase, uppercase and special characters are left unset, which permits them without demanding them. A test pins that, because setting 2 by mistake would forbid the class outright.
- Deliberately not configured: phone sign-in, which is a retired feature; enhanced anti-spoofing and Enhanced Sign-in Security, which depend on specific hardware and silently disable biometrics on devices that lack it; and cloud Kerberos trust, because this standard targets cloud-only tenants. A hybrid client needs that one added deliberately, or users sign in with Hello and are then prompted for a password by the file server.
- Maximum PIN length and PIN history are omitted: a maximum only constrains a user who wants a longer PIN, and history only acts when PINs expire.

# 1.1.0-preview.11

- The offline interface harness now captures the Build Standard and Policy automation pages as images, seeded with a supplied value and a rejected one so both states of the generated input form are visible. Every page was already materialised and binding-checked; these two were the pages a reviewer most needs to see and were the only ones not pictured.
- Added [testing this build](docs/TESTING-THIS-BUILD.md): where to download the ready-to-run package, how to unblock it, what to review without connecting a tenant, the order to follow when connecting one, and the known limits of this preview.

# 1.1.0-preview.10

- Added the client-facing build standard document, generated from the loaded standard rather than written by hand. It gives each control in plain English: what it protects, what good looks like, what happens without it, how it is applied, who it applies to, and what the client still has to supply or has had assumed on their behalf. It ends with a table of everything still waiting on the client. Exported as HTML or Markdown from the Build Standard page, prepared for a named client.
- The document deliberately excludes payloads, Graph paths and object identifiers. Those belong in the engineer reports, and a test holds the client document to it.
- Generating rather than writing is the point: a document maintained separately from the recipes drifts from them within a release or two, and then it describes a tenant nobody has.

# 1.1.0-preview.9

- Client inputs are entered as a generated form, one labelled field per input, instead of one hand-written JSON object. A list is typed the way people write lists, separated by commas or new lines, and a wrong value is explained beside the field it was entered in rather than when a plan later refuses to build. The saved JSON remains visible, read-only, behind an expander.
- A reviewable input the client has not supplied now falls back to a dated default and creates the candidate with a warning, instead of blocking the control. The candidate is inert either way, and the plan row says which value was assumed and that it must be confirmed before assigning. Identity inputs - emergency accounts, the office location, targeting groups - declare no default and still block, because a plausible but wrong exclusion is how a tenant locks itself out. A default older than 90 days reports itself as needing a re-check.
- Standard 2026.09.9 targets the built-in All users and All devices populations. Groups are created only where a population has to be named: user and device policy exclusions, unenrolled mobile users, and pilot devices. The managed-users, managed-devices, Office-install and Autopilot groups are gone; the built-in targets replace them.
- Reviewed minimum operating system versions: Windows 10.0.26200.0 (the 25H2 family; 24H2 Home and Pro reach end of updates on 13 October 2026), iOS 26.7 (the security-patched previous major; iOS 27 shipped in September 2026), Android 14 (Google issues security bulletins for 14 and later).
- An optional identifier list may now be empty, so an Enrolment Status Page with no blocking application is expressible.
- 50 controls, 42 recipes. Windows Autopatch is unchanged and remains deployment-mode only: the beta Windows updates namespace exposes no read-only permission.
- CI is the only build and test evidence for this increment; the authoring environment could not run the .NET SDK. No live tenant, consent or write was performed.

# 1.1.0-preview.8

- Standard 2026.09.8 provisions the directory objects every other control depends on through the existing Plan → Deploy path: seven security groups (managed users, managed Windows devices, Office install ready, MAM-only users, pilot devices, Autopilot devices, Conditional Access exclusions) and the office IP named location. This closes the gap that left an engineer building groups by hand before any assignment or Conditional Access recipe could be used.
- Group and named-location creation is constrained by a new guard rather than trusted from the recipe: only empty, assigned-membership security groups (no member, owner, dynamic rule, mail enablement or role-assignable flag), and only untrusted IP named locations carrying client-confirmed IPv4/IPv6 CIDR ranges. Existing objects are still never adopted by name or modified.
- ID-003 (administrator access) is assessed from directory role membership: covered when between two and four accounts hold Global Administrator directly. It cannot see eligible Privileged Identity Management assignments, so that remains an engineer check.
- Deployment mode now requests Group.ReadWrite.All and Conditional Access write for named locations. Both registrations need administrator consent again; assessment mode is unchanged and stays read-only.
- 53 controls, 45 recipes, 23 collections. 49 of 53 controls now report automatically.
- CI is the only build and test evidence for this increment; the authoring environment could not run the .NET SDK. No live tenant, consent or write was performed.

# 1.1.0-preview.7

- Standard 2026.09.7 assesses ID-002 (authentication methods and TAP), ENR-001 (MDM scope) and CMP-001 (tenant compliance default) from captured evidence using equivalence signals, so 40 of 45 controls now report automatically; the reviewed tenant actions that change them are unchanged. Equivalence paths can address array elements by key (`authenticationMethodConfigurations[id=Sms].state`).
- Cancelling a partial capture (operator stop, verification time budget) now stops reading at the first cancellation and records every remaining collection as *Not attempted* instead of a read error, so after-change evidence says what was and was not tried.
- Terminal evidence saves in the LAPS, reviewed-change, package-publishing and recovery services no longer mask the exception that ended the run; a save failure is recorded on the run and retried once, matching the deployment executor.
- Added synthetic tests for the reviewed-change guard (every kind: authentication methods, TAP, MDM scope, tenant compliance, CA activation, assignment, Autopatch, and that plan free-text cannot widen a route or payload), Entra LAPS payload preservation, capture cancellation and the equivalence array selector. These are the first tests covering the preview.6 write surface.
- CI is the only build and test evidence for this increment; the authoring environment could not run the .NET SDK. No live tenant, consent or write was performed.

# 1.1.0-preview.6

- Expand to 37 candidate recipes, with typed tenant inputs, broader Graph JSON imports and saved local standards.
- Add reviewed tenant compliance/authentication/MDM changes, CA activation/report-only, group assignment/removal and Autopatch device enrolment/removal, with before-evidence and read-only re-verification.
- Add encrypted Win32 package publishing for client-supplied RMM/endpoint installers; retain app/version/file IDs and forbid automatic write replay.
- Integrate policy/import/LAPS/package/readiness workflows into WPF. Four identity/service controls retain explicit external/engineer steps.
- Log unexpected deployment completion errors, show a shutdown summary, contain close-handler exceptions, tighten paging item validation and add null guards.
- Compilation only for this increment. Tests and live/GUI acceptance were not performed at the user's request.

# 1.1.0-preview.5

- Add Windows LAPS, Defender Antivirus, firewall and target-tenant Defender EDR candidates in standard 2026.09.5 (18 recipes; 27 manual controls).
- Add a bounded Graph JSON import API with type/URI validation, explicit source-metadata removal and a fresh catalogue digest.
- Add Entra LAPS preview, approved full-PUT enablement, preserved registration settings, durable evidence and read-only re-verification. Unknown writes are never retried.
- Add LAPS prerequisite and Defender EDR entitlement gates. Import and prerequisite UI integration is deferred to the next UI pass. Live tenant/device behaviour remains unverified.

# 1.1.0-preview.4

- Windows sign-in pop-up and browser fallback; exact registered administrator-consent callback.
- Reviewed existing-app repair, custom icon/GitHub branding, engineer assignment and clearer permission counts/handoff.
- Generic product naming; versioned BitLocker and optional long-path candidates, including bounded beta policy recovery.
- Original standards/evidence retained. Live authentication, consent and device effects remain unverified.

# Changelog

## 1.1.0-preview.3 (2026-09-14) — independent review fixes

- Expose the existing full captured-configuration HTML inventory on the desktop, retain exact capture details behind a clearer expander, put script form inputs ahead of lengthy requirements, and align current guides/sign-in wording with actual behaviour.

- Readiness reads now keep failed or malformed results Unknown, preserve exact emergency-account identity and give practical access/service next steps; no configuration is inferred from a failed request.

- Distinguish confirmed pre-request failures from uncertain write outcomes. Auth/guard failures before transport no longer permanently lock a control; original plans remain single-use.
- Add read-only deployment/recovery re-verification with separate integrity-checked evidence and safe local mapping finalisation. Unknown modern writes remain blocked; completed 1.0.0 records require explicit acknowledgement and fresh exact-ID, ownership and settings checks.
- Cancel deployment reads on Stop while preserving the in-flight write; bound policy readback and after-capture to 60 seconds and retain incomplete capture evidence.
- Explain consent redirect fallback to Validate setup, display interrupted runs on Overview and send the current application version during setup sign-in.
- Extend regression tests and offline UI rendering. Real consent redirects, Graph propagation, recovery and connected shutdown still require authorised tenant validation.

## 1.1.0-preview.2 (2026-09-14) — recovery and licence visibility

- Added a durable policy change register, selective deletion of recorded creations, restoration of supported inactive updates and reviewed Conditional Access disablement.
- Recorded exact returned IDs, write payloads and before/after objects. Recovery previews bind tenant, engineer, application, standard, source run, ownership and fresh evidence; writes are single-use and uncertain outcomes block further writes.
- Added tenant overview subscription counts, searchable assigned users and conservative licence checks for directly resolved Conditional Access user scopes. Failed or unsupported checks remain unknown.
- Tightened service-plan checks to active, provisioned subscriptions with enabled capacity. Recovery and deployment share a tenant evidence-store lock.
- Expanded synthetic safety and WPF coverage. Fixed idle window shutdown and IPv6 fallback delays in localhost callback tests. No live tenant acceptance is claimed.

## 1.1.0-preview.1 (2026-09-14) — controlled setup and engineer workflow

- Added a separate delegated app-setup wizard: permission preview, two single-tenant registrations and enterprise apps, explicit creation approval, consent links and configuration/grant/engineer-assignment validation.
- Added one-time connections and tenant-bound account lookup with exclusions, purpose, reason and persistent-effect review. Dedicated emergency-access and creator safeguards remain.
- Enforced complete durable snapshots and all plan bindings inside the executor, licence/reference readiness, single-use plans and truthful write acceptance/readback reporting.
- Fixed forced silent renewal after read 401, missing-versus-null comparison, unexpected terminal results and inaccurate client-report wording.
- Redesigned the Windows shell and pages, added cooperative stop/shutdown and moved exports off the UI thread.
- Review build only. Live app setup, consent, policy acceptance and functional tenant outcomes remain unverified. Scope remains 45 controls and 12 creation recipes.

## 1.1.0 (unreleased) - equivalent configuration and usability

### Assessment
- **Equivalence signals.** Controls may declare, as catalogue data, what an existing object must look like to cover them. A client policy that meets every required condition is reported as a partial match naming the object and the properties that satisfied each condition, instead of *Missing* or *Requires manual review*. Equivalence never reports Compliant on its own, never overrides a stronger settings result, and always surfaces exclusions and disabled states as caveats.
- Signals support grouping, so alternative routes to the same outcome (an MFA built-in control or an authentication strength) both count.
- Signals drafted for CA-001, CA-003, CA-004, CA-005, CA-006, CA-007, CA-008, CA-009, CA-010, MAM-IOS-001 and MAM-AND-001.
- Engineer HTML and the CSV/XLSX exports gained an equivalence table with expected and observed values, plus a caveats sheet.

### Fixes
- The access check no longer probes `/subscribedSkus` with `$top=1`; collections may declare `supportsQuery: false`. Previously a Graph `400 UnsupportedQuery` was reported as if it were a permission failure on `Organization.Read.All`.
- Selecting a saved client reloads its details reliably. Previously the selection could be set internally without loading the form, so re-selecting a client did nothing and saving then created a duplicate client.
- Saving a second client for a tenant that already has one is refused; evidence, mappings and deviations are keyed by tenant ID and would otherwise be split in two.

### Usability
- Connection details, application details, the access check, export paths and finding details are selectable text with Copy buttons.
- Exports report how many objects were written and offer **Open containing folder**.
- The Connect page names the selected client and separates "connect to selected client" from "save and connect".

## 1.0.0 (2026-09-11) - consolidated design

Replaces two earlier prototypes (Tenant Console 0.3.2-rc.1, Node/PowerShell; and the .NET/WPF Tenant Toolkit 1.0.0 draft). Nothing was merged mechanically; the design decisions are recorded in `docs/REVIEW-REPORT.md`.

### Architecture
- Single .NET 8 solution: Core, Graph, Engine, WPF App, Tests. Self-contained win-x64 portable release; no runtime installation.
- Native Microsoft Graph HTTP client replaces the PowerShell worker and the Graph PowerShell module dependency.
- Removed the CLI project and the What-If (beta) service from scope; both are documented as future options.

### Safety
- Two application registrations (assessment and deployment) for token-level read/write separation; incremental consent within one registration was rejected because tokens carry every consented scope.
- Conditional Access candidates are created `disabled` with expected targeting, emergency accounts, optional exclusion group and the verified operator excluded; report-only was rejected as the initial state.
- Write guard enforced in the catalogue validator, the planner and the Graph client; assignments are never written.
- Plans are digest-bound to tenant, profile, standard, snapshot, managed-object mapping, account and application; they expire with their snapshot.
- Pre-write reconciliation, readback verification, after-change snapshots, no automatic retry of writes, honest interrupted-run marking, close-waits-for-write shutdown.

### Assessment
- Collection-based comparison with property-level differences and name resolution; statuses distinguish enforced, not enforced, partial, missing, unknown, manual, licence and not applicable.
- Licence evaluation from subscribed SKUs.
- Deviation register (approved deviation or not applicable) that never hides unknown data.
- Drift analyser classifies toolkit-managed objects (removed, modified externally, metadata only) and explains control status changes.

### Evidence and reports
- Tenant-partitioned evidence with integrity digests; every load checks tenant binding.
- Engineer HTML, Markdown, JSON, CSV and XLSX; client-facing HTML summary; run and drift reports. Formula-safe CSV/XLSX with no third-party dependency.

### Build
- `BUILD-ME-FIRST.cmd` installs a user-local .NET 8 SDK into `.dotnet\` (no administrator rights) and runs the full restore, build, test, manifest, publish and package pipeline; all output is written to `build\last-build.log`.
- Repo-level `nuget.config` pins nuget.org so restore does not depend on machine-level NuGet settings.
- First green build: 0 errors, 89 tests passing, packaged to `dist\BDIT-Tenant-Toolkit-1.0.0-win-x64.zip`.

### Standards
- Catalogue schema v3 with expected production state and safe deployment state per control; 45 controls (12 automated recipes) carried over from Tenant Console 2026.09.2 and enriched with severity, business impact, engineer action, licence and manual instructions.
- SHA-256 manifest integrity check (documented as a digest, not a signature).
