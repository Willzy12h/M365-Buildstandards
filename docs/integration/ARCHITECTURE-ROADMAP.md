# Architecture roadmap

Written to be executed by a language model, not read by a human sponsor. It states what to change, where, in what order, and how to know each step is finished. Prose is kept to what an executor needs to make correct decisions when the instruction does not cover the case in front of it.

Baseline for every measurement here: `integration` at `e188d9e`, preview.13, standard 2026.09.11, 542 tests passing. Engine 6,353 lines, tests 6,916, app 4,976, core 2,908, graph 2,061, headless runner 257. Standards 27,747 lines across nine release files. Re-measure before quoting any of these; they moved once already between PR #9 and PR #12.

## Status

Updated 21 September 2026. **No claim is open on any repository and nothing here is blocked.** Pull request #9
(`astra/review-fixes`, safety findings F1–F5) and pull request #12 (Claude, post-merge review fixes) are both merged;
the earlier block on W0, W2 and W9 is lifted. Before starting, re-run the pre-flight check in
`AGENT-COORDINATION.md` rather than trusting this paragraph — a claim can open after it was written.

| Workstream | State | Note |
| --- | --- | --- |
| W3 headless runner | **Done** | `src/BDIT.TenantToolkit.Cli`, offline. Conformance tests in `HeadlessRunnerTests` and `TenantBindingTests`. Corrected in PR #12; read the W3 section before building on it. |
| W0 embedded NUL | **Mostly done, small remainder** | The raw NUL is gone: `EquivalenceEvaluator.cs` is ASCII text, greppable and diffable. The key is now the escape `"\u0000" + s.Key` at line 57, not a named constant, and there is still no test that an ungrouped signal keyed `x` and a signal declaring group `x` stay separate. Finish those two, or close W0 explicitly. |
| W2 parameter-usage helper | **Open, unblocked** | Still six substring scans across five call sites. Current line numbers are in the W2 section. |
| W9 structure and optimisation | **Open, unblocked** | Continuous. 9b is partly done in Graph; 9a, 9c, 9d, 9e remain. See the W9 section for what moved. |
| W1, W4, W5, W6, W7, W8 | Not started | W1 now targets 2026.09.11, not .10. W4's prerequisite (W3) is met. |

## How to use this document

Take one workstream at a time, in the order given under Sequencing. Do not start a workstream whose dependency is unmerged. Each workstream states its acceptance criteria as things a test or a command can decide; if you cannot make the criterion machine-checkable, say so in the pull request rather than declaring it met.

If a step conflicts with an invariant below, stop and report. The invariants outrank this plan.

## Invariants that are never traded

These are why the product is defensible. No workstream may weaken one, and a change that appears to require it is a signal the design is wrong, not that the invariant is negotiable.

1. Assessment holds no write permission at the token level.
2. Objects are created inert: Conditional Access disabled, Intune unassigned, groups empty, named locations untrusted.
3. The verified operator is excluded from every Conditional Access candidate, directly, exactly once, validated before execution. Group membership never carries this guarantee because it is read from a capture that can change before the write lands.
4. Existing objects are never adopted by name and never modified unless the toolkit created them and recorded them.
5. Durable intent is recorded before a write; no write is retried after an uncertain response.
6. Unknown is never rendered as absent. A read that failed is reported as unknown.
7. Nothing is activated or assigned without a separate reviewed action.

## Target architecture

The seams that must exist when this plan is complete:

- **Standard** is data, versioned, diffable, and consumable by anything. It is the asset; the application is one consumer.
- **Engine** is a library with no interface dependency and no interactive assumptions. It already satisfies this; do not regress it.
- **Consumers** are the WPF application and a headless runner. A third consumer must require no engine change.
- **Safety** stays compiled. Guards derive route, permission and payload shape from closed enumerations. This is deliberate: it is what makes the write surface provable. Extension through data is for policy content, never for the write surface.

## Sequencing

