# INT-088 slice 2: the owned read-only runner (Claude, 10 October 2026)

This is the second of the four source PRs that INT-088 ([READ-ONLY-RUNNER-CONTRACT-2026.10.08.md](READ-ONLY-RUNNER-CONTRACT-2026.10.08.md)) asks for: **the owned runner, with fake-process and module regressions**. It is stacked on slice 1 (#74), whose evidence kind it seals into.

It adds no shipped runnable item. The shipped registry stays schema 1, and every library item stays Copy-only. It also adds no Exchange adapter, no UI, no CLI command, no permission and no tenant access. Whether Run is offered at all is the experimental opt-in in slice 4, which will build on #73. This engine class is not that permission.

## What it adds

| File | Content |
|---|---|
| `Scripts/ScriptManifest.cs` | `ScriptExecution` (adapter, report, output kind and version, runner pin) and the optional `Execution` member, required by schema 2 and refused by schema 1 |
| `Scripts/ScriptCatalogue.cs` | Registry schema 2 with an explicit `manifestSchemaVersion` per entry, while registry schema 1 keeps its schema-1-only meaning; schema 2 admission; a text-level mirror of the wrapper's body check |
| `Scripts/Runner/ReadRunner.ps1` | The engine-embedded wrapper: ASCII, LF, and pinned by `ReadRunnerTemplate.Sha256` |
| `Scripts/Runner/ReadRunnerTemplate.cs` | The pin, the fixed limits, and the verified wrapper bytes |
| `Scripts/Runner/ScriptReadRequest.cs` | Context checks, form binding to the registered parameters, and the exact request bytes |
| `Scripts/Runner/ScriptReadResult.cs` | The strict `scriptReadResult` reader, bound to the exact request |
| `Scripts/Runner/OwnedProcess.cs` | The bounded owned process: timeout, output limit, stop file, then a tree kill after the grace period |
| `Scripts/Runner/ExchangeReportRunner.cs` | Staging, launch, outcome decisions and sealing into `exchangeReportEvidence` |
| `Scripts/Runner/OriginMetadata.cs` | Carries the application's own `Zone.Identifier` onto the staged files |
| `Scripts/ScriptCopy.cs` | A schema 2 item has no Copy form, because the gate does not exist in a copied script |

## Admission (schema 2)

- **Read-only only.** A `copyOnlyChange` with `execution` is refused.
- **Lower caps:** at most 1,800 seconds and at most 10,000 rows per section.
- **The engine registration is authoritative**, and the manifest may only repeat it:
  - the adapter equals the resource (only `exchangeOnline` is admitted in this release);
  - the report is registered for that adapter;
  - the output is `scriptReadResult` 1 for report schema 1;
  - the runner pin equals the engine constant;
  - the module is exactly `ExchangeOnlineManagement`.
- **Columns** must equal the registered row projection, exactly and in order.
- **Parameters** must equal the registered parameters by name, type and order: no arrays and no formats. Integers and dates must always have a value, because every registered value is recorded.
- **The body check at load time:**
  - every read must be written as `Use-BditRead -Command '<registered>' -Parameters …`;
  - any other command outside a short fixed list is refused;
  - `&`, `.`, `global:`, `$ExecutionContext`, `[scriptblock]` and similar are refused.

## The wrapper

- **Start marker.** The first statement writes `BDIT:STARTED`. Without it, the parent reports `HostRefused` whatever the exit code, with the security-owner route and no stderr.
- **No reachable run state.** `-File` runs a script in the global scope, which every module scope can see. All run state therefore lives inside a function, and the script-level parameters are removed first.
- **Request.**
  - The bytes must match the parent's SHA-256.
  - The shape must be exact.
  - Text and dates arrive as UTF-8 base64.
  - Dates are parsed only as `yyyy-MM-dd` and treated as UTC.
  - Integers and booleans keep their .NET types.
- **Body.**
  - The bytes must match the pinned SHA-256.
  - The body is then parsed, and its syntax tree is checked before any import or sign-in.
  - Commands come from a fixed list or must be functions the body defines, and those may not reuse reserved names.
  - Allowed cmdlets take only literals and literal script blocks; `ForEach-Object` takes no member names.
  - **Method calls are allow-listed:** `ToString`, `Trim`, `Contains`, `Add`, `new` and similar.
  - Type names and attributes are allow-listed.
  - Reflection and scope members (`Assembly`, `SessionState`, `ForEach`…), scoped variables, and automatic variables such as `$ExecutionContext` are denied.
  - Class, enum and workflow definitions and `#requires` are refused.
- **Module.**
  - The module must already be installed at the minimum version; the toolkit never installs one.
  - `Connect`, `Disconnect` and `Get-ConnectionInformation` are captured from the imported module or its nested modules before the body exists.
  - Sign-in uses the confirmed account as its hint and `-CommandName` with only the registered commands.
  - Source read commands are captured after sign-in, from the module, its nested modules, or the session module that sign-in created.
- **The gate (`Use-BditRead`).** It lives in a private module.
  - It accepts only registered commands, and only the read's own parameters, as data. Common parameters and script-block values are refused.
  - Before and after every read it requires exactly one Exchange connection to the expected tenant and account, and polls the stop file.
  - That connection must also have a usable token: `TokenStatus` is `Active` and `TokenExpiryTimeUTC` is after now (AST-20261010-03). A missing, null, malformed or ambiguous status or expiry is never guessed usable. A `DateTimeOffset` is an instant, a Local `DateTime` is converted, and an Unspecified `DateTime` is read as the documented UTC. Text is accepted only as ISO 8601 with `Z` or an explicit offset, under the invariant culture. An expired token before the first read refuses the run with nothing read; one that expires during the run keeps no rows, like an identity change. There is no retry or replay.
  - After any failure it refuses every later read.
- **The body's scope.** The body runs in its own module scope. It sees only the gate and its typed arguments.
- **After the body.**
  - The connection is checked once more, then disconnected.
  - Exactly the registered sections and columns are projected, with string, boolean or null values.
  - An identity change or an expired token keeps no rows and marks every section Failed.
  - A stop keeps any rows already returned, as Cancelled.
- **Result.** The envelope is written create-new to the parent-named file.

## The parent

- **Checks before anything exists:**
  - the item must be schema 2 and read-only;
  - the runtime must be one the item supports;
  - the tenant and account object ID must be canonical GUIDs;
  - the account must be a confirmed UPN with no surrounding space;
  - the form must bind exactly.
- **Runtime.** Only the fixed system locations are used: Windows PowerShell 5.1 and machine-wide PowerShell 7. There is no PATH lookup and no fallback to another host.
- **Staging:**
  - a new per-run folder, with create-new files;
  - the application's download marking is inherited;
  - everything is re-read and re-hashed immediately before launch, and removed afterwards;
  - leftovers are never read.
- **Launch:** `UseShellExecute=false`, an argument list, `-NoLogo -NoProfile -File`, with no `-Command`, policy override or module path.
- **Bounds:**
  - pipes that a descendant holds open after the process exits are given up on after 10 s, and the run keeps no rows;
  - progress output 1 MiB, with only fixed marker lines kept;
  - stderr drained, nothing kept, and only a flag recorded past 256 KiB;
  - result 32 MiB;
  - timeout at most 1,800 s;
  - on stop: the stop file, then a tree kill after 5 s.
- **Result:**
  - The strict reader binds the envelope to the exact request: run, item, pins, adapter, report, tenant, observed UPN, parameters and sections.
  - The child's clock is accepted only inside the parent's own window.
  - The module version must meet the minimum.
  - Sealing then applies every slice 1 rule.
  - A non-zero exit, a missing `DONE`, any failure marker or a refused envelope keeps no rows.
  - A time limit or stop keeps rows only from a complete envelope, downgraded to Partial or Cancelled.

## Proof

- **Real processes.** Every runner case launches a real PowerShell, with the synthetic stand-in module in `tests/…/Fixtures/ReadRunner`. The stand-in is versioned 99.0.0 so it outranks any installed module, and the module path is limited to it and PowerShell's own modules.
- **Runtimes.** Each case runs on every runtime found: 5.1 and 7 on the Windows CI runner, and 7 locally on Linux.
- **Cases covered:**
  - the happy path, archives, and a failed size read;
  - the wrong account, the wrong tenant, and two connections, each with no reads;
  - a sign-in failure;
  - an account change after the last read;
  - a missing module;
  - seven bodies that pass the text check but fail the syntax check, all refused before sign-in;
  - gate parameter refusal;
  - scope isolation;
  - cooperative and forced stops;
  - the time limit, the output limit, and diagnostics drained without blocking;
  - a missing runtime; a refusing host;
  - inherited origin marking, on Windows under RemoteSigned;
  - typed binding for date-looking text, dates, integers and booleans;
  - 14 malformed requests; a body changed after staging.
- **The strict reader:** 19 forged fields, exact repetition of parameters and sections, duplicate properties, invalid UTF-8 and oversize input.
- **Admission:** version dispatch both ways, copy-only refusal, the caps, registration equality, and 11 bodies that read outside the gate.
- **Failing first, twice.** Two defects in my own first draft were found by writing the attack as a test first.
  1. **Scope.** The wrapper ran in the global scope, so the body could read the wrapper's variables. The AST rules still stopped it from invoking them. All run state now lives inside a function, and the test passes.
  2. **Reflection.** Methods were deny-listed. A body could reach `System.IO.File` through `GetType().Assembly…InvokeMember(…)`, or by reading `Assembly` through `GetProperty(…).GetValue(…)`, and the test showed a file written. Methods are now allow-listed, so a deny-list can no longer be bypassed, and all six escape cases are refused before sign-in. One of them uses no denied member at all, so it fails only if the allow-list is removed.
- **Mutation-checked.** 32 guards were disabled one at a time: 17 in the wrapper and 15 in the C# code. Wrapper mutations were re-pinned so that only the guard changed.
  - 31 failed exactly their intended tests.
  - One first attempt broke the script's syntax instead of the guard. It was discarded and redone precisely.
  - The post-read identity check alone is backed up by the final connection check. Disabling both fails the late account-change test.

## Review corrections

- **AST-20261010-01 (Astra, from #74).** Carried in by merging the slice 1 branch: blank mailbox values are refused.
- **AST-20261010-02 (Astra, P2).** An explicit null collection, null element or null required member in the child envelope escaped the controlled refusal as an ordinary exception. `ScriptReadResult.Read` now refuses any null where a value is required, so the run ends Failed with no evidence and the fixed reason. Eight of 24 new null cases failed on `95a2343` (the rest were already refused); all pass now, and a null section error stays accepted.
- **AST-20261010-03 (Astra, P2).** The gate ignored `TokenExpiryTimeUTC` and `TokenStatus`, so an expired-but-Active connection still read and sealed Collected rows. The gate now checks the token as described above. Eleven unusable-token cases (expired but Active, expired status, missing or null status or expiry, malformed text, a number, ambiguous day/month text, a past instant written with a +14:00 offset, a past Unspecified `DateTime`) and two expiry-during-run cases failed on `95a2343` and pass now; five usable forms pass. The child runs in a time zone chosen so that reading an Unspecified UTC expiry as local time fails a test; that mutation and removing the time comparison were both caught. The final post-body check is backed up by the per-read check, as for identity. The wrapper pin changed to `20cc92dd…`.

## Not done here, and still required

- **Slice 3:** the registered Exchange adapters, meaning the first shipped schema 2 body for `exo-mailbox-inventory`, with its real projection, fixed reasons and live-unverified status. `-CommandName` with `Get-EXO*` REST cmdlets is unverified against the real module.
- **Slice 4:** the Reports and Scripts UI and the experimental opt-in, after #73.
- **Live acceptance:** none. Whether the real module returns `TokenStatus` and `TokenExpiryTimeUTC` on every connection, and in which type, is unverified; if either is missing the runner refuses, which a live run must confirm is not every run.
- **Live acceptance (other):** none. The behaviour of the real module, prompts, RBAC, `Get-ConnectionInformation` output and policy refusals stays unverified until William accepts each in a test tenant.
- **Not covered by a test:** two branches are refused in code, but the real wrapper and stand-in never produce them, so no test exercises them:
  - a non-zero runner exit with a plausible result;
  - a descendant holding the output pipes open.
