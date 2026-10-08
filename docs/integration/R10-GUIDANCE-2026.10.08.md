# Current guidance and captured-inventory access

Astra usability slice, 8 October 2026. Branched from integration `b755dbb3b8e1b958c12825ec296fee4e319e9ae4` after pre-flight across all three repositories. Claude's library #46 is merged; #47 owns Claude's review register, #53 owns engine readiness, #54 owns Astra status reconciliation and #55 is decision-only experimental gating. This slice changes current app guidance/export presentation, not their implementations or contracts.

## Scope

- Expose the existing full captured-configuration HTML exporter in the desktop, alongside JSON/CSV/Excel; retain raw capture/provenance and incomplete-read warnings.
- Make the configuration summary compact, with exact provenance still available in details and copy/export. Investigate native 1180×640 first-screen table visibility and preserve all data.
- Move script form inputs ahead of lengthy requirements/limitations where practical, preserving typed binding, review, copy-only execution and live-unverified status. Do not rebuild the library or add Run.
- Bring TESTING-THIS-BUILD and OPERATOR-START into line with actual buttons, portable CLI and current source/publication identities; remove obsolete blanket re-consent and unblock instructions. Respect security-owner approval without changing application control or execution policy.
- Correct deployment wording that promises a fresh sign-in when the existing verified-context acquisition may be silent.

## Evidence and gates

Astra inspected actual native synthetic configuration, scripts and Jobs renders from integration run 37832476624 at b755dbb. R10 newcomer/Narrator/physical scaling and second-engineer acceptance remain open. New source changes need meaningful adapter/native checks and independent Claude review. No tenant operation, permission change, module installation, version or publication occurs.