```
W0 (immediate) ─┐
W2 ─── W1 ──────┼─── W5
W3 ─── W4       │
W6, W7, W8 ─────┘   (independent, any order after W1)
W9 runs continuously alongside
```

W0's remainder is small and can be taken any time, or closed. W3 is done, so W4 is unblocked. W2 before W1 because W1's compiler needs the helper. W5 can proceed in parallel once W1 has settled the data shape. The diagram above still shows the original order; read it as dependencies, not as a queue with W0 at the front.

---

## W0. Remove the embedded NUL byte

**Status, 21 September 2026: mostly done.** The raw NUL was replaced with the escape sequence `"\u0000" + s.Key`
(`EquivalenceEvaluator.cs:57`). `file` now reports the source as ASCII text, `grep` matches it and diffs are readable;
no source file under `src/` contains a NUL byte. What the target below asked for and the fix did not deliver: the key
is still an inline literal rather than a named constant explaining why collision matters, and no test pins the
behaviour. Do those two, or close W0 with a note saying the escape is enough. Do not reintroduce a raw NUL.

**Problem, as originally found.** `src/BDIT.TenantToolkit.Engine/Assessment/EquivalenceEvaluator.cs` contained a raw NUL (0x00) inside a string literal, in the group key for ungrouped required signals. `file` reported the source as `data`; `grep` treated it as binary and suppressed matches; diffs were unreadable.

**Why it matters.** The behaviour is correct and probably intentional, namespacing an ungrouped signal so its key cannot collide with a real group name. But it is invisible in every editor, and an editor or tool that normalises the file changes behaviour silently. A tool that cannot grep the file also cannot review it.

**Target.** Replace the NUL with a named constant carrying a value that cannot collide with a caller-supplied group name, and say in a comment why collision matters.

```csharp
/// <summary>
/// Prefix for the synthetic group given to a required signal that declares none, so that an ungrouped signal is its
/// own group. It starts with a character a declared group name cannot contain, because a collision would silently
/// merge two independent requirements into one alternative.
/// </summary>
private const string UngroupedKeyPrefix = "\u0001ungrouped:";
```

**Acceptance.** `file` reports the source as text. A test asserts that two signals, one ungrouped with key `x` and one declaring group `x`, remain separate requirements. Existing equivalence tests unchanged and passing.

**Risk.** Low. Behaviour-preserving if the prefix remains outside the set of legal group names. Confirm no standard release declares a group beginning with `\u0001`.

---

## W2. One parameter-usage helper

**Problem.** The same scan appears six times across five call sites, verified at `e188d9e`:
`PolicyInputValidator.cs:13`, `PolicyInputDefaults.cs:34` and `:77`, `BuildStandardDocument.cs:47` and `:142`, and
`AutomationViewModel.cs:98`. Each serialises a payload to a string and does `Contains("{{" + key + "}}")`, so the cost
is one full serialisation plus one scan per parameter per control. For fifty controls and twenty-two parameters that is
over a thousand substring scans per plan, each over a freshly allocated string. No `ParameterUsage` helper exists yet.

**Target.** `BDIT.TenantToolkit.Core.Json.ParameterUsage`, with the serialisation done once per payload and the result reusable.

```csharp
public static class ParameterUsage
{
    /// <summary>Parameter keys referenced by this payload. Serialises once; callers filter the result.</summary>
    public static IReadOnlySet<string> Keys(JsonObject payload);

    /// <summary>True when the payload references this parameter.</summary>
    public static bool Uses(JsonObject payload, string key);
}
```

Prefer walking the node tree over substring matching on serialised JSON: a payload whose literal text happens to contain `{{x}}` inside an unrelated string is currently a false positive, and the tree walk removes that class of bug. `CanonicalJson.Resolve` already walks the tree and can share the traversal.

**Acceptance.** All six occurrences across the five call sites use the helper. A test proves a parameter referenced only inside a nested array is found, and that a literal `{{notAParameter}}` in a description field is not reported as a parameter. No behaviour change in existing tests.

---

## W1. Compile releases instead of copying them

