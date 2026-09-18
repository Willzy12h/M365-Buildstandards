# Architecture roadmap

Written to be executed by a language model, not read by a human sponsor. It states what to change, where, in what order, and how to know each step is finished. Prose is kept to what an executor needs to make correct decisions when the instruction does not cover the case in front of it.

Baseline for every measurement here: `integration` at preview.12, standard 2026.09.10. Engine 6,029 lines, tests 5,418, app 4,861, core 2,786, graph 2,061. Standards 23,647 lines across eight release files.

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

W0 first. W2 before W1 because W1's compiler needs the helper. W3 before W4 because scheduled reporting needs a headless entry point. W5 can proceed in parallel once W1 has settled the data shape.

---

## W0. Remove the embedded NUL byte

**Problem.** `src/BDIT.TenantToolkit.Engine/Assessment/EquivalenceEvaluator.cs` contains a raw NUL (0x00) inside a string literal, in the group key for ungrouped required signals. `file` reports the source as `data`; `grep` treats it as binary and suppresses matches; diffs are unreadable.

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

**Problem.** The same scan appears in four places: `PolicyInputValidator`, `PolicyInputDefaults`, `BuildStandardDocument` twice, and `AutomationViewModel`. Each serialises a payload to a string and does `Contains("{{" + key + "}}")`, so the cost is one full serialisation plus one scan per parameter per control. For fifty controls and twenty-two parameters that is over a thousand substring scans per plan, each over a freshly allocated string.

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

**Acceptance.** All five call sites use the helper. A test proves a parameter referenced only inside a nested array is found, and that a literal `{{notAParameter}}` in a description field is not reported as a parameter. No behaviour change in existing tests.

---

## W1. Compile releases instead of copying them

**Problem.** Eight release files, 23,647 lines, each a full copy of its predecessor. Release 2026.09.7 changed three controls of forty-five and cost a 3,317-line file. Release 2026.09.9 left only eight of fifty controls byte-identical to its predecessor because a targeting change rewrote one field on nearly every control. Consequences: nobody can see what changed between releases without diffing whole files; a payload correction must be hand-applied to every later release, so old releases rot; every publication adds roughly 3,600 lines.

**Target.** One source of truth per control, plus a per-release manifest of inclusions and overrides. A build step compiles the published flat release file. Everything downstream, the loader, the integrity manifest, the digest, is unchanged, because the compiled artefact is byte-for-byte the format that ships today.

```
standards/
  source/
    controls/CA-001.json          one control, current definition
    controls/CFG-WIN-003.json
    collections.json              shared collection definitions
    parameters.json               shared parameter definitions
  releases/
    2026.09.10.json               manifest: which controls, which overrides, release metadata
  2026.09.10.json                 compiled output, shipped, unchanged in format
```

A release manifest names the controls it includes and any per-release override, so a release that differs from source in one field records one field, not a whole control.

**Steps.**
1. Write the compiler as a build task that emits a release file from source plus manifest.
2. Decompose the current 2026.09.10 into `source/` and a manifest that compiles back to it byte-identically. This is the proof the compiler is correct.
3. Add a CI check that recompiles every release and fails if the committed output differs.
4. Leave 2026.09.3 to 2026.09.9 as frozen literal files. They are historical evidence, referenced by digest in stored plans. Do not decompose them, and do not regenerate them.

**Acceptance.** `standards/2026.09.10.json` regenerates byte-identically from source. CI fails on a hand-edit of a compiled file. Adding a setting to one control changes one source file and one manifest line. Historical releases still load and still match their recorded digests.

**Risk.** The integrity manifest covers compiled output, so a compiler bug becomes a digest mismatch rather than a silent wrong policy. Keep the byte-identical round trip as the gate and this workstream cannot ship a wrong payload.

---

## W3. Headless runner

**Problem.** The Engine is a clean library but has exactly one consumer, so the seam is asserted rather than proven. There is also no way to assess a tenant without a person at a desktop.

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

**9a. Split the two files that have become junction boxes.** `Workspace.cs` at 681 lines holds session state, profile persistence, standard selection, capture orchestration and export coordination. `AssessmentEngine.cs` at 506 lines holds candidate matching, scoring, enforcement interpretation and finding construction. Both are the files every change touches, which makes them the files every merge conflicts on. Split along the responsibilities already visible in their own method groups. Do not split for line count alone; split where a seam exists.

**9b. Decide the async convention and apply it.** The Engine has 84 `await` expressions and zero `ConfigureAwait`. That is correct for a WPF consumer and wrong for a library with a console consumer, where continuing on a captured context is needless overhead. Once W3 exists, apply `ConfigureAwait(false)` throughout the Engine and Graph projects and add an analyser rule so it stays applied. Do not do this before W3; without a second consumer it is churn.

**9c. Cache digests rather than recomputing them.** Roughly forty call sites compute a canonical serialisation or a SHA-256 over model objects, several inside loops over plan rows. Compute once per object and carry the result. Measure first: this is only worth doing where a profile shows it, and correctness matters more than the microseconds.

**9d. Resolve the export format overlap.** `ExportBuildStandard` maps both `ClientHtml` and `Html` to the same output, which means the enumeration no longer says what it means. Either give the client document its own format or collapse the two.

**9e. Keep the parser and the field in step.** `PolicyInputParser` in the Engine and `PolicyInputField` in the application must not drift; the field is a thin wrapper by design. A test in the Engine covers the parser; there is no test that the wrapper still delegates. Add one, or accept the risk explicitly.

**9f. Tests are 5,418 lines against 15,737 of source.** That ratio is healthy. Protect it: every workstream above adds tests before it adds behaviour, and no workstream is done while a new public surface has none.

## Non-goals

State these plainly so a future executor does not rediscover them as ideas.

- **Do not rebuild as a hosted multi-tenant portal.** That is a different product with a different risk model, and it would mean giving up invariants 5 and 7.
- **Do not add automatic remediation.** Reporting and alerting carry the commercial value; unattended writing carries the liability.
- **Do not move the write surface into data.** Guards stay compiled.
- **Do not decompose historical releases.** They are evidence.
