# Full review, 24 September 2026

A code and interface review of `integration` at `b4274c8`, carried out by Claude in PR #17. It asked two questions: is the code free of defects a compiler cannot see, and does the interface actually work for an engineer — readable, reachable without a mouse, and usable at the window sizes engineers really have. No tenant was contacted; every check below is offline and synthetic.

## How it was checked

**Build strictness.** CI now fails on any compiler warning (`-warnaserror`), and the interface harness, which sits outside the solution, is built under the same rule. The solution was already at zero warnings; this keeps it there. `.editorconfig` claimed CRLF while every tracked file is LF, so it now says LF, and `.gitattributes` pins the six `.cmd` files to CRLF, which `cmd.exe` expects.

**Interface harness.** Before this review the harness proved that every page draws with working bindings, that operable controls have accessible names and that table columns keep their designed widths. It now also checks, on every page at three window sizes:

| Check | What fails it |
|---|---|
| Text contrast | Any text below WCAG 2.2 AA (4.5:1, or 3:1 when large) against the colour actually behind it on the rendered page |
| Input boundaries | A text box or drop-down whose edge is below 3:1 against its background |
| Clipping | Text or a control that layout cuts off, so part of it cannot be seen or scrolled to |
| Minimum size | The main window's minimum not fitting a 1920x1080 laptop at 150% scaling (1280x672 above the taskbar) |
| Keyboard | An operable control that Tab never reaches on a shown window, or a button without a focus indicator |
| Commands | A command missing from the harness register, a local command that raises a defect-type exception or reaches the tenant stubs, or a run control enabled while nothing runs |
| Final confirmation | The deploy button enabled by anything but the exact tenant ID, or made the default button |

Each check counts what it inspected and fails if the count is zero, so a check that silently looks at nothing cannot pass. That guard exists because the accessible-name check added in PR #15 did exactly that until PR #16.

The third window size, 1180x640, is new. WPF lays out in device-independent units, so rendering at that logical size is what an engineer sees on a 1080p laptop at 150% — no high-DPI display is needed on the build machine.

**Code review.** Every view model, the workspace, the converters and the XAML were read in full; the engine's reports, name resolution, CSV and spreadsheet writers were read for injection and robustness; the build and launch scripts were read end to end. The protected areas — authentication, consent, Graph contracts, deployment and recovery semantics, evidence formats, tenant binding and Conditional Access targeting — were read but not changed; findings there are reported below for a decision.

## What the checks found, and the fixes

The new checks were pushed before any fix, so each finding below was observed failing first.