**Problem.** Nine release files, 27,747 lines, each a full copy of its predecessor. Release 2026.09.7 changed three controls of forty-five and cost a 3,317-line file. Release 2026.09.9 left only eight of fifty controls byte-identical to its predecessor because a targeting change rewrote one field on nearly every control. Consequences: nobody can see what changed between releases without diffing whole files; a payload correction must be hand-applied to every later release, so old releases rot; every publication adds roughly 3,600 lines.

**Target.** One source of truth per control, plus a per-release manifest of inclusions and overrides. A build step compiles the published flat release file. Everything downstream, the loader, the integrity manifest, the digest, is unchanged, because the compiled artefact is byte-for-byte the format that ships today.

```
standards/
  source/
    controls/CA-001.json          one control, current definition
    controls/CFG-WIN-003.json
    collections.json              shared collection definitions
    parameters.json               shared parameter definitions
  releases/
    2026.09.11.json               manifest: which controls, which overrides, release metadata
  2026.09.11.json                 compiled output, shipped, unchanged in format
```

A release manifest names the controls it includes and any per-release override, so a release that differs from source in one field records one field, not a whole control.

**Steps.**
1. Write the compiler as a build task that emits a release file from source plus manifest.
2. Decompose the current release — **2026.09.11**, not .10 — into `source/` and a manifest that compiles back to it byte-identically. This is the proof the compiler is correct.
3. Add a CI check that recompiles every release and fails if the committed output differs.
4. Leave 2026.09.3 to 2026.09.10 as frozen literal files. They are historical evidence, referenced by digest in stored plans. Do not decompose them, and do not regenerate them.

**Acceptance.** `standards/2026.09.11.json` regenerates byte-identically from source. CI fails on a hand-edit of a compiled file. Adding a setting to one control changes one source file and one manifest line. Historical releases still load and still match their recorded digests.

**Risk.** The integrity manifest covers compiled output, so a compiler bug becomes a digest mismatch rather than a silent wrong policy. Keep the byte-identical round trip as the gate and this workstream cannot ship a wrong payload.

**One debt to clear when the next release is authored, not before.** 2026.09.11 mixes operator casing: ID-002
`signals[4]` is `AtMost` while its `caveats[1]` is `atLeast`, and ID-003 uses `Equals`, `CountAtLeast`, `CountAtMost`
and `CountAtLeast`. The enum converter matches case-insensitively and throws on an unknown name, so this is cosmetic
and .11 must not be edited — its bytes are digested and referenced by stored plans. Author the source controls in
camelCase so the first compiled release is consistent.

---

## W3. Headless runner — DONE

Delivered as `src/BDIT.TenantToolkit.Cli`, assembly name `bdit`. Commands: `releases`, `report --snapshot <file>`, `document --client <name>`. It is offline: it reads evidence the application already captured and never authenticates, so no token, no Graph client and no write path is reachable. `HeadlessRunnerTests` asserts the absence of every write-capable type, that nothing authenticates, and that each command refuses rather than reporting an empty result.

It required no engine change, which is the result that matters: the seam is real, not asserted.

**Corrected after review (21 September 2026).** The first version met the acceptance criterion below only by accident: it loaded the profile and ownership records with its own copy of the rules, passed no deviations at all, and filled in a minimal profile and an empty mapping set when the installation held neither. That is precisely the failure the criterion was written to prevent, because all three inputs change the result — the profile carries the client inputs that resolve a standard's parameters, the mappings decide whether a candidate is toolkit-owned or assessed by equivalence, and a recorded deviation changes a finding's status. It also silently accepted a tenant mismatch in `managed-objects.json` where `EvidenceStore` refuses one. The runner now reads all three through `EvidenceStore` and refuses when this installation holds no client record for the snapshot's tenant. Two conformance tests hold the line: one that the shared loaders are used and no evidence file is read directly, one that no evidence mutator is reachable, since reading through the store put its writers in scope for the first time.

