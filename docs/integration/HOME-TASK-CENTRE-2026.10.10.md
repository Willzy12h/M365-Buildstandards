# Engineer Home task centre — PR #84

William preferred concept B's task-centre layout with concept A's working context. This slice adds Home while preserving the existing Overview/licences page and all workflow keys. It builds on #81, #80 and #73; those heads remain unchanged for their independent reviews.

Home shows the current client, connection, standard and evidence context, a readable next step, and shortcuts for a new tenant, legacy/backfill, repeat review and second-engineer handoff. Reports and the existing script library remain separate destinations. The shell's persistent tenant/account/access/evidence banner remains visible throughout navigation.

## Behaviour and boundaries

- Every Home action opens an existing page. It does not authenticate, collect, consent, create a plan, write an object, save a decision, acknowledge evidence or complete a job.
- Missing, mismatched, incomplete and historical evidence remain explicit. Scoped checks do not establish overall completion or complete before-evidence. A connection, licence subscription or available shortcut is not readiness or acceptance.
- Existing destination and engine safeguards still decide whether an operation is eligible. Manual work and experimental changes retain their approval boundaries.
- The projection is cheap and read-only: no stored-job inventory scan, network request, cache or authority mutation on refresh.
- Overview/licences is retained. This is a presentation extension, not a replacement application, report engine or script runner. It introduces no companion window or persisted schema.

## Verification

Meaningful synthetic state and navigation cases, strict Windows-targeted compilation, and native Home checks at all three existing harness sizes are required. Native checks must exercise actual buttons, keyboard/accessibility, important status text and retained context rather than merely count a drawn page.

Exact-source results will be recorded on #84. Independent review, parent integration, exact-head Windows checks and merge-commit validation remain separate gates. Native/human/newcomer/Narrator/physical-scaling and live Microsoft acceptance are not inferred from source implementation.

## Initial native validation correction

Exact source `024772e` passed 1,785 Engine/CLI and 283 App tests on Windows, but push `38089182226` and PR `38089185373` failed the native inspected-work counter. The shared actual-button invocation helper incremented the saved-client journey counter for Home calls as well, so nine saved-client presses were combined with 45 Home presses. This is a harness-accounting defect; it does not establish that the complete native run or extracted package passed.

Keep counters in their respective journey callers, preserving actual UI Automation invocation, every guard and both exact non-vacuous thresholds: three sizes/nine saved-client presses and three sizes/45 Home presses. Fresh exact-head native and portable validation remains required. The per-job log download was policy-blocked, but the authorised aggregate run-log endpoint worked; no proxy or TLS controls were bypassed.
