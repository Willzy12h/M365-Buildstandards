# Current guidance and captured-inventory access

Astra usability slice, 8 October 2026. Branched from freshly fetched integration `14e62275ca212b6f34ecd3b17d676f2445e39845` (#47 review record merged after the b755dbb library baseline) after pre-flight across all three repositories. Claude's library #46 is merged; #47 owns Claude's review register, #53 owns engine readiness, #54 owns Astra status reconciliation and #55 is decision-only experimental gating. This slice changes current app guidance/export presentation, not their implementations or contracts.

## Scope

- Expose the existing full captured-configuration HTML exporter in the desktop, alongside JSON/CSV/Excel; retain raw capture/provenance and incomplete-read warnings.
- Make the configuration summary compact, with exact provenance still available in details and copy/export. Investigate native 1180×640 first-screen table visibility and preserve all data.
- Move script form inputs ahead of lengthy requirements/limitations where practical, preserving typed binding, review, copy-only execution and live-unverified status. Do not rebuild the library or add Run.
- Bring TESTING-THIS-BUILD and OPERATOR-START into line with actual buttons, portable CLI and current source/publication identities; remove obsolete blanket re-consent and unblock instructions. Respect security-owner approval without changing application control or execution policy.
- Correct deployment wording that promises a fresh sign-in when the existing verified-context acquisition may be silent.

## Evidence and gates

Astra inspected actual native synthetic configuration, scripts and Jobs renders from integration run 37832476624 at b755dbb. R10 newcomer/Narrator/physical scaling and second-engineer acceptance remain open. New source changes need meaningful adapter/native checks and independent Claude review. No tenant operation, permission change, module installation, version or publication occurs.

## Native failing-first checkpoint

At `c5f19c4`, Windows push 37835171440 and PR 37835179227 failed the new viewport assertions on the unchanged layout: zero visible configuration rows at 1180×640 and the first script input below the viewport at 1180×760/640. The same run also exposed a fixture limitation: it contained only one captured object, so the two-row assertion failed at larger sizes too. Populate two distinct synthetic captured objects and retain the minimum-two assertion; this corrects the fixture rather than lowering the requirement. The successful 1,270 engine/156 app checks in that deliberately failing run are not candidate acceptance.

The corrected, two-object fixture at `27adbf37f225f68e6d8b088fada6584772468c37` still fails the unchanged layout: configuration has one full row at 1180×760 and none at 1180×640; the first script input is below the viewport at both sizes. Actual Windows run [37836127782](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/37836127782) executed 1,270 engine and 156 app tests successfully, then failed these four native checks. The 1480×940 checks passed.

The implementation compacts the capture summary while keeping the exact identity/provenance selectable in details, exposes the existing HTML inventory exporter and places lengthy script requirements in an expander. The copy approval still presents all manifest requirements. Native viewport assertions remain unchanged. A desktop command regression exports an offline, incomplete capture and verifies actual properties, HTML escaping, read-error visibility and unchanged evidence/context. Linux strict solution and harness cross-builds passed with zero warnings/errors; Windows execution of the new app regression and updated native layouts is pending at this commit.

Windows run 37837461079 at `bb56c1f` executed 1,270 engine and 157 app tests successfully, including the new desktop HTML command regression. Both script-form viewport checks and configuration at the larger sizes now passed; configuration at 1180×640 still failed (zero full rows). This intermediate failure prompted a further local page-spacing/lead-text correction, retaining the complete read-only meaning, standard fonts, row sizes, exact details and unchanged two-row assertion. No intermediate failed run establishes candidate acceptance. Its artifact download was denied by the current cloud network, so this record relies on actual CI annotations rather than claiming a visual inspection of that revision.

## Claude review response — 9 October 2026

CLA-20261008-32 correctly identified that the original copy approval did not include manifest requirements, despite the claim above. This revision adds the selected purpose, module/role requirements, supported runtime, prerequisites, live status and limitations to the actual Copy/Save confirmation, in a scrollable region above the exact input values. The engine-bound confirmation fingerprint already includes the pinned manifest hash. The page expander remains optional; approval now presents the requirements independently of it. App regression checks the review payload; native checks inspect the actual dialog text and render three dialog sizes, retaining its keyboard/tick-box gate.

CLA-33: the compact first-screen stored-capture summary now explicitly says not eligible for deployment. The desktop export regression checks that warning. The longer details retain exact provenance. These are presentation fixes, not changes to evidence eligibility or execution. Windows/native checks must run on the new exact head; newcomer, Narrator and physical-scaling gates remain open.