**The lesson for W4.** A second consumer is only proof of a seam if it consumes the same way. When W4 adds scheduled reporting, load stored records through `EvidenceStore` and refuse what is absent; do not reimplement a loader to keep a batch running.

What is deliberately not there: live capture. That needs interactive authentication, which needs either a window handle or the system-browser path, and adding it would give this assembly a token. If a future runner needs to capture, put the capture in a second executable and keep this one incapable of writing.

**Original problem statement, kept for context.** The Engine is a clean library but has exactly one consumer, so the seam is asserted rather than proven. There is also no way to assess a tenant without a person at a desktop.

**Target.** `src/BDIT.TenantToolkit.Cli`, a console application over the existing Engine. Read-only by construction: it takes an assessment-mode session and has no reference to `DeploymentExecutor`, `ReviewedChangeService`, `ApplicationPackageService` or `RecoveryService`.

```
bdit assess --tenant <id> --client <label> --standard <release> --out <dir> --format html,json
bdit report --snapshot <file> --standard <release> --out <dir>
```

**Acceptance.** The project compiles without referencing any write path; a test asserts the assembly does not reference the executor types. Running `assess` against a synthetic fixture produces the same findings as the desktop application for the same input. No new engine code: if the runner needs an engine change, that change is a seam defect worth reporting.

**Why this and not a service.** It is the cheapest thing that proves the Engine is reusable, and it is the prerequisite for W4. A service without this step would bake the same single-consumer assumptions into a larger surface.

---

## W4. Scheduled multi-tenant reporting

**Problem.** There is no answer to "show me every client against the standard". That is the one capability the comparable tools have and this does not.

**Target.** Scheduled execution of W3 across a list of tenants, writing one report per tenant plus a roll-up. Report and alert only. Never remediate.

Borrow the useful distinction from the comparable tools: a control can be reported, or reported and alerted on. Do not borrow automatic remediation. Continuous unattended writing is incompatible with invariants 5 and 7, and the evidence chain is the differentiator, not the enforcement loop.

**Acceptance.** A run over several tenant profiles produces per-tenant reports and a roll-up naming which controls regressed since the previous run. No write scope is requested anywhere in the path. A test asserts the scheduled path cannot be configured with a deployment-mode session.

---

## W5. Extension contract

**Problem.** Nothing states which extensions are data and which are code. A contributor guesses, and the guess is sometimes that a new tenant action can be added in JSON, which the guards will refuse.

**Target.** `docs/EXTENDING.md`, one page, with a checklist per change type.

| To add | Change | Code needed |
| --- | --- | --- |
| A policy recipe | A control in `standards/source/controls/` | No |
| A captured collection | `collections.json` | No, unless a new relationship shape |
| An assessment signal | `equivalence` on a control | No |
| A reviewed tenant action | `ReviewedChangeKind`, `ReviewedChangeSafety`, route allow list, tests | Yes, and this is deliberate |
| A creatable object type | Collection write scope, a creation guard, tests | Yes |

**Acceptance.** A conformance test enumerates `ReviewedChangeKind` and fails when a kind has no guard branch and no test, so the document cannot drift from the code.

---

## W6. Release engineering

**Problem.** The application ships as a continuous integration artefact. No signature, no version check, no update path. An engineer can run a six-week-old build against a newer standard and not know.

**Target.**
1. Tagged releases with the portable package attached and its checksum published.
2. A compatibility check at load: the application refuses to plan when the standard's schema version exceeds what the build understands, and warns when a newer release exists than the one loaded.
3. Code signing if a certificate is available. If not, document the checksum verification as the substitute rather than leaving it implicit.

**Acceptance.** A build older than the loaded standard's schema refuses to plan with a message naming both versions.

---

## W7. Graph surface drift watch

**Problem.** Dependence on beta endpoints and on payload shapes is discovered when a tenant rejects a write.

**Target.** A scheduled check that reports when a collection declared `beta` has a version-one equivalent, and when a property a recipe writes no longer appears in the documented resource. Reporting only; it never edits a standard.

**Acceptance.** The check runs in CI on a schedule and opens or updates one issue listing findings. A finding names the control, the collection and the property.

