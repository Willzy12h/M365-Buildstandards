# Reviewed integration merges — 10 October 2026

Source remains **1.1.0-preview.19, unpublished**. These are source/Windows checks, not live acceptance or release approval. Historical catalogues and earlier release evidence remain unchanged.

| PR | Exact reviewed/refreshed head | Integration merge | Actual integration push run |
|---|---|---|---|
| [#58](https://github.com/Willzy12h/M365-Buildstandards/pull/58) | `6b20bbf549fd0f121666fb1365e1de15c9c81125` | `f679d3da6beb7265fa8c0637d2cfa867b98c8e61` | [38041551599](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/38041551599) |
| [#62](https://github.com/Willzy12h/M365-Buildstandards/pull/62) | `c691baeac4094cca41d21d9544f0b31d510f10ff` | `ee9ca60436ee85fe6447bf281e9d72cb72c4db94` | [38042386974](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/38042386974) |
| [#65](https://github.com/Willzy12h/M365-Buildstandards/pull/65) | `08fd92f0f9db2273ee73a6acfe84e6b51060cd62` | `a55659defff56bd714fee604eeb82363cc7ee603` | [38043570359](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/38043570359) |
| [#68](https://github.com/Willzy12h/M365-Buildstandards/pull/68) | `831b5bc6451d6f3631116709da4be2f751a7b292` | `463c0c84e396aac4d3dcf4dfc1c8f3e4d9eae2a8` | [38044346614](https://github.com/Willzy12h/M365-Buildstandards/actions/runs/38044346614) |

Each merged slice retains Claude’s independent source review. Refreshed heads passed build/standard/secrets checks before merge; merge commits use actual successful integration push runs. Required jobs and package source identities were independently read. Append-only log conflicts preserve both histories and Claude claim rows. No production conflict was resolved by taking one side wholesale.

The final integration push verified 48 native page/size renders, 89 commands, zero binding issues/tenant calls, the fresh 302-file package, stage bytes/checksums, first launch/shutdown, Accessibility/context-menu Copy and portable CLI output/exit/inventory/jobs/report routes. Original ZIP SHA-256: `4238d9f70af4315175ac2f7ee8f83d3c4eb596caa1178b3667380fec2418911f`. This Preview.19 CI package is not the final candidate, a release pin or publication.

CI-record correction: the private merge observer initially chose the newest matching head-SHA run for #65/#68, which was the new main-promotion PR check. Those checks also passed; the canonical records above instead verify the actual integration **push** runs, with package source SHA matched. PR comments were corrected; future selection filters by event as well as branch/source. Do not reuse a prospective main-merge check as integration merge proof.

Pending own deltas: #57 at `665fa2d` has 1,464 restored engine/CLI tests and exact Windows push/PR runs 38043183294 / 38043186218 green; current ENR-002 import and naming-route/immutability tests address CLA-34 without bypassing guards. #67 at `381deb0` fixes the dispatch scheduling assumption with failing-first/cancellation proof; #69 at `8e8257b` records William’s Application.Read.All purpose acknowledgement and removes future-feature operator wording; #73 at `198cd9d` fixes the native header regression and passes the full Windows suite. Their narrow independent reviews remain pending.

Astra’s independent #74/#75 review found AST-20261010-01 to -03; Claude owns the fixes. #77 is a draft promotion placeholder with accuracy finding AST-20261010-04, and must not be merged on these checks. See [review findings and assurance strategy](release-readiness/REVIEW-STRATEGY-2026.10.10.md). No live gate, broad feedback item, consent, version bump or publication is marked complete.
