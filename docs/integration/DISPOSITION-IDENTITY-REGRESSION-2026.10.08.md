# Dedicated disposition identity regression — PR #60

Claude's merged review record #47 identified a coverage limitation for R02: the merged safeguard had an outcome identity test, but no dedicated conflicting-disposition regression was found. This slice adds that missing proof without changing production logic, evidence schemas or Claude's feedback state.

The new test refuses a second semantic identity for the same requirement instance, with and without an attempted supersession. It verifies every existing tenant-file byte/timestamp fingerprint and attachment remains unchanged, then successfully records a legitimate revision in the original line. A separate test constructs two intact historical decision records with conflicting identities: both remain visible and unsettled, completion reports Outstanding/Needs review, no one decision is selected as authoritative, and projection/completion reads do not change files.

The initial saved-history assertion expected an empty decision label. Inspection confirmed the settled existing UI contract is `Needs review`; the assertion was corrected to that explicit state, retaining the no-authoritative-decision and outstanding checks. This was a fixture expectation correction, not a production change or relaxed safeguard.

Failing-first method: on the same current source, temporarily substitute only `CheckSupersession` from pre-#36 first parent `d5bbe245^1`, keeping all other production logic/tests unchanged. The completed historical-guard run executed two dedicated cases: the attempted supersession still refused under an older, separate negative guard (not a positive control), but the non-superseding second identity was accepted and the refusal assertion failed. Current production bytes were restored in a finally block and verified byte-for-byte through a clean Git diff. An earlier interrupted attempt is not counted as completed proof. The temporary guard removal is never committed or pushed.

Final targeted/Windows results are recorded on the PR. Human/live completion and independent delta review of #36 remain open; this closes a test gap only and does not complete the master programme.

Final local targeted run: all 26 disposition workflow cases executed and passed, zero skipped, including the three new cases. No production source diff remains. Windows exact-head validation is pending at this push.

The first Windows build at `d0a85a0` refused the new assertion under xUnit2031 because strict CI treats analyser diagnostics as errors. Use the filtering overload of `Assert.Single` instead; the same single matching requirement and all safeguard assertions are preserved. This is a test-style correction, not a relaxed test or production change. Fresh exact-head Windows validation is required.

## CLA-20261008-55/56 response — 9 October 2026

Both conflicting-identity cases now require the specific already-has-a-different-identity message. Under the pre-#36 guard, the superseding case also fails because its earlier separate requirement/supersession refusal no longer satisfies that assertion. The genuine positive control is the legitimate revision recorded after refusal, not the invalid supersession. Production guards and historical records are unchanged. The own claim has a table header; Claude rows remain unchanged.

Review-response negative-control execution: remove only the current instance-identity guard (equivalent to the inspected pre-#36 CheckSupersession body). Both dedicated cases fail: non-superseding conflict is accepted, and superseding conflict fails the exact-message assertion under the older separate guard. The source is restored byte-for-byte; no production diff is committed. Current 26-case run passed before the mutation; the restored verification is recorded on the PR.