**Note for the executor.** The authoring sandbox cannot reach `learn.microsoft.com`. Any implementation must degrade to "could not check" rather than to "no drift found", per invariant 6.

---

## W8. Import instead of author

**Problem.** Hand-writing recipes for settings-catalogue policies is slow and error-prone, and two controls are blocked on exactly this.

**Target.** Extend the existing import path to consume exports from the established configuration-as-code tools, not only raw Graph exports. Same guarantees as today: source tenant identifiers stripped, foreign identifiers replaced explicitly, imported template expressions refused.

**Acceptance.** An export becomes a candidate control with no hand editing beyond naming and identifier replacement. A test asserts a foreign tenant identifier anywhere in the imported tree blocks the import.

---

## W9. Code structure and optimisation

Continuous, not a milestone. Each item is independently shippable.

**9a. Split the two files that have become junction boxes.** `Workspace.cs` at 681 lines holds session state, profile persistence, standard selection, capture orchestration and export coordination. `AssessmentEngine.cs`, now 560 lines, holds candidate matching, scoring, enforcement interpretation and finding construction. Both are the files every change touches, which makes them the files every merge conflicts on. Split along the responsibilities already visible in their own method groups. Do not split for line count alone; split where a seam exists.

**9b. Decide the async convention and apply it. Partly done — finish it.** Graph now carries 54 `ConfigureAwait` across its 101 `await` expressions, so the convention has been started there and is incomplete. The Engine still has 84 `await` expressions and zero `ConfigureAwait`, which is where the console consumer actually pays. W3 exists, so the prerequisite is met: finish Graph, apply `ConfigureAwait(false)` throughout the Engine, and add an analyser rule so it stays applied. A half-applied convention is worse than none, because the next reader cannot tell which state is intended.

**9c. Cache digests rather than recomputing them.** Roughly forty call sites compute a canonical serialisation or a SHA-256 over model objects, several inside loops over plan rows. Compute once per object and carry the result. Measure first: this is only worth doing where a profile shows it, and correctness matters more than the microseconds.

**9d. Resolve the export format overlap.** `ExportBuildStandard` maps both `ClientHtml` and `Html` to the same output, which means the enumeration no longer says what it means. Either give the client document its own format or collapse the two.

**9e. Keep the parser and the field in step.** `PolicyInputParser` in the Engine and `PolicyInputField` in the application must not drift; the field is a thin wrapper by design. A test in the Engine covers the parser; `PolicyInputField` is still named by no test at all, confirmed at `e188d9e`. Add one, or accept the risk explicitly. PR #12 shows why this matters: the headless runner drifted from the application in exactly this way, by reimplementing rather than delegating, and a source-scan conformance test is what now holds it.

**9f. Tests are 6,916 lines against 16,555 of source, and 542 of them pass.** That ratio is healthy and improved since the last measurement. Protect it: every workstream above adds tests before it adds behaviour, and no workstream is done while a new public surface has none.

## Non-goals

State these plainly so a future executor does not rediscover them as ideas.

- **Do not rebuild as a hosted multi-tenant portal.** That is a different product with a different risk model, and it would mean giving up invariants 5 and 7.
- **Do not add automatic remediation.** Reporting and alerting carry the commercial value; unattended writing carries the liability.
- **Do not move the write surface into data.** Guards stay compiled.
- **Do not decompose historical releases.** They are evidence.

## What this plan does not cover, and does not outrank

This is an architecture plan. Every workstream in it improves code that has **never run against a Microsoft 365
tenant**. 542 synthetic tests prove the safety logic holds against a scripted Graph client; they cannot prove that
Graph accepts these 42 creation payloads, that the permissions behave as documented, or that admin consent completes
at the registered callback. `docs/LIVE-VALIDATION.md` is a complete staged checklist that has not been executed.

If an executor has to choose between a workstream here and progressing live validation, live validation wins. Nothing
in this document is worth more than the first read-only capture against a real tenant.