- **Input fields were nearly invisible.** Every text box and drop-down was edged in the divider colour, 1.2–1.3:1 against its background — 43 fields across eleven pages, each failing at all three window sizes. They now use `InputLine` (#76889C), at least 3.2:1 on every background a field sits on. No text anywhere failed its contrast check.
- **The window did not fit a common laptop.** Its minimum height was 760 and it opened at 940, on a screen that offers 672. The minimum is now 600 and it opens within the work area.
- **Six pages hid content at small sizes.** Assessment, Configuration, Deviations, Manual checks, Plan and Build Standard give their tables whatever height is left under the heading, with no page scroll. At 1180x640 the Plan and Build Standard guidance was cut off with nothing to scroll to. These pages now fill the window down to `PageLayout.MinimumHeight` (590, just below the 593px they get at 1180x760; 560 left the Plan review guidance clipped at 1180x640) and scroll below it; at 1180x760 and the default size nothing changes, and the harness fails if they scroll there.
- **Table headers lost their second line.** Headers had a fixed 40px height while their text wraps in a narrow column, so "Admin consent" and "Regression" were cut in half. Headers now have a minimum height and grow.
- **Keyboard.** Every operable control on every page, and on every tab of every page, is reached by Tab. Nine tables and lists were not, and all nine were empty in the synthetic data: WPF gives an empty table no tab stop until it has a row to focus, and the check no longer expects one. *Corrected after independent review:* the first version walked only the tab each page opened on, so a button on a second tab could become unreachable without failing it; it now selects every tab, including tabs inside tabs.
- **Commands and the confirmation dialog.** The deploy button stayed disabled for an empty box, a truncated ID, an extended ID, another tenant's ID and the client's name, and was never the default button.

  The command check's first passing run needs a correction. It reported 29 commands completed and 19 refused, and on reading each refusal, 16 were not refusals: every export and the change-register load had failed with a cross-thread collection error. The cause was the harness, not the application — it pressed commands with no synchronisation context, so work after an `await` resumed on the thread pool and updated bound lists from there, which the real application, running commands on its dispatcher, cannot do. Two things let it through: that exception type was not in the harness's defect list, and a message in the error bar was counted as a refusal whatever raised it. The harness now runs commands on a dispatcher context as the application does, counts `NotSupportedException` and `InvalidOperationException` from product code as defects, and accepts a message as a refusal only when the product raised a `ToolkitException` — the one way it explains what must happen first. Refusals are printed on every run so a reviewer can read them.

## What the code review found, and the fixes

- **Stopping a recovery showed a null-reference error.** `Workspace.ExecuteRecoveryAsync` returned `null!` when the engineer pressed Stop, and the Recovery page dereferenced it. It now returns null honestly, and the page says the recovery stopped before returning a result and points to the change register — without claiming whether a write was sent, because it cannot know.
- **The Plan page's selected count did not move.** "N control(s) selected" was re-read only when the workspace changed, so ticking a control, Select eligible and Clear selection left the old number.
- **One unexpected captured value could stop a page.** The Configuration page and the shared `NameResolver` read captured names, states and assignment targets as strings unconditionally. A value of another type threw from the refresh that runs after every workspace change, so a capture could appear to fail. Both now skip such a value.
- **The Manual checks page read its register unguarded.** A damaged or mismatched register surfaced as a failure of whatever the engineer had just done. It is now reported the way the Deviations page reports its register.
- **Markdown reports rendered tenant-supplied HTML.** They escaped table pipes but not angle brackets, so an object named like a tag was rendered by wikis and ticketing systems that allow inline HTML. HTML, CSV (formula guard) and spreadsheet exports were already safe.
- **Statuses read as internal names.** The Assessment result filter listed `SettingsMatchNotEnforced` and friends, and a screen reader announced them; the deviation kind read `ApprovedDeviation`. Both now use the wording the tables use.
- **Status colours had gaps.** "Compliant with deviation", the Deploy page's Ready, Observed and Incomplete steps and a rejected write were uncoloured. The shared brushes are now frozen.
- **The first-build script pointed at a folder that does not exist.** `Setup-And-Build.ps1` printed `dist\BDIT-Tenant-Toolkit-…\Start.cmd` after a successful build; the package has been `M365-BuildStandard-Tool-…` for some time. `BUILD-ME-FIRST.cmd` said the same and also told the user to "say done", a leftover from an assistant conversation.
- **Wording.** The Settings page said sign-in always happens in the browser; by default it is the Windows sign-in window. The command-line help cited release 2026.09.10.
- **Dead code.** Three converters and a duplicate brush that nothing referenced were removed.

Tests were added for each behaviour a test can hold: status colours for every assessment status, plan action and run outcome; the filter and deviation wording; the Plan count; name resolution with unexpected values; Markdown escaping.

## Reported, not changed

These are in protected areas, or are product decisions. Each is stated with enough context to decide.

*Decided after merge.* The maintainer approved fixes for 1, 2, 3, 6 and 7, which PR #18 made as proposed below; 4 stays a hardening inventory by decision; 5 was fixed in this PR.

1. **Fixed in PR #18.** **Package execution can reach a null-forgiving mapping lookup.** *Corrected after independent review:* the method is `ApplicationPackageService.ExecuteAsync`, and an ordinary removal of the mapping after preview is already refused earlier, by the plan-digest check, with a `SafetyViolationException`. What remains is a change between that validation and the reload. Proposed fix: reuse the validated mappings and guard the mapping and collection lookups explicitly.
2. **Fixed in PR #18** (`TenantConfirmation.Matches`). **Tenant confirmations are not all compared the same way.** `AutomationViewModel.ExecuteLaps` compares the typed tenant ID case-sensitively, and so does package execution; *corrected after independent review*, since this record first said LAPS was the only one. It is stricter, not weaker, but an engineer typing the ID in capitals is refused in those two places only. Proposed fix: one exact, case-insensitive comparison everywhere, with mixed-case tests.
3. **Fixed in PR #18.** **A different standard release can be selected while connected.** Loading an imported or local candidate standard requires disconnecting first "so the next connection requests the correct routes and permissions"; choosing another release on the Build Standard page does not, and keeps the live capture. The plan is cleared, so nothing unsafe follows, but the two paths disagree.
4. **Kept as an inventory by decision.** **Reads of captured Graph values assume a string** in 99 lines across the engine and Graph layers (`GetValue<string>()`). Graph's schema makes those values strings, so this is a hardening inventory rather than a set of failures. Proposed fix: validate types at specific input boundaries, keeping explicit unknown outcomes and strict identity checks, rather than coercing everywhere.
5. **Fixed in this PR after independent review.** *Corrected:* lone surrogates did not make Excel refuse the workbook, as this record first said; the writer replaced them, losing the character. U+FFFE and U+FFFF, however, passed through and produced invalid worksheet XML. The writer now drops every character XML cannot hold, keeps valid surrogate pairs and never truncates between the halves of one.
6. **Moved after Deploy in PR #18.** **Policy automation is last in the navigation,** after Settings and diagnostics, because it is added after the other pages. Whether it belongs beside Plan and Deploy is a product decision.
7. **Fixed in PR #18.** **Duplicate exclusion accounts are detected case-sensitively** on the Connect page. Graph returns object IDs in lower case, so this does not arise in practice.

## Independent review

Astra reviewed the head of this PR (`41eae92`) against its base, reproduced the build and tests, and mutated each new check to prove it fails. Contrast, clipping, minimum size, page scrolling, command registration and both confirmation-dialog mutations failed as they should. It found, and this PR then fixed:

- **P1, pre-existing: History kept one client's comparison under another.** Switching client cleared the snapshot selections but kept the drift result and its enabled exports, so client A's comparison could be viewed and exported under client B. The page now clears its comparison and selected run whenever the client changes.
- **The keyboard check walked only the tab each page opened on.** It now walks every tab, including nested ones.
- **Thirteen commands registered to be pressed were silently skipped** because nothing had enabled them, so only 35 distinct commands actually ran; the count of 45 included repeated navigation. Each now gets the synthetic state it needs (a selection, stored captures and a run, a local candidate, a disconnected session for the two commands that require one), an unenabled Press command fails the run, and the run fails unless every one was exercised. Exercising them found one more defect: importing with no file chosen showed a raw path error; it now asks for a file.
- **The 600px page minimum was above the 593px these pages get at 1180x760**, so it added scrolling and reflow at a size that had worked. It is now 590, and the harness fails if a filling page scrolls at 1180x760 or larger. This record had claimed 1180x760 was unchanged without measuring it.
- **Markdown code spans were HTML-escaped,** so a setting path such as `A&B<value>` appeared as entity codes. Code spans now escape only what can break out of them.
- **The spreadsheet writer passed U+FFFE and U+FFFF,** producing invalid worksheet XML (finding 5 above).
- The command register said disconnecting was covered by the idle-close check; closing an already-disconnected window does not exercise it, and the register now says it is not exercised offline.

Astra also noted that the sandbox restore produced NuGet vulnerability-audit warnings (NU1900) only because the audit feed was unreachable, resolved on a successful restore without suppressing auditing, and that physical high-DPI rendering, Narrator and opening workbooks in Excel remain unverified by any machine.

## What only a person can check

The harness renders and measures; it cannot tell how the application feels. [Testing this build](../TESTING-THIS-BUILD.md) now carries a fifteen-minute checklist for a Windows machine: 150% scaling, keyboard only, Narrator, readability in daylight, and what the final confirmation must do. No tenant is needed for any of it except the last item.

Live-tenant validation remains the next step, and nothing in this review substitutes for it.
