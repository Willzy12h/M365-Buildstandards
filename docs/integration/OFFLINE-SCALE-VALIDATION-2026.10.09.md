# Offline report scale validation — 9 October 2026

### Independent review follow-up — 10 October, PR #78

CLA-20261009-01 remained open at #65's merge despite a contradictory ready comment. The follow-up verifies that an explicitly cap-reached Partial report retains its full truncation reason in JSON round-trip, HTML, CSV provenance and XLSX provenance. Removing the reason must fail the regression. Unknown last-sync rows now carry Partial and a read reason, matching collection semantics (CLA-02). The benchmark's `outputBytes` remains the measured return value size: seal returns the 64-byte digest text and read returns an object recorded as zero, not a measured document size (CLA-03). No machine-independent speed, memory or live-performance claim is made. The merged library contains 29 Copy/Save-only items; historical base descriptions below identify the original claim, not current source (CLA-04).

PR #65 is the first additional optimisation/robustness slice William authorised on 9 October. It branches from integration `92cb07c`, after Claude's second Exchange pack merged. The script library now contains 21 Copy-only items. Existing review and release work remains separate.

## Run and interpret

Use the repository's pinned .NET 10 SDK and Python 3, with the normal engine-test restore completed. From the repository root:

```text
python build/Measure-ReportScale.py --output <new-local-results-file.json> --repeats 3
```

The helper starts 1–5 separate test processes (default 3) and refuses existing output files. It requires all four correctness tests and every distinct stage/size measurement to succeed before saving JSON. Failed, skipped, incomplete and zero-test runs cannot produce a successful measurement record. There are no tenant requests, sign-ins or module loads. The fixture is synthetic, historical, and marked as such; it is never client evidence.

The suite checks 100, 1,000 and the existing maximum 5,000 device rows, plus refusal at 5,001. Each stage is warmed with a small fixture. Measurements cover sealing, strict JSON reading, HTML generation, CSV ZIP generation and XLSX generation. Fixture construction and verification are excluded. The helper saves raw samples and medians with the exact source commit, dirty-tree status, SDK and platform. A dirty-tree record is exploratory evidence; compare exact clean source records for release decisions.

Timings have no machine-dependent test threshold. Current-thread allocated bytes do not measure retained or peak process memory. Shared-host scheduling, JIT and GC can change measurements; compare like machines/runtimes and retain original samples. This measures one registered device-report shape, not all reports, WPF responsiveness, Graph throughput or Exchange behaviour. A successful measurement is not a promise of live speed.

Correctness checks parse the CSV fields and workbook XML, verify exact data-row counts and final object IDs, preserve unknown last-sync values, assert that the workbook contains no formula elements, and confirm that exporting leaves the original sealed evidence unchanged. Full machine results stay outside the repository.

## Validation and follow-up

Local .NET 10 strict compilation and the four tests passed in three separate measurement processes: 12 executed, zero skipped or failed. Python compilation and actual measurement parsing/output passed. Exact clean-head results and Windows CI are recorded in the PR before readiness. These checks preserve production source; no performance improvement is claimed yet.

Further source optimisation is selected only after profiling confirms a bottleneck. Repeated strict validation and intermediate table/string allocation are candidates for investigation; skipping integrity validation, raising reader limits, caching another account's evidence or retrying writes are not optimisation routes.

Next independent slice: exercise real read back-off cancellation and pagination interruption through synthetic HTTP handlers, extending existing retries/cancellation tests rather than replacing them. Follow with import/export robustness. Existing CLI work in #62 and readiness work in #53 settle before a `bdit doctor` command is implemented. Extra report/schema/access decisions precede new collectors; Claude retains script-library ownership. Nothing here authorises new live access or publication.

## Integration order

1. Claude independently reviews #65 and any subsequent slices; retain findings and fix evidence in the existing register.
2. Refresh each ready branch from current integration, preserve both sides of ledger conflicts and rerun exact-head required checks.
3. Merge commits only under HANDOVER's delegation, then verify the actual merge-commit CI and record SHA/run.
4. Dependent production optimisation follows the validated baseline; new report contracts merge before their implementation.
5. Live acceptance, release promotion and publication keep their separate approval gates. These additions do not postpone an otherwise usable preview candidate.
