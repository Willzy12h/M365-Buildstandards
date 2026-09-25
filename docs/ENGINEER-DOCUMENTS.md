# Export the engineer documents

Open **Build Standard**, load **2026.09.12**, and expand **Export engineer standards and manual guide**. Choose HTML for a standalone browser/print document or Markdown for editing and review. The output path appears below the buttons; files are saved in the portable toolkit's `reports` folder. No connection or client selection is required.

- **The Build Standard** gives every control's purpose, exact settings and values, intended scope, licence/edition, dependencies, required client inputs and safe delivery state.
- **Manual implementation and verification guide** adds, for every control, prerequisites before automation, portal steps, PowerShell references and the checks/pass criteria after automation. A manual-only procedure states why a supported write reference is unavailable.

Both documents are generated from the loaded catalogue and grouped as Entra, Intune, Exchange and Purview. The release and catalogue digest identify their source. They use no client profile, tenant identifiers, connection data or observed evidence. The existing client-facing build standard document is a separate export. Historical releases can export The Build Standard; the manual guide is unavailable when any control lacks its required sections.

Read the guide's delegated preflight and evidence requirements before using an individual reference. The PowerShell text is guidance for an engineer, not an execution service: it does not establish consent, preserve evidence automatically, suppress module retries or prove a change was applied. Resolve inputs locally, retain complete before evidence, record intent, review one request, confirm the tenant, and reconcile an uncertain response without retrying it. Keep new Conditional Access disabled, Intune objects unassigned, groups empty and locations untrusted.

Settings read-back, production activation, assignments, application installation, licence effects and device behaviour are separate checks. Microsoft behaviour remains unverified until those checks are performed. Exchange/Purview exports use the [documented manual proposal workflow](EXCHANGE-PURVIEW.md); DMARC and audit retention remain report-only.

The catalogue fields are the source of truth. Do not maintain a second handwritten copy of either generated document. The tests require all 96 controls to include all four manual sections and check output isolation, exact settings, area order and safe rendering.
