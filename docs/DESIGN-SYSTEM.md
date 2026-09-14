# Engineer interface design system

## Purpose and workflow

A native WPF workspace for deliberate tenant assessment and reviewed deployment. The navy navigation rail follows Connect → Configuration → Assessment → Deviations → Plan → Deploy, with application setup, evidence, manual checks and standards available alongside it. The persistent header identifies the tenant, operator and permission mode. Setup uses its own privileged session and must identify that session explicitly.

Each page has one clear title and a short purpose statement. Primary actions use blue; secondary actions use outlined white buttons; incidental copy/open actions use quiet text buttons. Export options are grouped so they do not compete with capture or assessment. Planning separates selecting controls from reviewing the exact payload. Deployment presents readiness, tenant confirmation and verification as separate sections.

## Shared tokens

| Token | Value / use |
| --- | --- |
| BgNav / BgNavHeader | #102B45 / #0C2238, workflow rail |
| BgWindow / BgContent / BgPanel | #F3F6FA / white / #F7F9FC |
| Accent / AccentSoft | #1665AD / #EAF3FC, actions and information |
| TextPrimary / TextMuted | #193247 / #596C7E |
| Line / BgSelected | #DAE3ED / #E8F1FB |
| Warn / WarnBg | #865A00 / #FFF5E1, write access and cautions |
| Danger / DangerBg | #AD282F / #FFF0F0, blocked and failed results |
| Good / GoodBg | #176B49 / #EAF5EF, verified/pass results only |

Typography is Segoe UI: page title 27px semibold, section title 16px semibold, body 13px, secondary/table copy 12px and context labels 10px. Object IDs and exact JSON use Consolas. Page inset is 28×24px, card padding 20px, card radius 9px and section gaps 18–24px. Buttons have a minimum 38px height; table rows have a minimum 38px height and 12×8px cell padding.

## Components and interaction

- `Card` (`Panel` retained as an alias): white bordered section; `WarnPanel` for write capability/cautions and `DangerPanel` for failures.
- `PrimaryButton`, `SecondaryButton`, `QuietButton`, `WarningButton`, `DangerButton`: shared rounded template with hover, pressed, disabled and visible keyboard-focus states. Use red actions only for destructive intent.
- `H1`, `H2`, `Lead`, `Eyebrow`, `FieldLabel`, `Muted`, `Mono`: shared text styles; field labels precede inputs.
- `ReadOnlyDetail`: selectable, wrapping text with vertical scrolling. JSON, full IDs and report paths remain copyable.
- Tables keep resizable columns, horizontal scrolling, row/column virtualisation, explicit headers and a visible selected row. Status badges include text; colour is supplementary. Planned actions, collection success and write acceptance do not appear as verified green.
- Tabs use an accent underline and keyboard focus. Forms retain native text/combo/check controls. The shell activity log and optional details expand without replacing the current task.

Application setup and connection forms scroll vertically. Setup displays registration writes, permission consent and engineer assignment separately. Account exclusion selection shows names, sign-in addresses, IDs, purpose and reason, plus a reminder that exclusions persist after policy activation.

## Validation and remaining checks

XAML XML parsing and named-resource resolution passed for the changed views. Calculated foreground/background contrast ratios: body 13.22:1, muted 5.14:1, navigation 10.18:1, primary-button text 6.00:1, warning text 5.58:1, failure text 6.08:1. These are token calculations, not an accessibility certification.

The main task records Windows compilation and rendered review separately. Complete keyboard-only, screen-reader, high-contrast and 125–200% display-scaling checks are still required. Empty, long-name, disconnected, partial-read and write-accepted/readback-failed states must be included in GUI acceptance. No screenshot or live Microsoft Graph validation is implied by this document.

